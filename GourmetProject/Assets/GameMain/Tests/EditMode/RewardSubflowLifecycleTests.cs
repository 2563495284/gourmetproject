using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Presentation.Battle;
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
using GourmetProject.Gameplay.Scoring;
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
            CollectionAssert.DoesNotContain(Enum.GetNames(typeof(GameplayView)), "RecipeInspect");
            CollectionAssert.DoesNotContain(Enum.GetNames(typeof(GameplayView)), "TableView");
            CollectionAssert.DoesNotContain(Enum.GetNames(typeof(GameplayView)), "TableEdit");
        }

        [Test]
        public void BattleFormPrefab_OwnsIndependentTableFragmentEditLayer()
        {
            const string path = "Assets/GameMain/Content/Prefabs/UI/BattleForm.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);

            BattleForm battle = prefab.GetComponent<BattleForm>();
            Transform hud = prefab.transform.Find("HudFrame");
            Transform center = hud?.Find("Center");
            Transform inspection = hud?.Find("InspectionLayer");
            Transform editTransform = hud?.Find("TableFragmentEditLayer");
            Transform leftColumn = hud?.Find("LeftColumn");
            Assert.That(battle, Is.Not.Null);
            Assert.That(center, Is.Not.Null);
            Assert.That(inspection, Is.Not.Null);
            Assert.That(editTransform, Is.Not.Null);
            Assert.That(leftColumn, Is.Not.Null);
            Assert.That(inspection.GetSiblingIndex(), Is.LessThan(editTransform.GetSiblingIndex()));
            Assert.That(editTransform.GetSiblingIndex(), Is.LessThan(leftColumn.GetSiblingIndex()));

            BattleTableFragmentEditLayer layer =
                editTransform.GetComponent<BattleTableFragmentEditLayer>();
            Assert.That(layer, Is.Not.Null);

            var battleSerialized = new SerializedObject(battle);
            Assert.That(
                battleSerialized.FindProperty("_fragmentEditLayer").objectReferenceValue,
                Is.SameAs(layer));

            var layerSerialized = new SerializedObject(layer);
            GameObject board =
                layerSerialized.FindProperty("_boardEditPanel").objectReferenceValue as GameObject;
            Button action =
                layerSerialized.FindProperty("_actionButton").objectReferenceValue as Button;
            Assert.That(board, Is.Not.Null);
            Assert.That(action, Is.Not.Null);
            Assert.That(board.transform.parent, Is.SameAs(editTransform));
            Assert.That(layer.IsConfigured, Is.True);
            Assert.That(board.transform.IsChildOf(center), Is.False,
                "BoardEditPanel 必须永久归属独立编辑层，不能再由 Center 运行时 reparent。 ");
        }

        [Test]
        public void BattleFormPrefab_OwnsIndependentInspectionLayerAndSeparateReadonlyRecipe()
        {
            const string path = "Assets/GameMain/Content/Prefabs/UI/BattleForm.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);

            BattleForm battle = prefab.GetComponent<BattleForm>();
            Transform hud = prefab.transform.Find("HudFrame");
            Transform rewardLayer = hud?.Find("RewardSubflowLayer");
            Transform inspectionTransform = hud?.Find("InspectionLayer");
            Transform leftColumn = hud?.Find("LeftColumn");
            Assert.That(battle, Is.Not.Null);
            Assert.That(rewardLayer, Is.Not.Null);
            Assert.That(inspectionTransform, Is.Not.Null);
            Assert.That(leftColumn, Is.Not.Null);
            Assert.That(inspectionTransform.GetComponent<CanvasGroup>(), Is.Not.Null);

            BattleInspectionLayer layer = inspectionTransform.GetComponent<BattleInspectionLayer>();
            Assert.That(layer, Is.Not.Null);
            Assert.That(layer.IsConfigured, Is.True);
            Assert.That(rewardLayer.GetSiblingIndex(), Is.LessThan(inspectionTransform.GetSiblingIndex()));
            Assert.That(inspectionTransform.GetSiblingIndex(), Is.LessThan(leftColumn.GetSiblingIndex()));

            var battleSerialized = new SerializedObject(battle);
            Assert.That(
                battleSerialized.FindProperty("_inspectionLayer").objectReferenceValue,
                Is.SameAs(layer));

            var layerSerialized = new SerializedObject(layer);
            Component inspectionRecipe =
                layerSerialized.FindProperty("_recipeView").objectReferenceValue as Component;
            Component tablePanel =
                layerSerialized.FindProperty("_tablePanel").objectReferenceValue as Component;
            Component functionalRecipe =
                battleSerialized.FindProperty("_recipeReadonlyBookView").objectReferenceValue as Component;
            Assert.That(inspectionRecipe, Is.Not.Null);
            Assert.That(tablePanel, Is.Not.Null);
            Assert.That(functionalRecipe, Is.Not.Null);
            Assert.That(inspectionRecipe, Is.Not.SameAs(functionalRecipe));
            Assert.That(inspectionRecipe.transform.parent, Is.SameAs(inspectionTransform));
            Assert.That(tablePanel.transform.parent, Is.SameAs(inspectionTransform));
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
        public void BattleFormPrefab_OverlaySuspensionOwnsAuthoredActionAxisGroup()
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
                serialized.FindProperty("_actionAxisGroup").objectReferenceValue,
                Is.SameAs(group));
        }

        [Test]
        public void TableFragmentRewardShell_SoftHideRestoresExactCanvasGroupState()
        {
            Type snapshotType = typeof(BattleOverlaySuspension).GetNestedType(
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
        public void OverlaySuspension_EditPolicyRestoresSourceExactlyOnce()
        {
            var root = new GameObject("OverlaySource");
            var centerObject = new GameObject("Center", typeof(CanvasGroup));
            var axisObject = new GameObject("Axis", typeof(CanvasGroup));
            var backdrop = new GameObject("Backdrop");
            var foodPanel = new GameObject("FoodPanel");
            var foodBarObject = new GameObject("FoodBar", typeof(BattleFoodActionBar));
            centerObject.transform.SetParent(root.transform, false);
            axisObject.transform.SetParent(root.transform, false);
            backdrop.transform.SetParent(root.transform, false);
            foodPanel.transform.SetParent(root.transform, false);
            foodBarObject.transform.SetParent(root.transform, false);

            CanvasGroup center = centerObject.GetComponent<CanvasGroup>();
            CanvasGroup axis = axisObject.GetComponent<CanvasGroup>();
            center.alpha = 0.6f;
            center.interactable = true;
            center.blocksRaycasts = false;
            axis.alpha = 0.8f;
            axis.interactable = false;
            axis.blocksRaycasts = true;
            var host = new OverlaySuspensionHost(
                center,
                axis,
                backdrop,
                foodPanel,
                foodBarObject.GetComponent<BattleFoodActionBar>());
            var suspension = new BattleOverlaySuspension(host);

            try
            {
                Assert.That(suspension.Suspend(
                    BattleOverlaySuspensionOptions.HideActionAxis
                    | BattleOverlaySuspensionOptions.HideBackdrop), Is.True);
                Assert.That(suspension.Suspend(BattleOverlaySuspensionOptions.None), Is.False);
                Assert.That(center.alpha, Is.Zero);
                Assert.That(axis.alpha, Is.Zero);
                Assert.That(backdrop.activeSelf, Is.False);
                Assert.That(foodPanel.activeSelf, Is.False);
                Assert.That(foodBarObject.activeSelf, Is.False);

                Assert.That(suspension.Restore(), Is.True);
                Assert.That(suspension.Restore(), Is.False);
                Assert.That(center.alpha, Is.EqualTo(0.6f));
                Assert.That(center.interactable, Is.True);
                Assert.That(center.blocksRaycasts, Is.False);
                Assert.That(axis.alpha, Is.EqualTo(0.8f));
                Assert.That(axis.interactable, Is.False);
                Assert.That(axis.blocksRaycasts, Is.True);
                Assert.That(backdrop.activeSelf, Is.True);
                Assert.That(foodPanel.activeSelf, Is.True);
                Assert.That(foodBarObject.activeSelf, Is.True);
                Assert.That(host.HideTipsCount, Is.EqualTo(1));
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
        public void CoveredMainPageSwap_RestoresCenterContentAndBackButtonInput()
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
        public void RecipeInspectionAvailability_OnlyDependsOnRealGameplayPage()
        {
            Assert.That(BattleInfoColumn.CanOpenRecipeInspection(GameplayView.Shop), Is.True);
            Assert.That(BattleInfoColumn.CanOpenRecipeInspection(GameplayView.Food), Is.True);
            Assert.That(BattleInfoColumn.CanOpenRecipeInspection(GameplayView.ActionSelect), Is.True);
            Assert.That(BattleInfoColumn.CanOpenRecipeInspection(GameplayView.RecipeSelection), Is.False);

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
        public void AfterMealTeaSet_TriggersInEarliestSettlementPhase()
        {
            var model = new LastServeMultFlatModel();
            using IEnumerator<ItemScoreSpec> specs =
                model.BuildScoreSpecs().GetEnumerator();

            Assert.That(specs.MoveNext(), Is.True);
            Assert.That(
                specs.Current.Type,
                Is.EqualTo(ItemScoreEffectType.NthServeMultFlat));

            var collector = new ScoreEffectCollector();
            new ItemScoreEffectSource(new[] { specs.Current })
                .CollectEffects(null, collector);

            Assert.That(collector.Entries, Has.Count.EqualTo(1));
            Assert.That(
                collector.Entries[0].Phase,
                Is.EqualTo(ScorePhase.BeforeAll));
            Assert.That(specs.MoveNext(), Is.False);
        }

        [Test]
        public void CandyCanePreviewBoard_ContainsOnlyOccupiedCells()
        {
            DishShape shape = DishShape.FromRows(new[]
            {
                "XX",
                ".X",
                ".X",
            });

            IReadOnlyList<GridPos> cells =
                DishIconPreviewRenderer.OccupiedBoardCells(shape);

            Assert.That(cells.Count, Is.EqualTo(4));
            CollectionAssert.Contains(cells, new GridPos(0, 0));
            CollectionAssert.Contains(cells, new GridPos(1, 0));
            CollectionAssert.Contains(cells, new GridPos(1, 1));
            CollectionAssert.Contains(cells, new GridPos(1, 2));
            CollectionAssert.DoesNotContain(cells, new GridPos(0, 1));
            CollectionAssert.DoesNotContain(cells, new GridPos(0, 2));
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

        [Test]
        public void RewardSubflowInspectionSuspend_HidesLayerAndRestoresTheSameTopFrameOnce()
        {
            var panelObject = new GameObject("RewardPanel", typeof(CanvasGroup));
            var host = new EmptyRewardHost();
            var coordinator = new RewardPageCoordinator(host);

            try
            {
                Component panel = panelObject.GetComponent<CanvasGroup>();
                SeedRewardFrame(coordinator, panel);

                Assert.That(coordinator.SuspendForInspection(), Is.True);
                Assert.That(coordinator.IsInspectionSuspended, Is.True);
                Assert.That(coordinator.Depth, Is.EqualTo(1));
                Assert.That(host.LayerHiddenCount, Is.EqualTo(1));

                Assert.That(coordinator.SuspendForInspection(), Is.False);
                Assert.That(host.LayerHiddenCount, Is.EqualTo(1));

                coordinator.ResumeFromInspection();

                Assert.That(coordinator.IsInspectionSuspended, Is.False);
                Assert.That(coordinator.Depth, Is.EqualTo(1));
                Assert.That(host.LayerPreparedCount, Is.EqualTo(1));
                Assert.That(host.PanelShownCount, Is.EqualTo(1));
                Assert.That(host.LastShownPanel, Is.SameAs(panel));

                coordinator.ResumeFromInspection();
                Assert.That(host.LayerPreparedCount, Is.EqualTo(1));
                Assert.That(host.PanelShownCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(panelObject);
            }
        }

        [Test]
        public void ClosingRewardSubflowWhileInspectionSuspended_PreventsLaterRestore()
        {
            var panelObject = new GameObject("RewardPanel", typeof(CanvasGroup));
            var host = new EmptyRewardHost();
            var coordinator = new RewardPageCoordinator(host);
            int closed = 0;

            try
            {
                SeedRewardFrame(coordinator, panelObject.GetComponent<CanvasGroup>(), () => closed++);
                Assert.That(coordinator.SuspendForInspection(), Is.True);

                coordinator.CloseRewardPages();
                coordinator.ResumeFromInspection();

                Assert.That(closed, Is.EqualTo(1));
                Assert.That(coordinator.IsActive, Is.False);
                Assert.That(coordinator.IsInspectionSuspended, Is.False);
                Assert.That(host.LayerPreparedCount, Is.Zero);
                Assert.That(host.PanelShownCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(panelObject);
            }
        }

        [Test]
        public void InspectionForceClose_DuringInitialTransition_RestoresFrozenSourceOnce()
        {
            var recipeObject = new GameObject(
                "InspectionRecipe",
                typeof(RectTransform),
                typeof(RecipeReadonlyBookView));
            var layer = new PendingInspectionLayer(
                recipeObject.GetComponent<RecipeReadonlyBookView>());
            var host = new PendingInspectionHost(layer);
            var coordinator = new BattleInspectionCoordinator(host);

            try
            {
                coordinator.OpenRecipe(0);

                Assert.That(host.CurrentView, Is.EqualTo(GameplayView.Shop));
                Assert.That(host.BeginCount, Is.EqualTo(1));
                Assert.That(layer.RequestedView, Is.EqualTo(BattleInspectionView.Recipe));
                Assert.That(coordinator.IsActive, Is.True,
                    "淡入交换点之前也必须记录为活跃，否则切页会遗留被冻结来源。");

                coordinator.ForceClose();
                coordinator.ForceClose();

                Assert.That(host.RestoreCount, Is.EqualTo(1));
                Assert.That(host.RestoredView, Is.EqualTo(GameplayView.Shop));
                Assert.That(layer.ForceHideCount, Is.EqualTo(2));
                Assert.That(coordinator.IsActive, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(recipeObject);
            }
        }

        [Test]
        public void TableFragmentEdit_LeavesGameplayViewUnchangedAndRestoresSourceExactlyOnce()
        {
            var host = new PendingTableFragmentEditHost();
            var coordinator = new BattleTableFragmentEditCoordinator(host);
            int completed = 0;
            bool? placed = null;

            bool opened = coordinator.Open(
                new[] { "fragment_a", "fragment_b" },
                result =>
                {
                    completed++;
                    placed = result;
                    host.Order.Add("Completed");
                });

            Assert.That(opened, Is.True);
            Assert.That(host.CurrentView, Is.EqualTo(GameplayView.Shop));
            Assert.That(coordinator.SourceView, Is.EqualTo(GameplayView.Shop));
            Assert.That(host.SuspendCount, Is.EqualTo(1));
            Assert.That(host.Layer.ShowCount, Is.EqualTo(1));
            Assert.That(host.Request, Is.Not.Null);

            host.Request.Completed(true);
            host.Request.Completed(true);

            Assert.That(completed, Is.EqualTo(1));
            Assert.That(placed, Is.True);
            Assert.That(host.RestoreCount, Is.EqualTo(1));
            Assert.That(host.HideWorldCount, Is.EqualTo(1));
            Assert.That(host.Layer.HideCount, Is.EqualTo(1));
            Assert.That(coordinator.IsActive, Is.False);
            CollectionAssert.AreEqual(
                new[]
                {
                    "InspectionBlocked:True",
                    "PreparingChild",
                    "Suspend",
                    "BeginWorld",
                    "LayerShow",
                    "ChildReady",
                    "PreparingReturn",
                    "HideWorld",
                    "Restore",
                    "InspectionBlocked:False",
                    "Refresh",
                    "Completed",
                    "LayerHide",
                    "ParentRestored",
                },
                host.Order);
        }

        [Test]
        public void TableFragmentEdit_RejectsConcurrentOpenAndForceCloseDoesNotInvokeCompletion()
        {
            var host = new PendingTableFragmentEditHost();
            var coordinator = new BattleTableFragmentEditCoordinator(host);
            int firstCompleted = 0;
            int rejectedCompleted = 0;

            Assert.That(
                coordinator.Open(new[] { "fragment_a" }, _ => firstCompleted++),
                Is.True);
            Assert.That(
                coordinator.Open(new[] { "fragment_b" }, result =>
                {
                    Assert.That(result, Is.False);
                    rejectedCompleted++;
                }),
                Is.False);

            coordinator.ForceClose();
            coordinator.ForceClose();

            Assert.That(firstCompleted, Is.Zero);
            Assert.That(rejectedCompleted, Is.EqualTo(1));
            Assert.That(host.SuspendCount, Is.EqualTo(1));
            Assert.That(host.RestoreCount, Is.EqualTo(1));
            Assert.That(host.HideWorldCount, Is.EqualTo(1));
            Assert.That(coordinator.IsActive, Is.False);
        }

        private sealed class PendingInspectionLayer : IBattleInspectionLayer
        {
            public PendingInspectionLayer(RecipeReadonlyBookView recipeView)
            {
                RecipeView = recipeView;
            }

            public BattleInspectionView RequestedView { get; private set; }
            public int ForceHideCount { get; private set; }
            public bool IsVisible => RequestedView != BattleInspectionView.None;
            public bool IsTransitioning => RequestedView != BattleInspectionView.None;
            public RecipeReadonlyBookView RecipeView { get; }
            public ViewTablePanel TablePanel => null;
            public void Initialize() { }
            public void TransitionTo(BattleInspectionView view, Action atSwap = null, Action onShown = null)
            {
                RequestedView = view;
            }
            public void Hide(Action onHidden = null)
            {
                RequestedView = BattleInspectionView.None;
                onHidden?.Invoke();
            }
            public void ForceHide()
            {
                ForceHideCount++;
                RequestedView = BattleInspectionView.None;
            }
        }

        private sealed class OverlaySuspensionHost : IBattleOverlaySuspensionHost
        {
            public OverlaySuspensionHost(
                CanvasGroup center,
                CanvasGroup axis,
                GameObject backdrop,
                GameObject foodPanel,
                BattleFoodActionBar foodBar)
            {
                Center = center;
                ActionAxisGroup = axis;
                Backdrop = backdrop;
                FoodBattlePanel = foodPanel;
                FoodActionBar = foodBar;
            }

            public int HideTipsCount { get; private set; }
            public CanvasGroup Center { get; }
            public CanvasGroup ActionAxisGroup { get; }
            public GameObject Backdrop { get; }
            public GameObject FoodBattlePanel { get; }
            public BattleFoodActionBar FoodActionBar { get; }
            public ServingOutletView ResolveServingOutlet() => null;
            public FoodDiscardBinView ResolveFoodDiscardBin() => null;
            public void SetFoodBattlePanelVisible(bool visible)
            {
                FoodBattlePanel.SetActive(visible);
                FoodActionBar.SetVisible(visible);
            }
            public void HideAllTips() => HideTipsCount++;
        }

        private sealed class PendingTableFragmentEditLayer : IBattleTableFragmentEditLayer
        {
            private readonly List<string> _order;

            public PendingTableFragmentEditLayer(List<string> order)
            {
                _order = order;
            }

            public int ShowCount { get; private set; }
            public int HideCount { get; private set; }
            public bool IsVisible { get; private set; }
            public bool IsConfigured => true;
            public GameObject BoardEditPanel => null;
            public void Initialize() { }
            public void Bind(Action onAction) { }
            public void Show()
            {
                ShowCount++;
                IsVisible = true;
                _order.Add("LayerShow");
            }
            public void HideImmediate()
            {
                HideCount++;
                IsVisible = false;
                _order.Add("LayerHide");
            }
            public void ApplyActionState(TableFragmentEditActionState state) { }
        }

        private sealed class PendingTableFragmentEditHost : IBattleTableFragmentEditHost
        {
#pragma warning disable SYSLIB0050
            private readonly GameRun _run = (GameRun)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(GameRun));
#pragma warning restore SYSLIB0050

            public PendingTableFragmentEditHost()
            {
                Layer = new PendingTableFragmentEditLayer(Order);
            }

            public List<string> Order { get; } = new List<string>();
            public PendingTableFragmentEditLayer Layer { get; }
            public TableFragmentChoiceRequest Request { get; private set; }
            public int SuspendCount { get; private set; }
            public int RestoreCount { get; private set; }
            public int HideWorldCount { get; private set; }
            public GameRun Run => _run;
            public GameplayView CurrentView => GameplayView.Shop;
            public IBattleTableFragmentEditLayer FragmentEditLayer => Layer;
            public bool CanInteract => true;
            public bool CanOpenFragmentEdit => true;
            public IReadOnlyList<int> CandidateRotations => Array.Empty<int>();
            public void ForceCloseInspection() { }
            public void CancelActiveItemUse() { }
            public bool SuspendFragmentEditSource()
            {
                SuspendCount++;
                Order.Add("Suspend");
                return true;
            }
            public void RestoreFragmentEditSource()
            {
                RestoreCount++;
                Order.Add("Restore");
            }
            public void RestoreBattleWorld() => Order.Add("RestoreBattleWorld");
            public void BeginTableFragmentChoice(TableFragmentChoiceRequest request)
            {
                Request = request;
                Order.Add("BeginWorld");
            }
            public void ConfirmTableEditPlacement() { }
            public void SkipTableEditPack() { }
            public void HideTableEditWorld()
            {
                HideWorldCount++;
                Order.Add("HideWorld");
            }
            public void SetInspectionNavigationBlocked(bool blocked) =>
                Order.Add($"InspectionBlocked:{blocked}");
            public void RefreshPersistent() => Order.Add("Refresh");
            public void NotifyPreparingChild() => Order.Add("PreparingChild");
            public void NotifyChildReady() => Order.Add("ChildReady");
            public void NotifyPreparingReturn() => Order.Add("PreparingReturn");
            public void NotifyParentRestored() => Order.Add("ParentRestored");
        }

        private sealed class PendingInspectionHost : IBattleInspectionHost
        {
#pragma warning disable SYSLIB0050
            private readonly GameRun _run = (GameRun)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(GameRun));
#pragma warning restore SYSLIB0050

            public PendingInspectionHost(IBattleInspectionLayer layer)
            {
                InspectionLayer = layer;
            }

            public int BeginCount { get; private set; }
            public int RestoreCount { get; private set; }
            public GameplayView RestoredView { get; private set; } = GameplayView.None;
            public GameRun Run => _run;
            public BattleSession Session => null;
            public GameplayView CurrentView => GameplayView.Shop;
            public BattleWorldController World => null;
            public IBattleInspectionLayer InspectionLayer { get; }
            public bool ActionAxisVisible => true;
            public void SetActionAxisVisible(bool visible) { }
            public void BeginInspectionSource() => BeginCount++;
            public void RestoreInspectionSource(GameplayView sourceView)
            {
                RestoreCount++;
                RestoredView = sourceView;
            }
            public void RestoreBattleWorld() { }
            public void BindWorldHoverCallbacks() { }
            public void RefreshPersistent() { }
            public FoodTipsView FoodTips() => null;
        }

        private sealed class EmptyRewardHost : IRewardPageHost
        {
            public int LayerHiddenCount { get; private set; }
            public int LayerPreparedCount { get; private set; }
            public int PanelShownCount { get; private set; }
            public Component LastShownPanel { get; private set; }

            public GameRun Run => null;
            public RewardDishPackPanel RewardDishPackPanel => null;
            public RewardItemChoicePanel RewardItemChoicePanel => null;
            public RandomizedItemsPanel RandomizedItemsPanel => null;
            public void PrepareRewardSubflowLayer() => LayerPreparedCount++;
            public void ShowRewardSubflowPanel(Component panel)
            {
                PanelShownCount++;
                LastShownPanel = panel;
            }
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

        private static void SeedRewardFrame(
            RewardPageCoordinator coordinator,
            Component panel,
            Action close = null)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public;
            Type frameType = typeof(RewardPageCoordinator).GetNestedType(
                "Frame",
                System.Reflection.BindingFlags.NonPublic);
            Assert.That(frameType, Is.Not.Null);

            object frame = Activator.CreateInstance(frameType);
            frameType.GetField("Kind", flags)?.SetValue(frame, RewardSubflowKind.DishPack);
            frameType.GetField("Panel", flags)?.SetValue(frame, panel);
            frameType.GetField("Close", flags)?.SetValue(frame, close);

            object stack = typeof(RewardPageCoordinator).GetField("_stack", flags)?.GetValue(coordinator);
            Assert.That(stack, Is.Not.Null);
            stack.GetType().GetMethod("Add")?.Invoke(stack, new[] { frame });
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
