#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using GourmetProject.Game.UI.Meta;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class TargetArrowViewTests
    {
        private const string PrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Targeting/TargetArrowView.prefab";
        private const string HeadPath =
            "Assets/GameMain/Content/Resources/Sprites/UI/target_arrow_head_v3.png";
        private const string SegmentPath =
            "Assets/GameMain/Content/Resources/Sprites/UI/target_arrow_segment_v3.png";

        [Test]
        public void Prefab_BuildsNineteenNonBlockingSegmentsFromSerializedTemplate()
        {
            GameObject root = InstantiateArrow(out TargetArrowView view);

            try
            {
                Assert.That(view.SegmentTemplate, Is.Not.Null);
                Assert.That(view.SegmentTemplate.gameObject.activeSelf, Is.False);
                Assert.That(view.Head, Is.Not.Null);
                Assert.That(view.Head.raycastTarget, Is.False);
                Assert.That(view.Head.rectTransform.sizeDelta, Is.EqualTo(new Vector2(183f, 203f)));
                Assert.That(view.Head.sprite, Is.Not.Null);

                IReadOnlyList<Image> segments = view.Segments;
                Assert.That(segments.Count, Is.EqualTo(TargetArrowView.SegmentCount));
                Assert.That(segments.All(segment => segment != null), Is.True);
                Assert.That(segments.All(segment => segment.gameObject.activeSelf), Is.True);
                Assert.That(segments.All(segment => !segment.raycastTarget), Is.True);
                Assert.That(
                    segments.All(segment => segment.rectTransform.sizeDelta == new Vector2(148f, 148f)),
                    Is.True);
                Assert.That(segments.Select(segment => segment.name), Is.Unique);
                Assert.That(
                    segments.All(segment => segment.transform.GetSiblingIndex()
                        < view.Head.transform.GetSiblingIndex()),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Geometry_MatchesReferenceBezierSamplingAndControlPointBranch(bool fromBottomHalf)
        {
            Vector2 initial = new Vector2(-720f, fromBottomHalf ? -340f : 340f);
            Vector2 target = new Vector2(280f, 110f);
            var positions = new Vector2[TargetArrowView.SegmentCount];
            var rotations = new float[TargetArrowView.SegmentCount];
            var scales = new float[TargetArrowView.SegmentCount];

            TargetArrowView.CalculateGeometry(
                initial,
                target,
                previousHeadRotationDegrees: 0f,
                referenceScale: 1f,
                fromBottomHalf,
                positions,
                rotations,
                scales,
                out Vector2 headPosition,
                out float headRotation,
                out Vector2 controlPoint,
                out Vector2 finalPosition);

            Vector2 expectedHead = target + Vector2.down * TargetArrowView.HeadTargetOffset;
            Vector2 expectedFinal = target + Vector2.down * TargetArrowView.SegmentEndOffset;
            Vector2 expectedControl = new Vector2(
                initial.x - (expectedHead.x - initial.x) * 0.25f,
                fromBottomHalf
                    ? expectedHead.y + (expectedHead.y - initial.y) * 0.5f
                    : expectedHead.y * 0.75f + initial.y * 0.25f);

            AssertVector(headPosition, expectedHead);
            AssertVector(finalPosition, expectedFinal);
            AssertVector(controlPoint, expectedControl);
            AssertAngle(headRotation, DirectionToUpRotation(target - expectedControl));

            for (int i = 0; i < TargetArrowView.SegmentCount; i++)
            {
                float sampleT = i / 20f;
                Vector2 expectedPosition = QuadraticBezier(
                    initial,
                    expectedFinal,
                    expectedControl,
                    sampleT);
                float expectedScale = Mathf.LerpUnclamped(
                    TargetArrowView.SegmentScaleStart,
                    TargetArrowView.SegmentScaleEnd,
                    i * 2f / TargetArrowView.SegmentCount);
                Vector2 direction = i == 0
                    ? positions[1] - positions[0]
                    : positions[i] - positions[i - 1];

                AssertVector(positions[i], expectedPosition);
                Assert.That(scales[i], Is.EqualTo(expectedScale).Within(0.0001f));
                AssertAngle(rotations[i], DirectionToUpRotation(direction));
            }
        }

        [Test]
        public void Geometry_ScalesReferenceOffsetsAndSpritesWithCanvasHeight()
        {
            const float referenceScale = 1.5f;
            Vector2 initial = new Vector2(-300f, -180f);
            Vector2 target = new Vector2(240f, 200f);
            var positions = new Vector2[TargetArrowView.SegmentCount];
            var rotations = new float[TargetArrowView.SegmentCount];
            var scales = new float[TargetArrowView.SegmentCount];

            TargetArrowView.CalculateGeometry(
                initial,
                target,
                previousHeadRotationDegrees: 0f,
                referenceScale,
                fromBottomHalf: true,
                positions,
                rotations,
                scales,
                out Vector2 headPosition,
                out _,
                out _,
                out Vector2 finalPosition);

            AssertVector(
                headPosition,
                target + Vector2.down * (TargetArrowView.HeadTargetOffset * referenceScale));
            AssertVector(
                finalPosition,
                target + Vector2.down * (TargetArrowView.SegmentEndOffset * referenceScale));
            Assert.That(
                scales[0],
                Is.EqualTo(TargetArrowView.SegmentScaleStart * referenceScale).Within(0.0001f));
        }

        [Test]
        public void Highlighting_TintsWholeArrowAndRestoresHeadScaleImmediately()
        {
            GameObject root = InstantiateArrow(out TargetArrowView view);

            try
            {
                float referenceScale = ((RectTransform)view.transform).rect.height
                    / TargetArrowView.ReferenceHeight;
                Assert.That(view.TargetHighlighted, Is.False);
                Assert.That(view.Head.color, Is.EqualTo(TargetArrowView.DefaultColor));
                Assert.That(
                    view.Head.rectTransform.localScale.x,
                    Is.EqualTo(TargetArrowView.HeadDefaultScale * referenceScale).Within(0.0001f));

                view.SetTargetHighlighted(true);

                Assert.That(view.TargetHighlighted, Is.True);
                Assert.That(view.Head.color, Is.EqualTo(TargetArrowView.HighlightColor));
                Assert.That(view.Segments.All(segment => segment.color == TargetArrowView.HighlightColor), Is.True);
                Assert.That(DOTween.IsTweening(view.Head.rectTransform), Is.True);
                DOTween.Complete(view.Head.rectTransform);
                Assert.That(
                    view.Head.rectTransform.localScale.x,
                    Is.EqualTo(TargetArrowView.HeadHoverScale * referenceScale).Within(0.0001f));

                view.SetTargetHighlighted(false);

                Assert.That(view.TargetHighlighted, Is.False);
                Assert.That(view.Head.color, Is.EqualTo(TargetArrowView.DefaultColor));
                Assert.That(view.Segments.All(segment => segment.color == TargetArrowView.DefaultColor), Is.True);
                Assert.That(DOTween.IsTweening(view.Head.rectTransform), Is.False);
                Assert.That(
                    view.Head.rectTransform.localScale.x,
                    Is.EqualTo(TargetArrowView.HeadDefaultScale * referenceScale).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [TestCase(HeadPath, 183, 203)]
        [TestCase(SegmentPath, 148, 148)]
        public void GeneratedSprite_IsTransparentSingleSpriteWithoutMipmaps(
            string assetPath,
            int expectedWidth,
            int expectedHeight)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

            Assert.That(sprite, Is.Not.Null, $"Missing sprite: {assetPath}");
            Assert.That(importer, Is.Not.Null, $"Missing texture importer: {assetPath}");
            Assert.That(sprite.texture.width, Is.EqualTo(expectedWidth));
            Assert.That(sprite.texture.height, Is.EqualTo(expectedHeight));
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single));
            Assert.That(importer.alphaIsTransparency, Is.True);
            Assert.That(importer.mipmapEnabled, Is.False);
        }

        private static GameObject InstantiateArrow(out TargetArrowView view)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);

            GameObject canvasObject = new GameObject(
                "TargetArrowTestCanvas",
                typeof(RectTransform),
                typeof(Canvas));
            RectTransform canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.sizeDelta = new Vector2(1920f, TargetArrowView.ReferenceHeight);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            GameObject instance = Object.Instantiate(prefab, canvasObject.transform);
            view = instance.GetComponent<TargetArrowView>();
            Assert.That(view, Is.Not.Null);
            view.SetupArrow(new Vector2(240f, 180f));
            Canvas.ForceUpdateCanvases();
            return canvasObject;
        }

        private static Vector2 QuadraticBezier(
            Vector2 initial,
            Vector2 final,
            Vector2 control,
            float t)
        {
            float oneMinusT = 1f - t;
            return oneMinusT * oneMinusT * initial
                + 2f * oneMinusT * t * control
                + t * t * final;
        }

        private static float DirectionToUpRotation(Vector2 direction)
        {
            return Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        }

        private static void AssertVector(Vector2 actual, Vector2 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f));
        }

        private static void AssertAngle(float actual, float expected)
        {
            Assert.That(Mathf.DeltaAngle(actual, expected), Is.Zero.Within(0.001f));
        }
    }
}
#endif
