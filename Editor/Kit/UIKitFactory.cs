using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Arvore.UIExporter.Editor
{
    /// <summary>Helpers para montar hierarquias de UGUI em código, sem repetição.</summary>
    internal static class UIKitFactory
    {
        internal static readonly Color Surface = new Color(0.16f, 0.17f, 0.22f, 1f);
        internal static readonly Color SurfaceRaised = new Color(0.22f, 0.23f, 0.29f, 1f);
        internal static readonly Color Accent = new Color(0.05f, 0.60f, 1f, 1f);
        internal static readonly Color AccentMuted = new Color(0.30f, 0.33f, 0.40f, 1f);
        internal static readonly Color Ink = new Color(0.95f, 0.96f, 0.98f, 1f);
        internal static readonly Color InkMuted = new Color(0.62f, 0.65f, 0.72f, 1f);
        internal static readonly Color Track = new Color(0.12f, 0.13f, 0.17f, 1f);

        internal static GameObject Root(string name, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(width, height);
            return go;
        }

        internal static GameObject Child(GameObject parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            return go;
        }

        internal static RectTransform Rect(GameObject go)
        {
            return (RectTransform)go.transform;
        }

        /// <summary>Estica o objeto no pai, com as margens dadas.</summary>
        internal static RectTransform Stretch(GameObject go, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
        {
            RectTransform rect = Rect(go);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        internal static RectTransform Size(GameObject go, float width, float height)
        {
            RectTransform rect = Rect(go);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        internal static Image AddImage(GameObject go, Sprite sprite, Color color, bool sliced = true)
        {
            Image image = ComponentUtil.Ensure<Image>(go);
            image.sprite = sprite;
            image.color = color;
            image.type = sprite != null && sliced ? Image.Type.Sliced : Image.Type.Simple;
            image.raycastTarget = false;
            return image;
        }

        internal static TextMeshProUGUI AddText(
            GameObject go,
            string content,
            float size,
            Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            TextMeshProUGUI text = ComponentUtil.Ensure<TextMeshProUGUI>(go);
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.richText = false;

            // Num projeto sem TMP Essential Resources não existe fonte default. Deixar sem
            // fonte é melhor que inventar uma; quem reclama é o report do import.
            if (TmpSupport.TryGetDefaultFont(out TMP_FontAsset defaultFont))
            {
                text.font = defaultFont;
            }

            return text;
        }

        internal static HorizontalLayoutGroup AddRow(
            GameObject go,
            float spacing,
            int paddingX,
            int paddingY,
            TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            HorizontalLayoutGroup group = ComponentUtil.Ensure<HorizontalLayoutGroup>(go);
            group.spacing = spacing;
            group.padding = new RectOffset(paddingX, paddingX, paddingY, paddingY);
            group.childAlignment = alignment;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            return group;
        }

        internal static VerticalLayoutGroup AddColumn(
            GameObject go,
            float spacing,
            int paddingX,
            int paddingY,
            TextAnchor alignment = TextAnchor.UpperCenter)
        {
            VerticalLayoutGroup group = ComponentUtil.Ensure<VerticalLayoutGroup>(go);
            group.spacing = spacing;
            group.padding = new RectOffset(paddingX, paddingX, paddingY, paddingY);
            group.childAlignment = alignment;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            return group;
        }

        internal static LayoutElement Flexible(GameObject go, float weight = 1f)
        {
            LayoutElement element = ComponentUtil.Ensure<LayoutElement>(go);
            element.flexibleWidth = weight;
            return element;
        }

        internal static LayoutElement Preferred(GameObject go, float width, float height)
        {
            LayoutElement element = ComponentUtil.Ensure<LayoutElement>(go);
            element.preferredWidth = width;
            element.preferredHeight = height;
            return element;
        }

        /// <summary>Configura as transições de estado de um Selectable via tint de cor.</summary>
        internal static void SetupStates(Selectable selectable, Graphic targetGraphic, Color baseColor)
        {
            selectable.targetGraphic = targetGraphic;
            selectable.transition = Selectable.Transition.ColorTint;

            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = new Color(1.06f, 1.06f, 1.06f, 1f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            colors.fadeDuration = 0.08f;
            selectable.colors = colors;

            if (targetGraphic != null)
            {
                targetGraphic.color = baseColor;
                targetGraphic.raycastTarget = true;
            }
        }

        internal static void Declare(GameObject root, string canonicalName, params (string name, Transform target)[] slots)
        {
            var list = new List<UIKitSlot>(slots.Length);

            foreach ((string name, Transform target) in slots)
            {
                list.Add(new UIKitSlot { name = name, target = target });
            }

            ComponentUtil.Ensure<UIKitComponent>(root).Configure(canonicalName, list);
        }
    }
}
