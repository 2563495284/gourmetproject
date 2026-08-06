using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Battle.Pages;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Game.Visual;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

namespace GourmetProject.Tests.EditMode
{
    public sealed class RewardSubflowLifecycleTests
    {
        [Test]
        public void GameplayView_DoesNotContainRewardSubflowPages()
        {
            CollectionAssert.DoesNotContain(Enum.GetNames(typeof(GameplayView)), "RewardDishPack");
            CollectionAssert.DoesNotContain(Enum.GetNames(typeof(GameplayView)), "RewardItemChoice");
            CollectionAssert.DoesNotContain(Enum.GetNames(typeof(GameplayView)), "RandomizedItems");
        }

        [Test]
        public void BattleFormPrefab_OwnsRewardLayerAndAllRewardPanels()
        {
            const string path = "Assets/GameMain/Content/Prefabs/UI/BattleForm.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);

            BattleForm battle = prefab.GetComponent<BattleForm>();
            Transform layerTransform = prefab.transform.Find("HudFrame/RewardSubflowLayer");
            Assert.That(battle, Is.Not.Null);
            Assert.That(layerTransform, Is.Not.Null);
            Assert.That(layerTransform.GetComponent<CanvasGroup>(), Is.Not.Null);
            Assert.That(layerTransform.GetComponent<Image>(), Is.Not.Null);

            var serialized = new SerializedObject(battle);
            Component layer = serialized.FindProperty("_rewardSubflowLayer").objectReferenceValue as Component;
            Assert.That(layer, Is.Not.Null);
            Assert.That(layer.transform, Is.SameAs(layerTransform));

            string[] panels = { "_rewardDishPackPanel", "_rewardItemChoicePanel", "_randomizedItemsPanel" };
            foreach (string propertyName in panels)
            {
                Component panel = serialized.FindProperty(propertyName).objectReferenceValue as Component;
                Assert.That(panel, Is.Not.Null, propertyName);
                Assert.That(panel.transform.parent, Is.SameAs(layerTransform), propertyName);
            }
        }

        [Test]
        public void BattleFormPrefab_TableFragmentRewardOwnsAuthoredActionAxisGroup()
        {
            const string path = "Assets/GameMain/Content/Prefabs/UI/BattleForm.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);

            BattleForm battle = prefab.GetComponent<BattleForm>();
            Assert.That(battle, Is.Not.Null);
            var serialized = new SerializedObject(battle);

            const string hierarchyPath = "HudFrame/TopAxis";
            Transform root = prefab.transform.Find(hierarchyPath);
            Assert.That(root, Is.Not.Null, hierarchyPath);
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            Assert.That(group, Is.Not.Null,
                $"{hierarchyPath} 必须在 prefab 上固定 CanvasGroup，禁止运行时生成。");
            Assert.That(
                serialized.FindProperty("_rewardTableEditActionAxisGroup").objectReferenceValue,
                Is.SameAs(group));
        }

