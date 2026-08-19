using Arvore.UIExporter.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Arvore.UIExporter.Tests
{
    /// <summary>
    /// O solver é onde a inversão do eixo Y acontece, e um erro de sinal aqui produz telas
    /// espelhadas verticalmente — o tipo de bug que passa despercebido num layout
    /// simétrico e explode num assimétrico. Daí a cobertura por combinação.
    /// </summary>
    public sealed class RectSolverTests
    {
        private const float Tolerance = 0.001f;

        private static IRRect Rect(float x, float y, float width, float height)
        {
            return new IRRect { X = x, Y = y, Width = width, Height = height };
        }

        private static IRConstraints Constraints(ConstraintMode horizontal, ConstraintMode vertical)
        {
            return new IRConstraints { Horizontal = horizontal, Vertical = vertical };
        }

        private static void AssertVector(Vector2 actual, float x, float y, string label)
        {
            Assert.AreEqual(x, actual.x, Tolerance, label + ".x");
            Assert.AreEqual(y, actual.y, Tolerance, label + ".y");
        }

        [Test]
        public void TopLeft_AnchorsToParentTopLeft()
        {
            RectSolution solution = RectSolver.Solve(
                Rect(40f, 120f, 1000f, 200f),
                new Vector2(1080f, 1920f),
                Constraints(ConstraintMode.Min, ConstraintMode.Min));

            // MIN vertical no IR é o topo, que na Unity é anchor.y = 1.
            AssertVector(solution.AnchorMin, 0f, 1f, "anchorMin");
            AssertVector(solution.AnchorMax, 0f, 1f, "anchorMax");
            AssertVector(solution.OffsetMin, 40f, -320f, "offsetMin");
            AssertVector(solution.OffsetMax, 1040f, -120f, "offsetMax");
        }

        [Test]
        public void BottomRight_AnchorsToParentBottomRight()
        {
            RectSolution solution = RectSolver.Solve(
                Rect(880f, 1820f, 160f, 60f),
                new Vector2(1080f, 1920f),
                Constraints(ConstraintMode.Max, ConstraintMode.Max));

            AssertVector(solution.AnchorMin, 1f, 0f, "anchorMin");
            AssertVector(solution.AnchorMax, 1f, 0f, "anchorMax");
            // 1920 - (1820 + 60) = 40 de folga até a base.
            AssertVector(solution.OffsetMin, -200f, 40f, "offsetMin");
            AssertVector(solution.OffsetMax, -40f, 100f, "offsetMax");
        }

        [Test]
        public void Stretch_FillsParentWithZeroOffsets()
        {
            RectSolution solution = RectSolver.Solve(
                Rect(0f, 0f, 1080f, 1920f),
                new Vector2(1080f, 1920f),
                Constraints(ConstraintMode.Stretch, ConstraintMode.Stretch));

            AssertVector(solution.AnchorMin, 0f, 0f, "anchorMin");
            AssertVector(solution.AnchorMax, 1f, 1f, "anchorMax");
            AssertVector(solution.OffsetMin, 0f, 0f, "offsetMin");
            AssertVector(solution.OffsetMax, 0f, 0f, "offsetMax");
        }

        [Test]
        public void Stretch_WithMargins_KeepsMarginsAsOffsets()
        {
            RectSolution solution = RectSolver.Solve(
                Rect(24f, 48f, 1032f, 1824f),
                new Vector2(1080f, 1920f),
                Constraints(ConstraintMode.Stretch, ConstraintMode.Stretch));

            AssertVector(solution.OffsetMin, 24f, 48f, "offsetMin");
            // Margem de cima (48) vira offsetMax negativo; a de baixo (1920-1872=48)
            // vira offsetMin positivo.
            AssertVector(solution.OffsetMax, -24f, -48f, "offsetMax");
        }

        [Test]
        public void Center_CentersOnParent()
        {
            RectSolution solution = RectSolver.Solve(
                Rect(400f, 400f, 200f, 200f),
                new Vector2(1000f, 1000f),
                Constraints(ConstraintMode.Center, ConstraintMode.Center));

            AssertVector(solution.AnchorMin, 0.5f, 0.5f, "anchorMin");
            AssertVector(solution.AnchorMax, 0.5f, 0.5f, "anchorMax");
            AssertVector(solution.OffsetMin, -100f, -100f, "offsetMin");
            AssertVector(solution.OffsetMax, 100f, 100f, "offsetMax");
        }

        [Test]
        public void Scale_EncodesProportionsInAnchorsAndZeroesOffsets()
        {
            RectSolution solution = RectSolver.Solve(
                Rect(100f, 200f, 200f, 300f),
                new Vector2(1000f, 1000f),
                Constraints(ConstraintMode.Scale, ConstraintMode.Scale));

            AssertVector(solution.AnchorMin, 0.1f, 0.5f, "anchorMin");
            AssertVector(solution.AnchorMax, 0.3f, 0.8f, "anchorMax");
            // Toda a informação está nas âncoras — offsets zerados é a assinatura de SCALE.
            AssertVector(solution.OffsetMin, 0f, 0f, "offsetMin");
            AssertVector(solution.OffsetMax, 0f, 0f, "offsetMax");
        }

        [Test]
        public void MixedAxes_StretchHorizontallyAndPinTop()
        {
            RectSolution solution = RectSolver.Solve(
                Rect(0f, 0f, 1080f, 140f),
                new Vector2(1080f, 1920f),
                Constraints(ConstraintMode.Stretch, ConstraintMode.Min));

            AssertVector(solution.AnchorMin, 0f, 1f, "anchorMin");
            AssertVector(solution.AnchorMax, 1f, 1f, "anchorMax");
            AssertVector(solution.OffsetMin, 0f, -140f, "offsetMin");
            AssertVector(solution.OffsetMax, 0f, 0f, "offsetMax");
        }

        [Test]
        public void NullConstraints_FallBackToTopLeft()
        {
            RectSolution solution = RectSolver.Solve(
                Rect(10f, 20f, 100f, 50f),
                new Vector2(500f, 500f),
                null);

            AssertVector(solution.AnchorMin, 0f, 1f, "anchorMin");
            AssertVector(solution.AnchorMax, 0f, 1f, "anchorMax");
        }

        [Test]
        public void Scale_WithZeroSizedParent_DoesNotProduceNaN()
        {
            RectSolution solution = RectSolver.Solve(
                Rect(0f, 0f, 100f, 100f),
                Vector2.zero,
                Constraints(ConstraintMode.Scale, ConstraintMode.Scale));

            Assert.IsFalse(float.IsNaN(solution.AnchorMin.x), "anchorMin.x virou NaN");
            Assert.IsFalse(float.IsNaN(solution.AnchorMin.y), "anchorMin.y virou NaN");
            Assert.IsFalse(float.IsNaN(solution.AnchorMax.x), "anchorMax.x virou NaN");
            Assert.IsFalse(float.IsNaN(solution.AnchorMax.y), "anchorMax.y virou NaN");
        }

        /// <summary>
        /// O teste que realmente importa: depois de aplicado num RectTransform de verdade,
        /// o retângulo resolvido tem que cair exatamente onde o design mandou.
        /// </summary>
        [Test]
        public void ApplyTo_ReproducesTheDesignedBox()
        {
            var parent = new GameObject("Parent", typeof(RectTransform));
            var child = new GameObject("Child", typeof(RectTransform));

            try
            {
                var parentRect = parent.GetComponent<RectTransform>();
                parentRect.sizeDelta = new Vector2(1080f, 1920f);

                var childRect = child.GetComponent<RectTransform>();
                childRect.SetParent(parentRect, worldPositionStays: false);

                IRRect designed = Rect(140f, 620f, 800f, 520f);
                RectSolver
                    .Solve(designed, new Vector2(1080f, 1920f), Constraints(ConstraintMode.Center, ConstraintMode.Center))
                    .ApplyTo(childRect);

                Assert.AreEqual(800f, childRect.rect.width, Tolerance, "largura");
                Assert.AreEqual(520f, childRect.rect.height, Tolerance, "altura");

                // Canto superior-esquerdo do filho, medido do canto superior-esquerdo do
                // pai, tem que bater com o rect do design.
                Vector3[] corners = new Vector3[4];
                childRect.GetWorldCorners(corners);
                Vector3[] parentCorners = new Vector3[4];
                parentRect.GetWorldCorners(parentCorners);

                float offsetX = corners[1].x - parentCorners[1].x;
                float offsetYFromTop = parentCorners[1].y - corners[1].y;

                Assert.AreEqual(designed.X, offsetX, Tolerance, "x relativo ao pai");
                Assert.AreEqual(designed.Y, offsetYFromTop, Tolerance, "y contado do topo");
            }
            finally
            {
                Object.DestroyImmediate(child);
                Object.DestroyImmediate(parent);
            }
        }
    }
}
