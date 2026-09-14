using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Arvore.UIExporter.Editor
{
    /// <summary>
    /// Escreve as imagens do pacote em <c>Assets/</c> e devolve os sprites resultantes.
    /// </summary>
    /// <remarks>
    /// O nome do arquivo de destino vem do <c>asset.id</c> do IR, cujo formato o schema
    /// restringe a <c>[A-Za-z0-9_-]+</c> — <b>nunca</b> do nome da entrada no zip. É essa
    /// escolha que tira zip-slip da mesa por construção, em vez de depender de acertar a
    /// normalização de caminho.
    /// </remarks>
    public static class SpriteImporter
    {
        public sealed class Result
        {
            private readonly Dictionary<string, Sprite> byAssetId =
                new Dictionary<string, Sprite>(StringComparer.Ordinal);

            public int Written { get; internal set; }

            public int Reused { get; internal set; }

            public Sprite Resolve(string assetId)
            {
                if (string.IsNullOrEmpty(assetId))
                {
                    return null;
                }

                return byAssetId.TryGetValue(assetId, out Sprite sprite) ? sprite : null;
            }

            internal void Add(string assetId, Sprite sprite)
            {
                byAssetId[assetId] = sprite;
            }
        }

        public static Result Import(
            IRDocument document,
            UIExportPackage package,
            string spritesFolder,
            UIImportSettings settings,
            ImportReport report)
        {
            var result = new Result();

            if (document.Assets.Count == 0)
            {
                return result;
            }

            AssetFolders.Ensure(spritesFolder);

            var pending = new List<(string assetId, string path, IRAsset asset)>();

            try
            {
                // Um único refresh no fim em vez de um por arquivo: com dezenas de imagens a
                // diferença é de segundos para minutos.
                AssetDatabase.StartAssetEditing();

                foreach (IRAsset asset in document.Assets)
                {
                    if (!IsSafeAssetId(asset.Id))
                    {
                        report.Warn(
                            "sprite/bad-id",
                            $"O asset '{asset.Id}' tem id fora do formato esperado e foi ignorado.");
                        continue;
                    }

                    byte[] bytes = package.TryGetImage(asset.File);
                    if (bytes == null)
                    {
                        report.Warn(
                            "sprite/missing",
                            $"O pacote declara '{asset.File}' mas não contém esse arquivo.");
                        continue;
                    }

                    string path = $"{spritesFolder}/{asset.Id}.png";

                    ReportSizePolicy(asset, settings, report);

                    if (WriteIfChanged(path, bytes))
                    {
                        result.Written++;
                    }
                    else
                    {
                        result.Reused++;
                    }

                    pending.Add((asset.Id, path, asset));
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            foreach ((string assetId, string path, IRAsset asset) in pending)
            {
                ConfigureImporter(path, asset, settings);
            }

            AssetDatabase.Refresh();

            foreach ((string assetId, string path, IRAsset _) in pending)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                {
                    report.Warn(
                        "sprite/import-failed",
                        $"'{path}' foi escrito mas a Unity não gerou sprite a partir dele.");
                    continue;
                }

                result.Add(assetId, sprite);
            }

            return result;
        }

        /// <summary>
        /// Relata sprites cuja dimensão custa memória na Unity. Nunca altera o pixel.
        /// </summary>
        /// <remarks>
        /// O que importa aqui é ser <b>múltiplo de 4</b>: a compressão em blocos (ASTC, DXT,
        /// ETC) só se aplica nessa condição, e sem ela a textura fica em RGBA32, várias vezes
        /// maior na memória. Potência de 2 quase nunca faz diferença para UI em UGUI.
        /// <para>
        /// O importador <b>não</b> corrige sozinho, e isso é deliberado. Corrigir exigiria
        /// preencher a textura e recortar o sprite de volta ao desenho; o recorte depende de
        /// <c>ISpriteEditorDataProvider</c>, que vive num pacote que este aqui não quer impor a
        /// todo jogo, e preencher sem recortar espremeria o desenho — imperceptível num fundo
        /// de 200px, 11% num ícone de 18px. Entre uma correção que às vezes deforma e um aviso
        /// preciso, o aviso é a escolha honesta. A correção de verdade é o <c>SpriteAtlas</c>,
        /// que resolve compressão e batching de uma vez.
        /// </para>
        /// </remarks>
        private static void ReportSizePolicy(
            IRAsset asset,
            UIImportSettings settings,
            ImportReport report)
        {
            if (settings.SpriteSizePolicy == SpriteSizePolicy.None) return;
            if (asset.Width <= 0 || asset.Height <= 0) return;

            int width = NextAllowed(asset.Width, settings.SpriteSizePolicy);
            int height = NextAllowed(asset.Height, settings.SpriteSizePolicy);

            if (width == asset.Width && height == asset.Height) return;

            report.Warn(
                "sprite/size-policy",
                $"'{asset.Id}' é {asset.Width}x{asset.Height}; {width}x{height} atenderia " +
                $"{settings.SpriteSizePolicy}. Como está, a compressão em blocos não se aplica " +
                "e a textura ocupa mais memória do que precisaria. Ajuste o tamanho da layer " +
                "no Figma, ou aceite o custo.");
        }

        private static int NextAllowed(int value, SpriteSizePolicy policy)
        {
            if (value <= 0) return value;

            switch (policy)
            {
                case SpriteSizePolicy.MultipleOfFour:
                    return (value + 3) / 4 * 4;

                case SpriteSizePolicy.PowerOfTwo:
                    int size = 1;
                    while (size < value) size <<= 1;
                    return size;

                default:
                    return value;
            }
        }

        /// <summary>
        /// Só grava quando os bytes mudaram.
        /// </summary>
        /// <remarks>
        /// Reescrever um PNG idêntico faz a Unity reimportar a textura, o que custa segundos
        /// por arquivo e — pior — invalida referências durante o import. Comparar antes é
        /// barato.
        /// </remarks>
        private static bool WriteIfChanged(string assetPath, byte[] bytes)
        {
            string absolute = AssetFolders.ToAbsolute(assetPath);

            if (File.Exists(absolute))
            {
                byte[] existing = File.ReadAllBytes(absolute);
                if (existing.Length == bytes.Length && BytesEqual(existing, bytes))
                {
                    return false;
                }
            }

            File.WriteAllBytes(absolute, bytes);
            return true;
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static void ConfigureImporter(string assetPath, IRAsset asset, UIImportSettings settings)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = settings.MaxTextureSize;

            // O PNG sai em @2x (ou @3x); pixelsPerUnit igual à escala mantém o sprite do
            // tamanho de design ao ser desenhado.
            importer.spritePixelsPerUnit = Mathf.Max(1, asset.Scale) * 100f;

            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = ResolveBorder(asset);

            importer.SaveAndReimport();
        }

        /// <summary>
        /// Converte as bordas de 9-slice para a ordem do Unity.
        /// </summary>
        /// <remarks>
        /// IR usa ordem CSS de arestas — <c>[top, right, bottom, left]</c> — e
        /// <c>spriteBorder</c> é <c>(left, bottom, right, top)</c>. Além da ordem, os valores
        /// estão em px de design e a textura está na escala do export, então precisam ser
        /// multiplicados.
        /// </remarks>
        private static Vector4 ResolveBorder(IRAsset asset)
        {
            if (asset.NineSlice == null || asset.NineSlice.Length != 4)
            {
                return Vector4.zero;
            }

            float scale = Mathf.Max(1, asset.Scale);

            return new Vector4(
                asset.NineSlice[3] * scale,
                asset.NineSlice[2] * scale,
                asset.NineSlice[1] * scale,
                asset.NineSlice[0] * scale);
        }

        private static bool IsSafeAssetId(string assetId)
        {
            if (string.IsNullOrEmpty(assetId) || assetId.Length > 96)
            {
                return false;
            }

            foreach (char c in assetId)
            {
                bool ok = (c >= 'a' && c <= 'z')
                    || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9')
                    || c == '_'
                    || c == '-';

                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
