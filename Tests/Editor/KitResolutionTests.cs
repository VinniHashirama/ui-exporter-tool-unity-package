using System.Collections.Generic;
using System.IO;
using Arvore.UIExporter.Editor;
using NUnit.Framework;
using UnityEditor;
// Alias: UnityEditor tem um PackageInfo legado com o mesmo nome simples.
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;
using UnityEngine.UI;

namespace Arvore.UIExporter.Tests
{
    /// <summary>
    /// Cobre os caminhos em que o import <b>destrói</b> trabalho do dev.
    /// </summary>
    /// <remarks>
    /// São os três pontos mais caros do sistema e os que ficavam sem teste: qual prefab um
    /// nome canônico resolve, o que acontece quando essa resolução é ambígua, e se o dev
    /// consegue ver a destruição antes de autorizá-la.
    /// <para>
    /// A aposta toda do importador é que o trabalho do dev sobrevive ao re-export. Essa
    /// promessa não depende só da reconciliação por <c>FigmaNodeRef</c>: se o prefab do kit
    /// por trás de um nome mudar, <see cref="PrefabBuilder"/> destrói e refaz a instância,
    /// o <c>fileID</c> muda e os overrides do Variant viram órfãos — em silêncio, porque o
    /// node continua existindo no design e a comparação de ids não acusa nada.
    /// </para>
    /// </remarks>
    public sealed class KitResolutionTests
    {
        private const string TestRoot = "Assets/__UIExporterKitTests__";

        private UIImportSettings settings;
        private string packageFile;
        private string basePrefabPath;

        [SetUp]
        public void SetUp()
        {
            AssetFolders.Ensure(TestRoot);
            AssetFolders.Ensure($"{TestRoot}/Custom");

            UIKitGenerator.Result kit = UIKitGenerator.Generate($"{TestRoot}/Kit");
            Assert.IsNotEmpty(kit.Created, "o kit placeholder não foi gerado");

            settings = ScriptableObject.CreateInstance<UIImportSettings>();

            var serialized = new SerializedObject(settings);
            serialized.FindProperty("generatedRoot").stringValue = $"{TestRoot}/Generated";
            serialized.FindProperty("screensRoot").stringValue = $"{TestRoot}/Screens";
            serialized.FindProperty("kitSearchFolders").arraySize = 1;
            serialized.FindProperty("kitSearchFolders").GetArrayElementAtIndex(0).stringValue =
                $"{TestRoot}/Kit";
            serialized.FindProperty("roundedSprite").objectReferenceValue = kit.RoundedSprite;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            packageFile = Path.Combine(
                Path.GetTempPath(),
                "HomeMenu-" + Path.GetRandomFileName() + ".uiexport");
            File.WriteAllBytes(packageFile, LoadFixture());

            basePrefabPath = $"{TestRoot}/Generated/HomeMenu/HomeMenu_Base.prefab";
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
        public void Prepare_RefusesWhenTwoPrefabsClaimTheSameCanonicalName()
        {
            CreateKitPrefab("Button/Primary", $"{TestRoot}/Kit/Impostor.prefab");

            ScreenImporter.Plan plan = ScreenImporter.Prepare(packageFile, settings);

            Assert.IsFalse(
                plan.CanImport,
                "nome ambíguo precisa bloquear o import: qual prefab venceria dependeria da " +
                "ordem da varredura, e trocar de prefab recria as instâncias");
            Assert.IsTrue(plan.Report.HasErrors, "ambiguidade deveria ser erro, não aviso");
            StringAssert.Contains("Button/Primary", plan.Report.ToText());
        }

        [Test]
        public void Prepare_AcceptsWhenMappingTableBreaksTheTie()
        {
            GameObject impostor = CreateKitPrefab("Button/Primary", $"{TestRoot}/Kit/Impostor.prefab");
            UseMappingTable(("Button/Primary", impostor));

            ScreenImporter.Plan plan = ScreenImporter.Prepare(packageFile, settings);

            Assert.IsTrue(
                plan.CanImport,
                "com override explícito não há ambiguidade: " + plan.Report.ToText());
        }

        /// <summary>
        /// O teste que fecha o buraco: mudar de prefab do kit destrói instâncias, e isso
        /// precisa aparecer no diff <b>antes</b> de gravar.
        /// </summary>
        /// <remarks>
        /// Sem isto o caso é invisível. O node continua no IR, então a comparação de ids diz
        /// "preservado", o diálogo de confirmação não dispara, e o dev só descobre que perdeu
        /// os overrides depois que o import já rodou.
        /// </remarks>
        [Test]
        public void Prepare_ReportsRecreationWhenKitResolutionChanges()
        {
            ScreenImporter.Plan first = ScreenImporter.Prepare(packageFile, settings);
            Assert.IsTrue(first.CanImport, first.Report.ToText());
            ScreenImporter.Execute(first);

            Assert.IsNotNull(
                AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath),
                "o primeiro import deveria ter criado o prefab base");

            // Fora da pasta de busca, então não vira um segundo reivindicador: a troca de
            // resolução vem só do override.
            GameObject replacement = CreateKitPrefab(
                "Button/Primary",
                $"{TestRoot}/Custom/OutroBotao.prefab");
            UseMappingTable(("Button/Primary", replacement));

            ScreenImporter.Plan second = ScreenImporter.Prepare(packageFile, settings);

            Assert.IsTrue(second.CanImport, second.Report.ToText());
            Assert.Contains(
                "PlayButton",
                second.Diff.Recreated,
                "PlayButton é a única instância de Button/Primary do sample; trocando o prefab " +
                "por trás do nome, ela é destruída e refeita");
            Assert.IsTrue(
                second.Diff.IsDestructive,
                "recriação perde overrides, então o diff precisa pedir confirmação");
        }

