using System.IO;
using Arvore.UIExporter.Editor;
using NUnit.Framework;
using TMPro;
using UnityEditor;
// Alias: UnityEditor tem um PackageInfo legado com o mesmo nome simples.
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;
using UnityEngine.UI;

namespace Arvore.UIExporter.Tests
{
    /// <summary>
    /// Import de um componente do kit, ponta a ponta, com o pacote de sample de verdade.
    /// </summary>
    /// <remarks>
    /// É o teste que decide se a autoria no Figma fecha o ciclo: um botão desenhado lá vira um
    /// prefab com a arte, o tamanho, o 9-slice e o comportamento certos aqui — em vez do
    /// placeholder cinza que o kit gerado por código produz.
    /// </remarks>
    public sealed class KitImportTests
    {
        private const string TestRoot = "Assets/__UIExporterKitImportTests__";

        private UIImportSettings settings;
        private string packageFile;
        private string prefabPath;

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
                "ButtonPrimary-" + Path.GetRandomFileName() + ".uikit");
            File.WriteAllBytes(packageFile, LoadFixture());

            prefabPath = $"{TestRoot}/Generated/Kit/Button_Primary.prefab";
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

        private ImportReport Import()
        {
            KitImporter.Plan plan = KitImporter.Prepare(packageFile, settings);
            Assert.IsTrue(plan.CanImport, "Prepare recusou o pacote: " + plan.Report.ToText());
            plan.AdoptExisting = true;
            return KitImporter.Execute(plan);
        }

        private static GameObject Find(GameObject root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (child.name == name)
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        [Test]
        public void Prepare_ReadsWithoutWritingAnything()
        {
            KitImporter.Plan plan = KitImporter.Prepare(packageFile, settings);

            Assert.IsTrue(plan.CanImport, plan.Report.ToText());
            Assert.AreEqual("Button/Primary", plan.CanonicalName);
            Assert.AreEqual(KitRole.Button, plan.Document.Kit.Role);
            Assert.IsNull(
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath),
                "Prepare escreveu o prefab, mas deveria ser somente leitura");
        }

        [Test]
        public void Import_CreatesPrefabWithKitIdentity()
        {
            ImportReport report = Import();

            Assert.IsFalse(report.HasErrors, "import reportou erro: " + report.ToText());

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, "o prefab do componente não foi criado");

