using UnityEngine;
using UnityEngine.UI;

namespace Arvore.UIExporter.Editor
{
    /// <summary>
    /// Traduz Auto Layout do design para os LayoutGroups do UGUI.
    /// </summary>
    /// <remarks>
    /// Duas coisas moldam este arquivo:
    /// <para>
    /// <b>1. O que o UGUI não tem.</b> <c>SPACE_BETWEEN</c> e <c>WRAP</c> não têm
    /// equivalente nativo. As aproximações estão marcadas e reportadas — fingir suporte
    /// completo produziria telas erradas em silêncio, que é o pior resultado possível.
    /// </para>
    /// <para>
    /// <b>2. Reconciliação.</b> O importador reusa GameObjects entre imports, então não
    /// basta adicionar componentes: é preciso <i>remover</i> os que não pertencem mais. Um
    /// container que deixou de ter Auto Layout no design tem que perder o LayoutGroup aqui,
    /// senão o layout velho continua mandando e ninguém entende por quê.
    /// </para>
    /// </remarks>
    public static class LayoutApplier
    {
        public static void Apply(GameObject target, IRNode node, ImportReport report)
        {
            IRLayout layout = node.Layout;

            if (layout == null)
            {
                ComponentUtil.Remove<HorizontalLayoutGroup>(target);
                ComponentUtil.Remove<VerticalLayoutGroup>(target);
                ComponentUtil.Remove<GridLayoutGroup>(target);
                ComponentUtil.Remove<ContentSizeFitter>(target);
                return;
            }

            switch (layout.Mode)
            {
                case LayoutMode.Horizontal:
                    ComponentUtil.Remove<VerticalLayoutGroup>(target);
                    ComponentUtil.Remove<GridLayoutGroup>(target);
                    ApplyAxisGroup(
                        ComponentUtil.Ensure<HorizontalLayoutGroup>(target),
                        layout,
                        horizontal: true);
                    break;

                case LayoutMode.Vertical:
                    ComponentUtil.Remove<HorizontalLayoutGroup>(target);
                    ComponentUtil.Remove<GridLayoutGroup>(target);
                    ApplyAxisGroup(
                        ComponentUtil.Ensure<VerticalLayoutGroup>(target),
                        layout,
                        horizontal: false);
                    break;

                case LayoutMode.Wrap:
                    ComponentUtil.Remove<HorizontalLayoutGroup>(target);
                    ComponentUtil.Remove<VerticalLayoutGroup>(target);
                    ApplyGrid(ComponentUtil.Ensure<GridLayoutGroup>(target), node, report);
                    break;
            }

            ApplyContentSizeFitter(target, layout);

            if (layout.PrimaryAlign == PrimaryAlign.SpaceBetween)
            {
                report.Warn(
                    "layout/space-between",
                    "Space between não existe em UGUI: o espaço sobrando foi distribuído " +
                    "dentro dos itens, não entre eles. Para resultado exato, use " +
                    "\"Fill container\" em um dos itens no Figma.",
                    node.Name);
            }

            if (layout.CounterAlign == CounterAlign.Baseline)
            {
                report.Info(
                    "layout/baseline",
                    "Alinhamento por baseline foi aproximado para o topo.",
                    node.Name);
            }
        }

        private static void ApplyAxisGroup(HorizontalOrVerticalLayoutGroup group, IRLayout layout, bool horizontal)
        {
            float[] padding = layout.Padding ?? new float[4];

            // IR: [top, right, bottom, left]. RectOffset: (left, right, top, bottom).
            group.padding = new RectOffset(
                Mathf.RoundToInt(padding[3]),
                Mathf.RoundToInt(padding[1]),
                Mathf.RoundToInt(padding[0]),
                Mathf.RoundToInt(padding[2]));

            group.spacing = layout.Spacing;
            group.childAlignment = ResolveAlignment(layout, horizontal);
            group.reverseArrangement = layout.ReverseZIndex;

            // childControl* liga o grupo aos LayoutElement dos filhos, que é como o
            // FIXED/HUG/FILL de cada filho chega até aqui.
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childScaleWidth = false;
            group.childScaleHeight = false;

            // Space between é aproximado forçando os filhos a absorver a sobra no eixo do
            // layout. Não é igual, e o report diz isso.
            bool spaceBetween = layout.PrimaryAlign == PrimaryAlign.SpaceBetween;
            group.childForceExpandWidth = horizontal && spaceBetween;
            group.childForceExpandHeight = !horizontal && spaceBetween;
        }

        private static void ApplyGrid(GridLayoutGroup grid, IRNode node, ImportReport report)
        {
            IRLayout layout = node.Layout;
            float[] padding = layout.Padding ?? new float[4];

            grid.padding = new RectOffset(
                Mathf.RoundToInt(padding[3]),
                Mathf.RoundToInt(padding[1]),
                Mathf.RoundToInt(padding[0]),
                Mathf.RoundToInt(padding[2]));

            grid.spacing = new Vector2(layout.Spacing, layout.Spacing);
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.Flexible;

            // GridLayoutGroup só faz células de tamanho igual. O primeiro filho define a
            // célula, o que acerta grade uniforme e erra tudo o resto.
            IRNode firstChild = node.Children != null && node.Children.Count > 0 ? node.Children[0] : null;
            grid.cellSize = firstChild?.Rect != null
                ? new Vector2(firstChild.Rect.Width, firstChild.Rect.Height)
                : new Vector2(100f, 100f);

            report.Warn(
                "layout/wrap",
                $"Wrap virou uma grade de células {grid.cellSize.x} x {grid.cellSize.y} " +
                "(tamanho do primeiro item). Se os itens têm tamanhos diferentes, o " +
                "resultado não vai bater com o design.",
                node.Name);
        }

