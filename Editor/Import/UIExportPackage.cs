using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Arvore.UIExporter.Editor
{
    /// <summary>
    /// Conteúdo validado de um pacote <c>.uiexport</c>.
    /// </summary>
    /// <remarks>
    /// Um pacote é um arquivo que chegou de fora do projeto, ou seja, <b>dado não
    /// confiável</b>. Esta classe é a fronteira: nada é escrito em disco aqui, e o que sai
    /// dela já passou por validação de nome, de tipo e de tamanho.
    /// <para>
    /// Decisão de projeto que carrega a maior parte da segurança: os nomes de entrada do
    /// zip <b>nunca</b> viram caminho de arquivo. O destino dentro de <c>Assets/</c> é
    /// derivado do <c>asset.id</c> do IR, cujo formato o schema restringe a
    /// <c>[A-Za-z0-9_-]+</c>. Isso tira zip-slip da mesa por construção, em vez de depender
    /// de acertar a normalização de path.
    /// </para>
    /// </remarks>
    public sealed class UIExportPackage
    {
        public const string JsonEntryName = "ui.json";

        /// <summary>Entrada JSON do pacote de componente.</summary>
        public const string KitEntryName = "kit.json";

        private const int MaxEntries = 2048;
        private const long MaxJsonBytes = 32L * 1024 * 1024;
        private const long MaxImageBytes = 32L * 1024 * 1024;
        private const long MaxTotalBytes = 192L * 1024 * 1024;
        private const long MaxArchiveBytes = 256L * 1024 * 1024;

        /// <summary>Whitelist: só estes dois formatos de nome são aceitos.</summary>
        private static readonly Regex ImageEntryPattern =
            new Regex(@"^images/[A-Za-z0-9._@-]+\.png$", RegexOptions.Compiled);

        private readonly Dictionary<string, byte[]> images;

        /// <summary>
        /// Interno em vez de privado: <see cref="UIKitSetPackage"/> monta um
        /// <see cref="UIExportPackage"/> em memória por componente do lote, com o json e as
        /// imagens já extraídos do zip do <c>.uikitset</c> — sem isso ele precisaria duplicar
        /// toda a superfície de <see cref="TryGetImage"/>/<see cref="IsKit"/> em vez de
        /// reusá-la.
        /// </summary>
        internal UIExportPackage(string json, Dictionary<string, byte[]> images, bool isKit)
        {
            Json = json;
            IsKit = isKit;
            this.images = images;
        }

        public string Json { get; }

        /// <summary>
        /// true quando o pacote é de componente (<c>kit.json</c>), false quando é de tela.
        /// </summary>
        /// <remarks>
        /// O discriminador é o nome da entrada, não o conteúdo. Assim um pacote nunca é
        /// importado como a coisa errada por causa de um campo ausente ou inesperado.
        /// </remarks>
        public bool IsKit { get; }

        public IReadOnlyCollection<string> ImagePaths => images.Keys;

        public int ImageCount => images.Count;

        /// <summary>Bytes da imagem referenciada por <c>asset.file</c>, ou null.</summary>
        public byte[] TryGetImage(string entryPath)
        {
            if (string.IsNullOrEmpty(entryPath))
            {
                return null;
            }

            return images.TryGetValue(entryPath, out byte[] bytes) ? bytes : null;
        }

        public static UIExportPackage Read(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new UIExportException("nenhum caminho de pacote informado.");
            }

            var info = new FileInfo(filePath);
            if (!info.Exists)
            {
                throw new UIExportException($"pacote não encontrado: {filePath}");
            }

            if (info.Length == 0)
            {
                throw new UIExportException($"pacote está vazio: {info.Name}");
            }

            if (info.Length > MaxArchiveBytes)
            {
                throw new UIExportException(
                    $"pacote tem {ZipEntryReader.Megabytes(info.Length)} MB, acima do limite de " +
                    $"{ZipEntryReader.Megabytes(MaxArchiveBytes)} MB.");
            }

            try
            {
                using FileStream stream = File.OpenRead(filePath);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
                return ReadArchive(archive, info.Name);
            }
            catch (InvalidDataException error)
            {
                throw new UIExportException(
                    $"{info.Name} não é um zip válido: {error.Message}", error);
            }
        }

        private static UIExportPackage ReadArchive(ZipArchive archive, string packageName)
        {
            if (archive.Entries.Count > MaxEntries)
            {
                throw new UIExportException(
                    $"{packageName} tem {archive.Entries.Count} entradas, acima do limite de " +
                    $"{MaxEntries}.");
            }

            string json = null;
            bool isKit = false;
            var images = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            long budget = MaxTotalBytes;

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string name = entry.FullName;

                // Diretórios não carregam conteúdo e não precisam existir: o destino é
                // criado pelo importador, não espelhado do zip.
                if (name.EndsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }

                if (name == JsonEntryName || name == KitEntryName)
                {
                    // Um pacote é de tela OU de componente. Os dois juntos não é um pacote
                    // ambíguo que dá para salvar escolhendo um: é um pacote de origem
                    // desconhecida.
                    if (json != null)
                    {
                        throw new UIExportException(
                            $"{packageName} tem mais de uma entrada JSON. Um pacote carrega " +
                            $"{JsonEntryName} (tela) ou {KitEntryName} (componente), nunca ambos.");
                    }

                    byte[] jsonBytes = ZipEntryReader.ReadCounted(
                        entry, MaxJsonBytes, ref budget, packageName, MaxTotalBytes);
                    json = ZipEntryReader.DecodeUtf8(jsonBytes, name);
                    isKit = name == KitEntryName;
                    continue;
                }

                if (ImageEntryPattern.IsMatch(name))
                {
                    if (images.ContainsKey(name))
                    {
                        throw new UIExportException(
                            $"{packageName} tem a entrada '{name}' repetida.");
                    }

                    byte[] bytes = ZipEntryReader.ReadCounted(
                        entry, MaxImageBytes, ref budget, packageName, MaxTotalBytes);
                    if (!ZipEntryReader.HasPngSignature(bytes))
                    {
                        throw new UIExportException(
                            $"'{name}' tem extensão .png mas não é um PNG.");
                    }

                    images.Add(name, bytes);
                    continue;
                }

                // Qualquer coisa fora da whitelist derruba o pacote inteiro, em vez de ser
                // ignorada: um pacote com entrada inesperada não é um pacote parcialmente
                // bom, é um pacote que não sabemos de onde veio.
                throw new UIExportException(
                    $"{packageName} tem a entrada inesperada '{ZipEntryReader.Describe(name)}'. " +
                    $"Um pacote válido contém apenas {JsonEntryName} (ou {KitEntryName}) e " +
                    "images/*.png.");
            }

            if (json == null)
            {
                throw new UIExportException(
                    $"{packageName} não contém {JsonEntryName} nem {KitEntryName}.");
            }

            return new UIExportPackage(json, images, isKit);
        }
    }
}
