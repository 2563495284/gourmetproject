#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public sealed class SlotFragmentRewardLimitTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            string directory = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name =>
                JSON.Parse(File.ReadAllText(Path.Combine(directory, name + ".json"))));
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void Roll_FirstFragmentRewardIsAvailableAndReportsItsKind()
        {
            GameRun run = CreateRun();
            SlotMachineConfig config = GetSlotConfig(run);
            int fragmentWeightIndex = FindFragmentSlotIndex(config) + 1;
            var rng = new PreferredWeightRandomStream(fragmentWeightIndex);

            SlotSpinResult result = SlotService.Roll(
                run,
                config,
                rng,
                actionContext: null,
                fragmentRewardAlreadyGranted: false);

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.IsEmpty, Is.False);
            Assert.That(result.RewardKind, Is.EqualTo(cfg.RewardKind.FragmentChoice));
            Assert.That(result.Offer, Is.Not.Null);
            Assert.That(result.Offer.MainChoices, Has.Count.EqualTo(2));
            Assert.That(result.Offer.MainRequiredChoiceCount, Is.EqualTo(1));
        }

        [Test]
        public void Configuration_AllFragmentRewardsOfferTwoAndRequireOne()
        {
            List<cfg.RewardSlot> fragmentSlots = _tables.TbRewardSlot.DataList
                .Where(slot => slot.Kind == cfg.RewardKind.FragmentChoice)
                .ToList();

            Assert.That(fragmentSlots, Is.Not.Empty);
            foreach (cfg.RewardSlot slot in fragmentSlots)
            {
                Assert.That(slot.ChoiceCount, Is.EqualTo(2), slot.Id);
                Assert.That(slot.RequiredPickCount, Is.EqualTo(1), slot.Id);
            }

            Assert.That(_tables.TbRewardSlot.GetOrDefault("fragment_choice_2"), Is.Not.Null);
            Assert.That(_tables.TbRewardSlot.GetOrDefault("fragment_choice_3"), Is.Null);
            Assert.That(
                _tables.TbPassiveItem.GetOrDefault("item_super_fragment_reward")?.EffectParam,
                Is.EqualTo("fragment_choice_2"));
        }

        [Test]
        public void ShopFragmentPack_RollsTwoCandidates()
        {
            List<string> pack = ShopService.RollFragmentPack(CreateRun());

            Assert.That(pack, Has.Count.EqualTo(2));
        }

        [Test]
        public void FragmentHiddenScore_FloorsFractionalDayWithoutChangingOtherCurves()
        {
            GameRun run = CreateRun();
            run.CurrentDay = 4.6f;

            Assert.That(HiddenScoreService.FragmentHiddenScore(run), Is.EqualTo(24));
            Assert.That(HiddenScoreService.DishHiddenScore(run), Is.EqualTo(25));
            Assert.That(HiddenScoreService.TargetScore(run), Is.EqualTo(1000));
        }

        [Test]
        public void BossFragmentReward_DoesNotUnlockFourCellBeforeIntegerBoundary()
        {
            GameRun run = CreateRun();
            run.CurrentDay = 4.6f;
            cfg.RewardSlot slot = _tables.TbRewardSlot.GetOrDefault("slot_boss_fragment");
            RewardContext context = CreateBossRewardContext(run);

            Assert.That(RewardPoolService.ResolveHiddenScoreForSlot(context, slot), Is.EqualTo(29));

            List<RewardChoice> choices = RewardPoolService.RollChoices(context, slot);
            Assert.That(choices, Has.Count.EqualTo(2));
            Assert.That(
                choices.Select(choice => _database.GetFragment(choice.Id).HiddenMin),
                Has.All.LessThan(30));
        }

        [Test]
        public void BossFragmentReward_UnlocksFourCellAtIntegerBoundary()
        {
            GameRun run = CreateRun();
            run.CurrentDay = 5f;
            cfg.RewardSlot slot = _tables.TbRewardSlot.GetOrDefault("slot_boss_fragment");
            RewardContext context = CreateBossRewardContext(run);

            Assert.That(RewardPoolService.ResolveHiddenScoreForSlot(context, slot), Is.EqualTo(30));

            List<RewardChoice> choices = RewardPoolService.RollChoices(context, slot);
            Assert.That(
                choices.Select(choice => _database.GetFragment(choice.Id).HiddenMin),
                Has.Some.EqualTo(30));
        }

        [Test]
        public void Roll_AfterFragmentRewardConvertsThatOutcomeToEmpty()
        {
            GameRun run = CreateRun();
            SlotMachineConfig config = GetSlotConfig(run);
            int fragmentWeightIndex = FindFragmentSlotIndex(config) + 1;
            var rng = new PreferredWeightRandomStream(fragmentWeightIndex);

            SlotSpinResult result = SlotService.Roll(
                run,
                config,
                rng,
                actionContext: null,
                fragmentRewardAlreadyGranted: true);

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.IsEmpty, Is.True);
            Assert.That(result.RewardKind, Is.EqualTo(cfg.RewardKind.None));
            Assert.That(rng.FirstWeightedCall[fragmentWeightIndex], Is.Zero);
        }

        [Test]
        public void FragmentLimit_MovesEveryFragmentWeightToEmptyAndKeepsOtherRewards()
        {
            SlotMachineConfig config = GetSlotConfig(CreateRun());
            cfg.RewardSlot fragment = config.RewardSlots[FindFragmentSlotIndex(config)];
            cfg.RewardSlot gold = config.RewardSlots.First(slot => slot.Kind == cfg.RewardKind.Gold);
            var slots = new[] { fragment, gold, fragment };
            var weights = new[] { 0.2f, 0.1f, 0.3f, 0.4f };

            List<float> limited = SlotService.ApplyFragmentRewardLimit(
                weights,
                slots,
                fragmentRewardAlreadyGranted: true);

            Assert.That(limited, Has.Count.EqualTo(weights.Length));
            Assert.That(limited[0], Is.EqualTo(0.7f).Within(0.000001f));
            Assert.That(limited[1], Is.Zero);
            Assert.That(limited[2], Is.EqualTo(weights[2]).Within(0.000001f));
            Assert.That(limited[3], Is.Zero);
            Assert.That(limited.Sum(), Is.EqualTo(weights.Sum()).Within(0.000001f));
        }

        [Test]
        public void FragmentLimit_AfterWinBonusPreservesAdjustedNonFragmentWeights()
        {
            SlotMachineConfig config = GetSlotConfig(CreateRun());
            List<float> adjusted = SlotService.ApplyWinChanceBonus(
                new[] { 0.45f, 0.25f, 0.1f, 0.1f, 0.1f },
                bonus: 0.25f,
                machineId: "test_slot");
            List<float> limited = SlotService.ApplyFragmentRewardLimit(
                adjusted,
                config.RewardSlots,
                fragmentRewardAlreadyGranted: true);
            int fragmentWeightIndex = FindFragmentSlotIndex(config) + 1;

            float fragmentWeight = adjusted[fragmentWeightIndex];
            Assert.That(
                limited[0],
                Is.EqualTo(adjusted[0] + fragmentWeight).Within(0.000001f));
            Assert.That(limited[fragmentWeightIndex], Is.Zero);
            for (int i = 1; i < limited.Count; i++)
            {
                if (i != fragmentWeightIndex)
                {
                    Assert.That(limited[i], Is.EqualTo(adjusted[i]).Within(0.000001f));
                }
            }

            Assert.That(limited.Sum(), Is.EqualTo(adjusted.Sum()).Within(0.000001f));
        }

        [Test]
        public void PendingFragmentQuota_SurvivesStageChangesAndSaveRestoreThenResetsForNewAction()
        {
            GameRun run = CreateRun();
            cfg.GameAction action = _tables.TbAction.GetOrDefault("act_slot");
            var context = new ActionExecutionContext(action, stepIndex: 2);
            ActionOutcome outcome = ActionOutcome.Slot(action.EffectParam);
            run.SetPendingActionExecution(context, outcome);

            Assert.That(run.GetPendingActionExecution().SlotFragmentRewardGranted, Is.False);
            Assert.That(run.TryMarkPendingSlotFragmentRewardGranted(), Is.True);
            Assert.That(run.TryMarkPendingSlotFragmentRewardGranted(), Is.False);

            Assert.That(
                run.SetPendingSlotExecutionState(
                    action.EffectParam,
                    spinsUsed: 1,
                    stage: SlotExecutionStage.AwaitingReward,
                    rewardKey: "test_fragment_reward"),
                Is.True);
            Assert.That(
                run.SetPendingSlotExecutionState(
                    action.EffectParam,
                    spinsUsed: 1,
                    stage: SlotExecutionStage.Ready),
                Is.True);
            Assert.That(
                run.SetPendingSlotExecutionState(
                    action.EffectParam,
                    spinsUsed: 2,
                    stage: SlotExecutionStage.EmptyResult),
                Is.True);
            Assert.That(run.GetPendingActionExecution().SlotFragmentRewardGranted, Is.True);

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            PendingActionExecutionSaveData restoredPending = restored.GetPendingActionExecution();
            Assert.That(restoredPending.SlotFragmentRewardGranted, Is.True);
            Assert.That(restoredPending.SlotStage, Is.EqualTo(SlotExecutionStage.EmptyResult));
            Assert.That(restoredPending.SlotSpinsUsed, Is.EqualTo(2));

            restored.SetPendingActionExecution(context, outcome);
            Assert.That(restored.GetPendingActionExecution().SlotFragmentRewardGranted, Is.False);
        }

        private GameRun CreateRun() =>
            new(
                _tables,
                _database,
                "glutton_dog",
                "slot-fragment-reward-limit-tests",
                weekIndex: 1,
                isTutorialRun: false);

        private RewardContext CreateBossRewardContext(GameRun run)
        {
            cfg.GameAction action = _tables.TbAction.GetOrDefault("act_boss");
            Assert.That(action, Is.Not.Null);
            return new RewardContext(
                _tables,
                run,
                week: null,
                package: null,
                rng: new LastCandidateRandomStream(),
                actionContext: new ActionExecutionContext(action, stepIndex: 0));
        }

        private SlotMachineConfig GetSlotConfig(GameRun run)
        {
            cfg.GameAction action = _tables.TbAction.GetOrDefault("act_slot");
            Assert.That(action, Is.Not.Null);
            Assert.That(
                SlotService.TryGetConfig(run, action, out SlotMachineConfig config, out string error),
                Is.True,
                error);
            return config;
        }

        private static int FindFragmentSlotIndex(SlotMachineConfig config)
        {
            for (int i = 0; i < config.RewardSlots.Count; i++)
            {
                if (config.RewardSlots[i]?.Kind == cfg.RewardKind.FragmentChoice)
                {
                    return i;
                }
            }

            Assert.Fail("测试抽奖机配置缺少餐桌格奖励槽。");
            return -1;
        }

        private sealed class PreferredWeightRandomStream : IRandomStream
        {
            private readonly int _preferredIndex;
            private int _weightedCalls;

            public PreferredWeightRandomStream(int preferredIndex)
            {
                _preferredIndex = preferredIndex;
            }

            public IReadOnlyList<float> FirstWeightedCall { get; private set; }

            public RngState State { get; set; }

            public uint NextUInt() => 0u;

            public ulong NextULong() => 0UL;

            public int Range(int minInclusive, int maxExclusive) => minInclusive;

            public float Range(float minInclusive, float maxExclusive) => minInclusive;

            public float NextFloat() => 0f;

            public double NextDouble() => 0d;

            public bool NextBool(double probability = 0.5d) => probability > 0d;

            public void Shuffle<T>(IList<T> list)
            {
            }

            public T Pick<T>(IReadOnlyList<T> list) => list[0];

            public int WeightedPickIndex(IReadOnlyList<float> weights)
            {
                if (_weightedCalls++ > 0)
                {
                    return 0;
                }

                FirstWeightedCall = weights.ToArray();
                return _preferredIndex >= 0
                    && _preferredIndex < weights.Count
                    && weights[_preferredIndex] > 0f
                        ? _preferredIndex
                        : 0;
            }
        }

        private sealed class LastCandidateRandomStream : IRandomStream
        {
            public RngState State { get; set; }

            public uint NextUInt() => 0u;

            public ulong NextULong() => 0UL;

            public int Range(int minInclusive, int maxExclusive) => minInclusive;

            public float Range(float minInclusive, float maxExclusive) => minInclusive;

            public float NextFloat() => 0f;

            public double NextDouble() => 0d;

            public bool NextBool(double probability = 0.5d) => probability > 0d;

            public void Shuffle<T>(IList<T> list)
            {
            }

            public T Pick<T>(IReadOnlyList<T> list) => list[list.Count - 1];

            public int WeightedPickIndex(IReadOnlyList<float> weights) => weights.Count - 1;
        }
    }
}
#endif
