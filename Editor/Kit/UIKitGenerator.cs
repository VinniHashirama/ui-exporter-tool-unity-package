using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Arvore.UIExporter.Editor.UIKitFactory;

namespace Arvore.UIExporter.Editor
{
    /// <summary>
    /// Gera o kit placeholder: os 15 componentes canônicos do MVP, funcionais e sem arte.
    /// </summary>
    /// <remarks>
    /// Existe para desacoplar a prova técnica do cronograma de arte. Com o kit placeholder o
    /// pipeline inteiro roda ponta a ponta hoje; quando a arte real chegar, o caminho é trocar
    /// os sprites <b>dentro destes prefabs</b> — nenhuma tela precisa ser re-exportada nem
    /// re-importada, porque as telas referenciam os prefabs por instância aninhada.
    /// <para>
    /// Gerar em código, e não commitar prefabs prontos, é deliberado: YAML de prefab escrito à
    /// mão é frágil e ilegível em review, e um kit gerado é reproduzível e diffável no código
    /// que o produz.
    /// </para>
    /// </remarks>
    public static class UIKitGenerator
    {
        public sealed class Result
        {
            public List<string> Created { get; } = new List<string>();

            public List<string> Skipped { get; } = new List<string>();

            public Sprite RoundedSprite { get; internal set; }
        }

        [MenuItem("Window/Arvore/UI Exporter/Gerar kit placeholder", priority = 20)]
        private static void GenerateFromMenu()
        {
            UIImportSettings settings = UIImportSettings.LoadOrDefault();
            string folder = $"{settings.GeneratedRoot}/Kit";

            bool proceed = EditorUtility.DisplayDialog(
                "Gerar kit placeholder",
                $"Cria os 15 prefabs canônicos em:\n{folder}\n\n" +
                "Prefabs que já existirem não são tocados.",
                "Gerar",
                "Cancelar");

            if (!proceed)
            {
                return;
            }

            Result result = Generate(folder);

            EditorUtility.DisplayDialog(
                "Kit placeholder",
                $"{result.Created.Count} prefab(s) criado(s).\n" +
                $"{result.Skipped.Count} já existia(m) e não foi(ram) alterado(s).\n\n" +
                (result.RoundedSprite != null
                    ? "Aponte 'Rounded Sprite' nas Import Settings para o sprite gerado em " +
                      "Kit/Sprites para que cantos arredondados funcionem."
                    : string.Empty),
                "Ok");

            if (result.Created.Count > 0)
            {
                var first = AssetDatabase.LoadAssetAtPath<GameObject>(result.Created[0]);
                if (first != null)
                {
                    EditorGUIUtility.PingObject(first);
                }
            }
        }

