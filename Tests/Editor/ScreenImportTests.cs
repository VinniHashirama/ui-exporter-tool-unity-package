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
    /// Import ponta a ponta usando o pacote de sample de verdade.
    /// </summary>
    /// <remarks>
    /// É o teste que decide se o MVP funciona. Os outros verificam peças; este verifica a
    /// promessa: um pacote entra, uma tela montada sai, e re-importar não destrói o trabalho
    /// do dev.
    /// <para>
    /// O fixture é o próprio <c>Samples~/HomeMenu.uiexport</c> do pacote — o mesmo arquivo que
    /// quem instala pode abrir na janela de import. Ele é reproduzível byte a byte
    /// (<c>npm run sample</c> no repositório do plugin), então este teste exercita exatamente
    /// o formato que sai do Figma.
    /// </para>
    /// </remarks>
    public sealed class ScreenImportTests
    {
        private const string TestRoot = "Assets/__UIExporterTests__";

        private UIImportSettings settings;
        private string packageFile;
        private string basePrefabPath;
        private string variantPath;

        [SetUp]
        public void SetUp()
        {
            AssetFolders.Ensure(TestRoot);

            UIKitGenerator.Result kit = UIKitGenerator.Generate($"{TestRoot}/Kit");
            Assert.IsNotEmpty(kit.Created, "o kit placeholder não foi gerado");

            settings = ScriptableObject.CreateInstance<UIImportSettings>();

            // SerializedObject em vez de setters públicos: o teste não deve forçar a API de
            // produção a expor o que só ele precisa.
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("generatedRoot").stringValue = $"{TestRoot}/Generated";
            serialized.FindProperty("screensRoot").stringValue = $"{TestRoot}/Screens";
            serialized.FindProperty("kitSearchFolders").arraySize = 1;
            serialized.FindProperty("kitSearchFolders").GetArrayElementAtIndex(0).stringValue =
                $"{TestRoot}/Kit";
            serialized.FindProperty("roundedSprite").objectReferenceValue = kit.RoundedSprite;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            packageFile = Path.Combine(Path.GetTempPath(), "HomeMenu-" + Path.GetRandomFileName() + ".uiexport");
            File.WriteAllBytes(packageFile, LoadFixture());

            basePrefabPath = $"{TestRoot}/Generated/HomeMenu/HomeMenu_Base.prefab";
            variantPath = $"{TestRoot}/Screens/HomeMenu.prefab";
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

        private static byte[] LoadFixture()
        {
            string path = ResolveSamplePath();

            Assert.IsNotNull(
                path,
                "não consegui localizar o pacote de sample. Ele deveria estar em " +
                "Samples~/HomeMenu.uiexport dentro do pacote; regenere com 'npm run sample' " +
                "no repositório do plugin.");
            Assert.IsTrue(File.Exists(path), $"sample não encontrado em disco: {path}");

            return File.ReadAllBytes(path);
        }

        /// <summary>
        /// Resolve o caminho de <c>Samples~/HomeMenu.uiexport</c>.
        /// </summary>
        /// <remarks>
        /// A pasta termina com <c>~</c>, então a Unity não a importa e ela não existe na
        /// AssetDatabase — o caminho precisa ser resolvido no filesystem. <c>PackageInfo</c>
        /// devolve o caminho real tanto para pacote local (<c>file:</c>) quanto para pacote de
        /// git em cache sob <c>Library/PackageCache</c>, que é justamente o caso de quem
        /// instala pela URL.
        /// <para>
        /// Ficar com um arquivo só, em vez de manter uma cópia em <c>Tests/</c>, elimina o
        /// drift entre golden file e sample — e de graça entrega um exemplo navegável para
        /// quem acabou de instalar o pacote.
        /// </para>
        /// </remarks>
        private static string ResolveSamplePath()
        {
            const string relative = "Samples~/HomeMenu.uiexport";

            PackageInfo package = PackageInfo.FindForAssembly(typeof(ScreenImporter).Assembly);
            if (package != null && !string.IsNullOrEmpty(package.resolvedPath))
            {
                string resolved = Path.Combine(package.resolvedPath, relative);
                if (File.Exists(resolved))
                {
                    return resolved;
                }
            }

            // Fallback para o pacote embutido direto em Assets/, onde não há PackageInfo.
            string embedded = Path.GetFullPath($"Packages/com.arvore.uiexporter/{relative}");
            return File.Exists(embedded) ? embedded : null;
        }

        private ImportReport Import()
        {
            ScreenImporter.Plan plan = ScreenImporter.Prepare(packageFile, settings);
            Assert.IsTrue(plan.CanImport, "Prepare recusou o pacote: " + plan.Report.ToText());
            return ScreenImporter.Execute(plan);
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
        public void Prepare_ReadsPackageWithoutWritingAnything()
        {
            ScreenImporter.Plan plan = ScreenImporter.Prepare(packageFile, settings);

            Assert.IsTrue(plan.CanImport);
            Assert.AreEqual("HomeMenu", plan.ScreenName);
            Assert.AreEqual(1080f, plan.Document.Canvas.Width);
            Assert.IsTrue(plan.Diff.IsNewScreen, "a tela deveria ser nova");
            Assert.IsFalse(plan.Diff.IsDestructive, "tela nova não remove nada");

            // A fase de preparo não pode ter criado nada.
            Assert.IsNull(
                AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath),
                "Prepare escreveu o prefab, mas deveria ser somente leitura");
        }

        [Test]
        public void Import_CreatesBasePrefabAndVariant()
        {
            ImportReport report = Import();

            Assert.IsFalse(report.HasErrors, "import reportou erro: " + report.ToText());

            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath);
            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);

            Assert.IsNotNull(basePrefab, "prefab base não foi criado");
            Assert.IsNotNull(variant, "Prefab Variant não foi criado");
            Assert.AreEqual(
                PrefabAssetType.Variant,
                PrefabUtility.GetPrefabAssetType(variant),
                "o asset em Screens/ deveria ser um Prefab Variant do base");
        }

        [Test]
        public void Import_ReproducesHierarchyAndBinds()
        {
            Import();
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath);

            Assert.IsNotNull(Find(basePrefab, "Background"));
            Assert.IsNotNull(Find(basePrefab, "logo"));
            Assert.IsNotNull(Find(basePrefab, "Menu"));
            Assert.IsNotNull(Find(basePrefab, "Hud"));

            Assert.IsTrue(basePrefab.TryGetComponent(out UIViewRefs refs), "raiz sem UIViewRefs");
            Assert.AreEqual(6, refs.Refs.Count, "todos os @binds do sample deveriam estar aqui");
            Assert.AreEqual(new Vector2(1080f, 1920f), refs.DesignResolution);

            Assert.IsNotNull(refs.Find("PlayButton"));
            Assert.IsNotNull(refs.Find("SettingsButton"));
            Assert.IsNotNull(refs.Find("QuitButton"));
            Assert.IsNotNull(refs.Find("TitleLabel"));
            Assert.IsNotNull(refs.Find("XpBar"));
            Assert.IsNotNull(refs.Find("CoinLabel"));
        }

        [Test]
        public void Import_EveryObjectCarriesItsSourceNodeId()
        {
            Import();
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath);

            // Sem FigmaNodeRef em todo objeto, a reconciliação não tem como reencontrá-lo e
            // o modelo base+variant quebra em silêncio.
            foreach (Transform child in basePrefab.GetComponentsInChildren<Transform>(true))
            {
                // Filhos internos dos prefabs do kit pertencem ao kit, não ao export. A
                // subida é manual porque GetComponentInParent ignora objetos inativos, e o
                // kit desativa os slots de ícone que a tela não usa.
                if (IsInsideKitPrefab(child, basePrefab.transform))
                {
                    continue;
                }

                Assert.IsNotNull(
                    child.GetComponent<FigmaNodeRef>(),
                    $"'{child.name}' está sem FigmaNodeRef");
            }
        }

        [Test]
        public void Import_AppliesAutoLayoutToMenu()
        {
            Import();
            GameObject menu = Find(AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath), "Menu");

            Assert.IsNotNull(menu);
            Assert.IsTrue(menu.TryGetComponent(out VerticalLayoutGroup layout), "Menu sem VerticalLayoutGroup");
            Assert.AreEqual(24f, layout.spacing, 0.01f, "spacing");
            Assert.AreEqual(40, layout.padding.left, "padding esquerdo");
            Assert.AreEqual(40, layout.padding.top, "padding superior");
            Assert.AreEqual(TextAnchor.MiddleCenter, layout.childAlignment);

            // sizing.vertical = HUG no sample.
            Assert.IsTrue(menu.TryGetComponent(out ContentSizeFitter fitter), "Menu sem ContentSizeFitter");
            Assert.AreEqual(ContentSizeFitter.FitMode.PreferredSize, fitter.verticalFit);
            Assert.AreEqual(ContentSizeFitter.FitMode.Unconstrained, fitter.horizontalFit);
        }

        [Test]
        public void Import_StretchesBackgroundAcrossTheScreen()
        {
            Import();
            GameObject background =
                Find(AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath), "Background");

            var rect = (RectTransform)background.transform;

            Assert.AreEqual(Vector2.zero, rect.anchorMin, "anchorMin");
            Assert.AreEqual(Vector2.one, rect.anchorMax, "anchorMax");
            Assert.AreEqual(Vector2.zero, rect.offsetMin, "offsetMin");
            Assert.AreEqual(Vector2.zero, rect.offsetMax, "offsetMax");
        }

        [Test]
        public void Import_InstantiatesKitPrefabsAndFillsLabelSlot()
        {
            Import();
            GameObject play = Find(AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath), "PlayButton");

            Assert.IsNotNull(play, "PlayButton não foi criado");

            // Instância aninhada do prefab do kit: é isso que faz melhoria no kit propagar
            // para todas as telas de graça.
            Assert.IsNotNull(
                play.GetComponent<UIKitComponent>(),
                "PlayButton deveria ser uma instância do prefab do kit");
            Assert.IsNotNull(play.GetComponent<Button>(), "o botão do kit traz o componente Button");

            Assert.IsTrue(play.GetComponent<UIKitComponent>().TryGetSlot("label", out Transform labelSlot));
            Assert.AreEqual("Jogar", labelSlot.GetComponent<TMP_Text>().text);
        }

        [Test]
        public void Import_MapsTextNodeToTextMeshPro()
        {
            Import();
            GameObject title = Find(AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath), "TitleLabel");

            Assert.IsNotNull(title);
            Assert.IsTrue(title.TryGetComponent(out TextMeshProUGUI text), "TitleLabel sem TMP");
            Assert.AreEqual("Menu Principal", text.text);
            Assert.AreEqual(56f, text.fontSize, 0.01f);
            // O sample tem alignHorizontal CENTER e alignVertical CENTER, o que em TMP é
            // TextAlignmentOptions.Center (centro nos dois eixos).
            Assert.AreEqual(TextAlignmentOptions.Center, text.alignment);
        }

        [Test]
        public void Import_ImportsFlattenedImageAsSprite()
        {
            Import();
            GameObject logo = Find(AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath), "logo");

            Assert.IsNotNull(logo);
            Assert.IsTrue(logo.TryGetComponent(out Image image), "logo sem Image");
            Assert.IsNotNull(image.sprite, "o sprite do logo não foi importado");
            Assert.AreEqual(800, image.sprite.texture.width, "o PNG do sample é @2x de 400px");
        }

        [Test]
        public void Reimport_IsIdempotent()
        {
            Import();
            var first = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath);
            int countBefore = first.GetComponentsInChildren<Transform>(true).Length;

            ImportReport report = Import();

            var second = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath);
            int countAfter = second.GetComponentsInChildren<Transform>(true).Length;

            Assert.IsFalse(report.HasErrors, report.ToText());
            Assert.AreEqual(countBefore, countAfter, "reimportar duplicou ou perdeu objetos");
        }

        /// <summary>
        /// O teste da promessa central da ferramenta.
        /// </summary>
        /// <remarks>
        /// O dev pendura trabalho no Variant, o designer muda a tela e re-exporta, e o
        /// trabalho continua lá. Se a reconciliação recriasse os GameObjects do base, os
        /// <c>fileID</c> mudariam e este teste falharia — que é exatamente o bug silencioso
        /// que ele existe para impedir.
        /// </remarks>
        [Test]
        public void Reimport_PreservesDevWorkInTheVariant()
        {
            Import();

            const float marker = 0.4242f;

            // O dev adiciona um componente e um override no Variant.
            GameObject variantContents = PrefabUtility.LoadPrefabContents(variantPath);
            try
            {
                GameObject play = Find(variantContents, "PlayButton");
                Assert.IsNotNull(play, "PlayButton não existe no Variant");

                AudioSource added = play.AddComponent<AudioSource>();
                added.volume = marker;
                added.playOnAwake = false;

                GameObject title = Find(variantContents, "TitleLabel");
                title.GetComponent<TextMeshProUGUI>().text = "TEXTO DO DEV";

                PrefabUtility.SaveAsPrefabAsset(variantContents, variantPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(variantContents);
            }

            // O designer mexe na tela: move o painel e renomeia uma layer.
            ScreenImporter.Plan plan = ScreenImporter.Prepare(packageFile, settings);
            Assert.IsTrue(plan.CanImport);

            IRNode menuNode = FindNode(plan.Document.Root, "Menu");
            Assert.IsNotNull(menuNode);
            menuNode.Rect.Y += 40f;
            menuNode.Name = "MenuPrincipal";

            ImportReport report = ScreenImporter.Execute(plan);
            Assert.IsFalse(report.HasErrors, report.ToText());

            // A mudança do designer chegou...
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath);
            Assert.IsNotNull(Find(basePrefab, "MenuPrincipal"), "o rename do designer não chegou ao base");
            Assert.IsNull(Find(basePrefab, "Menu"), "o nome antigo ainda está lá");

            // ...e o trabalho do dev sobreviveu.
            GameObject afterContents = PrefabUtility.LoadPrefabContents(variantPath);
            try
            {
                GameObject play = Find(afterContents, "PlayButton");
                Assert.IsNotNull(play, "PlayButton desapareceu do Variant");

                Assert.IsTrue(
                    play.TryGetComponent(out AudioSource survivor),
                    "o AudioSource que o dev adicionou foi perdido no re-import");
                Assert.AreEqual(marker, survivor.volume, 0.0001f, "o valor configurado pelo dev mudou");

                GameObject title = Find(afterContents, "TitleLabel");
                Assert.AreEqual(
                    "TEXTO DO DEV",
                    title.GetComponent<TextMeshProUGUI>().text,
                    "o override de texto do dev foi sobrescrito pelo base");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(afterContents);
            }
        }

        [Test]
        public void Reimport_ReportsRemovedNodesInTheDiff()
        {
            Import();

            ScreenImporter.Plan plan = ScreenImporter.Prepare(packageFile, settings);

            // O designer apaga o HUD.
            IRNode root = plan.Document.Root;
            IRNode hud = FindNode(root, "Hud");
            Assert.IsNotNull(hud);
            root.Children.Remove(hud);

            ImportDiff diff = ImportDiff.Compute(plan.Document, basePrefabPath);

            Assert.IsTrue(diff.IsDestructive, "apagar o HUD deveria contar como destrutivo");
            Assert.Contains("Hud", diff.Removed, "o HUD deveria aparecer entre os removidos");
            Assert.IsFalse(diff.IsNewScreen);
        }

        /// <summary>
        /// True quando algum ancestral (excluindo a raiz da tela) é a raiz de um prefab do kit.
        /// </summary>
        private static bool IsInsideKitPrefab(Transform child, Transform screenRoot)
        {
            for (Transform current = child.parent; current != null && current != screenRoot; current = current.parent)
            {
                if (current.GetComponent<UIKitComponent>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static IRNode FindNode(IRNode node, string name)
        {
            if (node.Name == name)
            {
                return node;
            }

            if (node.Children == null)
            {
                return null;
            }

            foreach (IRNode child in node.Children)
            {
                IRNode found = FindNode(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
