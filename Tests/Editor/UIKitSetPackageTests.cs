using System.IO;
using System.IO.Compression;
using System.Text;
using Arvore.UIExporter.Editor;
using NUnit.Framework;

namespace Arvore.UIExporter.Tests
{
    /// <summary>
    /// Um <c>.uikitset</c> chega de fora do projeto como o <c>.uiexport</c>/<c>.uikit</c>: é
    /// entrada não confiável, e a whitelist e os orçamentos de bytes são os mesmos por
    /// construção (ver <see cref="ZipEntryReader"/>). O que estes testes cobrem de específico
    /// do formato de lote é o manifesto ser a única fonte de verdade sobre os componentes, e
    /// cada componente enxergar só as próprias imagens.
    /// </summary>
    public sealed class UIKitSetPackageTests
    {
        private static readonly byte[] PngSignature =
            { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        private string tempDir;

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "uikitset-tests-" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        private string WriteZip(string name, params (string entry, byte[] bytes)[] entries)
        {
            string path = Path.Combine(tempDir, name);

            using (FileStream stream = File.Create(path))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach ((string entry, byte[] bytes) in entries)
                {
                    ZipArchiveEntry created = archive.CreateEntry(entry);
                    using Stream target = created.Open();
                    target.Write(bytes, 0, bytes.Length);
                }
            }

            return path;
        }

        private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

        private static byte[] FakePng(int padding = 16)
        {
            byte[] bytes = new byte[PngSignature.Length + padding];
            PngSignature.CopyTo(bytes, 0);
            return bytes;
        }

        private static byte[] KitJson(string canonicalName)
        {
            return Utf8($"{{\"schemaVersion\":\"1.0.0\",\"kit\":{{\"canonicalName\":\"{canonicalName}\"}}}}");
        }

        private static string Manifest(params (string canonicalName, string path)[] components)
        {
            var builder = new StringBuilder();
            builder.Append("{\"schemaVersion\":\"1.0.0\",\"pluginVersion\":\"1.0.0\",")
                .Append("\"generatedAt\":\"2026-09-15T12:00:00.000Z\",")
                .Append("\"source\":{\"fileKey\":\"k\",\"fileName\":\"f.fig\",\"pageName\":\"Kit\"},")
                .Append("\"components\":[");

            for (int i = 0; i < components.Length; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append(
                    $"{{\"canonicalName\":\"{components[i].canonicalName}\",\"path\":\"{components[i].path}\"}}");
            }

            builder.Append("],\"skipped\":[]}");
            return builder.ToString();
        }

        private static string ManifestWithSkipped(
            (string canonicalName, string path)[] components,
            params (string name, string reason)[] skipped)
        {
            var builder = new StringBuilder();
            builder.Append("{\"schemaVersion\":\"1.0.0\",\"components\":[");

            for (int i = 0; i < components.Length; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append(
                    $"{{\"canonicalName\":\"{components[i].canonicalName}\",\"path\":\"{components[i].path}\"}}");
            }

            builder.Append("],\"skipped\":[");

            for (int i = 0; i < skipped.Length; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append($"{{\"name\":\"{skipped[i].name}\",\"reason\":\"{skipped[i].reason}\"}}");
            }

            builder.Append("]}");
            return builder.ToString();
        }

        [Test]
        public void ReadsTwoComponentsWithImagesScopedPerComponent()
        {
            string manifest = Manifest(
                ("Button/Primary", "components/Button_Primary/kit.json"),
                ("Button/Secondary", "components/Button_Secondary/kit.json"));

            string path = WriteZip(
                "ok.uikitset",
                ("kitset.json", Utf8(manifest)),
                ("components/Button_Primary/kit.json", KitJson("Button/Primary")),
                ("components/Button_Primary/images/x.png", FakePng(padding: 4)),
                ("components/Button_Secondary/kit.json", KitJson("Button/Secondary")),
                ("components/Button_Secondary/images/x.png", FakePng(padding: 40)));

            UIKitSetPackage kitSet = UIKitSetPackage.Read(path);

            Assert.AreEqual(2, kitSet.Components.Count);

            UIKitSetPackage.Component primary = Find(kitSet, "Button/Primary");
            UIKitSetPackage.Component secondary = Find(kitSet, "Button/Secondary");

            Assert.AreEqual(1, primary.Package.ImageCount);
            Assert.AreEqual(1, secondary.Package.ImageCount);

            byte[] primaryImage = primary.Package.TryGetImage("images/x.png");
            byte[] secondaryImage = secondary.Package.TryGetImage("images/x.png");

            Assert.IsNotNull(primaryImage);
            Assert.IsNotNull(secondaryImage);

            // Mesmo nome de arquivo nos dois componentes: se a re-chave por slug vazasse, os
            // dois teriam o mesmo conteúdo (ou um sobrescreveria o outro).
            Assert.AreNotEqual(primaryImage.Length, secondaryImage.Length);
        }