        [Test]
        public void Prepare_DoesNotReportRecreationWhenNothingChanged()
        {
            ScreenImporter.Plan first = ScreenImporter.Prepare(packageFile, settings);
            ScreenImporter.Execute(first);

            ScreenImporter.Plan second = ScreenImporter.Prepare(packageFile, settings);

            Assert.IsEmpty(
                second.Diff.Recreated,
                "re-import do mesmo pacote com o mesmo kit não pode recriar nada");
            Assert.IsFalse(second.Diff.IsDestructive, "re-import idêntico não destrói nada");
        }

        // ------------------------------------------------------------------ helpers

        private static GameObject CreateKitPrefab(string canonicalName, string path)
        {
            var go = new GameObject(
                Path.GetFileNameWithoutExtension(path),
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));

            go.AddComponent<Button>();
            go.AddComponent<UIKitComponent>().Configure(canonicalName, new List<UIKitSlot>());

            try
            {
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"não consegui criar o prefab de apoio em '{path}'");

            return prefab;
        }

        private void UseMappingTable(params (string canonicalName, GameObject prefab)[] entries)
        {
            var table = ScriptableObject.CreateInstance<UIMappingTable>();
            string path = $"{TestRoot}/MappingTable.asset";
            AssetDatabase.CreateAsset(table, path);

            var serializedTable = new SerializedObject(table);
            SerializedProperty list = serializedTable.FindProperty("overrides");
            list.arraySize = entries.Length;

            for (int i = 0; i < entries.Length; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("canonicalName").stringValue = entries[i].canonicalName;
                element.FindPropertyRelative("prefab").objectReferenceValue = entries[i].prefab;
            }

            serializedTable.ApplyModifiedPropertiesWithoutUndo();

            var serializedSettings = new SerializedObject(settings);
            serializedSettings.FindProperty("mappingTable").objectReferenceValue = table;
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static byte[] LoadFixture()
        {
            const string relative = "Samples~/HomeMenu.uiexport";

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
            Assert.IsTrue(File.Exists(embedded), "não consegui localizar o pacote de sample");

            return File.ReadAllBytes(embedded);
        }
    }
}
