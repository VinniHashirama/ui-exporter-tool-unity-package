using System.IO;
using UnityEditor;
using UnityEngine;

namespace Arvore.UIExporter.Editor
{
    /// <summary>
    /// Gera os sprites do kit placeholder.
    /// </summary>
    /// <remarks>
    /// Gerar em vez de commitar binário: o sprite arredondado é a única textura de que o
    /// importador precisa para fundos com canto, e uma textura versionada em Git é ruído de
    /// diff que ninguém consegue revisar.
    /// <para>
    /// São placeholders honestos — branco, para serem tingidos por <c>Image.color</c>. Quando
    /// a arte real entrar, o caminho é trocar os sprites dos prefabs do kit; nenhuma tela
    /// precisa ser re-exportada.
    /// </para>
    /// </remarks>
    public static class PlaceholderSprites
    {
        private const int RoundedSize = 48;
        private const int RoundedRadius = 12;

        public const string RoundedName = "UIKit_RoundedRect.png";
        public const string CircleName = "UIKit_Circle.png";
        public const string SquareName = "UIKit_Square.png";

        public static Sprite EnsureRounded(string folder)
        {
            return Ensure(
                folder,
                RoundedName,
                () => RoundedRect(RoundedSize, RoundedRadius),
                new Vector4(RoundedRadius, RoundedRadius, RoundedRadius, RoundedRadius));
        }

        public static Sprite EnsureCircle(string folder)
        {
            return Ensure(folder, CircleName, () => Circle(64), Vector4.zero);
        }

        public static Sprite EnsureSquare(string folder)
        {
            return Ensure(folder, SquareName, () => Solid(8), Vector4.zero);
        }

        private static Sprite Ensure(
            string folder,
            string fileName,
            System.Func<Texture2D> create,
            Vector4 border)
        {
            AssetFolders.Ensure(folder);
            string assetPath = $"{folder}/{fileName}";

            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (existing != null)
            {
                return existing;
            }

            Texture2D texture = create();

            try
            {
                File.WriteAllBytes(AssetFolders.ToAbsolute(assetPath), texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.alphaIsTransparency = true;
                importer.spriteBorder = border;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        /// <summary>Retângulo branco com cantos arredondados, com 1px de suavização.</summary>
        private static Texture2D RoundedRect(int size, int radius)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Distância até o centro do canto mais próximo; fora do raio, transparente.
                    float dx = Mathf.Max(radius - (x + 0.5f), (x + 0.5f) - (size - radius));
                    float dy = Mathf.Max(radius - (y + 0.5f), (y + 0.5f) - (size - radius));

                    float alpha;
                    if (dx <= 0f || dy <= 0f)
                    {
                        alpha = 1f;
                    }
                    else
                    {
                        float distance = Mathf.Sqrt((dx * dx) + (dy * dy));
                        alpha = Mathf.Clamp01(radius - distance + 0.5f);
                    }

                    pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D Circle(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
            var pixels = new Color32[size * size];
            float center = size / 2f;
            float radius = center - 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - center;
                    float dy = y + 0.5f - center;
                    float distance = Mathf.Sqrt((dx * dx) + (dy * dy));
                    float alpha = Mathf.Clamp01(radius - distance + 0.5f);

                    pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D Solid(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
            var pixels = new Color32[size * size];

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