        [Test]
        public void RejectsEntryOutsideWhitelist()
        {
            string manifest = Manifest(("Button/Primary", "components/Button_Primary/kit.json"));

            string path = WriteZip(
                "evil.uikitset",
                ("kitset.json", Utf8(manifest)),
                ("components/Button_Primary/kit.json", KitJson("Button/Primary")),
                ("readme.txt", Utf8("nao deveria estar aqui")));

            var error = Assert.Throws<UIExportException>(() => UIKitSetPackage.Read(path));
            StringAssert.Contains("entrada inesperada", error.Message);
        }

        [Test]
        public void RejectsOversizedImageEntry()
        {
            // 33 MB de zeros comprimem para poucos KB: o pacote parece minúsculo em disco e só
            // se revela ao ser descomprimido, igual ao caso do UIExportPackage. O orçamento por
            // imagem do .uikitset é o mesmo do .uikit avulso — um componente não fica maior só
            // por estar empacotado junto com outros.
            byte[] oversized = Prefixed(new byte[33 * 1024 * 1024]);

            string manifest = Manifest(("Button/Primary", "components/Button_Primary/kit.json"));

            string path = WriteZip(
                "bomb.uikitset",
                ("kitset.json", Utf8(manifest)),
                ("components/Button_Primary/kit.json", KitJson("Button/Primary")),
                ("components/Button_Primary/images/x.png", oversized));

            var error = Assert.Throws<UIExportException>(() => UIKitSetPackage.Read(path));
            StringAssert.Contains("descomprimido", error.Message);
        }

        [Test]
        public void RejectsManifestReferencingPathThatDoesNotExistInTheZip()
        {
            string manifest = Manifest(("Button/Primary", "components/Button_Primary/kit.json"));

            // O zip nunca chega a ter a entrada que o manifesto promete.
            string path = WriteZip("ghost.uikitset", ("kitset.json", Utf8(manifest)));

            var error = Assert.Throws<UIExportException>(() => UIKitSetPackage.Read(path));
            StringAssert.Contains("não contém essa entrada", error.Message);
        }

        [Test]
        public void RejectsComponentFolderNotReferencedByTheManifest()
        {
            string manifest = Manifest(("Button/Primary", "components/Button_Primary/kit.json"));

            // "Button/Secondary" existe no zip mas o manifesto nunca fala dele — dado escondido
            // do único lugar que diz o que o pacote contém.
            string path = WriteZip(
                "orphan.uikitset",
                ("kitset.json", Utf8(manifest)),
                ("components/Button_Primary/kit.json", KitJson("Button/Primary")),
                ("components/Button_Secondary/kit.json", KitJson("Button/Secondary")));

            var error = Assert.Throws<UIExportException>(() => UIKitSetPackage.Read(path));
            StringAssert.Contains("nenhum componente do manifesto", error.Message);
        }

        [Test]
        public void RejectsComponentWhosePathDoesNotMatchItsCanonicalName()
        {
            // "Button/Primary" deveria virar "components/Button_Primary/kit.json"; um path
            // divergente é o manifesto se contradizendo sobre o próprio conteúdo do zip.
            string manifest = Manifest(("Button/Primary", "components/Outro_Nome/kit.json"));

            string path = WriteZip(
                "mismatch.uikitset",
                ("kitset.json", Utf8(manifest)),
                ("components/Outro_Nome/kit.json", KitJson("Button/Primary")));

            var error = Assert.Throws<UIExportException>(() => UIKitSetPackage.Read(path));
            StringAssert.Contains("esperado", error.Message);
        }

        [Test]
        public void SkippedEntriesSurfaceWithoutBlockingValidComponents()
        {
            string manifest = ManifestWithSkipped(
                new[] { ("Button/Primary", "components/Button_Primary/kit.json") },
                ("Frame solto", "sem export settings"));

            string path = WriteZip(
                "withskips.uikitset",
                ("kitset.json", Utf8(manifest)),
                ("components/Button_Primary/kit.json", KitJson("Button/Primary")));

            UIKitSetPackage kitSet = UIKitSetPackage.Read(path);

            Assert.AreEqual(1, kitSet.Components.Count);
            Assert.AreEqual(1, kitSet.Skipped.Count);
            Assert.AreEqual("Frame solto", kitSet.Skipped[0].Name);
            Assert.AreEqual("sem export settings", kitSet.Skipped[0].Reason);
        }

        [Test]
        public void RejectsPackageWithoutManifest()
        {
            string path = WriteZip(
                "nomanifest.uikitset",
                ("components/Button_Primary/kit.json", KitJson("Button/Primary")));

            var error = Assert.Throws<UIExportException>(() => UIKitSetPackage.Read(path));
            StringAssert.Contains("kitset.json", error.Message);
        }

        private static UIKitSetPackage.Component Find(UIKitSetPackage kitSet, string canonicalName)
        {
            foreach (UIKitSetPackage.Component component in kitSet.Components)
            {
                if (component.CanonicalName == canonicalName)
                {
                    return component;
                }
            }

            Assert.Fail($"componente '{canonicalName}' não encontrado no pacote lido.");
            return null;
        }

        private static byte[] Prefixed(byte[] payload)
        {
            byte[] bytes = new byte[PngSignature.Length + payload.Length];
            PngSignature.CopyTo(bytes, 0);
            payload.CopyTo(bytes, PngSignature.Length);
            return bytes;
        }
    }
}
