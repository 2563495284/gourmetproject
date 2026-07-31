using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Game.Visual;
using GourmetProject.Gameplay.Battle;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BattleRecipeStatusViewTests
    {
        private const string PrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/RecipeEditDishView.prefab";

        [Test]
        public void Bind_WaitingForPlacement_ShowsGreenOverlayOnly()
        {
            GameObject gameObject = InstantiatePrefab();
            try
            {
                RecipeEditDishView view = gameObject.GetComponent<RecipeEditDishView>();

                view.Bind(
                    "测试菜",
                    "X",
                    0,
                    0,
                    dragEnabled: false,
                    battleStatus: BattleRecipeEntryStatus.WaitingForPlacement);

                Image overlay = BattleStatusOverlay(gameObject);
                RawImage food = FoodImage(gameObject);
                Assert.That(overlay.gameObject.activeSelf, Is.True);
                Assert.That(overlay.color.r, Is.EqualTo(0.20f).Within(0.001f));
                Assert.That(overlay.color.g, Is.EqualTo(0.58f).Within(0.001f));
                Assert.That(overlay.color.b, Is.EqualTo(0.27f).Within(0.001f));
                Assert.That(overlay.color.a, Is.EqualTo(0.12f).Within(0.001f));
                Assert.That(food.color.a, Is.EqualTo(1f).Within(0.001f));
                Assert.That(DebuffVisualStyle.IsAppliedToGraphic(food), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Bind_CannotPlace_AppliesDebuffMaskToRenderTextureImage()
        {
            GameObject gameObject = InstantiatePrefab();
            try
            {
                RecipeEditDishView view = gameObject.GetComponent<RecipeEditDishView>();

                view.Bind(
                    "测试菜",
                    "X",
                    0,
                    0,
                    dragEnabled: false,
                    battleStatus: BattleRecipeEntryStatus.CannotPlace);

                Image overlay = BattleStatusOverlay(gameObject);
                RawImage food = FoodImage(gameObject);
                Assert.That(overlay.gameObject.activeSelf, Is.False);
                Assert.That(DebuffVisualStyle.IsAppliedToGraphic(food), Is.True);
                Assert.That(
                    food.material.shader.name,
                    Is.EqualTo("GourmetProject/DebuffMask"));
                Assert.That(food.color.a, Is.EqualTo(1f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [TestCase(BattleRecipeEntryStatus.Served)]
        [TestCase(BattleRecipeEntryStatus.Discarded)]
        [TestCase(BattleRecipeEntryStatus.Removed)]
        public void Bind_FinishedStatus_OnlyMakesFoodSemiTransparent(
            BattleRecipeEntryStatus status)
        {
            GameObject gameObject = InstantiatePrefab();
            try
            {
                RecipeEditDishView view = gameObject.GetComponent<RecipeEditDishView>();

                view.Bind(
                    "测试菜",
                    "X",
                    0,
                    0,
                    dragEnabled: false,
                    battleStatus: status);

                Image overlay = BattleStatusOverlay(gameObject);
                RawImage food = FoodImage(gameObject);
                Assert.That(overlay.gameObject.activeSelf, Is.False);
                Assert.That(food.color.a, Is.EqualTo(0.35f).Within(0.001f));
                Assert.That(DebuffVisualStyle.IsAppliedToGraphic(food), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Bind_Normal_ClearsPreviousBattleVisuals()
        {
            GameObject gameObject = InstantiatePrefab();
            try
            {
                RecipeEditDishView view = gameObject.GetComponent<RecipeEditDishView>();
                view.Bind(
                    "测试菜",
                    "X",
                    0,
                    0,
                    dragEnabled: false,
                    battleStatus: BattleRecipeEntryStatus.CannotPlace);

                view.Bind(
                    "测试菜",
                    "X",
                    0,
                    0,
                    dragEnabled: false,
                    battleStatus: BattleRecipeEntryStatus.Normal);

                Image overlay = BattleStatusOverlay(gameObject);
                RawImage food = FoodImage(gameObject);
                Assert.That(overlay.gameObject.activeSelf, Is.False);
                Assert.That(food.color.a, Is.EqualTo(1f).Within(0.001f));
                Assert.That(DebuffVisualStyle.IsAppliedToGraphic(food), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Bind_InWarehouseMode_UsesPrefabHighlightWithoutAddingObjects()
        {
            GameObject gameObject = InstantiatePrefab();
            try
            {
                RecipeEditDishView view = gameObject.GetComponent<RecipeEditDishView>();
                int initialObjectCount =
                    gameObject.GetComponentsInChildren<Transform>(true).Length;

                view.Bind(
                    "测试菜",
                    "X",
                    0,
                    0,
                    dragEnabled: false,
                    previewMode: DishIconPreviewMode.Warehouse);

                Transform highlight =
                    gameObject.transform.Find("WarehouseHighlight");
                Assert.That(highlight, Is.Not.Null);
                Assert.That(highlight.gameObject.activeSelf, Is.True);
                Assert.That(
                    gameObject.GetComponentsInChildren<Transform>(true).Length,
                    Is.EqualTo(initialObjectCount));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        private static GameObject InstantiatePrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            return Object.Instantiate(prefab);
        }

        private static Image BattleStatusOverlay(GameObject gameObject)
        {
            Transform overlay = gameObject.transform.Find("BattleStatusOverlay");
            Assert.That(overlay, Is.Not.Null);
            return overlay.GetComponent<Image>();
        }

        private static RawImage FoodImage(GameObject gameObject)
        {
            RawImage food = gameObject.GetComponentInChildren<RawImage>(true);
            Assert.That(food, Is.Not.Null);
            return food;
        }
    }
}
