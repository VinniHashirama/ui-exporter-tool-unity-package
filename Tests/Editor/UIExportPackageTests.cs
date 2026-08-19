using System.IO;
using System.IO.Compression;
using System.Text;
using Arvore.UIExporter.Editor;
using NUnit.Framework;

namespace Arvore.UIExporter.Tests
{
    /// <summary>
    /// Um <c>.uiexport</c> chega de fora do projeto: é entrada não confiável, e estes
    /// testes existem para provar que ela é tratada como tal. Cada caso aqui é um ataque
    /// conhecido contra leitores de zip.
    /// </summary>
    public sealed class UIExportPackageTests
    {
        private static readonly byte[] PngSignature =
            { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        private string tempDir;

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "uiexport-tests-" + Path.GetRandomFileName());
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

        private static byte[] Utf8(string value)
        {
            return Encoding.UTF8.GetBytes(value);
        }

        private static byte[] FakePng(int padding = 16)
        {
            byte[] bytes = new byte[PngSignature.Length + padding];
            PngSignature.CopyTo(bytes, 0);
            return bytes;
        }

        private const string MinimalJson = "{\"schemaVersion\":\"1.0.0\"}";

        [Test]
        public void ReadsJsonAndImages()
        {
            string path = WriteZip(
                "ok.uiexport",
                ("ui.json", Utf8(MinimalJson)),
                ("images/hero_1-2@2x.png", FakePng()));

            UIExportPackage package = UIExportPackage.Read(path);

            Assert.AreEqual(MinimalJson, package.Json);
            Assert.AreEqual(1, package.ImageCount);
            Assert.IsNotNull(package.TryGetImage("images/hero_1-2@2x.png"));
            Assert.IsNull(package.TryGetImage("images/inexistente.png"));
        }

        [Test]
        public void RejectsPathTraversalEntry()
        {
            string path = WriteZip(
                "evil.uiexport",
                ("ui.json", Utf8(MinimalJson)),
                ("../../../evil.cs", Utf8("class Evil {}")));

            var error = Assert.Throws<UIExportException>(() => UIExportPackage.Read(path));
            StringAssert.Contains("entrada inesperada", error.Message);
        }

        [Test]
        public void RejectsTraversalInsideImagesFolder()
        {
            string path = WriteZip(
                "evil2.uiexport",
                ("ui.json", Utf8(MinimalJson)),
                ("images/../../evil.png", FakePng()));

            Assert.Throws<UIExportException>(() => UIExportPackage.Read(path));
        }

        [Test]
        public void RejectsAbsolutePathEntry()
        {
            string path = WriteZip(
                "evil3.uiexport",
                ("ui.json", Utf8(MinimalJson)),
                ("/etc/passwd", Utf8("root")));

            Assert.Throws<UIExportException>(() => UIExportPackage.Read(path));
        }

        [Test]
        public void RejectsWindowsDrivePathEntry()
        {
            string path = WriteZip(
                "evil4.uiexport",
                ("ui.json", Utf8(MinimalJson)),
                ("C:/Windows/evil.png", FakePng()));

            Assert.Throws<UIExportException>(() => UIExportPackage.Read(path));
        }

        [Test]
        public void RejectsUnexpectedExtension()
        {
            string path = WriteZip(
                "evil5.uiexport",
                ("ui.json", Utf8(MinimalJson)),
                ("images/payload.dll", new byte[] { 1, 2, 3 }));

            Assert.Throws<UIExportException>(() => UIExportPackage.Read(path));
        }

        [Test]
        public void RejectsNestedScriptEvenWithAllowedExtension()
        {
            string path = WriteZip(
                "evil6.uiexport",
                ("ui.json", Utf8(MinimalJson)),
                ("Editor/Malicious.png", FakePng()));

            Assert.Throws<UIExportException>(() => UIExportPackage.Read(path));
        }

