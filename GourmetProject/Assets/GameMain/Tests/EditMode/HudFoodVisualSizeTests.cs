using GourmetProject.Game.Presentation.Battle;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class HudFoodVisualSizeTests
    {
        [Test]
        public void DefaultFoodCellSize_MatchesFourByFourMaxCell()
        {
            Assert.That(
                DiningTableLayout.DefaultFoodCellSize,
                Is.EqualTo(DiningTableLayout.MaxCellSize));
        }

        [Test]
        public void CanvasPixelsForWorldSize_FallsBackToReferencePixels()
        {
            Vector2 pixels = DiningTableLayout.CanvasPixelsForWorldSize(
                Vector2.one * DiningTableLayout.DefaultFoodCellSize,
                null,
                null);

            Assert.That(
                pixels.x,
                Is.EqualTo(
                        DiningTableLayout.DefaultFoodCellSize
                        * DiningTableLayout.FallbackCanvasPixelsPerWorldUnit)
                    .Within(0.01f));
            Assert.That(pixels.y, Is.EqualTo(pixels.x).Within(0.01f));
        }

        [Test]
        public void DefaultFoodCanvasSize_ScalesWithGrid()
        {
            var cell = new Vector2(120f, 120f);
            Vector2 oneByOne = DiningTableLayout.DefaultFoodCanvasSize(Vector2Int.one, cell);
            Vector2 twoByOne = DiningTableLayout.DefaultFoodCanvasSize(new Vector2Int(2, 1), cell);

            Assert.That(oneByOne, Is.EqualTo(cell));
            Assert.That(twoByOne.x, Is.EqualTo(cell.x * 2f).Within(0.01f));
            Assert.That(twoByOne.y, Is.EqualTo(cell.y).Within(0.01f));
        }

        [Test]
        public void CanvasSizeForTableFood_KeepsSizeWhenSourceMatchesTable()
        {
            Vector2 captured = new Vector2(240f, 240f);
            Vector2 sized = DiningTableLayout.CanvasSizeForTableFood(
                captured,
                DiningTableLayout.DefaultFoodCellSize,
                DiningTableLayout.DefaultFoodCellSize);

            Assert.That(sized, Is.EqualTo(captured));
        }

        [Test]
        public void CanvasSizeForTableFood_ScalesOutletCaptureDownToTableCell()
        {
            Vector2 captured = new Vector2(240f, 240f);
            float tableCell = DiningTableLayout.DefaultFoodCellSize * 0.5f;
            Vector2 sized = DiningTableLayout.CanvasSizeForTableFood(
                captured,
                DiningTableLayout.DefaultFoodCellSize,
                tableCell);

            Assert.That(sized.x, Is.EqualTo(120f).Within(0.01f));
            Assert.That(sized.y, Is.EqualTo(120f).Within(0.01f));
        }

        [Test]
        public void CanvasSizeForTableFood_RemovesDragVisualScale()
        {
            Vector2 captured = new Vector2(276f, 276f);
            Vector2 sized = DiningTableLayout.CanvasSizeForTableFood(
                captured,
                DiningTableLayout.DefaultFoodCellSize,
                DiningTableLayout.DefaultFoodCellSize,
                1.15f);

            Assert.That(sized.x, Is.EqualTo(240f).Within(0.01f));
            Assert.That(sized.y, Is.EqualTo(240f).Within(0.01f));
        }

        [Test]
        public void CapToDefaultFoodCanvasSize_ShrinksOversizedOneByOne()
        {
            var cell = new Vector2(120f, 120f);
            Vector2 capped = DiningTableLayout.CapToDefaultFoodCanvasSize(
                new Vector2(300f, 300f),
                Vector2Int.one,
                cell);

            Assert.That(capped.x, Is.EqualTo(120f).Within(0.01f));
            Assert.That(capped.y, Is.EqualTo(120f).Within(0.01f));
        }

        [Test]
        public void CapToDefaultFoodCanvasSize_AllowsTwoByTwoFourTimesOneByOne()
        {
            var cell = new Vector2(120f, 120f);
            Vector2 twoByTwo = new Vector2(240f, 240f);
            Vector2 capped = DiningTableLayout.CapToDefaultFoodCanvasSize(
                twoByTwo,
                new Vector2Int(2, 2),
                cell);

            Assert.That(capped, Is.EqualTo(twoByTwo));
        }

        [Test]
        public void FitInside_DoesNotEnlargeSmallerFood()
        {
            Vector2 fitted = DiningTableLayout.FitInside(
                new Vector2(120f, 120f),
                new Vector2(420f, 210f));

            Assert.That(fitted, Is.EqualTo(new Vector2(120f, 120f)));
        }

        [Test]
        public void FitInside_ShrinksTwoByTwoToOutletHeight()
        {
            Vector2 fitted = DiningTableLayout.FitInside(
                new Vector2(240f, 240f),
                new Vector2(420f, 210f));

            Assert.That(fitted.x, Is.EqualTo(210f).Within(0.01f));
            Assert.That(fitted.y, Is.EqualTo(210f).Within(0.01f));
        }

        [Test]
        public void CapToDefaultFoodAndFitParent_UsesDefaultMaxThenFrame()
        {
            var cell = new Vector2(120f, 120f);
            Vector2 oversizedOneByOne = DiningTableLayout.CapToDefaultFoodAndFitParent(
                new Vector2(300f, 300f),
                Vector2Int.one,
                cell,
                new Vector2(420f, 210f));
            Vector2 twoByTwoInShortFrame = DiningTableLayout.CapToDefaultFoodAndFitParent(
                new Vector2(240f, 240f),
                new Vector2Int(2, 2),
                cell,
                new Vector2(420f, 210f));

            Assert.That(oversizedOneByOne, Is.EqualTo(new Vector2(120f, 120f)));
            Assert.That(twoByTwoInShortFrame.x, Is.EqualTo(210f).Within(0.01f));
            Assert.That(twoByTwoInShortFrame.y, Is.EqualTo(210f).Within(0.01f));
        }

        [Test]
        public void TemporaryAreaStackLayout_SingleDefaultCell_KeepsFullScale()
        {
            TemporaryAreaStackSlot[] slots = TemporaryAreaStackLayout.Calculate(
                new Rect(0f, 0f, 2.1f, 2.1f),
                new[] { Vector2.one * DiningTableLayout.DefaultFoodCellSize },
                0.92f,
                0.45f);

            Assert.That(slots.Length, Is.EqualTo(1));
            Assert.That(slots[0].Scale, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void TemporaryAreaStackLayout_ScaleNeverExceedsOne()
        {
            TemporaryAreaStackSlot[] slots = TemporaryAreaStackLayout.Calculate(
                new Rect(0f, 0f, 10f, 10f),
                new[] { Vector2.one * DiningTableLayout.DefaultFoodCellSize },
                0.92f,
                0.45f);

            Assert.That(slots[0].Scale, Is.LessThanOrEqualTo(1f));
        }
    }
}