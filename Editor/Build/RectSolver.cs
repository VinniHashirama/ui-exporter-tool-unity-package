using UnityEngine;

namespace Arvore.UIExporter.Editor
{
    /// <summary>Um RectTransform resolvido, pronto para aplicar.</summary>
    public readonly struct RectSolution
    {
        public RectSolution(Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            AnchorMin = anchorMin;
            AnchorMax = anchorMax;
            OffsetMin = offsetMin;
            OffsetMax = offsetMax;
        }

        public Vector2 AnchorMin { get; }

        public Vector2 AnchorMax { get; }

        public Vector2 OffsetMin { get; }

        public Vector2 OffsetMax { get; }

        /// <summary>Preenche o pai inteiro. Usado no node raiz da tela.</summary>
        public static RectSolution FullStretch =>
            new RectSolution(Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        public void ApplyTo(RectTransform target)
        {
            // A ordem importa: escrever anchorMin/anchorMax NÃO preserva a posição
            // resolvida (isso é conveniência do Inspector, não do runtime). Os offsets
            // precisam ser escritos depois, já com as âncoras finais no lugar.
            target.anchorMin = AnchorMin;
            target.anchorMax = AnchorMax;
            target.offsetMin = OffsetMin;
            target.offsetMax = OffsetMax;
        }
    }

    /// <summary>
    /// Converte a caixa e as constraints do IR em âncoras e offsets de RectTransform.
    /// </summary>
    /// <remarks>
    /// Aqui vive o <b>único</b> lugar do importador que inverte o eixo Y. O IR usa a
    /// convenção de ferramenta de design — origem no canto superior-esquerdo, Y crescendo
    /// para baixo — e a Unity usa origem no canto inferior-esquerdo, Y para cima. No eixo
    /// vertical isso troca o significado de MIN e MAX: <c>MIN</c> no IR é a borda de cima,
    /// que na Unity é <c>anchor.y = 1</c>.
    /// <para>
    /// A saída é sempre expressa em <c>offsetMin</c>/<c>offsetMax</c>, nunca em
    /// <c>anchoredPosition</c>+<c>sizeDelta</c>. Os offsets determinam posição e tamanho
    /// independentemente do pivot e funcionam igual para âncora pontual e esticada — o que
    /// dispensa um caso especial por combinação de constraints, inclusive as mistas
    /// (esticar na horizontal e colar no topo, por exemplo).
    /// </para>
    /// <para>
    /// Só se aplica a filho de container <b>sem</b> layout automático. Quando o pai tem
    /// layout, quem posiciona é o LayoutGroup — escrever posição ali cria disputa entre os
    /// dois e o resultado fica instável.
    /// </para>
    /// </remarks>
    public static class RectSolver
    {
        public static RectSolution Solve(IRRect rect, Vector2 parentSize, IRConstraints constraints)
        {
            ConstraintMode horizontal = constraints?.Horizontal ?? ConstraintMode.Min;
            ConstraintMode vertical = constraints?.Vertical ?? ConstraintMode.Min;

            // Distâncias até cada borda do pai. `top` está em espaço de design (do topo
            // para baixo) e `bottom` já em espaço Unity (de baixo para cima).
            float left = rect.X;
            float bottom = parentSize.y - (rect.Y + rect.Height);

            float childLeft = left;
            float childRight = left + rect.Width;
            float childBottom = bottom;
            float childTop = bottom + rect.Height;

            SolveAxis(
                horizontal,
                childLeft,
                childRight,
                parentSize.x,
                out float anchorMinX,
                out float anchorMaxX);

            // O eixo vertical entra invertido: o MIN do IR é a borda de cima.
            SolveAxis(
                FlipVertical(vertical),
                childBottom,
                childTop,
                parentSize.y,
                out float anchorMinY,
                out float anchorMaxY);

            var anchorMin = new Vector2(anchorMinX, anchorMinY);
            var anchorMax = new Vector2(anchorMaxX, anchorMaxY);

            var offsetMin = new Vector2(
                childLeft - (anchorMin.x * parentSize.x),
                childBottom - (anchorMin.y * parentSize.y));

            var offsetMax = new Vector2(
                childRight - (anchorMax.x * parentSize.x),
                childTop - (anchorMax.y * parentSize.y));

            return new RectSolution(anchorMin, anchorMax, offsetMin, offsetMax);
        }

        /// <summary>
        /// No vertical, colar na borda de origem do design (topo) é colar na borda máxima
        /// da Unity, e vice-versa. STRETCH, CENTER e SCALE são simétricos.
        /// </summary>
        private static ConstraintMode FlipVertical(ConstraintMode mode)
        {
            switch (mode)
            {
                case ConstraintMode.Min:
                    return ConstraintMode.Max;
                case ConstraintMode.Max:
                    return ConstraintMode.Min;
                default:
                    return mode;
            }
        }

        /// <summary>
        /// Resolve as âncoras de um eixo. <paramref name="nearEdge"/> e
        /// <paramref name="farEdge"/> já estão em espaço da Unity, medidos da borda de
        /// menor coordenada do pai.
        /// </summary>
        private static void SolveAxis(
            ConstraintMode mode,
            float nearEdge,
            float farEdge,
            float parentExtent,
            out float anchorMin,
            out float anchorMax)
        {
            switch (mode)
            {
                case ConstraintMode.Min:
                    anchorMin = 0f;
                    anchorMax = 0f;
                    return;

                case ConstraintMode.Max:
                    anchorMin = 1f;
                    anchorMax = 1f;
                    return;

                case ConstraintMode.Center:
                    anchorMin = 0.5f;
                    anchorMax = 0.5f;
                    return;

                case ConstraintMode.Stretch:
                    anchorMin = 0f;
                    anchorMax = 1f;
                    return;

                case ConstraintMode.Scale:
                    // SCALE é proporcional ao tamanho do pai; sem tamanho de pai não há
                    // proporção a preservar, então degrada para colar na borda de origem.
                    if (parentExtent <= 0f)
                    {
                        anchorMin = 0f;
                        anchorMax = 0f;
                        return;
                    }

                    anchorMin = nearEdge / parentExtent;
                    anchorMax = farEdge / parentExtent;
                    return;

                default:
                    anchorMin = 0f;
                    anchorMax = 0f;
                    return;
            }
        }
    }
}
