using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Arvore.UIExporter.Editor;
using NUnit.Framework;
using UnityEditor;
// Alias: UnityEditor tem um PackageInfo legado com o mesmo nome simples.
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace Arvore.UIExporter.Tests
{
    /// <summary>
    /// Import de um <c>.uikitset</c> com um componente bom e um quebrado, ponta a ponta.
    /// </summary>
    /// <remarks>
    /// O ponto do lote é justamente não deixar um componente ruim travar os outros — este é o
    /// teste que prova isso: o componente quebrado aparece como erro no relatório agregado, e o
    /// bom termina como prefab de verdade no projeto, não só como um <see cref="KitImporter.Plan"/>
    /// marcado como pronto.
    /// </remarks>
    public sealed class KitBatchImporterTests
    {
        private const string TestRoot = "Assets/__UIExporterKitBatchImportTests__";
        private const string GoodCanonicalName = "Button/Primary";
        private const string GoodSlug = "Button_Primary";
        private const string BrokenSlug = "Broken_Component";

        private UIImportSettings settings;
        private string packageFile;

        [SetUp]
        public void SetUp()
        {
            AssetFolders.Ensure(TestRoot);

            settings = ScriptableObject.CreateInstance<UIImportSettings>();

            var serialized = new SerializedObject(settings);
            serialized.FindProperty("generatedRoot").stringValue = $"{TestRoot}/Generated";
            serialized.FindProperty("screensRoot").stringValue = $"{TestRoot}/Screens";
            serialized.FindProperty("kitSearchFolders").arraySize = 1;
            serialized.FindProperty("kitSearchFolders").GetArrayElementAtIndex(0).stringValue =
                $"{TestRoot}/Generated/Kit";
            serialized.ApplyModifiedPropertiesWithoutUndo();

            packageFile = Path.Combine(
                Path.GetTempPath(),
                "Kit-" + Path.GetRandomFileName() + ".uikitset");
            File.WriteAllBytes(packageFile, BuildKitSet());
        }

        [TearDown]
        public void TearDown()
        {
            if (settings != null)
            {
                Object.DestroyImmediate(settings);
            }

            if (packageFile != null && File.Exists(packageFile))
            {
                File.Delete(packageFile);
            }

            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AssetDatabase.DeleteAsset(TestRoot);
            }

            AssetDatabase.Refresh();
        }

        [Test]
        public void Prepare_ReadsOneReadyAndOneBlockedComponent()
        {
            KitBatchImporter.BatchPlan batch = KitBatchImporter.Prepare(packageFile, settings);

            Assert.AreEqual(2, batch.Components.Count, batch.Report.ToText());
            Assert.AreEqual(1, batch.ReadyCount);
            Assert.AreEqual(1, batch.BlockedCount);
            Assert.AreEqual(0, batch.NeedsAdoptionCount);
        }

        [Test]
        public void Prepare_SurfacesSkippedEntriesAsWarningsNotErrors()
        {
            KitBatchImporter.BatchPlan batch = KitBatchImporter.Prepare(packageFile, settings);

            StringAssert.Contains("kitset/skipped", batch.Report.ToText());
            StringAssert.Contains("Frame solto", batch.Report.ToText());
        }

        [Test]
        public void Execute_ImportsTheGoodComponentDespiteTheBrokenOne()
        {
            KitBatchImporter.BatchPlan batch = KitBatchImporter.Prepare(packageFile, settings);
            ImportReport report = KitBatchImporter.Execute(batch);

            Assert.IsTrue(report.HasErrors, "o componente quebrado deveria aparecer como erro");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{TestRoot}/Generated/Kit/{GoodSlug}.prefab");
            Assert.IsNotNull(
                prefab,
                "o componente bom deveria ter sido importado mesmo com o outro quebrado no lote");

            Assert.IsTrue(prefab.TryGetComponent(out UIKitComponent kit));
            Assert.AreEqual(GoodCanonicalName, kit.CanonicalName);
        }

        [Test]
        public void Execute_NeverThrowsWhenOneComponentIsBroken()
        {
            KitBatchImporter.BatchPlan batch = KitBatchImporter.Prepare(packageFile, settings);

            Assert.DoesNotThrow(() => KitBatchImporter.Execute(batch));
        }

        /// <summary>
        /// Monta um <c>.uikitset</c> reaproveitando o sample real (bom) e um componente
        /// deliberadamente quebrado — sem <c>kit.canonicalName</c>, o que faz
        /// <see cref="KitImporter.Prepare(UIExportPackage, string, UIImportSettings)"/> recusar
        /// já na leitura do IR, antes de qualquer escrita.
        /// </summary>
        private static byte[] BuildKitSet()
        {
            byte[] fixtureBytes = LoadFixture();
            string tempFixture = Path.Combine(
                Path.GetTempPath(), "fixture-" + Path.GetRandomFileName() + ".uikit");
            File.WriteAllBytes(tempFixture, fixtureBytes);

            UIExportPackage fixture;
            try
            {
                fixture = UIExportPackage.Read(tempFixture);
            }
            finally
            {
                File.Delete(tempFixture);
            }

            const string needle = "\"canonicalName\": \"Button/Primary\"";
            StringAssert.Contains(
                needle,
                fixture.Json,
                "o sample mudou de formato; ajuste o replace que quebra o componente de propósito");
            string brokenJson = fixture.Json.Replace(needle, "\"canonicalName\": \"\"");

            var entries = new List<(string entry, byte[] bytes)>
            {
                ("kitset.json", Encoding.UTF8.GetBytes(BuildManifestJson())),
                ($"components/{GoodSlug}/kit.json", Encoding.UTF8.GetBytes(fixture.Json)),
                ($"components/{BrokenSlug}/kit.json", Encoding.UTF8.GetBytes(brokenJson)),
            };

            foreach (string imagePath in fixture.ImagePaths)
            {
                entries.Add(($"components/{GoodSlug}/{imagePath}", fixture.TryGetImage(imagePath)));
            }

            string zipPath = Path.Combine(
                Path.GetTempPath(), "kitset-src-" + Path.GetRandomFileName() + ".zip");

            using (FileStream stream = File.Create(zipPath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach ((string entry, byte[] bytes) in entries)
                {
                    ZipArchiveEntry created = archive.CreateEntry(entry);
                    using Stream target = created.Open();
                    target.Write(bytes, 0, bytes.Length);
                }
            }

            try
            {
                return File.ReadAllBytes(zipPath);
            }
            finally
            {
                File.Delete(zipPath);
            }
        }

        private static string BuildManifestJson()
        {
            // Escrito à mão em vez de serializado: o que importa aqui é o formato exato que o
            // plugin do Figma produz, não o que o Newtonsoft escolheria por conta própria.
            return "{"
                + "\"schemaVersion\":\"1.0.0\","
                + "\"pluginVersion\":\"1.0.0\","
                + "\"generatedAt\":\"2026-09-15T12:00:00.000Z\","
                + "\"source\":{\"fileKey\":\"TESTFILEKEY\",\"fileName\":\"Test.fig\",\"pageName\":\"Kit\"},"
                + "\"components\":["
                + $"{{\"canonicalName\":\"{GoodCanonicalName}\",\"path\":\"components/{GoodSlug}/kit.json\"}},"
                + $"{{\"canonicalName\":\"Broken/Component\",\"path\":\"components/{BrokenSlug}/kit.json\"}}"
                + "],"
                + "\"skipped\":[{\"name\":\"Frame solto\",\"reason\":\"sem export settings\"}]"
                + "}";
        }

        private static byte[] LoadFixture(string relative = "Samples~/Button_Primary.uikit")
        {
            PackageInfo package = PackageInfo.FindForAssembly(typeof(ScreenImporter).Assembly);
            if (package != null && !string.IsNullOrEmpty(package.resolvedPath))
            {
                string resolved = Path.Combine(package.resolvedPath, relative);
                if (File.Exists(resolved))
                {
                    return File.ReadAllBytes(resolved);
                }
            }

            string embedded = Path.GetFullPath($"Packages/com.arvore.uiexporter/{relative}");
            Assert.IsTrue(File.Exists(embedded), $"não consegui localizar o sample '{relative}'");

            return File.ReadAllBytes(embedded);
        }
    }
}
