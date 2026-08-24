#if UNITY_EDITOR
using System;
using System.Reflection;
using BreakInfinity;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class DishPieceShadowPlayModeTests
    {
        private const string PiecePrefabPath =
            "Assets/GameMain/Content/Prefabs/Battle/Dishes/DishPiece.prefab";

        private const string DishSpritePath =
            "Assets/GameMain/Content/Resources/Sprites/Dishes/candycane.png";

        private const string CellPrefabPath =
            "Assets/GameMain/Content/Prefabs/Battle/Board/DiningTableCell.prefab";

        private const string BadgePrefabPath =
            "Assets/GameMain/Content/Prefabs/Battle/Dishes/DishValueBadge.prefab";

        private const float CellSize = 1f;
        private const float Pitch = 1.05f;

        [Test]
        public void ContactShadow_ReusesBodySilhouette_ForCommonFootprintsAndRotation()
        {
            AssertFootprintShadow(DishShape.FromRows(new[] { "X" }), 0);
            AssertFootprintShadow(DishShape.FromRows(new[] { "XXX" }), 0);
            AssertFootprintShadow(DishShape.FromRows(new[] { "XX", "XX" }), 0);

            DishShape lShape = DishShape.FromRows(new[] { "X.", "XX" });
            AssertFootprintShadow(lShape, 0);
            AssertFootprintShadow(lShape.RotatedBy(1), 1);
        }

        [Test]
        public void LiftDragAndFlying_KeepShadowsOnGroundLayer_AndRestoreLandingState()
        {
            DishShape shape = DishShape.FromRows(new[] { "X.", "XX", "X." });
            GameObject pieceObject = BuildPiece(shape, 0, out DishPieceView view);

            try
            {
                SpriteRenderer body = FindRenderer(pieceObject, "Sprite");
                SpriteRenderer core = FindRenderer(pieceObject, "Shadow");
                SpriteRenderer halo = FindRenderer(pieceObject, "ShadowHalo");
                Vector3 groundPosition = core.transform.localPosition;
                Vector3 groundScale = core.transform.localScale;

                view.SetLiftHeight(1.5f);

                Assert.That(core.sortingLayerName, Is.EqualTo(BattleSorting.Pieces));
                Assert.That(halo.sortingLayerName, Is.EqualTo(BattleSorting.Pieces));
                Assert.That(core.color.a, Is.EqualTo(0.055f).Within(0.001f));
                Assert.That(halo.color.a, Is.EqualTo(0.08f).Within(0.001f));
                AssertScale(core.transform.localScale, groundScale, 1.08f);
                AssertScale(halo.transform.localScale, groundScale, 1.12f);
                AssertVector(core.transform.localPosition, groundPosition);
                AssertVector(halo.transform.localPosition, groundPosition);

                view.SetFlying(true);

                Assert.That(body.sortingLayerName, Is.EqualTo(BattleSorting.PiecesFlying));
                Assert.That(core.sortingLayerName, Is.EqualTo(BattleSorting.Pieces));
                Assert.That(halo.sortingLayerName, Is.EqualTo(BattleSorting.Pieces));

                view.SetDragPresentation(true);

                Vector3 expectedDragPosition = groundPosition + new Vector3(0.12f, -0.20f, 0f);
                Assert.That(body.sortingLayerName, Is.EqualTo(BattleSorting.PiecesFlying));
                Assert.That(core.sortingLayerName, Is.EqualTo(BattleSorting.Pieces));
                Assert.That(halo.sortingLayerName, Is.EqualTo(BattleSorting.Pieces));
                Assert.That(core.color.a, Is.EqualTo(0.18f).Within(0.001f));
                Assert.That(halo.color.a, Is.EqualTo(0.07f).Within(0.001f));
                AssertVector(core.transform.localPosition, expectedDragPosition);
                AssertVector(halo.transform.localPosition, expectedDragPosition);
                AssertScale(core.transform.localScale, groundScale, 1.06f);
                AssertScale(halo.transform.localScale, groundScale, 1.14f);

                view.SetDragPresentation(false);

                Assert.That(body.sortingLayerName, Is.EqualTo(BattleSorting.Pieces));
                Assert.That(core.sortingLayerName, Is.EqualTo(BattleSorting.Pieces));
                Assert.That(halo.sortingLayerName, Is.EqualTo(BattleSorting.Pieces));
                Assert.That(core.color.a, Is.EqualTo(0.22f).Within(0.001f));
                Assert.That(halo.color.a, Is.Zero.Within(0.001f));
                AssertVector(core.transform.localPosition, groundPosition);
                AssertVector(halo.transform.localPosition, groundPosition);
                AssertVector(core.transform.localScale, groundScale);
                AssertVector(halo.transform.localScale, groundScale);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(pieceObject);
            }
        }

        [Test]
        public void CardPreview_ReusesDishSilhouetteTransform()
        {
            GameObject piecePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PiecePrefabPath);
            GameObject cellPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CellPrefabPath);
            GameObject badgePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BadgePrefabPath);
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(DishSpritePath);
            var definition = new DishDef(
                "preview-shadow-test",
                "Preview Shadow Test",
                10,
                DishShape.FromRows(new[] { "XX", "X.", "X." }),
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty);
            RenderTexture target = null;
            DishIconPreviewRenderer rig = null;

            try
            {
                target = DishIconPreviewRenderer.Render(
                    definition,
                    sprite,
                    new BigDouble(10),
                    Array.Empty<string>(),
                    cellPrefab.GetComponentInChildren<SpriteRenderer>(true),
                    piecePrefab.GetComponent<DishPieceView>(),
                    badgePrefab.GetComponent<DishValueBadgeView>(),
                    64,
                    DishIconPreviewMode.Card,
                    0f,
                    1);
                Assert.That(target, Is.Not.Null);

                DishIconPreviewRenderer[] rigs =
                    Resources.FindObjectsOfTypeAll<DishIconPreviewRenderer>();
                Assert.That(rigs, Has.Length.GreaterThanOrEqualTo(1));
                rig = rigs[rigs.Length - 1];

                SpriteRenderer body = PrivateField<SpriteRenderer>(rig, "_dishRenderer");
                SpriteRenderer shadow = PrivateField<SpriteRenderer>(rig, "_dishShadowRenderer");
                Transform dishRoot = PrivateField<Transform>(rig, "_dishRoot");

                Assert.That(shadow.gameObject.activeSelf, Is.True);
                Assert.That(shadow.sprite, Is.SameAs(body.sprite));
                Assert.That(Quaternion.Angle(shadow.transform.localRotation, dishRoot.localRotation),
                    Is.LessThan(0.01f));
                AssertVector(
                    shadow.transform.localScale,
                    Vector3.Scale(dishRoot.localScale, new Vector3(1.004f, 1.004f, 1f)));
                AssertVector(
                    shadow.transform.localPosition,
                    new Vector3(0.007f, -0.014f, 0.05f));
                Assert.That(shadow.color.a, Is.EqualTo(0.22f).Within(0.001f));
                Assert.That(shadow.sortingLayerName, Is.EqualTo(BattleSorting.Pieces));
            }
            finally
            {
                if (target != null)
                {
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }

                if (rig != null)
                {
                    UnityEngine.Object.DestroyImmediate(rig.gameObject);
                }
            }
        }

        private static void AssertFootprintShadow(DishShape orientation, int rotationIndex)
        {
            GameObject pieceObject = BuildPiece(orientation, rotationIndex, out _);

            try
            {
                SpriteRenderer body = FindRenderer(pieceObject, "Sprite");
                SpriteRenderer core = FindRenderer(pieceObject, "Shadow");
                SpriteRenderer halo = FindRenderer(pieceObject, "ShadowHalo");

                Assert.That(core.sprite, Is.SameAs(body.sprite));
                Assert.That(halo.sprite, Is.SameAs(body.sprite));
                Assert.That(Quaternion.Angle(core.transform.localRotation, body.transform.localRotation),
                    Is.LessThan(0.01f));
                Assert.That(Quaternion.Angle(halo.transform.localRotation, body.transform.localRotation),
                    Is.LessThan(0.01f));
                Assert.That(core.flipX, Is.EqualTo(body.flipX));
                Assert.That(core.flipY, Is.EqualTo(body.flipY));

                Vector3 expectedBaseScale = Vector3.Scale(
                    body.transform.localScale,
                    new Vector3(1.004f, 1.004f, 1f));
                AssertVector(core.transform.localScale, expectedBaseScale);
                AssertVector(halo.transform.localScale, expectedBaseScale);

                Vector3 expectedPosition = new Vector3(
                    (orientation.Width - 1) * Pitch * 0.5f + 0.007f,
                    -(orientation.Height - 1) * Pitch * 0.5f - 0.014f,
                    0.05f);
                AssertVector(core.transform.localPosition, expectedPosition);
                AssertVector(halo.transform.localPosition, expectedPosition);
                Assert.That(core.color.a, Is.EqualTo(0.22f).Within(0.001f));
                Assert.That(halo.color.a, Is.Zero.Within(0.001f));
                Assert.That(core.sortingLayerName, Is.EqualTo(BattleSorting.Pieces));
                Assert.That(halo.sortingLayerName, Is.EqualTo(BattleSorting.Pieces));
                Assert.That(halo.sortingOrder, Is.LessThan(core.sortingOrder));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(pieceObject);
            }
        }

        private static GameObject BuildPiece(
            DishShape orientation,
            int rotationIndex,
            out DishPieceView view)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PiecePrefabPath);
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(DishSpritePath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(sprite, Is.Not.Null);

            GameObject pieceObject = UnityEngine.Object.Instantiate(prefab);
            view = pieceObject.GetComponent<DishPieceView>();
            Assert.That(view, Is.Not.Null);

            var definition = new DishDef(
                "shadow-test",
                "Shadow Test",
                1,
                orientation,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty);
            var placement = new Placement(orientation, rotationIndex, new GridPos(0, 0));
            var instance = new DishInstance(
                1,
                definition,
                placement,
                Array.Empty<string>(),
                Array.Empty<string>());
            view.BuildPlaced(instance, sprite, CellSize, Pitch, null);
            return pieceObject;
        }

        private static SpriteRenderer FindRenderer(GameObject root, string objectName)
        {
            foreach (SpriteRenderer renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer.name == objectName)
                {
                    return renderer;
                }
            }

            Assert.Fail($"Missing SpriteRenderer child '{objectName}'.");
            return null;
        }

        private static T PrivateField<T>(object instance, string fieldName)
            where T : class
        {
            FieldInfo field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing private field '{fieldName}'.");
            T value = field.GetValue(instance) as T;
            Assert.That(value, Is.Not.Null, $"Private field '{fieldName}' is null.");
            return value;
        }

        private static void AssertScale(Vector3 actual, Vector3 baseScale, float multiplier)
        {
            AssertVector(actual, new Vector3(
                baseScale.x * multiplier,
                baseScale.y * multiplier,
                1f));
        }

        private static void AssertVector(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.001f));
        }
    }
}
#endif
