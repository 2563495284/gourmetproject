using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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
                Assert.That(badge.GetComponentInChildren<TextMesh>(true).text, Is.EqualTo("10"));

                dish.AddPermanentFlat(5f);
                InvokeNonPublic(piece, "RefreshDishValueBadge");
                Assert.That(badge.GetComponentInChildren<TextMesh>(true).text, Is.EqualTo("15"));

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
                    new Dictionary<int, DishPieceView> { [dish.Id] = piece },
                    null,
                    null);
                Assert.That(FindBadge(instance), Is.SameAs(badge));
                Assert.That(CountBadges(instance), Is.EqualTo(1));
                Assert.That(badge.GetComponentInChildren<TextMesh>(true).text, Is.EqualTo("18"));

                dish.AddPermanentFlat(100f);
                InvokeNonPublic(piece, "RefreshDishValueBadge");
                Assert.That(
                    badge.GetComponentInChildren<TextMesh>(true).text,
                    Is.EqualTo("18"),
                    "待领奖期间 RefreshAll 不能覆盖最终结算贡献值。");

                sequencer.ClearRetainedDishValueBadges();
                Assert.That(FindBadge(instance), Is.SameAs(badge));
                Assert.That(CountBadges(instance), Is.EqualTo(1));
                Assert.That(badge.GetComponentInChildren<TextMesh>(true).text, Is.EqualTo("18"));

                InvokeNonPublic(piece, "ClearDishValueBadgeOverride");
                Assert.That(badge.GetComponentInChildren<TextMesh>(true).text, Is.EqualTo("115"));
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
                "_dishValueBadgePrefab");
            AssertSerializedReference<ServingOutletView>(
                ServingOutletPrefabPath,
                "_dishPreview");
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
            }
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
