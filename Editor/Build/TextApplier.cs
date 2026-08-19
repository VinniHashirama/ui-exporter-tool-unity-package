using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Arvore.UIExporter.Editor
{
    /// <summary>
    /// Traduz um node de texto para <see cref="TextMeshProUGUI"/>.
    /// </summary>
    /// <remarks>
    /// <b>Aviso honesto sobre métricas.</b> <c>lineSpacing</c> e <c>characterSpacing</c> do
    /// TMP não estão nas mesmas unidades que entressenha e entreletra do Figma, e a
    /// conversão depende das métricas da fonte concreta. As fórmulas aqui derivam do
    /// <c>faceInfo</c> da fonte e são o ponto de partida correto, <b>não</b> a resposta
    /// final: precisam de calibração visual por família de fonte.
    /// <para>
    /// Além disso a caixa de texto do Figma e a baseline do TMP não coincidem exatamente —
    /// espere ±1–2px de diferença vertical. O alvo é fonte, tamanho, cor e alinhamento
    /// certos, com o texto sem vazar da caixa; perseguir pixel-perfect em texto é dinheiro
    /// jogado fora.
    /// </para>
    /// </remarks>
    public static class TextApplier
    {
        public static void Apply(
            GameObject target,
            IRNode node,
            UIImportSettings settings,
            ImportReport report)
        {
            IRText text = node.Text;

            // Um node de texto não tem fundo próprio: a cor em `text.color` é do glifo.
            ComponentUtil.Remove<Image>(target);

            TextMeshProUGUI label = ComponentUtil.Ensure<TextMeshProUGUI>(target);

            label.text = text.Characters ?? string.Empty;
            label.fontSize = text.Size;
            label.color = ComponentUtil.ParseColor(text.Color, Color.white);
            label.alignment = ResolveAlignment(text.AlignHorizontal, text.AlignVertical);
            label.richText = false;
            label.raycastTarget = false;

            ApplyFont(label, node, settings, report);
            ApplyMetrics(label, text);
            ApplyStyle(label, node, report);
            ApplyOverflow(label, text);
            ApplyAutoSize(target, text);
        }

        private static void ApplyFont(
            TextMeshProUGUI label,
            IRNode node,
            UIImportSettings settings,
            ImportReport report)
        {
            string family = node.Text.Font?.Family;
            string style = node.Text.Font?.Style;

            if (settings.FontMap == null)
            {
                // Sem Font Map o texto depende da fonte default do TMP — que num projeto sem
                // Essential Resources não existe. São dois problemas diferentes e o dev
                // precisa saber qual dos dois tem.
                report.Warn(
                    "text/no-font-map",
                    TmpSupport.TryGetDefaultFont(out _)
                        ? "Nenhum Font Map configurado nas Import Settings: todos os textos " +
                          "ficam com a fonte default do TextMeshPro."
                        : "Nenhum Font Map configurado. " + TmpSupport.MissingResourcesMessage,
                    node.Name);
                return;
            }

            TMP_FontAsset font = settings.FontMap.Resolve(family, style, out FontMatch match);

            if (font != null)
            {
                label.font = font;
            }

            switch (match)
            {
                case FontMatch.FamilyOnly:
                    report.Warn(
                        "text/font-style-missing",
                        $"'{family} {style}' não está no Font Map; usei outro estilo da " +
                        "mesma família. O peso do texto vai sair diferente do design.",
                        node.Name);
                    break;

                case FontMatch.Fallback:
                    report.Warn(
                        "text/font-missing",
                        $"'{family} {style}' não está no Font Map. " +
                        (font != null
                            ? "Usei a fonte de fallback."
                            : "Nenhum fallback configurado: ficou a fonte default do TMP."),
                        node.Name);
                    break;
            }
        }

        /// <summary>Entressenha e entreletra, com as ressalvas do comentário da classe.</summary>
        private static void ApplyMetrics(TextMeshProUGUI label, IRText text)
        {
            label.lineSpacing = ResolveLineSpacing(label, text);
            label.characterSpacing = ResolveCharacterSpacing(text);
        }

        private static float ResolveLineSpacing(TextMeshProUGUI label, IRText text)
        {
            IRLineHeight lineHeight = text.LineHeight;

            // AUTO significa "use a métrica da fonte", que é exatamente lineSpacing = 0.
            if (lineHeight == null || lineHeight.Unit == LineHeightUnit.Auto || text.Size <= 0f)
            {
                return 0f;
            }

            float desiredPixels = lineHeight.Unit == LineHeightUnit.Percent
                ? text.Size * lineHeight.Value / 100f
                : lineHeight.Value;

            float defaultPixels = DefaultLineHeightPixels(label, text.Size);
            if (defaultPixels <= 0f)
            {
                return 0f;
            }

            // lineSpacing do TMP é um ajuste em porcentagem do tamanho da fonte, somado à
            // entressenha natural — daí a diferença, e não o valor absoluto.
            return (desiredPixels - defaultPixels) / text.Size * 100f;
        }

        private static float DefaultLineHeightPixels(TextMeshProUGUI label, float fontSize)
        {
            TMP_FontAsset font = label.font;
            if (font == null || font.faceInfo.pointSize <= 0f)
            {
                return 0f;
            }

            return font.faceInfo.lineHeight * (fontSize / font.faceInfo.pointSize);
        }

        private static float ResolveCharacterSpacing(IRText text)
        {
            IRLetterSpacing spacing = text.LetterSpacing;
            if (spacing == null || spacing.Value == 0f)
            {
                return 0f;
            }

            // characterSpacing do TMP é relativo ao em; entreletra em px precisa virar
            // porcentagem do tamanho da fonte.
            if (spacing.Unit == SpacingUnit.Percent)
            {
                return spacing.Value;
            }

            return text.Size > 0f ? spacing.Value / text.Size * 100f : 0f;
        }

        private static void ApplyStyle(TextMeshProUGUI label, IRNode node, ImportReport report)
        {
            IRText text = node.Text;
            FontStyles style = FontStyles.Normal;

            switch (text.Case)
            {
                case TextCasing.Upper:
                    style |= FontStyles.UpperCase;
                    break;
                case TextCasing.Lower:
                    style |= FontStyles.LowerCase;
                    break;
                case TextCasing.Title:
                    // TMP não tem title case. Manter o texto como está é melhor que
                    // aproximar para maiúscula e mudar a aparência sem aviso.
                    report.Warn(
                        "text/title-case",
                        "Title case não existe em TextMeshPro: o texto ficou como está no " +
                        "design. Escreva o texto já capitalizado no Figma.",
                        node.Name);
                    break;
            }

            switch (text.Decoration)
            {
                case TextDecorationMode.Underline:
                    style |= FontStyles.Underline;
                    break;
                case TextDecorationMode.Strikethrough:
                    style |= FontStyles.Strikethrough;
                    break;
            }

            label.fontStyle = style;
        }

        private static void ApplyOverflow(TextMeshProUGUI label, IRText text)
        {
            label.overflowMode = text.Truncation == TextTruncation.Ellipsis
                ? TextOverflowModes.Ellipsis
                : TextOverflowModes.Overflow;

            if (text.MaxLines.HasValue && text.MaxLines.Value >= 1)
            {
                label.maxVisibleLines = text.MaxLines.Value;
            }
            else
            {
                // 0 não é válido; int.MaxValue é como o TMP representa "sem limite".
                label.maxVisibleLines = int.MaxValue;
            }
        }

        private static void ApplyAutoSize(GameObject target, IRText text)
        {
            if (text.AutoResize == TextAutoResize.None)
            {
                ComponentUtil.Remove<ContentSizeFitter>(target);
                return;
            }

            ContentSizeFitter fitter = ComponentUtil.Ensure<ContentSizeFitter>(target);

            fitter.horizontalFit = text.AutoResize == TextAutoResize.WidthAndHeight
                ? ContentSizeFitter.FitMode.PreferredSize
                : ContentSizeFitter.FitMode.Unconstrained;

            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private static TextAlignmentOptions ResolveAlignment(TextAlignH horizontal, TextAlignV vertical)
        {
            switch (vertical)
            {
                case TextAlignV.Top:
                    switch (horizontal)
                    {
                        case TextAlignH.Center:
                            return TextAlignmentOptions.Top;
                        case TextAlignH.Right:
                            return TextAlignmentOptions.TopRight;
                        case TextAlignH.Justified:
                            return TextAlignmentOptions.TopJustified;
                        default:
                            return TextAlignmentOptions.TopLeft;
                    }

                case TextAlignV.Bottom:
                    switch (horizontal)
                    {
                        case TextAlignH.Center:
                            return TextAlignmentOptions.Bottom;
                        case TextAlignH.Right:
                            return TextAlignmentOptions.BottomRight;
                        case TextAlignH.Justified:
                            return TextAlignmentOptions.BottomJustified;
                        default:
                            return TextAlignmentOptions.BottomLeft;
                    }

                default:
                    switch (horizontal)
                    {
                        case TextAlignH.Center:
                            return TextAlignmentOptions.Center;
                        case TextAlignH.Right:
                            return TextAlignmentOptions.Right;
                        case TextAlignH.Justified:
                            return TextAlignmentOptions.Justified;
                        default:
                            return TextAlignmentOptions.Left;
                    }
            }
        }
    }
}
