using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Battle.Pages;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Tooltips;
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
            Assert.That(layerTransform.gameObject.activeSelf, Is.False);

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
        public void BattleFormPrefab_TableFragmentRewardCanSoftHideTheCompleteBattleShell()
        {
            const string path = "Assets/GameMain/Content/Prefabs/UI/BattleForm.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);

            BattleForm battle = prefab.GetComponent<BattleForm>();
            Assert.That(battle, Is.Not.Null);
            var serialized = new SerializedObject(battle);

            (string property, string hierarchyPath)[] shellGroups =
            {
                ("_rewardTableEditActionAxisGroup", "HudFrame/TopAxis"),
                ("_rewardTableEditLeftColumnGroup", "HudFrame/LeftColumn"),
                ("_rewardTableEditRightColumnGroup", "HudFrame/RightColumn"),
            };

            foreach ((string property, string hierarchyPath) in shellGroups)
            {
                Transform root = prefab.transform.Find(hierarchyPath);
                Assert.That(root, Is.Not.Null, hierarchyPath);
                CanvasGroup group = root.GetComponent<CanvasGroup>();
                Assert.That(group, Is.Not.Null,
                    $"{hierarchyPath} 必须在 prefab 上固定 CanvasGroup，禁止运行时生成。");
                Assert.That(
                    serialized.FindProperty(property).objectReferenceValue,
                    Is.SameAs(group),
                    property);
            }
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
    }
}
