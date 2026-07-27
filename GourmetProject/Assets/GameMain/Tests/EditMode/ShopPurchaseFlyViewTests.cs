using System.Reflection;
using GourmetProject.Config;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ShopPurchaseFlyViewTests
    {
        [Test]
        public void ShopFlyPrefab_ContainsPurchaseFlyComponent()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/GameMain/Content/Prefabs/UI/Hud/ShopItemFlyFx.prefab");

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<ShopPurchaseFlyView>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<RectTransform>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<CanvasGroup>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<Image>(), Is.Not.Null);
        }

        [TestCase("Assets/GameMain/Content/Prefabs/UI/ShopActiveItemBuyItemView.prefab")]
        [TestCase("Assets/GameMain/Content/Prefabs/UI/ShopPassiveItemBuyItemView.prefab")]
        [TestCase("Assets/GameMain/Content/Prefabs/UI/ShopFragmentPackBuyItemView.prefab")]
        public void NonFoodShopCards_UseWholeCardAsBuyButton(string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null);

            ShopBuyItemViewBase card = prefab.GetComponent<ShopBuyItemViewBase>();
            Button[] buttons = prefab.GetComponentsInChildren<Button>(true);
            Assert.That(card, Is.Not.Null);
            Assert.That(buttons, Has.Length.EqualTo(1));
            Assert.That(buttons[0].gameObject, Is.SameAs(prefab));
        }

        [Test]
        public void ActiveShopPurchase_WhenSlotsAreFull_DoesNotChargeOrClearStock()
        {
            var config = new ConfigService();
            config.LoadAll();
            var run = new GameRun(
                config.Tables,
                GameplayContentBuilder.BuildDatabase(config.Tables),
                "shop_test_character",
                "shop_test_seed");
            run.Gold = 1000;
            run.AcquireItem("item_active_season_sweet", 0);
            run.AcquireItem("item_active_season_bitter", 0);
            Assert.That(run.HasFreeActiveSlot, Is.False);

            var entry = new ShopEntry(
                ShopEntryKind.ActiveItem,
                "item_active_season_numb",
                "加麻单",
                string.Empty,
                35,
                slotIndex: 0);
            int goldBefore = run.Gold;

            bool purchased = ShopService.Purchase(run, entry);

            Assert.That(purchased, Is.False);
            Assert.That(run.Gold, Is.EqualTo(goldBefore));
            Assert.That(run.ActiveItemCount, Is.EqualTo(run.ActiveSlotCapacity));
            Assert.That(entry.IsStocked, Is.True);
        }

        [Test]
        public void ActiveItemSlot_UsesVisibleIconRectForPurchaseFlyTarget()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/GameMain/Content/Prefabs/UI/BattleForm.prefab");
            Assert.That(prefab, Is.Not.Null);

            BattleItemsColumn column = prefab.GetComponentInChildren<BattleItemsColumn>(true);
            Assert.That(column, Is.Not.Null);

            RunItemSlotView[] slots = column.GetComponentsInChildren<RunItemSlotView>(true);
            Assert.That(slots, Is.Not.Empty);

            bool foundZeroSizedAnchor = false;
            foreach (RunItemSlotView slot in slots)
            {
                RectTransform root = slot.RectTransform;
                RectTransform visual = slot.VisualRectTransform;
                Assert.That(visual, Is.Not.Null);
                Assert.That(visual.rect.width, Is.GreaterThan(1f));
                Assert.That(visual.rect.height, Is.GreaterThan(1f));
                if (root != null && (root.rect.width <= 1f || root.rect.height <= 1f))
                {
                    foundZeroSizedAnchor = true;
                }
            }

            Assert.That(
                foundZeroSizedAnchor,
                Is.True,
                "测试应覆盖 BattleForm 中仅负责定位、没有可见尺寸的主动槽根节点。");
        }

        [Test]
        public void FoodProgress_UsesNormalizedAccelerationAndReachesEndpoints()
        {
            float start = ShopPurchaseFlyView.EvaluateAcceleratedProgress(0f, 1.1f, 2f);
            float middle = ShopPurchaseFlyView.EvaluateAcceleratedProgress(0.5f, 1.1f, 2f);
            float end = ShopPurchaseFlyView.EvaluateAcceleratedProgress(1f, 1.1f, 2f);
            float firstHalfDistance = middle - start;
            float secondHalfDistance = end - middle;

            Assert.That(start, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(end, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(secondHalfDistance, Is.GreaterThan(firstHalfDistance));
        }

        [TestCase(20f, ShopPurchaseFlyView.FoodControlOffsetMin)]
        [TestCase(250f, 250f)]
        [TestCase(800f, ShopPurchaseFlyView.FoodControlOffsetMax)]
        public void FoodControlPoint_ClampsOffsetToStsRange(float requested, float expected)
        {
            Vector2 start = new(-200f, -100f);
            Vector2 end = new(100f, 200f);
            Vector2 midpoint = (start + end) * 0.5f;
            Vector2 control = ShopPurchaseFlyView.CalculateFoodControlPoint(
                start,
                end,
                requested,
                0f);

            Assert.That(Vector2.Distance(midpoint, control), Is.EqualTo(expected).Within(0.01f));
        }

        [Test]
        public void FoodBezier_UsesExactStartAndEnd()
        {
            Vector2 start = new(-10f, 20f);
            Vector2 control = new(30f, 90f);
            Vector2 end = new(100f, -25f);

            Assert.That(
                ShopPurchaseFlyView.CalculateQuadraticBezier(start, control, end, 0f),
                Is.EqualTo(start));
            Assert.That(
                ShopPurchaseFlyView.CalculateQuadraticBezier(start, control, end, 1f),
                Is.EqualTo(end));
            Assert.That(
                ShopPurchaseFlyView.CalculateQuadraticTangent(start, control, end, 0f),
                Is.EqualTo(2f * (control - start)));
        }

        [Test]
        public void CancelFoodFly_CompletesCallbacksOnceAndReleasesOwnedTexture()
        {
            var layerObject = new GameObject("ShopFlyLayer", typeof(RectTransform));
            var flyObject = new GameObject(
                "ShopFly",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(ShopPurchaseFlyView));
            RenderTexture owned = null;
            try
            {
                flyObject.transform.SetParent(layerObject.transform, false);
                ShopPurchaseFlyView fly = flyObject.GetComponent<ShopPurchaseFlyView>();
                Assert.That(fly.Initialize(layerObject.transform as RectTransform), Is.True);
                owned = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32);
                owned.Create();
                int arrived = 0;
                int finished = 0;

                fly.PlayFood(
                    Vector2.zero,
                    new Vector2(80f, 80f),
                    new Vector2(100f, 100f),
                    owned,
                    () => arrived++,
                    () => finished++);
                fly.Cancel();
                fly.Cancel();

                Assert.That(arrived, Is.EqualTo(1));
                Assert.That(finished, Is.EqualTo(1));
                Assert.That(owned == null, Is.True);
            }
            finally
            {
                if (flyObject != null)
                {
                    Object.DestroyImmediate(flyObject);
                }

                Object.DestroyImmediate(layerObject);
            }
        }

        [Test]
        public void ItemTimings_MatchStsPurchaseAnimations()
        {
            Assert.That(ShopPurchaseFlyView.ActiveFlyDuration, Is.EqualTo(0.35f));
            Assert.That(ShopPurchaseFlyView.PassiveFlyDuration, Is.EqualTo(0.35f));
            Assert.That(ShopPurchaseFlyView.ActiveFlashDuration, Is.EqualTo(1f));
            Assert.That(ShopPurchaseFlyView.PassiveFlashDuration, Is.EqualTo(0.75f));
        }

        [Test]
        public void CopyCurrentTexture_RemainsValidAfterSourceChanges()
        {
            var go = new GameObject(
                "DishPreviewCopyTest",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage),
                typeof(DishIconRenderTexturePreview));
            RenderTexture source = null;
            RenderTexture copy = null;
            Texture2D readback = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                source = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32);
                source.Create();
                RenderTexture.active = source;
                GL.Clear(true, true, Color.red);

                DishIconRenderTexturePreview preview =
                    go.GetComponent<DishIconRenderTexturePreview>();
                FieldInfo field = typeof(DishIconRenderTexturePreview).GetField(
                    "_renderTexture",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null);
                field.SetValue(preview, source);

                copy = preview.CopyCurrentTexture();
                Assert.That(copy, Is.Not.Null);
                Assert.That(copy.IsCreated(), Is.True);

                RenderTexture.active = source;
                GL.Clear(true, true, Color.blue);
                RenderTexture.active = copy;
                readback = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                readback.ReadPixels(new Rect(0f, 0f, 1f, 1f), 0, 0);
                readback.Apply();

                Color pixel = readback.GetPixel(0, 0);
                Assert.That(pixel.r, Is.GreaterThan(0.9f));
                Assert.That(pixel.b, Is.LessThan(0.1f));
            }
            finally
            {
                RenderTexture.active = previous;
                if (readback != null)
                {
                    Object.DestroyImmediate(readback);
                }

                if (copy != null)
                {
                    copy.Release();
                    Object.DestroyImmediate(copy);
                }

                Object.DestroyImmediate(go);
            }
        }
    }
}