        [Test]
        public void TableFragmentRewardShell_SoftHideRestoresExactCanvasGroupState()
        {
            Type snapshotType = typeof(BattleForm).GetNestedType(
                "CanvasGroupSnapshot",
                System.Reflection.BindingFlags.NonPublic);
            Assert.That(snapshotType, Is.Not.Null);

            object snapshot = Activator.CreateInstance(snapshotType);
            var root = new GameObject("BattleShell", typeof(CanvasGroup));
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            group.alpha = 0.42f;
            group.interactable = false;
            group.blocksRaycasts = true;

            try
            {
                snapshotType.GetMethod("Capture").Invoke(snapshot, new object[] { group });
                snapshotType.GetMethod("Hide").Invoke(snapshot, null);
                Assert.That(group.alpha, Is.Zero);
                Assert.That(group.interactable, Is.False);
                Assert.That(group.blocksRaycasts, Is.False);

                snapshotType.GetMethod("Restore").Invoke(snapshot, null);
                Assert.That(group.alpha, Is.EqualTo(0.42f));
                Assert.That(group.interactable, Is.False);
                Assert.That(group.blocksRaycasts, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RewardTableEdit_KeepsPersistentColumnVisibleButBlocksInspectionNavigation()
        {
            var root = new GameObject("BattleInfo", typeof(BattleInfoColumn));
            var recipeObject = new GameObject("ViewRecipe", typeof(RectTransform), typeof(Button));
            var tableObject = new GameObject("ViewTable", typeof(RectTransform), typeof(Button));
            recipeObject.transform.SetParent(root.transform, false);
            tableObject.transform.SetParent(root.transform, false);
            BattleInfoColumn info = root.GetComponent<BattleInfoColumn>();
            Button recipeButton = recipeObject.GetComponent<Button>();
            Button tableButton = tableObject.GetComponent<Button>();

            try
            {
                var serialized = new SerializedObject(info);
                serialized.FindProperty("_viewRecipeButton").objectReferenceValue = recipeButton;
                serialized.FindProperty("_viewTableButton").objectReferenceValue = tableButton;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(root.activeSelf, Is.True);
                Assert.That(recipeButton.interactable, Is.True);
                Assert.That(tableButton.interactable, Is.True);

                info.SetInspectionNavigationBlocked(true);
                Assert.That(root.activeSelf, Is.True, "常驻栏视觉必须保留。");
                Assert.That(recipeButton.interactable, Is.False);
                Assert.That(tableButton.interactable, Is.False);

                info.SetInspectionNavigationBlocked(false);
                Assert.That(recipeButton.interactable, Is.True);
                Assert.That(tableButton.interactable, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CoveredInspectionSwap_RestoresCenterContentAndBackButtonInput()
        {
            var root = new GameObject("Center", typeof(CanvasGroup));
            CanvasGroup center = root.GetComponent<CanvasGroup>();
            center.alpha = 0f;
            center.interactable = false;
            center.blocksRaycasts = false;

            try
            {
                GameplayPageRouter.RestoreCenterForCoveredSwap(center);
                Assert.That(center.alpha, Is.EqualTo(1f));
                Assert.That(center.interactable, Is.True);
                Assert.That(center.blocksRaycasts, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void InspectionPeerSwitches_PreserveRewardBattleAsTheReturnOrigin()
        {
            var navigation = new InspectionNavigationContext();
            navigation.Capture(GameplayView.Food, ActionSelectSnapshot.None);
            navigation.Capture(GameplayView.TableView, ActionSelectSnapshot.None);
            navigation.Capture(GameplayView.RecipeInspect, ActionSelectSnapshot.None);

            Assert.That(navigation.HasOrigin, Is.True);
            Assert.That(navigation.ReturnView, Is.EqualTo(GameplayView.Food));
        }

        [Test]
        public void RewardResultPeek_RestoresItsReturnButtonAfterLeavingInspection()
        {
            var root = new GameObject("RewardForm", typeof(RectTransform), typeof(RewardForm));
            var returnObject = new GameObject("PeekReturn", typeof(RectTransform), typeof(Button));
            returnObject.transform.SetParent(root.transform, false);
            RewardForm reward = root.GetComponent<RewardForm>();
            Button returnButton = returnObject.GetComponent<Button>();

            try
            {
                var serialized = new SerializedObject(reward);
                serialized.FindProperty("_peekReturnButton").objectReferenceValue = returnButton;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                SetPrivateField(reward, "_allowResultPeek", true);
                SetPrivateField(reward, "_peekHidden", true);

                reward.SetResultPeekInspectionActive(true);
                Assert.That(returnObject.activeSelf, Is.False,
                    "查看页应使用自己的返回按钮，避免两个返回入口重叠。");

                reward.SetResultPeekInspectionActive(false);
                Assert.That(returnObject.activeSelf, Is.True,
                    "退出餐桌/菜谱后必须恢复 RewardForm 的返回奖励按钮。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RecipeInspect_DisablesRecipeButtonLikeTableInspect()
        {
            Assert.That(BattleInfoColumn.CanOpenRecipeInspection(GameplayView.RecipeInspect), Is.False);
            Assert.That(BattleInfoColumn.CanOpenRecipeInspection(GameplayView.TableView), Is.True);

            const string path = "Assets/GameMain/Content/Prefabs/UI/BattleForm.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            BattleInfoColumn info = prefab.GetComponentInChildren<BattleInfoColumn>(true);
            var serialized = new SerializedObject(info);
            Button recipeButton = serialized.FindProperty("_viewRecipeButton").objectReferenceValue as Button;
            Assert.That(recipeButton, Is.Not.Null);
            Assert.That(recipeButton.targetGraphic, Is.TypeOf<TMPro.TextMeshProUGUI>(),
                "Button disabled 色应直接作用于‘查看菜谱’字体，与查看餐桌一致。");
        }

        [Test]
        public void RecipeDishDisplayValue_IncludesPermanentScoreModifiers()
        {
            var def = new DishDef(
                "test_dish",
                "Test Dish",
                30,
                DishShape.FromRows(new[] { "X" }),
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                false);
            var slot = new RecipeBookSlot(def.Id);
            slot.AddScoreFlat(60f);
            slot.MultiplyScore(1.5f);

            Assert.That(
                RecipeReadonlyBookView.ResolveRecipeDishDisplayValue(def, slot),
                Is.EqualTo(135));
        }

        [Test]
        public void ShopDeleteFoodServiceItem_CanStayVisibleWhilePurchaseIsRejected()
        {
            const string path =
                "Assets/GameMain/Content/Prefabs/UI/ShopDeleteFoodServiceItem.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);

            try
            {
                ShopBuyItemViewBase card =
                    instance.GetComponent<ShopBuyItemViewBase>();
                var serialized = new SerializedObject(card);
                Button buyButton = serialized.FindProperty("_buyButton")
                    .objectReferenceValue as Button;

                card.SetStocked(true);
                card.SetPurchaseEnabled(false);

                Assert.That(instance.activeSelf, Is.True);
                Assert.That(buyButton, Is.Not.Null);
                Assert.That(buyButton.interactable, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ActiveItemPopup_ShowsDescriptionAndBlockedUseReason()
        {
            string text = ActiveItemActionPopup.BuildDescription(
                "丢弃一份食物。",
                canUse: false,
                "当前奖励流程中不能使用消耗品。");

            StringAssert.Contains("丢弃一份食物。", text);
            StringAssert.Contains("暂不可使用", text);
            StringAssert.Contains("当前奖励流程中不能使用消耗品。", text);
        }

        [Test]
        public void ActiveItemPopup_DoesNotShowBlockedReasonWhenUseIsAllowed()
        {
            string text = ActiveItemActionPopup.BuildDescription(
                "丢弃一份食物。",
                canUse: true,
                "不应显示");

            Assert.That(text, Is.EqualTo("丢弃一份食物。"));
        }

        [Test]
        public void BattleRecipeCannotPlace_UsesNormalDishShader()
        {
            const string path =
                "Assets/GameMain/Content/Prefabs/UI/RecipeEditDishView.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);

            try
            {
                RecipeEditDishView dish =
                    instance.GetComponent<RecipeEditDishView>();
                DishIconRenderTexturePreview preview =
                    instance.GetComponentInChildren<DishIconRenderTexturePreview>(true);

                Assert.That(dish, Is.Not.Null);
                Assert.That(preview, Is.Not.Null);
                dish.Bind(
                    "测试食物",
                    string.Empty,
                    0,
                    0,
                    dragEnabled: false,
                    battleStatus: BattleRecipeEntryStatus.CannotPlace);

                Assert.That(
                    DebuffVisualStyle.IsAppliedToGraphic(preview.TargetImage),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void Layer_HandoffAlwaysKeepsSourceOrBlockingLayerAlive()
        {
            var root = new GameObject("Root", typeof(RectTransform));
            var center = new GameObject("Center", typeof(RectTransform));
            var source = new GameObject("ShopSource", typeof(RectTransform));
            var layerObject = new GameObject(
                "RewardSubflowLayer",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup),
                typeof(RewardSubflowLayer));
            var panel = new GameObject("Child", typeof(RectTransform), typeof(CanvasGroup));

            try
            {
                center.transform.SetParent(root.transform, false);
                source.transform.SetParent(center.transform, false);
                layerObject.transform.SetParent(root.transform, false);
                panel.transform.SetParent(layerObject.transform, false);
                var layer = layerObject.GetComponent<RewardSubflowLayer>();

                layer.Initialize();

                Assert.That(source.activeInHierarchy, Is.True);
                Assert.That(layer.IsVisible, Is.False);

                layer.Prepare();
                Assert.That(source.activeInHierarchy, Is.True);
                Assert.That(layer.IsVisible, Is.True);

                layer.Show(panel.GetComponent<CanvasGroup>());
                Assert.That(panel.activeInHierarchy, Is.True);
                Assert.That(layer.IsVisible, Is.True);

                layer.Hide(panel.GetComponent<CanvasGroup>());
                Assert.That(panel.activeSelf, Is.False);
                Assert.That(layer.IsVisible, Is.True,
                    "子页隐藏后，覆盖层必须继续拦截输入，直到父页已经恢复。");

                layer.HideImmediate();
                Assert.That(source.activeInHierarchy, Is.True);
                Assert.That(layer.IsVisible, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void InvalidOpen_InvokesCompletionExactlyOnceAndLeavesNoLayer()
        {
            var host = new EmptyRewardHost();
            var coordinator = new RewardPageCoordinator(host);
            int completed = 0;

            bool opened = coordinator.OpenRewardDishPack(
                null,
                Array.Empty<RewardChoice>(),
                null,
                () => completed++);

            Assert.That(opened, Is.False);
            Assert.That(completed, Is.EqualTo(1));
            Assert.That(coordinator.IsActive, Is.False);
            Assert.That(host.LayerHiddenCount, Is.EqualTo(0));
        }

        private sealed class EmptyRewardHost : IRewardPageHost
        {
            public int LayerHiddenCount { get; private set; }

            public GameRun Run => null;
            public RewardDishPackPanel RewardDishPackPanel => null;
            public RewardItemChoicePanel RewardItemChoicePanel => null;
            public RandomizedItemsPanel RandomizedItemsPanel => null;
            public void PrepareRewardSubflowLayer() { }
            public void ShowRewardSubflowPanel(Component panel) { }
            public void HideRewardSubflowPanel(Component panel) { }
            public void HideRewardSubflowLayer() => LayerHiddenCount++;
            public void NotifyRewardSubflowLifecycle(RewardSubflowLifecycle lifecycle) { }
            public void RefreshPersistent() { }
            public FoodTipsView FoodTips() => null;
            public ItemTipView ItemTips() => null;
            public void PlayRewardDishSelectionFly(RewardDishChoiceCardView sourceCard) { }
            public Action PrepareRewardItemSelectionFly(
                RewardChoice choice,
                cfg.ItemKind kind,
                RewardItemChoiceCardView sourceCard) => null;
            public void PlayRandomizedItemFlys(IReadOnlyList<RandomizedItemResult> results) { }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            target.GetType()
                .GetField(fieldName, System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }
    }
}
