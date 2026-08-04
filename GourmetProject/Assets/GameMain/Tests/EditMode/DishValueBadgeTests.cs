using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using TMPro;

namespace GourmetProject.Tests.EditMode
{
    public sealed class DishValueBadgeTests
    {
        private const string DishPiecePrefabPath =
            "Assets/GameMain/Content/Prefabs/Battle/DishPiece.prefab";
        private const string ServingOutletPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Hud/ServingOutlet.prefab";
        private const string RewardFormPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/RewardForm.prefab";

        [Test]
        public void CurrentContribution_IncludesAllRuntimeScoreFactors()
        {
            DishInstance dish = CreateDish(deliciousness: 10);
            dish.AddPermanentFlat(2f);
            dish.MultiplyTemporaryBase(1.25f);
            dish.MultiplyPermanentMult(1.5f);
            dish.MultiplyServeMultiplier(0.8f);
            dish.AddServeMultiplierFlat(0.3f);

            Assert.That(dish.BaseScoreBeforeSettlement, Is.EqualTo(15f).Within(0.001f));
            Assert.That(dish.BaseMultiplierBeforeSettlement, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(DishValueDisplay.CurrentContribution(dish), Is.EqualTo(23f));
        }

        [TestCase(-1f, "0")]
        [TestCase(12f, "12")]
        [TestCase(12.25f, "12.3")]
        public void Format_UsesSharedBadgeFormatting(float value, string expected)
        {
            Assert.That(DishValueDisplay.Format(value), Is.EqualTo(expected));
        }

        [Test]
        public void RotationOverride_DrivesPreviewGridSize()
        {
            DishDef definition = CreateDefinition(
                10,
                DishShape.FromRows(new[] { "XX" }));

            Assert.That(
                DishIconRenderTexturePreview.DisplayedGridSizeFor(
                    definition,
                    rotationIndexOverride: 1),
                Is.EqualTo(new Vector2Int(1, 2)));
        }

        [Test]
        public void BadgeLayout_WorldAndUiUseTheSameRelativeTopEdgeAnchor()
        {
            DishShape shape = DishShape.FromRows(new[] { "XX.", "XXX" });
            const float worldBadgeTopExtent = 0.3f;
            const float uiBadgeTopExtent = 0.15f;

            Vector3 worldPosition =
                DishBadgeLayout.PositionFromTopLeftCellOrigin(
                    shape,
                    1f,
                    1f,
                    worldBadgeTopExtent);
            Vector3 uiPosition = DishBadgeLayout.PositionFromShapeCenter(
                shape,
                1f,
                1f,
                uiBadgeTopExtent);
            Vector3 uiPositionInTopLeftOrigin = uiPosition + new Vector3(
                (shape.Width - 1) * 0.5f,
                -(shape.Height - 1) * 0.5f,
                0f);

            var worldTopEdge = new Vector2(
                worldPosition.x,
                worldPosition.y + worldBadgeTopExtent);
            var uiTopEdge = new Vector2(
                uiPositionInTopLeftOrigin.x,
                uiPositionInTopLeftOrigin.y + uiBadgeTopExtent);

            Assert.That(uiTopEdge.x, Is.EqualTo(worldTopEdge.x).Within(0.0001f));
            Assert.That(uiTopEdge.y, Is.EqualTo(worldTopEdge.y).Within(0.0001f));
            Assert.That(worldTopEdge, Is.EqualTo(new Vector2(0.5f, 0.5f)));
        }

        [Test]
        public void Preview_ReusesRenderTextureWhenRequestIsUnchanged()
        {
            const string prefabPath =
                "Assets/GameMain/Content/Prefabs/UI/ShopFoodBuyItemView.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                DishIconRenderTexturePreview preview =
                    instance.GetComponentInChildren<DishIconRenderTexturePreview>(true);
                DishDef definition = CreateDefinition(
                    10,
                    DishShape.FromRows(new[] { "XX", "X." }));
                Sprite sprite = Resources.Load<Sprite>("Sprites/Dishes/donut");
                DishPreviewRequest request =
                    DishPreviewRequest.FromDefinition(definition, sprite);

                preview.Bind(request);
                RenderTexture first = preview.CurrentTexture;
                preview.Bind(request);

                Assert.That(first, Is.Not.Null);
                Assert.That(preview.CurrentTexture, Is.SameAs(first));

                preview.Bind(new DishPreviewRequest(
                    definition,
                    sprite,
                    11,
                    null,
                    DishIconPreviewMode.Card,
                    null));
                Assert.That(preview.CurrentTexture, Is.Not.SameAs(first));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ServingOutlet_PreparedDishIsActivatedAndRendered()
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(ServingOutletPrefabPath);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                ServingOutletView outlet =
                    instance.GetComponentInChildren<ServingOutletView>(true);
                var serialized = new SerializedObject(outlet);
                var preparedDishRoot =
                    (RectTransform)serialized.FindProperty("_preparedDishRoot")
                        .objectReferenceValue;
                DishIconRenderTexturePreview preview =
                    (DishIconRenderTexturePreview)serialized.FindProperty("_dishPreview")
                        .objectReferenceValue;
                BattleSession session = CreatePreparedSession();

                outlet.Bind(
                    session,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);

                Assert.That(preparedDishRoot, Is.Not.Null);
                Assert.That(preparedDishRoot.gameObject.activeSelf, Is.True);
                Assert.That(preview.gameObject.activeInHierarchy, Is.True);
                Assert.That(preview.CurrentTexture, Is.Not.Null);
                Assert.That(preview.CurrentTexture.IsCreated(), Is.True);
                Assert.That(
                    CountVisiblePixels(preview.CurrentTexture),
                    Is.GreaterThan(0),
                    "出餐口 RenderTexture 必须实际画出菜品和 Badge，不能只是创建空纹理。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void DishPiece_BuildRefreshAndPlacementReuseExactlyOneBadge()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DishPiecePrefabPath);
            Assert.That(prefab, Is.Not.Null);

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            GameObject sequencerObject = new("SettlementSequencer");
            Texture2D texture = new(8, 8);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 8f, 8f),
                new Vector2(0.5f, 0.5f),
                8f);
            try
            {
                DishPieceView piece = instance.GetComponent<DishPieceView>();
                DishInstance dish = CreateDish(deliciousness: 10);
                piece.BuildPlaced(dish, sprite, 1f, 1f, null);

                Transform badge = FindBadge(instance);
                Assert.That(badge, Is.Not.Null);
                Assert.That(CountBadges(instance), Is.EqualTo(1));
                Assert.That(badge.GetComponentInChildren<TextMeshPro>(true).text, Is.EqualTo("10"));

                dish.AddPermanentFlat(5f);
                InvokeNonPublic(piece, "RefreshDishValueBadge");
                Assert.That(badge.GetComponentInChildren<TextMeshPro>(true).text, Is.EqualTo("15"));

                piece.UpdatePlacement(new Placement(
                    dish.Def.Shape,
                    0,
                    new GridPos(2, 1)));
                Assert.That(FindBadge(instance), Is.SameAs(badge));
                Assert.That(CountBadges(instance), Is.EqualTo(1));

                piece.SetFlying(true);
                Assert.That(
                    badge.GetComponentsInChildren<Renderer>(true)
                        .All(renderer => renderer.sortingLayerName == "PiecesFlying"),
                    Is.True);

                SettlementSequencer sequencer =
                    sequencerObject.AddComponent<SettlementSequencer>();
                sequencer.RestoreDishValueBadges(
                    new[] { new DishScore(dish.Id, dish.Def.Id, 10f, 2f, 1.5f) },
                    new Dictionary<int, DishPieceView> { [dish.Id] = piece });
                Assert.That(FindBadge(instance), Is.SameAs(badge));
                Assert.That(CountBadges(instance), Is.EqualTo(1));
                Assert.That(badge.GetComponentInChildren<TextMeshPro>(true).text, Is.EqualTo("18"));

                dish.AddPermanentFlat(100f);
                InvokeNonPublic(piece, "RefreshDishValueBadge");
                Assert.That(
                    badge.GetComponentInChildren<TextMeshPro>(true).text,
                    Is.EqualTo("18"),
                    "待领奖期间 RefreshAll 不能覆盖最终结算贡献值。");

                sequencer.ClearRetainedDishValueBadges();
                Assert.That(FindBadge(instance), Is.SameAs(badge));
                Assert.That(CountBadges(instance), Is.EqualTo(1));
                Assert.That(badge.GetComponentInChildren<TextMeshPro>(true).text, Is.EqualTo("18"));

                InvokeNonPublic(piece, "ClearDishValueBadgeOverride");
                Assert.That(badge.GetComponentInChildren<TextMeshPro>(true).text, Is.EqualTo("115"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
                UnityEngine.Object.DestroyImmediate(sequencerObject);
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void AllDishDisplayPrefabs_AreWiredToBadgePreviews()
        {
            AssertSerializedReference<DishPieceView>(
                DishPiecePrefabPath,
                "_dishValueBadgePresenter");
            AssertSerializedReference<DishPieceValueBadgePresenter>(
                DishPiecePrefabPath,
                "_badgePrefab");
            AssertSerializedReference<ServingOutletView>(
                ServingOutletPrefabPath,
                "_dishPreview");
            AssertSerializedReference<ServingOutletView>(
                ServingOutletPrefabPath,
                "_preparedDishRoot");
            AssertSerializedReference<ServingOutletView>(
                ServingOutletPrefabPath,
                "_dishHoverTrigger");
            AssertSerializedReference<ServingOutletView>(
                ServingOutletPrefabPath,
                "_worldCanvas");
            AssertSerializedReference<RewardChoiceRowView>(
                RewardFormPrefabPath,
                "_dishPreview");

            string[] previewPrefabs =
            {
                "Assets/GameMain/Content/Prefabs/UI/RecipeEditDishView.prefab",
                "Assets/GameMain/Content/Prefabs/UI/ShopFoodBuyItemView.prefab",
                "Assets/GameMain/Content/Prefabs/UI/ShopBuyCardView.prefab",
                "Assets/GameMain/Content/Prefabs/UI/RewardDishPanel.prefab",
                ServingOutletPrefabPath,
                RewardFormPrefabPath,
            };
            foreach (string path in previewPrefabs)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                DishIconRenderTexturePreview preview =
                    prefab.GetComponentInChildren<DishIconRenderTexturePreview>(true);
                Assert.That(preview, Is.Not.Null, path);
                SerializedProperty badge = new SerializedObject(preview)
                    .FindProperty("_badgePrefab");
                Assert.That(badge?.objectReferenceValue, Is.Not.Null, path);
                SerializedProperty fitter = new SerializedObject(preview)
                    .FindProperty("_aspectRatioFitter");
                Assert.That(fitter?.objectReferenceValue, Is.Not.Null, path);
            }
        }

        private static BattleSession CreatePreparedSession()
        {
            var definition = new DishDef(
                "dish_value_badge_outlet_test",
                "测试菜",
                10,
                DishShape.FromRows(new[] { "XX", "X." }),
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                allowRotate: false,
                baseId: "donut");
            var database = new GameplayDatabase(
                new[] { definition },
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var session = new BattleSession(
                new DiningTable(8, 5),
                database,
                new Xoshiro256SS(1UL),
                new[]
                {
                    new RecipeSlot(
                        "dish_value_badge_test_recipe",
                        new[] { new RecipeSlotEntry(definition.Id) }),
                },
                requiredScore: 0);
            Assert.That(session.PrepareServe(0).Success, Is.True);
            return session;
        }

        private static DishInstance CreateDish(int deliciousness)
        {
            DishDef definition = CreateDefinition(
                deliciousness,
                DishShape.FromRows(new[] { "XX", "X." }));
            return new DishInstance(
                1,
                definition,
                new Placement(definition.Shape, 0, new GridPos(0, 0)),
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        private static DishDef CreateDefinition(int deliciousness, DishShape shape)
        {
            return new DishDef(
                "dish_value_badge_test",
                "测试菜",
                deliciousness,
                shape,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                allowRotate: false);
        }

        private static void AssertSerializedReference<T>(string path, string fieldName)
            where T : Component
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            T component = prefab.GetComponentInChildren<T>(true);
            Assert.That(component, Is.Not.Null, path);
            SerializedProperty property = new SerializedObject(component).FindProperty(fieldName);
            Assert.That(property, Is.Not.Null, $"{path}: {fieldName}");
            Assert.That(property.objectReferenceValue, Is.Not.Null, $"{path}: {fieldName}");
        }

        private static Transform FindBadge(GameObject root)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(child => child.name == "DishValueBadge");
        }

        private static int CountBadges(GameObject root)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .Count(child => child.name == "DishValueBadge");
        }

        private static int CountVisiblePixels(RenderTexture texture)
        {
            RenderTexture previous = RenderTexture.active;
            var readable = new Texture2D(
                texture.width,
                texture.height,
                TextureFormat.RGBA32,
                false);
            try
            {
                RenderTexture.active = texture;
                readable.ReadPixels(
                    new Rect(0f, 0f, texture.width, texture.height),
                    0,
                    0,
                    false);
                readable.Apply(false, false);

                int visible = 0;
                Color32[] pixels = readable.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (pixels[i].a > 8)
                    {
                        visible++;
                    }
                }

                return visible;
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(readable);
            }
        }

        private static void InvokeNonPublic(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName);
            method.Invoke(target, null);
        }
    }
}