        [Test]
        public void RejectsPngWithoutPngSignature()
        {
            string path = WriteZip(
                "fake.uiexport",
                ("ui.json", Utf8(MinimalJson)),
                ("images/notreally.png", Utf8("MZ este e um executavel")));

            var error = Assert.Throws<UIExportException>(() => UIExportPackage.Read(path));
            StringAssert.Contains("não é um PNG", error.Message);
        }

        [Test]
        public void RejectsPackageWithoutJson()
        {
            string path = WriteZip("nojson.uiexport", ("images/a@2x.png", FakePng()));

            var error = Assert.Throws<UIExportException>(() => UIExportPackage.Read(path));
            StringAssert.Contains("ui.json", error.Message);
        }

        [Test]
        public void RejectsNonUtf8Json()
        {
            // 0xFF nunca é byte válido em UTF-8.
            string path = WriteZip("badenc.uiexport", ("ui.json", new byte[] { 0x7B, 0xFF, 0x7D }));

            var error = Assert.Throws<UIExportException>(() => UIExportPackage.Read(path));
            StringAssert.Contains("UTF-8", error.Message);
        }

        [Test]
        public void AcceptsJsonWithUtf8Bom()
        {
            byte[] withBom = new byte[3 + Utf8(MinimalJson).Length];
            withBom[0] = 0xEF;
            withBom[1] = 0xBB;
            withBom[2] = 0xBF;
            Utf8(MinimalJson).CopyTo(withBom, 3);

            string path = WriteZip("bom.uiexport", ("ui.json", withBom));

            UIExportPackage package = UIExportPackage.Read(path);
            Assert.AreEqual(MinimalJson, package.Json);
        }

        [Test]
        public void RejectsZipBomb()
        {
            // 300 MB de zeros comprimem para poucos KB: o pacote parece minúsculo em disco
            // e só se revela ao ser descomprimido. É por isso que o limite tem que ser
            // aplicado contando os bytes que saem do stream, não o tamanho declarado.
            byte[] oneMegabyte = new byte[1024 * 1024];
            var entries = new (string, byte[])[301];
            entries[0] = ("ui.json", Utf8(MinimalJson));
            for (int i = 1; i < entries.Length; i++)
            {
                entries[i] = ($"images/bomb{i}@2x.png", Prefixed(oneMegabyte));
            }

            string path = WriteZip("bomb.uiexport", entries);

            var error = Assert.Throws<UIExportException>(() => UIExportPackage.Read(path));
            StringAssert.Contains("descomprimido", error.Message);
        }

        [Test]
        public void RejectsDuplicateImageEntry()
        {
            string path = WriteZip(
                "dupe.uiexport",
                ("ui.json", Utf8(MinimalJson)),
                ("images/a@2x.png", FakePng()),
                ("images/a@2x.png", FakePng()));

            var error = Assert.Throws<UIExportException>(() => UIExportPackage.Read(path));
            StringAssert.Contains("repetida", error.Message);
        }

        [Test]
        public void RejectsMissingFile()
        {
            var error = Assert.Throws<UIExportException>(
                () => UIExportPackage.Read(Path.Combine(tempDir, "nao-existe.uiexport")));
            StringAssert.Contains("não encontrado", error.Message);
        }

        [Test]
        public void RejectsFileThatIsNotAZip()
        {
            string path = Path.Combine(tempDir, "texto.uiexport");
            File.WriteAllText(path, "isto nao e um zip, e so texto");

            var error = Assert.Throws<UIExportException>(() => UIExportPackage.Read(path));
            StringAssert.Contains("zip", error.Message);
        }

        [Test]
        public void IgnoresDirectoryEntries()
        {
            string path = WriteZip(
                "dirs.uiexport",
                ("images/", new byte[0]),
                ("ui.json", Utf8(MinimalJson)));

            UIExportPackage package = UIExportPackage.Read(path);
            Assert.AreEqual(0, package.ImageCount);
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