        private static void ApplyContentSizeFitter(GameObject target, IRLayout layout)
        {
            IRSizing sizing = layout.Sizing;
            bool hugHorizontal = sizing?.Horizontal == SizingMode.Hug;
            bool hugVertical = sizing?.Vertical == SizingMode.Hug;

            if (!hugHorizontal && !hugVertical)
            {
                ComponentUtil.Remove<ContentSizeFitter>(target);
                return;
            }

            ContentSizeFitter fitter = ComponentUtil.Ensure<ContentSizeFitter>(target);
            fitter.horizontalFit = hugHorizontal
                ? ContentSizeFitter.FitMode.PreferredSize
                : ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = hugVertical
                ? ContentSizeFitter.FitMode.PreferredSize
                : ContentSizeFitter.FitMode.Unconstrained;
        }

        /// <summary>
        /// Como ESTE node se comporta dentro do layout do pai.
        /// </summary>
        /// <remarks>
        /// FILL vira <c>flexible</c> (absorve sobra), FIXED vira <c>preferred</c> com o
        /// tamanho literal, e HUG deixa <c>preferred = -1</c> para o próprio conteúdo do
        /// filho decidir — que é justamente o que HUG significa.
        /// </remarks>
        public static void ApplyChild(GameObject target, IRNode node)
        {
            IRLayoutChild child = node.LayoutChild;

            if (child == null)
            {
                ComponentUtil.Remove<LayoutElement>(target);
                return;
            }

            LayoutElement element = ComponentUtil.Ensure<LayoutElement>(target);
            IRSizing sizing = child.Sizing;

            SizingMode horizontal = sizing?.Horizontal ?? SizingMode.Fixed;
            SizingMode vertical = sizing?.Vertical ?? SizingMode.Fixed;

            element.preferredWidth = horizontal == SizingMode.Fixed ? node.Rect.Width : -1f;
            element.preferredHeight = vertical == SizingMode.Fixed ? node.Rect.Height : -1f;

            element.flexibleWidth = horizontal == SizingMode.Fill ? 1f : 0f;
            element.flexibleHeight = vertical == SizingMode.Fill ? 1f : 0f;

            // layoutGrow do design vira peso extra no eixo primário.
            if (child.Grow > 0f)
            {
                element.flexibleWidth = Mathf.Max(element.flexibleWidth, child.Grow);
                element.flexibleHeight = Mathf.Max(element.flexibleHeight, child.Grow);
            }

            element.minWidth = -1f;
            element.minHeight = -1f;
        }

        private static TextAnchor ResolveAlignment(IRLayout layout, bool horizontal)
        {
            // SPACE_BETWEEN não é um alinhamento; para efeito de âncora vale como MIN.
            PrimaryAlign primary = layout.PrimaryAlign == PrimaryAlign.SpaceBetween
                ? PrimaryAlign.Min
                : layout.PrimaryAlign;

            // BASELINE também não tem equivalente; o topo é a aproximação mais próxima.
            CounterAlign counter = layout.CounterAlign == CounterAlign.Baseline
                ? CounterAlign.Min
                : layout.CounterAlign;

            if (horizontal)
            {
                // Primário = horizontal, cruzado = vertical. MIN no eixo vertical do IR é
                // o topo, porque o Y do design cresce para baixo.
                return Combine(vertical: counter, horizontal: PrimaryToAxis(primary));
            }

            return Combine(vertical: PrimaryToAxis(primary), horizontal: counter);
        }

        private static CounterAlign PrimaryToAxis(PrimaryAlign primary)
        {
            switch (primary)
            {
                case PrimaryAlign.Center:
                    return CounterAlign.Center;
                case PrimaryAlign.Max:
                    return CounterAlign.Max;
                default:
                    return CounterAlign.Min;
            }
        }

        private static TextAnchor Combine(CounterAlign vertical, CounterAlign horizontal)
        {
            switch (vertical)
            {
                case CounterAlign.Center:
                    switch (horizontal)
                    {
                        case CounterAlign.Center:
                            return TextAnchor.MiddleCenter;
                        case CounterAlign.Max:
                            return TextAnchor.MiddleRight;
                        default:
                            return TextAnchor.MiddleLeft;
                    }

                case CounterAlign.Max:
                    switch (horizontal)
                    {
                        case CounterAlign.Center:
                            return TextAnchor.LowerCenter;
                        case CounterAlign.Max:
                            return TextAnchor.LowerRight;
                        default:
                            return TextAnchor.LowerLeft;
                    }

                default:
                    switch (horizontal)
                    {
                        case CounterAlign.Center:
                            return TextAnchor.UpperCenter;
                        case CounterAlign.Max:
                            return TextAnchor.UpperRight;
                        default:
                            return TextAnchor.UpperLeft;
                    }
            }
        }
    }
}