        public static Result Generate(string folder)
        {
            var result = new Result();

            AssetFolders.Ensure(folder);
            string spriteFolder = $"{folder}/Sprites";

            Sprite rounded = PlaceholderSprites.EnsureRounded(spriteFolder);
            Sprite circle = PlaceholderSprites.EnsureCircle(spriteFolder);
            Sprite square = PlaceholderSprites.EnsureSquare(spriteFolder);
            result.RoundedSprite = rounded;

            var recipes = new List<(string canonical, System.Func<Sprite, Sprite, Sprite, GameObject> build)>
            {
                ("Screen", (r, c, s) => BuildScreen()),
                ("Panel", (r, c, s) => BuildPanel(r)),
                ("Window/Modal", (r, c, s) => BuildWindow(r, c)),
                ("ScrollView", (r, c, s) => BuildScrollView(r, s)),
                ("Button/Primary", (r, c, s) => BuildButton(r, s, "Button/Primary", Accent, Ink)),
                ("Button/Secondary", (r, c, s) => BuildButton(r, s, "Button/Secondary", AccentMuted, Ink)),
                ("Button/Icon", (r, c, s) => BuildIconButton(r, s)),
                ("Toggle/Checkbox", (r, c, s) => BuildCheckbox(r, s)),
                ("Slider", (r, c, s) => BuildSlider(r, c)),
                ("InputField", (r, c, s) => BuildInputField(r)),
                ("Label", (r, c, s) => BuildLabel()),
                ("Icon", (r, c, s) => BuildIcon(s)),
                ("Image", (r, c, s) => BuildImage(s)),
                ("ProgressBar", (r, c, s) => BuildProgressBar(r)),
                ("Tabs", (r, c, s) => BuildTabs(r)),
            };

            foreach ((string canonical, System.Func<Sprite, Sprite, Sprite, GameObject> build) in recipes)
            {
                string path = $"{folder}/{FileNameFor(canonical)}.prefab";

                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                {
                    result.Skipped.Add(path);
                    continue;
                }

                GameObject built = build(rounded, circle, square);

                try
                {
                    PrefabUtility.SaveAsPrefabAsset(built, path, out bool success);
                    if (success)
                    {
                        result.Created.Add(path);
                    }
                }
                finally
                {
                    Object.DestroyImmediate(built);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return result;
        }

        /// <summary><c>Button/Primary</c> -&gt; <c>Button_Primary</c>.</summary>
        public static string FileNameFor(string canonicalName)
        {
            return canonicalName.Replace('/', '_');
        }

        // ------------------------------------------------------------------ containers

        private static GameObject BuildScreen()
        {
            GameObject root = Root("Screen", 1080f, 1920f);

            Canvas canvas = ComponentUtil.Ensure<Canvas>(root);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = ComponentUtil.Ensure<CanvasScaler>(root);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            ComponentUtil.Ensure<GraphicRaycaster>(root);

            GameObject safeArea = Child(root, "SafeArea");
            Stretch(safeArea);

            Declare(root, "Screen", ("content", safeArea.transform));
            return root;
        }

        private static GameObject BuildPanel(Sprite rounded)
        {
            GameObject root = Root("Panel", 600f, 400f);
            AddImage(root, rounded, Surface);

            Declare(root, "Panel", ("content", root.transform));
            return root;
        }

        private static GameObject BuildWindow(Sprite rounded, Sprite circle)
        {
            GameObject root = Root("Window_Modal", 800f, 600f);
            AddImage(root, rounded, Surface);
            AddColumn(root, spacing: 0f, paddingX: 0, paddingY: 0, alignment: TextAnchor.UpperCenter);

            GameObject header = Child(root, "Header");
            AddImage(header, rounded, SurfaceRaised);
            AddRow(header, spacing: 12f, paddingX: 24, paddingY: 12, alignment: TextAnchor.MiddleLeft);
            Preferred(header, -1f, 80f);
            Flexible(header);

            GameObject title = Child(header, "Title");
            AddText(title, "Título", 32f, Ink, TextAlignmentOptions.Left);
            Flexible(title);

            GameObject close = Child(header, "Close");
            Image closeImage = AddImage(close, circle, AccentMuted, sliced: false);
            Preferred(close, 48f, 48f);
            Button closeButton = ComponentUtil.Ensure<Button>(close);
            SetupStates(closeButton, closeImage, AccentMuted);

            GameObject closeGlyph = Child(close, "Glyph");
            Stretch(closeGlyph);
            AddText(closeGlyph, "x", 28f, Ink);

            GameObject body = Child(root, "Body");
            AddColumn(body, spacing: 16f, paddingX: 24, paddingY: 24);
            Flexible(body);
            ComponentUtil.Ensure<LayoutElement>(body).flexibleHeight = 1f;

            GameObject footer = Child(root, "Footer");
            AddRow(footer, spacing: 16f, paddingX: 24, paddingY: 16, alignment: TextAnchor.MiddleRight);
            Preferred(footer, -1f, 96f);
            Flexible(footer);

            Declare(
                root,
                "Window/Modal",
                ("title", title.transform),
                ("body", body.transform),
                ("footer", footer.transform),
                ("close", close.transform));

            return root;
        }

        private static GameObject BuildScrollView(Sprite rounded, Sprite square)
        {
            GameObject root = Root("ScrollView", 600f, 800f);
            AddImage(root, rounded, Track);

            ScrollRect scroll = ComponentUtil.Ensure<ScrollRect>(root);
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;

            GameObject viewport = Child(root, "Viewport");
            Stretch(viewport);
            AddImage(viewport, square, new Color(1f, 1f, 1f, 0.004f), sliced: false);
            ComponentUtil.Ensure<RectMask2D>(viewport);

            GameObject content = Child(viewport, "Content");
            RectTransform contentRect = Rect(content);
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;

            AddColumn(content, spacing: 12f, paddingX: 16, paddingY: 16);
            ContentSizeFitter fitter = ComponentUtil.Ensure<ContentSizeFitter>(content);
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = Rect(viewport);
            scroll.content = contentRect;

            Declare(root, "ScrollView", ("content", content.transform));
            return root;
        }

        // --------------------------------------------------------------------- ações

        private static GameObject BuildButton(
            Sprite rounded,
            Sprite square,
            string canonical,
            Color background,
            Color foreground)
        {
            GameObject root = Root(FileNameFor(canonical), 320f, 96f);
            Image image = AddImage(root, rounded, background);
            AddRow(root, spacing: 12f, paddingX: 24, paddingY: 12);

            Button button = ComponentUtil.Ensure<Button>(root);
            SetupStates(button, image, background);

            GameObject iconLeft = Child(root, "IconLeft");
            AddImage(iconLeft, square, foreground, sliced: false).preserveAspect = true;
            Preferred(iconLeft, 40f, 40f);
            iconLeft.SetActive(false);

            GameObject label = Child(root, "Label");
            AddText(label, "Botão", 32f, foreground);
            Flexible(label);

            GameObject iconRight = Child(root, "IconRight");
            AddImage(iconRight, square, foreground, sliced: false).preserveAspect = true;
            Preferred(iconRight, 40f, 40f);
            iconRight.SetActive(false);

            Declare(
                root,
                canonical,
                ("label", label.transform),
                ("iconLeft", iconLeft.transform),
                ("iconRight", iconRight.transform));

            return root;
        }

        private static GameObject BuildIconButton(Sprite rounded, Sprite square)
        {
            GameObject root = Root("Button_Icon", 96f, 96f);
            Image image = AddImage(root, rounded, AccentMuted);

            Button button = ComponentUtil.Ensure<Button>(root);
            SetupStates(button, image, AccentMuted);

            GameObject icon = Child(root, "Icon");
            Stretch(icon, 20f, 20f, 20f, 20f);
            AddImage(icon, square, Ink, sliced: false).preserveAspect = true;

            Declare(root, "Button/Icon", ("icon", icon.transform));
            return root;
        }

        // -------------------------------------------------------------------- entrada

        private static GameObject BuildCheckbox(Sprite rounded, Sprite square)
        {
            GameObject root = Root("Toggle_Checkbox", 320f, 56f);
            AddRow(root, spacing: 16f, paddingX: 0, paddingY: 0, alignment: TextAnchor.MiddleLeft);

            GameObject box = Child(root, "Box");
            Image boxImage = AddImage(box, rounded, Track);
            Preferred(box, 48f, 48f);

            GameObject checkmark = Child(box, "Checkmark");
            Stretch(checkmark, 10f, 10f, 10f, 10f);
            AddImage(checkmark, square, Accent, sliced: false);

            GameObject label = Child(root, "Label");
            AddText(label, "Opção", 28f, Ink, TextAlignmentOptions.Left);
            Flexible(label);

            Toggle toggle = ComponentUtil.Ensure<Toggle>(root);
            toggle.graphic = checkmark.GetComponent<Image>();
            toggle.isOn = true;
            SetupStates(toggle, boxImage, Track);

            Declare(
                root,
                "Toggle/Checkbox",
                ("label", label.transform),
                ("checkmark", checkmark.transform));

            return root;
        }

        private static GameObject BuildSlider(Sprite rounded, Sprite circle)
        {
            GameObject root = Root("Slider", 400f, 48f);

            GameObject background = Child(root, "Background");
            Stretch(background, 0f, 18f, 0f, 18f);
            AddImage(background, rounded, Track);

            GameObject fillArea = Child(root, "Fill Area");
            Stretch(fillArea, 12f, 18f, 12f, 18f);

            GameObject fill = Child(fillArea, "Fill");
            RectTransform fillRect = Rect(fill);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fillRect.sizeDelta = new Vector2(24f, 0f);
            AddImage(fill, rounded, Accent);

            GameObject handleArea = Child(root, "Handle Slide Area");
            Stretch(handleArea, 12f, 0f, 12f, 0f);

            GameObject handle = Child(handleArea, "Handle");
            RectTransform handleRect = Rect(handle);
            handleRect.anchorMin = new Vector2(0f, 0f);
            handleRect.anchorMax = new Vector2(0f, 1f);
            handleRect.sizeDelta = new Vector2(44f, 0f);
            Image handleImage = AddImage(handle, circle, Ink, sliced: false);

            Slider slider = ComponentUtil.Ensure<Slider>(root);
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0.5f;
            SetupStates(slider, handleImage, Ink);

            Declare(root, "Slider", ("fill", fill.transform), ("handle", handle.transform));
            return root;
        }

        private static GameObject BuildInputField(Sprite rounded)
        {
            GameObject root = Root("InputField", 480f, 80f);
            Image background = AddImage(root, rounded, Track);

            GameObject textArea = Child(root, "Text Area");
            Stretch(textArea, 20f, 12f, 20f, 12f);
            ComponentUtil.Ensure<RectMask2D>(textArea);

            GameObject placeholder = Child(textArea, "Placeholder");
            Stretch(placeholder);
            TextMeshProUGUI placeholderText =
                AddText(placeholder, "Digite aqui...", 28f, InkMuted, TextAlignmentOptions.Left);

            GameObject text = Child(textArea, "Text");
            Stretch(text);
            TextMeshProUGUI inputText = AddText(text, string.Empty, 28f, Ink, TextAlignmentOptions.Left);

            TMP_InputField input = ComponentUtil.Ensure<TMP_InputField>(root);
            input.textViewport = Rect(textArea);
            input.textComponent = inputText;
            input.placeholder = placeholderText;
            input.lineType = TMP_InputField.LineType.SingleLine;
            SetupStates(input, background, Track);

            Declare(
                root,
                "InputField",
                ("text", text.transform),
                ("placeholder", placeholder.transform));

            return root;
        }

        // ------------------------------------------------------------------- exibição

        private static GameObject BuildLabel()
        {
            GameObject root = Root("Label", 320f, 48f);
            AddText(root, "Texto", 32f, Ink, TextAlignmentOptions.Left);

            Declare(root, "Label");
            return root;
        }

        private static GameObject BuildIcon(Sprite square)
        {
            GameObject root = Root("Icon", 64f, 64f);
            AddImage(root, square, Ink, sliced: false).preserveAspect = true;

            Declare(root, "Icon");
            return root;
        }

        private static GameObject BuildImage(Sprite square)
        {
            GameObject root = Root("Image", 200f, 200f);
            AddImage(root, square, Color.white, sliced: false);

            Declare(root, "Image");
            return root;
        }

        private static GameObject BuildProgressBar(Sprite rounded)
        {
            GameObject root = Root("ProgressBar", 400f, 32f);
            AddImage(root, rounded, Track);

            GameObject fill = Child(root, "Fill");
            Stretch(fill);
            Image fillImage = AddImage(fill, rounded, Accent);
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillAmount = 0.6f;

            GameObject label = Child(root, "Label");
            Stretch(label);
            AddText(label, "60%", 20f, Ink);
            label.SetActive(false);

            Declare(root, "ProgressBar", ("fill", fill.transform), ("label", label.transform));
            return root;
        }

        // ------------------------------------------------------------------ navegação

        private static GameObject BuildTabs(Sprite rounded)
        {
            GameObject root = Root("Tabs", 600f, 72f);
            AddRow(root, spacing: 8f, paddingX: 0, paddingY: 0);
            ComponentUtil.Ensure<ToggleGroup>(root).allowSwitchOff = false;

            for (int i = 0; i < 2; i++)
            {
                GameObject tab = Child(root, $"Tab{i + 1}");
                Image tabImage = AddImage(tab, rounded, i == 0 ? Accent : AccentMuted);
                Flexible(tab);

                GameObject tabLabel = Child(tab, "Label");
                Stretch(tabLabel);
                AddText(tabLabel, $"Aba {i + 1}", 26f, Ink);

                Toggle toggle = ComponentUtil.Ensure<Toggle>(tab);
                toggle.group = root.GetComponent<ToggleGroup>();
                toggle.isOn = i == 0;
                SetupStates(toggle, tabImage, i == 0 ? Accent : AccentMuted);
            }

            GameObject indicator = Child(root, "Indicator");
            RectTransform indicatorRect = Rect(indicator);
            indicatorRect.anchorMin = new Vector2(0f, 0f);
            indicatorRect.anchorMax = new Vector2(0.5f, 0f);
            indicatorRect.sizeDelta = new Vector2(0f, 4f);
            AddImage(indicator, rounded, Ink);
            ComponentUtil.Ensure<LayoutElement>(indicator).ignoreLayout = true;

            Declare(root, "Tabs", ("tabContainer", root.transform), ("indicator", indicator.transform));
            return root;
        }
    }
}
