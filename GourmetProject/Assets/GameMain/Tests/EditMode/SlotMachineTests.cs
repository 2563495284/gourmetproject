using System.Collections.Generic;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using GourmetProject.Runtime;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SlotMachineTests
    {
        private static cfg.Tables _tables;
        private static GameplayDatabase _database;
        private static RandomService _previousRandom;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = GameplayContentBuilder.BuildDatabase(_tables);

            _previousRandom = GameApp.Random;
            var random = new RandomService();
            random.Init(0x5A107UL);
            SetGameRandom(random);
        }

        [OneTimeTearDown]
        public void RestoreRandom()
        {
            SetGameRandom(_previousRandom);
        }

        [Test]
        public void BasicMachine_HasExpectedWeightsAndLimits()
        {
            GameRun run = CreateRun();
            cfg.GameAction action = _tables.TbAction.Get("act_slot");

            bool valid = SlotService.TryGetConfig(
                run,
                action,
                out SlotMachineConfig machine,
                out string error);

            Assert.That(valid, Is.True, error);
            Assert.That(action.Behavior, Is.EqualTo(cfg.ActionBehavior.Slot));
            Assert.That(action.EffectParam, Is.EqualTo("ev_slot_machine"));
            Assert.That(machine.Event.Id, Is.EqualTo("ev_slot_machine"));
            Assert.That(machine.EmptyWeight, Is.EqualTo(40f));
            Assert.That(machine.FreeSpins, Is.EqualTo(1));
            Assert.That(machine.PaidCost, Is.EqualTo(20));
            Assert.That(machine.MaxSpins, Is.EqualTo(5));

            AssertSlot(machine, "slot_machine_gold", cfg.RewardKind.Gold, 35f);
            AssertSlot(machine, "slot_machine_fragment", cfg.RewardKind.FragmentChoice, 15f);
            AssertSlot(machine, "slot_machine_active", cfg.RewardKind.ActiveItemGrant, 5f);
            AssertSlot(machine, "slot_machine_passive", cfg.RewardKind.PassiveItemChoice, 5f);
            Assert.That(
                machine.EmptyWeight + machine.RewardSlots.Sum(slot => slot.Weight),
                Is.EqualTo(100f));

            Assert.That(
                machine.Options.Select(option => option.Id),
                Is.EqualTo(new[]
                {
                    SlotService.SpinOptionId,
                    SlotService.LeaveOptionId,
                }));
            cfg.EventOption spin = machine.Options[0];
            cfg.EventOption leave = machine.Options[1];
            Assert.That(spin.Text, Is.EqualTo("{slotCostText}（{slotStatus}）"));
            Assert.That(spin.EffectTypes, Is.All.EqualTo(cfg.EffectType.None));
            Assert.That(spin.AutoEnd, Is.False);
            Assert.That(leave.Text, Is.EqualTo("离开"));
            Assert.That(leave.EffectTypes, Is.All.EqualTo(cfg.EffectType.None));
            Assert.That(leave.AutoEnd, Is.True);
        }

        [Test]
        public void CostForNextSpin_FirstIsFree_ThenUsesPaidCost()
        {
            SlotMachineConfig machine = LoadMachine(CreateRun());

            Assert.That(SlotService.CostForNextSpin(machine, 0), Is.Zero);
            Assert.That(SlotService.CostForNextSpin(machine, 1), Is.EqualTo(20));
            Assert.That(SlotService.CostForNextSpin(machine, 2), Is.EqualTo(20));
            Assert.That(SlotService.CostForNextSpin(machine, 4), Is.EqualTo(20));
        }

        [Test]
        public void SlotWinChanceBonus_MultipliesNormalizedChanceAndKeepsRewardRatios()
        {
            GameRun run = CreateRun();
            AttachPassiveModelUsingDefinition(
                run,
                "item_slot_win_chance",
                _tables.TbPassiveItem.Get("item_lucky_chance")); // effectValue = 0.2
            SlotMachineConfig machine = LoadMachine(run);

            IReadOnlyList<float> weights = SlotService.BuildRollWeights(run, machine);

            Assert.That(weights, Has.Count.EqualTo(5));
            Assert.That(weights[0], Is.EqualTo(28f).Within(0.0001f));
            Assert.That(weights[1], Is.EqualTo(42f).Within(0.0001f));
            Assert.That(weights[2], Is.EqualTo(18f).Within(0.0001f));
            Assert.That(weights[3], Is.EqualTo(6f).Within(0.0001f));
            Assert.That(weights[4], Is.EqualTo(6f).Within(0.0001f));
            Assert.That(weights.Sum(), Is.EqualTo(100f).Within(0.0001f));
        }

        [Test]
        public void SlotWinChanceBonus_OverflowCapsAtCertainWin()
        {
            List<float> weights = SlotService.ApplyWinChanceBonus(
                new[] { 40f, 35f, 15f, 5f, 5f },
                bonus: 1f);

            Assert.That(weights[0], Is.Zero);
            Assert.That(weights.Skip(1).Sum(), Is.EqualTo(100f).Within(0.0001f));
            Assert.That(weights[1] / weights[2], Is.EqualTo(35f / 15f).Within(0.0001f));
        }

        [Test]
        public void OptionText_UsesEventOptionTemplateWithRuntimeSlotState()
        {
            GameRun run = CreateRun();
            SlotMachineConfig machine = LoadMachine(run);
            cfg.EventOption spin =
                machine.Options.Single(option => option.Id == SlotService.SpinOptionId);
            cfg.EventOption leave =
                machine.Options.Single(option => option.Id == SlotService.LeaveOptionId);

            Assert.That(
                SlotService.FormatOptionText(run, machine, spin, 0, canAfford: true),
                Is.EqualTo("免费抽一次（0/5）"));
            Assert.That(
                SlotService.FormatOptionText(run, machine, spin, 1, canAfford: true),
                Is.EqualTo("投入 20 金币（1/5）"));
            Assert.That(
                SlotService.FormatOptionText(run, machine, spin, 1, canAfford: false),
                Is.EqualTo("投入 20 金币（金币不足）"));
            Assert.That(
                SlotService.FormatOptionText(run, machine, leave, 1, canAfford: false),
                Is.EqualTo("离开"));
        }

        [TestCase(0.00, true, null)]
        [TestCase(0.50, false, cfg.RewardKind.Gold)]
        [TestCase(0.80, false, cfg.RewardKind.FragmentChoice)]
        [TestCase(0.92, false, cfg.RewardKind.ActiveItemGrant)]
        [TestCase(0.97, false, cfg.RewardKind.PassiveItemChoice)]
        public void FixedRoll_HitsEmptyAndEveryConfiguredReward(
            double firstRoll,
            bool expectedEmpty,
            cfg.RewardKind? expectedKind)
        {
            GameRun run = CreateRun();
            SlotMachineConfig machine = LoadMachine(run);
            var rng = new FirstRollRandomStream(firstRoll);

            SlotSpinResult result;
            try
            {
                result = SlotService.Roll(run, machine, rng);
            }
            catch (System.Exception ex)
            {
                Assert.Fail(ex.ToString());
                return;
            }

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.IsEmpty, Is.EqualTo(expectedEmpty));
            if (expectedEmpty)
            {
                Assert.That(result.Offer, Is.Null);
                return;
            }

            Assert.That(result.Offer, Is.Not.Null);
            Assert.That(result.Offer.MainChoices, Is.Not.Empty);
            Assert.That(result.Offer.MainChoices[0].Kind, Is.EqualTo(expectedKind.Value));
        }

        [Test]
        public void PendingSlotState_RoundTripsThroughRunSave()
        {
            GameRun run = CreateRun();
            cfg.GameAction action = _tables.TbAction.Get("act_slot");
            var context = new ActionExecutionContext(action, 2, 7, "sg_test", 0f)
            {
                SourceKey = "runtime_w1_3",
                IsExtraTimelineExecution = true,
                NodeRepeatIndex = 2,
                NodeRepeatTotal = 3,
            };
            ActionOutcome outcome = ActionOutcome.Slot(action.EffectParam);
            run.SetPendingActionExecution(context, outcome);
            Assert.That(
                run.SetPendingSlotExecutionState(
                    action.EffectParam,
                    3,
                    SlotExecutionStage.AwaitingReward,
                    "slot_runtime_w1_3_spin3"),
                Is.True);
            run.PendingGenericRewardContinuation = PendingGenericRewardContinuationKind.Slot;

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            PendingActionExecutionSaveData pending = restored.GetPendingActionExecution();

            Assert.That(pending, Is.Not.Null);
            Assert.That(pending.OutcomeKind, Is.EqualTo(ActionOutcomeKind.Slot));
            Assert.That(pending.SlotEventId, Is.EqualTo("ev_slot_machine"));
            Assert.That(pending.SlotSpinsUsed, Is.EqualTo(3));
            Assert.That(pending.SlotStage, Is.EqualTo(SlotExecutionStage.AwaitingReward));
            Assert.That(pending.SlotRewardKey, Is.EqualTo("slot_runtime_w1_3_spin3"));
            Assert.That(pending.SourceKey, Is.EqualTo("runtime_w1_3"));
            Assert.That(pending.IsExtraTimelineExecution, Is.True);
            Assert.That(pending.NodeRepeatIndex, Is.EqualTo(2));
            Assert.That(pending.NodeRepeatTotal, Is.EqualTo(3));
            Assert.That(
                restored.PendingGenericRewardContinuation,
                Is.EqualTo(PendingGenericRewardContinuationKind.Slot));
            Assert.That(restored.PendingGenericRewardsConfirmBattleAfterDone, Is.False);
        }

        [Test]
        public void LegacyBattleRewardContinuationFlag_RemainsCompatible()
        {
            GameRun run = CreateRun();
            RunSaveData save = run.ToSaveData();
            save.PendingGenericRewardContinuation = PendingGenericRewardContinuationKind.None;
            save.PendingGenericRewardsConfirmBattleAfterDone = true;

            GameRun restored = GameRun.FromSaveData(_tables, _database, save);

            Assert.That(
                restored.PendingGenericRewardContinuation,
                Is.EqualTo(PendingGenericRewardContinuationKind.Battle));
            Assert.That(restored.PendingGenericRewardsConfirmBattleAfterDone, Is.True);
        }

        [Test]
        public void SlotEvent_IsNotPartOfOrdinaryActionEventPool()
        {
            cfg.GameEvent ev = _tables.TbEvent.Get("ev_slot_machine");

            Assert.That(ev.EventTypes, Is.EqualTo(new[] { cfg.ActionBehavior.Slot }));
            Assert.That(ev.HasEventType(cfg.ActionBehavior.Slot), Is.True);
            Assert.That(ev.IsActionEventPoolMember, Is.False);
            Assert.That(ev.Weight, Is.Zero);
        }

        private static SlotMachineConfig LoadMachine(GameRun run)
        {
            cfg.GameAction action = _tables.TbAction.Get("act_slot");
            Assert.That(
                SlotService.TryGetConfig(run, action, out SlotMachineConfig machine, out string error),
                Is.True,
                error);
            return machine;
        }

        private static void AssertSlot(
            SlotMachineConfig machine,
            string slotId,
            cfg.RewardKind kind,
            float weight)
        {
            cfg.RewardSlot slot = machine.RewardSlots.Single(entry => entry.Id == slotId);
            Assert.That(slot.Kind, Is.EqualTo(kind));
            Assert.That(slot.Weight, Is.EqualTo(weight));
        }

        private static GameRun CreateRun()
        {
            string characterId = _tables.TbCharacter.DataList.First().Id;
            return new GameRun(_tables, _database, characterId, "slot-machine-tests");
        }

        private static void AttachPassiveModelUsingDefinition(
            GameRun run,
            string registeredItemId,
            cfg.PassiveItem valueSource)
        {
            var state = new RunItemState(registeredItemId, 1);
            PassiveItemModel model = PassiveItemModelRegistry.Create(registeredItemId);
            model.Bind(run, ItemDefinition.From(valueSource), state);
            state.Model = model;
            ((List<RunItemState>)run.Items).Add(state);
        }

        private static void SetGameRandom(RandomService random)
        {
            typeof(GameApp)
                .GetProperty(nameof(GameApp.Random))
                ?.GetSetMethod(nonPublic: true)
                ?.Invoke(null, new object[] { random });
        }

        private sealed class FirstRollRandomStream : IRandomStream
        {
            private readonly double _firstRoll;
            private bool _firstWeightedPick = true;

            public FirstRollRandomStream(double firstRoll)
            {
                _firstRoll = firstRoll;
            }

            public RngState State { get; set; }

            public uint NextUInt() => 0;

            public ulong NextULong() => 0;

            public int Range(int minInclusive, int maxExclusive) => minInclusive;

            public float Range(float minInclusive, float maxExclusive) => minInclusive;

            public float NextFloat() => 0f;

            public double NextDouble() => 0d;

            public bool NextBool(double probability = 0.5) => probability > 0d;

            public void Shuffle<T>(IList<T> list)
            {
            }

            public T Pick<T>(IReadOnlyList<T> list) => list[0];

            public int WeightedPickIndex(IReadOnlyList<float> weights)
            {
                double roll = _firstWeightedPick ? _firstRoll : 0d;
                _firstWeightedPick = false;
                double total = 0d;
                for (int i = 0; i < weights.Count; i++)
                {
                    if (weights[i] > 0f)
                    {
                        total += weights[i];
                    }
                }

                double target = roll * total;
                double cumulative = 0d;
                for (int i = 0; i < weights.Count; i++)
                {
                    if (weights[i] <= 0f)
                    {
                        continue;
                    }

                    cumulative += weights[i];
                    if (target < cumulative)
                    {
                        return i;
                    }
                }

                return weights.Count - 1;
            }
        }
    }
}
