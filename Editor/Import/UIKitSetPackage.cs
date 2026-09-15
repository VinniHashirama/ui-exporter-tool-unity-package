using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Arvore.UIExporter.Editor
{
    /// <summary>
    /// Conteúdo validado de um pacote <c>.uikitset</c> — a exportação de todos os componentes
    /// de uma página do Figma de uma vez, para atualizar o kit inteiro num só import.
    /// </summary>
    /// <remarks>
    /// Mesma fronteira de <see cref="UIExportPackage"/> e a mesma postura: dado que chega de
    /// fora do projeto é dado não confiável, então a whitelist de nomes de entrada, a
    /// contagem de bytes de verdade e a checagem de assinatura PNG são idênticas — só que
    /// aplicadas a um zip que agora carrega N componentes em vez de um. Ver
    /// <see cref="ZipEntryReader"/> para a lógica compartilhada.
    /// <para>
    /// O manifesto (<see cref="ManifestEntryName"/>) é o único ponto de verdade sobre quais
    /// componentes o zip carrega: uma entrada <c>components/&lt;slug&gt;/kit.json</c> que
    /// exista no zip mas não seja referenciada por nenhum componente do manifesto derruba o
    /// pacote inteiro, pelo mesmo motivo que uma entrada fora da whitelist derruba — dado
    /// escondido do manifesto é dado de origem desconhecida.
    /// </para>
    /// </remarks>
    public sealed class UIKitSetPackage
    {
        public const string ManifestEntryName = "kitset.json";

        // O .uikitset carrega ~15 componentes de uma vez, cada um com seu próprio kit.json e
        // pasta de imagens — por isso os orçamentos de "quantas entradas" e "quantos bytes no
        // total" são multiplicados em relação ao UIExportPackage de um componente só. Os
        // orçamentos POR ARQUIVO (um kit.json, uma imagem) ficam iguais: um componente sozinho
        // não fica maior só por vir empacotado junto com outros.
        private const int MaxEntries = 16384;
        private const int MaxComponents = 512;
        private const long MaxManifestBytes = 8L * 1024 * 1024;
        private const long MaxJsonBytes = 32L * 1024 * 1024;
        private const long MaxImageBytes = 32L * 1024 * 1024;
        private const long MaxTotalBytes = 768L * 1024 * 1024;
        private const long MaxArchiveBytes = 512L * 1024 * 1024;

        private static readonly Regex ComponentKitPattern =
            new Regex(@"^components/([A-Za-z0-9_]+)/kit\.json$", RegexOptions.Compiled);

        private static readonly Regex ComponentImagePattern =
            new Regex(@"^components/([A-Za-z0-9_]+)/images/([A-Za-z0-9._@-]+\.png)$", RegexOptions.Compiled);

        private static readonly Regex SlugPattern =
            new Regex(@"^[A-Za-z0-9_]+$", RegexOptions.Compiled);

        private static readonly JsonSerializerSettings ManifestSettings = BuildManifestSettings();

        /// <summary>Um componente do lote, já adaptado para o mesmo formato de um <c>.uikit</c>.</summary>
        public sealed class Component
        {
            internal Component(string canonicalName, string entryPath, UIExportPackage package)
            {
                CanonicalName = canonicalName;
                EntryPath = entryPath;
                Package = package;
            }

            public string CanonicalName { get; }

            /// <summary>Caminho da entrada dentro do zip, ex.: <c>components/Button_Primary/kit.json</c>.</summary>
            public string EntryPath { get; }

            /// <summary>
            /// O <c>kit.json</c> e as imagens deste componente, já re-chaveadas para
            /// <c>images/&lt;arquivo&gt;.png</c> — a mesma convenção de um <c>.uikit</c>
            /// avulso, para que <see cref="KitImporter"/> e <see cref="SpriteImporter"/> não
            /// precisem saber que vieram de um lote.
            /// </summary>
            public UIExportPackage Package { get; }
        }

        /// <summary>Componente que existia na página do Figma mas falhou ao exportar.</summary>
        public readonly struct SkippedComponent
        {
            public SkippedComponent(string name, string reason)
            {
                Name = name;
                Reason = reason;
            }

            public string Name { get; }

            public string Reason { get; }
        }

        private readonly List<Component> components;
        private readonly List<SkippedComponent> skipped;

        private UIKitSetPackage(
            string schemaVersion,
            string pluginVersion,
            string generatedAt,
            string sourceFileKey,
            string sourceFileName,
            string sourcePageName,
            List<Component> components,
            List<SkippedComponent> skipped)
        {
            SchemaVersion = schemaVersion;
            PluginVersion = pluginVersion;
            GeneratedAt = generatedAt;
            SourceFileKey = sourceFileKey;
            SourceFileName = sourceFileName;
            SourcePageName = sourcePageName;
            this.components = components;
            this.skipped = skipped;
        }

        public string SchemaVersion { get; }

        public string PluginVersion { get; }

        public string GeneratedAt { get; }

        public string SourceFileKey { get; }

        public string SourceFileName { get; }

        public string SourcePageName { get; }

        public IReadOnlyList<Component> Components => components;

        public IReadOnlyList<SkippedComponent> Skipped => skipped;

        public static UIKitSetPackage Read(string filePath)
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

        private static UIKitSetPackage ReadArchive(ZipArchive archive, string packageName)
        {
            if (archive.Entries.Count > MaxEntries)
            {
                throw new UIExportException(
                    $"{packageName} tem {archive.Entries.Count} entradas, acima do limite de " +
                    $"{MaxEntries}.");
            }

            string manifestJson = null;
            var kitJsonBySlug = new Dictionary<string, string>(StringComparer.Ordinal);
            var imagesBySlug = new Dictionary<string, Dictionary<string, byte[]>>(StringComparer.Ordinal);
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

                if (name == ManifestEntryName)
                {
                    if (manifestJson != null)
                    {
                        throw new UIExportException(
                            $"{packageName} tem mais de uma entrada {ManifestEntryName}.");
                    }

                    byte[] bytes = ZipEntryReader.ReadCounted(
                        entry, MaxManifestBytes, ref budget, packageName, MaxTotalBytes);
                    manifestJson = ZipEntryReader.DecodeUtf8(bytes, ManifestEntryName);
                    continue;
                }

                Match kitMatch = ComponentKitPattern.Match(name);
                if (kitMatch.Success)
                {
                    string slug = kitMatch.Groups[1].Value;

                    if (kitJsonBySlug.ContainsKey(slug))
                    {
                        throw new UIExportException(
                            $"{packageName} tem a entrada '{name}' repetida.");
                    }

                    byte[] bytes = ZipEntryReader.ReadCounted(
                        entry, MaxJsonBytes, ref budget, packageName, MaxTotalBytes);
                    kitJsonBySlug[slug] = ZipEntryReader.DecodeUtf8(bytes, name);
                    continue;
                }

                Match imageMatch = ComponentImagePattern.Match(name);
                if (imageMatch.Success)
                {
                    string slug = imageMatch.Groups[1].Value;
                    string fileName = imageMatch.Groups[2].Value;
                    string reKeyedPath = "images/" + fileName;

                    if (!imagesBySlug.TryGetValue(slug, out Dictionary<string, byte[]> images))
                    {
                        images = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                        imagesBySlug[slug] = images;
                    }

                    if (images.ContainsKey(reKeyedPath))
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

                    images[reKeyedPath] = bytes;
                    continue;
                }

                // Mesma filosofia do UIExportPackage: uma entrada fora da whitelist derruba o
                // pacote inteiro, em vez de ser ignorada.
                throw new UIExportException(
                    $"{packageName} tem a entrada inesperada '{ZipEntryReader.Describe(name)}'. " +
                    $"Um pacote .uikitset válido contém apenas {ManifestEntryName}, " +
                    "components/<slug>/kit.json e components/<slug>/images/*.png.");
            }

            if (manifestJson == null)
            {
                throw new UIExportException($"{packageName} não contém {ManifestEntryName}.");
            }

            UIKitSetManifest manifest = ParseManifest(manifestJson, packageName);

            if (manifest.Components.Count > MaxComponents)
            {
                throw new UIExportException(
                    $"{packageName} declara {manifest.Components.Count} componentes, acima do " +
                    $"limite de {MaxComponents}.");
            }

            var components = new List<Component>(manifest.Components.Count);
            var referencedSlugs = new HashSet<string>(StringComparer.Ordinal);

            foreach (UIKitSetManifestComponent entry in manifest.Components)
            {
                if (string.IsNullOrWhiteSpace(entry.CanonicalName))
                {
                    throw new UIExportException(
                        $"{packageName} tem um componente no manifesto sem canonicalName.");
                }

                if (string.IsNullOrWhiteSpace(entry.Path))
                {
                    throw new UIExportException(
                        $"'{entry.CanonicalName}' no manifesto de {packageName} está sem path.");
                }

                string expectedSlug = entry.CanonicalName.Replace('/', '_');
                string expectedPath = $"components/{expectedSlug}/kit.json";

                if (!SlugPattern.IsMatch(expectedSlug))
                {
                    throw new UIExportException(
                        $"'{entry.CanonicalName}' no manifesto de {packageName} produz um slug " +
                        $"inválido ('{expectedSlug}').");
                }

                if (!string.Equals(entry.Path, expectedPath, StringComparison.Ordinal))
                {
                    throw new UIExportException(
                        $"'{entry.CanonicalName}' no manifesto de {packageName} declara path " +
                        $"'{entry.Path}', mas o esperado é '{expectedPath}'.");
                }

                if (!kitJsonBySlug.TryGetValue(expectedSlug, out string json))
                {
                    throw new UIExportException(
                        $"o manifesto de {packageName} referencia '{entry.Path}' mas o zip não " +
                        "contém essa entrada.");
                }

                referencedSlugs.Add(expectedSlug);

                imagesBySlug.TryGetValue(expectedSlug, out Dictionary<string, byte[]> images);
                images ??= new Dictionary<string, byte[]>(StringComparer.Ordinal);

                var package = new UIExportPackage(json, images, isKit: true);
                components.Add(new Component(entry.CanonicalName, entry.Path, package));
            }

            // Uma pasta components/<slug>/... que existe no zip mas não é citada por nenhum
            // componente do manifesto é dado escondido do único lugar que diz o que o pacote
            // contém — mesma razão pela qual uma entrada fora da whitelist derruba tudo.
            foreach (string slug in kitJsonBySlug.Keys)
            {
                if (!referencedSlugs.Contains(slug))
                {
                    throw new UIExportException(
                        $"{packageName} tem 'components/{slug}/kit.json' mas nenhum componente " +
                        "do manifesto o referencia.");
                }
            }

            foreach (string slug in imagesBySlug.Keys)
            {
                if (!referencedSlugs.Contains(slug))
                {
                    throw new UIExportException(
                        $"{packageName} tem imagens em 'components/{slug}/' mas nenhum " +
                        "componente do manifesto o referencia.");
                }
            }

            var skipped = new List<SkippedComponent>(manifest.Skipped.Count);
            foreach (UIKitSetManifestSkipped entry in manifest.Skipped)
            {
                skipped.Add(new SkippedComponent(entry.Name ?? string.Empty, entry.Reason ?? string.Empty));
            }

            return new UIKitSetPackage(
                manifest.SchemaVersion,
                manifest.PluginVersion,
                manifest.GeneratedAt,
                manifest.Source?.FileKey,
                manifest.Source?.FileName,
                manifest.Source?.PageName,
                components,
                skipped);
        }

        private static UIKitSetManifest ParseManifest(string json, string packageName)
        {
            UIKitSetManifest manifest;

            try
            {
                manifest = JsonConvert.DeserializeObject<UIKitSetManifest>(json, ManifestSettings);
            }
            catch (JsonException error)
            {
                throw new UIExportException(
                    $"{ManifestEntryName} de {packageName} não é um JSON válido: {error.Message}", error);
            }

            if (manifest == null)
            {
                throw new UIExportException(
                    $"{ManifestEntryName} de {packageName} não produziu nenhum manifesto.");
            }

            manifest.Components ??= new List<UIKitSetManifestComponent>();
            manifest.Skipped ??= new List<UIKitSetManifestSkipped>();

            return manifest;
        }

        private static JsonSerializerSettings BuildManifestSettings()
        {
            return new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy(),
                },

                // Mesma postura de IRReader: o manifesto também é dado não confiável, então
                // campo desconhecido é ignorado (compatibilidade para frente) e nada aqui deixa
                // o JSON escolher que tipo instanciar.
                MissingMemberHandling = MissingMemberHandling.Ignore,
                TypeNameHandling = TypeNameHandling.None,
                MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
                DateParseHandling = DateParseHandling.None,
            };
        }

        // ------------------------------------------------------------------ DTOs do manifesto

        private sealed class UIKitSetManifest
        {
            public string SchemaVersion { get; set; }

            public string PluginVersion { get; set; }

            public string GeneratedAt { get; set; }

            public UIKitSetManifestSource Source { get; set; }

            public List<UIKitSetManifestComponent> Components { get; set; }

            public List<UIKitSetManifestSkipped> Skipped { get; set; }
        }

        private sealed class UIKitSetManifestSource
        {
            public string FileKey { get; set; }

            public string FileName { get; set; }

            public string PageName { get; set; }
        }

        private sealed class UIKitSetManifestComponent
        {
            public string CanonicalName { get; set; }

            public string Path { get; set; }
        }

        private sealed class UIKitSetManifestSkipped
        {
            public string Name { get; set; }

            public string Reason { get; set; }
        }
    }
}
