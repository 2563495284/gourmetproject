using System.Collections.Generic;
using System.IO;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class EventEffectRuntimeTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [SetUp]
        public void SetUp()
        {
            string configRoot = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(tableName =>
                JSON.Parse(File.ReadAllText(Path.Combine(configRoot, tableName + ".json"))));
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void BusinessGoldEventMultipliers_MultiplyConsumeAndClearAtWeekChange()
        {
            GameRun run = CreateRun();
            cfg.GameAction normalAction = FindFoodAction(cfg.FoodActionKind.Normal);
            run.SetLastActionContext(new ActionExecutionContext(normalAction));
            run.AddBusinessGoldPct(0.3f, nextBusiness: false);
            run.AddBusinessGoldPct(0.6f, nextBusiness: true);
            run.AddBusinessGoldPct(-0.4f, nextBusiness: true);

            int before = run.Gold;
            RewardGranter.ApplyBaseGold(run, EmptyOffer(100));
            Assert.That(run.Gold - before, Is.EqualTo(125));

            before = run.Gold;
            RewardGranter.ApplyBaseGold(run, EmptyOffer(100));
            Assert.That(run.Gold - before, Is.EqualTo(130), "下次营业倍率只应消费一次");

            run.IncrementWeek();
            before = run.Gold;
            RewardGranter.ApplyBaseGold(run, EmptyOffer(100));
            Assert.That(run.Gold - before, Is.EqualTo(100), "本周倍率换周后应清除");
        }

        [Test]
        public void BossModifiers_AccumulateLinearlyAndDoNotConsumeBusinessMultiplier()
        {
            GameRun run = CreateRun();
            run.AddBossTargetScorePct(0.2f);
            run.AddBossTargetScorePct(0.2f);
            run.AddBossBaseGoldPct(1f);
            run.AddBossBaseGoldPct(1f);
            run.AddBusinessGoldPct(0.6f, nextBusiness: true);

            Assert.That(run.ModifyBossTargetScore(100), Is.EqualTo(140));

            run.SetLastActionContext(new ActionExecutionContext(FindFoodAction(cfg.FoodActionKind.Feast)));
            int before = run.Gold;
            RewardGranter.ApplyBaseGold(run, EmptyOffer(100));
            Assert.That(run.Gold - before, Is.EqualTo(300));

            run.SetLastActionContext(new ActionExecutionContext(FindFoodAction(cfg.FoodActionKind.Normal)));
            before = run.Gold;
            RewardGranter.ApplyBaseGold(run, EmptyOffer(100));
            Assert.That(run.Gold - before, Is.EqualTo(160));
        }

        [Test]
        public void RandomActiveReward_IsFixedInPendingRewardSave()
        {
            GameRun run = CreateRun();
            const string param = "Ids:item_active_lay_gold|Replace";
            EffectResolver.Apply(run, cfg.EffectType.GrantRandomActiveItems, 2, param, new Xoshiro256SS(123UL));

            Assert.That(run.TryPeekPendingGenericReward(out _, out _, out RewardOffer before), Is.True);
            Assert.That(before.MainChoices.Count, Is.EqualTo(2));
            Assert.That(before.MainChoices[0].Id, Is.EqualTo(before.MainChoices[1].Id), "放回随机应允许同一结果出现两次");
            var expected = new List<string> { before.MainChoices[0].Id, before.MainChoices[1].Id };

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            Assert.That(restored.TryPeekPendingGenericReward(out _, out _, out RewardOffer after), Is.True);
            Assert.That(after.MainChoices.Count, Is.EqualTo(2));
            Assert.That(new[] { after.MainChoices[0].Id, after.MainChoices[1].Id }, Is.EqualTo(expected));
        }

        [Test]
        public void EventMultiplierState_PersistsAndOldSaveDefaultsAreNeutral()
        {
            var oldSaveDefaults = new RunSaveData();
            Assert.That(oldSaveDefaults.NextBusinessGoldMultiplier, Is.EqualTo(1f));
            Assert.That(oldSaveDefaults.CurrentWeekBusinessGoldMultiplier, Is.EqualTo(1f));

            GameRun run = CreateRun();
            run.AddBusinessGoldPct(0.6f, nextBusiness: true);
            run.AddBusinessGoldPct(0.3f, nextBusiness: false);
            run.AddBossTargetScorePct(0.2f);
            run.AddBossBaseGoldPct(1f);

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            Assert.That(restored.ConsumeNextBusinessGoldMultiplier(), Is.EqualTo(1.6f).Within(0.0001f));
            Assert.That(restored.CurrentWeekBusinessGoldMultiplier, Is.EqualTo(1.3f).Within(0.0001f));
            Assert.That(restored.ModifyBossTargetScore(100), Is.EqualTo(120));
            Assert.That(restored.BossBaseGoldMultiplier, Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public void EscalatingHospitalFee_IncreasesAcrossRunAndClampsAtZero()
        {
            GameRun run = CreateRun();
            run.Gold = 100;

            string first = EffectResolver.Apply(
                run,
                cfg.EffectType.LoseEscalatingGold,
                20,
                "mushroom_poison|20",
                new Xoshiro256SS(1UL));
            Assert.That(run.Gold, Is.EqualTo(80));
            Assert.That(first, Does.Contain("第 1 次中毒"));

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            string second = EffectResolver.Apply(
                restored,
                cfg.EffectType.LoseEscalatingGold,
                20,
                "mushroom_poison|20",
                new Xoshiro256SS(2UL));
            Assert.That(restored.Gold, Is.EqualTo(40));
            Assert.That(second, Does.Contain("第 2 次中毒"));

            string third = EffectResolver.Apply(
                restored,
                cfg.EffectType.LoseEscalatingGold,
                20,
                "mushroom_poison|20",
                new Xoshiro256SS(3UL));
            Assert.That(restored.Gold, Is.Zero, "金币不足时应扣到 0，不应出现负数");
            Assert.That(third, Does.Contain("抢救费应为 60 金币"));
        }

        [Test]
        public void MushroomFeast_ConfiguresEligibilityBranchesAndRewards()
        {
            cfg.GameEvent mushroom = _tables.TbEvent.Get("ev_crossroad_sign");
            Assert.That(mushroom.Preconditions, Is.EqualTo("minGold:61"));

            GameRun run = CreateRun();
            run.Gold = 60;
            Assert.That(PreconditionEvaluator.IsSatisfied(run, mushroom.Preconditions), Is.False);
            run.Gold = 61;
            Assert.That(PreconditionEvaluator.IsSatisfied(run, mushroom.Preconditions), Is.True);

            AssertOption("opt_mushroom_white_poison", 20f, cfg.EffectType.LoseEscalatingGold, 20f);
            AssertOption("opt_mushroom_white_safe", 80f, cfg.EffectType.None, 0f);
            AssertOption("opt_mushroom_red_poison", 40f, cfg.EffectType.LoseEscalatingGold, 20f);
            AssertOption("opt_mushroom_red_safe", 60f, cfg.EffectType.None, 0f);
            AssertOption("opt_mushroom_green_poison", 60f, cfg.EffectType.LoseEscalatingGold, 20f);
            AssertOption("opt_mushroom_green_safe", 40f, cfg.EffectType.GainLegendaryItem, 1f);

            cfg.EventOption whiteCashout = _tables.TbEventOption.Get("opt_mushroom_white_cashout");
            cfg.EventOption redCashout = _tables.TbEventOption.Get("opt_mushroom_red_cashout");
            Assert.That(whiteCashout.EffectValues[0], Is.EqualTo(70f));
            Assert.That(redCashout.EffectValues[0], Is.EqualTo(120f));
            Assert.That(_tables.TbEventOption.Get("opt_mushroom_green_safe").EffectParams[0], Does.StartWith("legendary_choice_1"));
        }

        [Test]
        public void EventTree_ParentsOnlyNavigateAndLeavesAutoEnd()
        {
            var parentIds = new HashSet<string>();
            foreach (cfg.EventOption option in _tables.TbEventOption.DataList)
            {
                if (!string.IsNullOrEmpty(option.ParentId))
                {
                    parentIds.Add(option.ParentId);
                }
            }

            foreach (cfg.EventOption option in _tables.TbEventOption.DataList)
            {
                if (parentIds.Contains(option.Id))
                {
                    Assert.That(option.AutoEnd, Is.False, option.Id);
                    Assert.That(option.EffectTypes, Is.EqualTo(new[] { cfg.EffectType.None }), option.Id);
                }
                else
                {
                    Assert.That(option.AutoEnd, Is.True, option.Id);
                }
            }
        }

        private GameRun CreateRun()
        {
            string characterId = _tables.TbCharacter.DataList[0].Id;
            var run = new GameRun(_tables, _database, characterId, "event-effect-test");
            var itemIds = new List<string>();
            foreach (RunItemState item in run.Items)
            {
                itemIds.Add(item.ItemId);
            }

            foreach (string itemId in itemIds)
            {
                run.RemoveItem(itemId);
            }

            return run;
        }

        private cfg.GameAction FindFoodAction(cfg.FoodActionKind kind)
        {
            foreach (cfg.GameAction action in _tables.TbAction.DataList)
            {
                cfg.Food food = FoodService.Resolve(_tables, action);
                if (food != null && food.ActionKind == kind)
                {
                    return action;
                }
            }

            throw new AssertionException($"没有找到 {kind} 经营挑战行动");
        }

        private void AssertOption(string id, float branchWeight, cfg.EffectType effectType, float effectValue)
        {
            cfg.EventOption option = _tables.TbEventOption.Get(id);
            Assert.That(option.BranchWeight, Is.EqualTo(branchWeight), id);
            Assert.That(option.BranchPageText, Is.Not.Empty, id);
            Assert.That(option.Text, Is.Not.Empty, id);
            Assert.That(option.EffectTypes[0], Is.EqualTo(effectType), id);
            Assert.That(option.EffectValues[0], Is.EqualTo(effectValue), id);
        }

        private static RewardOffer EmptyOffer(int baseGold)
        {
            return new RewardOffer(
                baseGold,
                (IReadOnlyList<RewardChoice>)null,
                (IReadOnlyList<RewardChoice>)null);
        }
    }
}
