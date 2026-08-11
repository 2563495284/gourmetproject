using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class RewardActiveItemCapacityTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            string configDirectory = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name =>
                JSON.Parse(File.ReadAllText(Path.Combine(configDirectory, name + ".json"))));
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void TryClaimChoice_FullActiveSlotsRejectsWithoutGoldConversion()
        {
            var run = new GameRun(_tables, _database, "glutton_dog", "reward-capacity-test", 1);
            cfg.ActiveItem activeItem = _tables.TbActiveItem.DataList[0];

            while (run.HasFreeActiveSlot)
            {
                ItemAcquireResult result = run.AcquireItem(activeItem.Id, fallbackGold: 0, fireOnAcquire: false);
                Assert.That(result.Outcome, Is.EqualTo(ItemAcquireOutcome.Stacked));
            }

            int countBefore = run.ActiveItemCount;
            int goldBefore = run.Gold;
            var choice = new RewardChoice(
                cfg.RewardKind.ActiveItemGrant,
                activeItem.Id,
                activeItem.Name,
                activeItem.Desc,
                goldAmount: 40);

            bool claimed = RewardGranter.TryClaimChoice(run, choice, out string rewardText);

            Assert.That(claimed, Is.False);
            Assert.That(rewardText, Is.Empty);
            Assert.That(run.ActiveItemCount, Is.EqualTo(countBefore));
            Assert.That(run.Gold, Is.EqualTo(goldBefore));
        }

        [Test]
        public void ItemChoicePanel_RejectedPickStaysUnclaimedAndStartsFailureShake()
        {
            var panelObject = new GameObject("RewardItemChoicePanel", typeof(RectTransform));
            var cardObject = new GameObject("RewardItemChoiceCard", typeof(RectTransform));
            RewardItemChoicePanel panel = panelObject.AddComponent<RewardItemChoicePanel>();
            RewardItemChoiceCardView card = cardObject.AddComponent<RewardItemChoiceCardView>();

            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var cards = (List<RewardItemChoiceCardView>)typeof(RewardItemChoicePanel)
                    .GetField("_cards", flags)
                    ?.GetValue(panel);
                cards?.Add(card);
                typeof(RewardItemChoicePanel)
                    .GetField("_onPick", flags)
                    ?.SetValue(panel, new Func<RewardItemChoiceCardView, int, bool>((_, _) => false));

                typeof(RewardItemChoicePanel)
                    .GetMethod("OnCardClicked", flags)
                    ?.Invoke(panel, new object[] { 0 });

                var claimed = (HashSet<int>)typeof(RewardItemChoicePanel)
                    .GetField("_claimedOnPage", flags)
                    ?.GetValue(panel);
                object failureTween = typeof(RewardItemChoiceCardView)
                    .GetField("_failureTween", flags)
                    ?.GetValue(card);

                Assert.That(claimed, Is.Empty);
                Assert.That(failureTween, Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cardObject);
                UnityEngine.Object.DestroyImmediate(panelObject);
            }
        }

        [Test]
        public void KitchenGodNegativeReward_GrantsGoldAndNegativePassiveWithoutRewardQueue()
        {
            var run = new GameRun(_tables, _database, "glutton_dog", "kitchen-god-direct-test", 1);
            cfg.EventOption option = _tables.TbEventOption.Get("opt_kitchen_god_statue_choice_2");
            var rng = new Xoshiro256SS(0xC0FFEEUL);
            int goldBefore = run.Gold;

            EventService.ResolveOption(
                run,
                option,
                rng,
                cfg.EffectType.GrantRandomPassiveItems);
            RandomizedItemResult result = EffectResolver.GrantRandomNegativePassiveDirect(run, rng);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Acquired, Is.True);
            Assert.That(result.Item.IsNegative, Is.True);
            Assert.That(run.HasItem(result.Item.Id), Is.True);
            Assert.That(run.Gold, Is.EqualTo(goldBefore + 100));
            Assert.That(run.HasPendingGenericRewards, Is.False);
        }

        [Test]
        public void KitchenGodNegativeReward_WhenPoolIsEmpty_AddsFortyFallbackGoldWithoutQueue()
        {
            var run = new GameRun(_tables, _database, "glutton_dog", "kitchen-god-empty-test", 1);
            foreach (cfg.PassiveItem passive in _tables.TbPassiveItem.DataList)
            {
                ItemDefinition item = ItemDefinition.From(passive);
                if (item.IsNegative && !run.HasItem(item.Id))
                {
                    run.AcquireItem(item.Id, fallbackGold: 0, fireOnAcquire: false);
                }
            }

            cfg.EventOption option = _tables.TbEventOption.Get("opt_kitchen_god_statue_choice_2");
            var rng = new Xoshiro256SS(0xBAD5EEDUL);
            int goldBefore = run.Gold;

            EventService.ResolveOption(
                run,
                option,
                rng,
                cfg.EffectType.GrantRandomPassiveItems);
            RandomizedItemResult result = EffectResolver.GrantRandomNegativePassiveDirect(run, rng);

            Assert.That(result, Is.Null);
            Assert.That(run.Gold, Is.EqualTo(goldBefore + 140));
            Assert.That(run.HasPendingGenericRewards, Is.False);
        }

        [Test]
        public void PassiveAcquireScroll_TargetVisibleKeepsPosition_OverflowScrollsToBottom()
        {
            float visible = BattleItemsColumn.CalculatePassiveAcquireEndTop(
                index: 3,
                slotHeight: 100f,
                currentTop: 0f,
                viewportHeight: 220f,
                contentHeight: 220f);
            float overflow = BattleItemsColumn.CalculatePassiveAcquireEndTop(
                index: 8,
                slotHeight: 100f,
                currentTop: 0f,
                viewportHeight: 220f,
                contentHeight: 520f);

            Assert.That(visible, Is.EqualTo(0f));
            Assert.That(overflow, Is.EqualTo(300f));
        }

        [Test]
        public void PassiveAcquirePlan_AppendsOneSlotWithoutReplacingExistingSlots()
        {
            GameObject battlePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/GameMain/Content/Prefabs/UI/BattleForm.prefab");
            BattleItemsColumn prefabColumn = battlePrefab.GetComponentInChildren<BattleItemsColumn>(true);
            GameObject layerObject = new GameObject("Layer", typeof(RectTransform));
            GameObject columnObject = UnityEngine.Object.Instantiate(prefabColumn.gameObject, layerObject.transform);
            var layer = layerObject.GetComponent<RectTransform>();
            layer.sizeDelta = new Vector2(1920f, 1080f);

            try
            {
                var run = new GameRun(_tables, _database, "glutton_dog", "passive-local-append-test", 1);
                int added = 0;
                foreach (cfg.PassiveItem passive in _tables.TbPassiveItem.DataList)
                {
                    ItemDefinition definition = ItemDefinition.From(passive);
                    if (definition.IsNegative || run.HasItem(definition.Id))
                    {
                        continue;
                    }

                    run.AcquireItem(definition.Id, fallbackGold: 0, fireOnAcquire: false);
                    if (++added == 4)
                    {
                        break;
                    }
                }

                BattleItemsColumn column = columnObject.GetComponent<BattleItemsColumn>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(BattleItemsColumn)
                    .GetMethod("RefreshPassive", flags)
                    ?.Invoke(column, new object[] { run, _tables, null, null });
                var slots = (System.Collections.IList)typeof(BattleItemsColumn)
                    .GetField("_passiveSlots", flags)
                    ?.GetValue(column);
                object[] existing = new object[slots.Count];
                slots.CopyTo(existing, 0);

                RandomizedItemResult direct = EffectResolver.GrantRandomNegativePassiveDirect(
                    run,
                    new Xoshiro256SS(0x51A7UL));
                bool prepared = column.TryPreparePassiveAcquire(
                    run,
                    direct.Item,
                    layer,
                    null,
                    null,
                    out BattleItemsColumn.PassiveAcquirePlan plan);

                Assert.That(prepared, Is.True);
                Assert.That(plan, Is.Not.Null);
                Assert.That(slots.Count, Is.EqualTo(existing.Length + 1));
                for (int i = 0; i < existing.Length; i++)
                {
                    Assert.That(slots[i], Is.SameAs(existing[i]));
                }

                plan.Complete();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(columnObject);
                UnityEngine.Object.DestroyImmediate(layerObject);
            }
        }
    }
}
