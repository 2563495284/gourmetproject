using System;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class DishIconRenderTexturePreviewTests
    {
        [TestCase(new[] { "X" }, 3, 3)]
        [TestCase(new[] { "XXX" }, 5, 3)]
        [TestCase(new[] { "XX" }, 4, 3)]
        [TestCase(new[] { "X", "X", "X" }, 3, 5)]
        public void ExpandedBoardSize_LeavesOneCellOnEverySide(string[] rows, int width, int height)
        {
            DishShape shape = DishShape.FromRows(rows);

            Assert.That(
                DishIconRenderTexturePreview.ExpandedBoardSize(shape),
                Is.EqualTo(new Vector2Int(width, height)));
        }

        [Test]
        public void RewardAndShopPrefabs_UseTheSameRenderTexturePreview()
        {
            GameObject rewardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/GameMain/UI/RewardDishPanel.prefab");
            GameObject shopPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/GameMain/UI/ShopFoodBuyItemView.prefab");

            DishIconRenderTexturePreview rewardPreview = rewardPrefab
                .transform
                .Find("ChoiceContainer/RewardDishChoiceCardTemplate/IconFrame/DishRenderTexture/Output")
                .GetComponent<DishIconRenderTexturePreview>();
            DishIconRenderTexturePreview shopPreview = shopPrefab
                .transform
                .Find("DishRenderTexture/Output")
                .GetComponent<DishIconRenderTexturePreview>();

            AssertPreviewIsConfigured(rewardPreview);
            AssertPreviewIsConfigured(shopPreview);

            var rewardCard = rewardPrefab.GetComponentInChildren<RewardDishChoiceCardView>(true);
            var rewardSerialized = new SerializedObject(rewardCard);
            Assert.That(
                rewardSerialized.FindProperty("_dishPreview").objectReferenceValue,
                Is.SameAs(rewardPreview));

            var shopCard = shopPrefab.GetComponent<ShopFoodBuyItemView>();
            var shopSerialized = new SerializedObject(shopCard);
            Assert.That(
                shopSerialized.FindProperty("_dishIconPreview").objectReferenceValue,
                Is.SameAs(shopPreview));
        }

        [Test]
        public void EveryDishPrefab_UsesRenderTexturePreview()
        {
            AssertSerializedPreviewReference<ShopBuyCardView>(
                "Assets/GameMain/UI/ShopBuyCardView.prefab",
                "_dishIconPreview");
            AssertSerializedPreviewReference<ShopFoodBuyItemView>(
                "Assets/GameMain/UI/ShopFoodBuyItemView.prefab",
                "_dishIconPreview");
            AssertSerializedPreviewReference<RecipeEditDishView>(
                "Assets/GameMain/UI/RecipeEditDishView.prefab",
                "_dishPreview");
        }

        [Test]
        public void ShopFoodCard_UsesRenderTextureAsTipPlacementTarget()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/GameMain/UI/ShopFoodBuyItemView.prefab");
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                ShopFoodBuyItemView card = instance.GetComponent<ShopFoodBuyItemView>();
                DishIconRenderTexturePreview preview =
                    instance.GetComponentInChildren<DishIconRenderTexturePreview>(true);

                Assert.That(card.TipPlacementTarget, Is.SameAs(preview.transform));
                Assert.That(card.PurchaseFlySource, Is.Not.SameAs(preview.transform));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ShopFoodCard_RtReceivesPointerEventsForTipsAndPurchase()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/GameMain/UI/ShopFoodBuyItemView.prefab");
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                ShopFoodBuyItemView card = instance.GetComponent<ShopFoodBuyItemView>();
                DishIconRenderTexturePreview preview =
                    instance.GetComponentInChildren<DishIconRenderTexturePreview>(true);
                Sprite sprite = Resources.Load<Sprite>("Sprites/Dishes/donut");
                var dish = new DishDef(
                    "shop_pointer_test",
                    "Shop Pointer Test",
                    10,
                    DishShape.FromRows(new[] { "X" }),
                    0,
                    0,
                    1f,
                    Array.Empty<string>(),
                    string.Empty,
                    false,
                    baseId: "donut");

                card.Bind(new ShopBuyItemViewContext(
                    null,
                    null,
                    true,
                    sprite,
                    dish,
                    null));

                RawImage rawImage = preview.GetComponent<RawImage>();
                Assert.That(rawImage.raycastTarget, Is.True);
                Assert.That(
                    rawImage.GetComponentInParent<Button>(),
                    Is.SameAs(instance.GetComponent<Button>()),
                    "The RT hit must bubble to the shop card Button.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [TestCase("Assets/GameMain/UI/RecipeEditDishView.prefab", 1)]
        [TestCase("Assets/GameMain/UI/ShopBuyCardView.prefab", 1)]
        [TestCase("Assets/GameMain/UI/ShopFoodBuyItemView.prefab", 1)]
        [TestCase("Assets/GameMain/UI/ShopPassiveItemBuyItemView.prefab", 0)]
        [TestCase("Assets/GameMain/UI/ShopActiveItemBuyItemView.prefab", 0)]
        [TestCase("Assets/GameMain/UI/ShopFragmentPackBuyItemView.prefab", 0)]
        public void FormerShapePreviewPrefabs_HaveNoMissingScripts(
            string prefabPath,
            int expectedRtPreviewCount)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, prefabPath);

            int missingScriptCount = 0;
            foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
            {
                missingScriptCount +=
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
            }

            Assert.That(missingScriptCount, Is.Zero, prefabPath);
            Assert.That(
                prefab.GetComponentsInChildren<DishIconRenderTexturePreview>(true),
                Has.Length.EqualTo(expectedRtPreviewCount),
                prefabPath);
        }

        [Test]
        public void Preview_RendersExpectedRectangleToTexture()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/GameMain/UI/ShopFoodBuyItemView.prefab");
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                DishIconRenderTexturePreview preview = instance.GetComponentInChildren<DishIconRenderTexturePreview>(true);
                Sprite sprite = Resources.Load<Sprite>("Sprites/Dishes/donut");
                var dish = new DishDef(
                    "preview_test_donut",
                    "Preview Test Donut",
                    100000,
                    DishShape.FromRows(new[] { "X" }),
                    0,
                    0,
                    1f,
                    Array.Empty<string>(),
                    string.Empty,
                    false,
                    baseId: "donut");

                preview.gameObject.SetActive(true);
                preview.Bind(dish, sprite, dish.Deliciousness);

                Assert.That(preview.CurrentTexture, Is.Not.Null);
                Assert.That(preview.CurrentTexture.IsCreated(), Is.True);
                Assert.That(preview.CurrentTexture.width, Is.EqualTo(288));
                Assert.That(preview.CurrentTexture.height, Is.EqualTo(288));

                Camera previewCamera = FindPreviewCamera();
                Assert.That(previewCamera, Is.Not.Null);
                Assert.That(previewCamera.transform.parent.position.x, Is.GreaterThan(9000f));

                int previewLayer = LayerMask.NameToLayer("DishIconPreview");
                Assert.That(previewLayer, Is.GreaterThanOrEqualTo(0));
                Assert.That(previewCamera.cullingMask, Is.EqualTo(1 << previewLayer));

                Transform previewStage = previewCamera.transform.parent;
                foreach (Transform child in previewStage.GetComponentsInChildren<Transform>(true))
                {
                    Assert.That(child.gameObject.layer, Is.EqualTo(previewLayer));
                }

                Assert.That(
                    previewStage.GetComponentsInChildren<DishPieceView>(true),
                    Is.Empty,
                    "Preview objects must never register as gameplay dishes.");
                Assert.That(
                    previewStage.GetComponentsInChildren<DiningTableCellView>(true),
                    Is.Empty,
                    "Preview grid cells must remain render-only objects.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ShopPreview_AppliesFlavorShaderAndNumbRotationInsideRtStage()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/GameMain/UI/ShopFoodBuyItemView.prefab");
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                DishIconRenderTexturePreview preview = instance.GetComponentInChildren<DishIconRenderTexturePreview>(true);
                Sprite sprite = Resources.Load<Sprite>("Sprites/Dishes/donut");
                var dish = new DishDef(
                    "preview_test_numb_donut",
                    "Preview Test Numb Donut",
                    100000,
                    DishShape.FromRows(new[] { "XX" }),
                    0,
                    0,
                    1f,
                    Array.Empty<string>(),
                    "t_numb",
                    false,
                    baseId: "donut");

                preview.gameObject.SetActive(true);
                preview.Bind(dish, sprite, dish.Deliciousness);

                Camera previewCamera = FindPreviewCamera();
                SpriteRenderer dishRenderer = previewCamera
                    .transform
                    .parent
                    .Find("Dish")
                    .GetComponent<SpriteRenderer>();

                Assert.That(dishRenderer.sharedMaterial, Is.Not.Null);
                Assert.That(
                    dishRenderer.sharedMaterial.shader.name,
                    Is.EqualTo("GourmetProject/FlavorStain"));
                Assert.That(
                    Mathf.Abs(Mathf.DeltaAngle(dishRenderer.transform.localEulerAngles.z, 90f)),
                    Is.LessThan(0.01f));
                Assert.That(preview.CurrentTexture.width, Is.EqualTo(288));
                Assert.That(preview.CurrentTexture.height, Is.EqualTo(384));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static Camera FindPreviewCamera()
        {
            foreach (Camera camera in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (camera.name == "Dish Icon Camera")
                {
                    return camera;
                }
            }

            return null;
        }

        private static void AssertPreviewIsConfigured(DishIconRenderTexturePreview preview)
        {
            Assert.That(preview, Is.Not.Null);
            var serialized = new SerializedObject(preview);
            Assert.That(serialized.FindProperty("_targetImage").objectReferenceValue, Is.Not.Null);
            Assert.That(serialized.FindProperty("_cellPrefab").objectReferenceValue, Is.Not.Null);
            Assert.That(serialized.FindProperty("_badgePrefab").objectReferenceValue, Is.Not.Null);
        }

        private static void AssertSerializedPreviewReference<T>(
            string prefabPath,
            string propertyName)
            where T : Component
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, prefabPath);

            T view = prefab.GetComponentInChildren<T>(true);
            Assert.That(view, Is.Not.Null, $"{prefabPath} is missing {typeof(T).Name}.");

            DishIconRenderTexturePreview[] previews =
                prefab.GetComponentsInChildren<DishIconRenderTexturePreview>(true);
            Assert.That(previews, Has.Length.EqualTo(1), prefabPath);
            AssertPreviewIsConfigured(previews[0]);

            var serialized = new SerializedObject(view);
            Assert.That(
                serialized.FindProperty(propertyName).objectReferenceValue,
                Is.SameAs(previews[0]),
                $"{typeof(T).Name}.{propertyName} must reference the RT preview.");
        }
    }
}