            Assert.IsTrue(prefab.TryGetComponent(out UIKitComponent kit), "raiz sem UIKitComponent");
            Assert.AreEqual("Button/Primary", kit.CanonicalName);
        }

        [Test]
        public void Import_WiresSlotsToTheRightObjects()
        {
            Import();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            Assert.IsTrue(prefab.TryGetComponent(out UIKitComponent kit));
            Assert.IsTrue(kit.TryGetSlot("label", out Transform label), "slot 'label' não foi ligado");
            Assert.IsNotNull(
                label.GetComponent<TMP_Text>(),
                "o slot 'label' deveria apontar para o texto");
        }

        /// <summary>
        /// O hazard que nenhum teste de asset pega: o botão existe, parece certo no Inspector
        /// e não recebe clique.
        /// </summary>
        [Test]
        public void Import_ButtonKeepsItsClickableArea()
        {
            Import();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            Assert.IsTrue(prefab.TryGetComponent(out Button button), "o papel 'button' não virou Button");
            Assert.IsNotNull(button.targetGraphic, "Button sem targetGraphic: não há tint de estado");
            Assert.IsTrue(
                button.targetGraphic.raycastTarget,
                "o targetGraphic precisa receber raycast, senão o botão não é clicável");
            Assert.AreEqual(Selectable.Transition.ColorTint, button.transition);
        }

        [Test]
        public void Import_RootIsFixedSizeAtTheDesignSize()
        {
            Import();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var rect = (RectTransform)prefab.transform;

            // Raiz de componente não é esticada: quem a posiciona é a tela onde ela for
            // instanciada. Esticar aqui faria o componente ignorar o próprio tamanho.
            Assert.AreEqual(new Vector2(320f, 96f), rect.sizeDelta);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), rect.anchorMin);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), rect.anchorMax);
        }

        [Test]
        public void Import_AppliesNineSliceSoTheBackgroundDoesNotDistort()
        {
            Import();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            GameObject background = Find(prefab, "bg");
            Assert.IsNotNull(background, "o fundo achatado não veio");

            Assert.IsTrue(background.TryGetComponent(out Image image));
            Assert.IsNotNull(image.sprite, "o sprite do fundo não foi importado");

            // 20px de design a @2x = 40px de textura, na ordem (left, bottom, right, top).
            Assert.AreEqual(new Vector4(40f, 40f, 40f, 40f), image.sprite.border);

            // Borda sem Sliced é borda ignorada: o fundo esticaria inteiro e os cantos
            // deformariam, que é exatamente o que o 9-slice existe para evitar.
            Assert.AreEqual(Image.Type.Sliced, image.type);
        }

        [Test]
        public void Reimport_IsIdempotent()
        {
            Import();
            var first = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            int countBefore = first.GetComponentsInChildren<Transform>(true).Length;

            ImportReport report = Import();

            var second = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            int countAfter = second.GetComponentsInChildren<Transform>(true).Length;

            Assert.IsFalse(report.HasErrors, report.ToText());
            Assert.AreEqual(countBefore, countAfter, "o re-import duplicou objetos");
        }

        /// <summary>
        /// Componente que a ferramenta não gerencia sobrevive ao re-import.
        /// </summary>
        /// <remarks>
        /// É a garantia que sustenta o caso real: o dev pendura som de clique, analytics ou um
        /// script próprio no prefab do kit, e o designer continua re-exportando a arte.
        /// </remarks>
        [Test]
        public void Reimport_KeepsComponentsTheToolDoesNotManage()
        {
            Import();

            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                contents.AddComponent<AudioSource>().volume = 0.25f;
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            Import();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsTrue(
                prefab.TryGetComponent(out AudioSource audio),
                "o componente que o dev adicionou sumiu no re-import");
            Assert.AreEqual(0.25f, audio.volume, 0.001f, "o ajuste do dev foi sobrescrito");
        }

        /// <summary>
        /// Componente que a ferramenta gerencia <b>é</b> revertido — e isso é intencional.
        /// </summary>
        /// <remarks>
        /// Pinar aqui porque é a face desagradável do modelo escolhido, e é melhor que esteja
        /// num teste do que descoberto em produção. Aparência (sprite, cor, tamanho, opacidade,
        /// layout) é território da ferramenta: sem isso, um botão que perdeu a transparência no
        /// Figma continuaria transparente na Unity para sempre. Quem precisa de um visual que o
        /// Figma não dita aponta um prefab próprio pela <see cref="UIMappingTable"/> — a
        /// aparência vem do Figma <i>ou</i> do jogo, nunca das duas na mesma propriedade.
        /// </remarks>
        [Test]
        public void Reimport_RevertsComponentsTheToolManages()
        {
            Import();

            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                contents.AddComponent<CanvasGroup>().alpha = 0.5f;
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            Import();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            Assert.IsFalse(
                prefab.TryGetComponent(out CanvasGroup _),
                "opacidade é território da ferramenta: o componente do design manda, e o " +
                "CanvasGroup só existe quando o design pede opacidade");
        }

        /// <summary>
        /// O sample é 640x192: múltiplo de 4 (o que importa para compressão), mas não potência
        /// de 2. Sob a política padrão ele passa calado; sob POT, é relatado.
        /// </summary>
        [Test]
        public void Import_ReportsSizeOnlyWhenThePolicyIsNotMet()
        {
            ImportReport quiet = Import();

            StringAssert.DoesNotContain(
                "sprite/size-policy",
                quiet.ToText(),
                "múltiplo de 4 atende a política padrão e não deveria gerar ruído");

            var serialized = new SerializedObject(settings);
            serialized.FindProperty("spriteSizePolicy").enumValueIndex =
                (int)SpriteSizePolicy.PowerOfTwo;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            ImportReport strict = Import();

            StringAssert.Contains("sprite/size-policy", strict.ToText());
            Assert.IsFalse(strict.HasErrors, "dimensão é aviso, nunca bloqueia o import");
        }

        [Test]
        public void Prepare_RefusesAScreenPackage()
        {
            string screenPackage = Path.Combine(
                Path.GetTempPath(),
                "HomeMenu-" + Path.GetRandomFileName() + ".uiexport");

            File.WriteAllBytes(screenPackage, LoadFixture("Samples~/HomeMenu.uiexport"));

            try
            {
                KitImporter.Plan plan = KitImporter.Prepare(screenPackage, settings);

                Assert.IsFalse(plan.CanImport, "pacote de tela não pode entrar como componente");
                StringAssert.Contains("tela", plan.Report.ToText());
            }
            finally
            {
                File.Delete(screenPackage);
            }
        }

        /// <summary>
        /// Um prefab feito à mão no mesmo nome não pode ser sobrescrito sem uma decisão
        /// explícita: seria apagar trabalho do dev sem perguntar.
        /// </summary>
        [Test]
        public void Execute_RefusesToOverwriteAnUnmanagedPrefabWithoutConsent()
        {
            AssetFolders.Ensure($"{TestRoot}/Generated/Kit");

            var handmade = new GameObject("Button_Primary", typeof(RectTransform), typeof(Image));
            try
            {
                PrefabUtility.SaveAsPrefabAsset(handmade, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(handmade);
            }

            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceSynchronousImport);

            KitImporter.Plan plan = KitImporter.Prepare(packageFile, settings);

            Assert.IsTrue(plan.NeedsAdoption, "o prefab existente deveria exigir adoção");

            ImportReport report = KitImporter.Execute(plan);

            Assert.IsTrue(report.HasErrors, "sem consentimento, o import não pode gravar");
            StringAssert.Contains("adoção", report.ToText());
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
