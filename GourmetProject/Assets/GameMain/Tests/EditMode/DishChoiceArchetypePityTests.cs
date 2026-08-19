using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    /// <summary>
    /// 食物多选一流派保底契约：只记录原始槽位为 N 选 1 的食物奖励，连续两次未展示
    /// 当前单流派目标后，第三次必须展示至少一个该流派食物。
    /// </summary>
    public sealed class DishChoiceArchetypePityTests
    {
        private const int PityThreshold = 2;
        private const string TestCharacterId = "__dish_choice_pity_test__";
        private const string Target0DishId = "pity_target_0";
        private const string Target1DishId = "pity_target_1";
        private const string MultiArchetypeDishId = "pity_multi_archetype";

        private string _configDirectory;
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            _configDirectory = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = LoadTables();
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void StateMachine_TwoConsecutiveMisses_ArmThirdOffer()
        {
            GameRun run = CreateRun(_database);

            Assert.That(run.BeginDishChoiceArchetypePity("0", PityThreshold), Is.False);
            run.CompleteDishChoiceArchetypePity("0", targetOffered: false, PityThreshold);
            Assert.That(run.DishChoiceArchetypeMissStreak, Is.EqualTo(1));

            Assert.That(run.BeginDishChoiceArchetypePity("0", PityThreshold), Is.False);
            run.CompleteDishChoiceArchetypePity("0", targetOffered: false, PityThreshold);

            Assert.That(run.LastDishChoiceArchetypeId, Is.EqualTo("0"));
            Assert.That(run.DishChoiceArchetypeMissStreak, Is.EqualTo(PityThreshold));
            Assert.That(run.BeginDishChoiceArchetypePity("0", PityThreshold), Is.True);
        }

        [Test]
        public void StateMachine_NaturalTargetHit_ResetsMissStreak()
        {
            GameRun run = CreateRun(_database);
            RecordMiss(run, "1");

            Assert.That(run.BeginDishChoiceArchetypePity("1", PityThreshold), Is.False);
            run.CompleteDishChoiceArchetypePity("1", targetOffered: true, PityThreshold);

            Assert.That(run.LastDishChoiceArchetypeId, Is.EqualTo("1"));
            Assert.That(run.DishChoiceArchetypeMissStreak, Is.Zero);
            Assert.That(run.BeginDishChoiceArchetypePity("1", PityThreshold), Is.False);
        }

        [Test]
        public void StateMachine_MixedArchetype_ClearsTracking()
        {
            GameRun run = CreateRun(_database);
            RecordMiss(run, "2");

            Assert.That(run.BeginDishChoiceArchetypePity("mixed", PityThreshold), Is.False);

            Assert.That(run.LastDishChoiceArchetypeId, Is.Empty);
            Assert.That(run.DishChoiceArchetypeMissStreak, Is.Zero);
        }

        [Test]
        public void StateMachine_ArchetypeChange_CountsCurrentOfferAsFirstNewMiss()
        {
            GameRun run = CreateRun(_database);
            RecordMiss(run, "0");

            Assert.That(run.BeginDishChoiceArchetypePity("2", PityThreshold), Is.False);
            Assert.That(run.DishChoiceArchetypeMissStreak, Is.Zero,
                "流派变化应在生成本次候选前建立新的零连败基线。");
            run.CompleteDishChoiceArchetypePity("2", targetOffered: false, PityThreshold);

            Assert.That(run.LastDishChoiceArchetypeId, Is.EqualTo("2"));
            Assert.That(run.DishChoiceArchetypeMissStreak, Is.EqualTo(1),
                "转流派当次未命中应成为新流派的第一次 miss。");
        }

        [Test]
        public void StateMachine_ZeroThreshold_DisablesAndClearsTracking()
        {
            GameRun run = CreateRun(_database);
            RecordMiss(run, "0");

            Assert.That(run.BeginDishChoiceArchetypePity("0", missThreshold: 0), Is.False);
            run.CompleteDishChoiceArchetypePity("0", targetOffered: false, missThreshold: 0);

            Assert.That(run.LastDishChoiceArchetypeId, Is.Empty);
            Assert.That(run.DishChoiceArchetypeMissStreak, Is.Zero);
        }

        [Test]
        public void SaveRoundTrip_PreservesArchetypeAndMissStreak()
        {
            GameRun run = CreateRun(_database);
            RecordMiss(run, "2");
            RecordMiss(run, "2");

            RunSaveData save = run.ToSaveData();
            GameRun restored = GameRun.FromSaveData(_tables, _database, save);

            Assert.That(save.LastDishChoiceArchetypeId, Is.EqualTo("2"));
            Assert.That(save.DishChoiceArchetypeMissStreak, Is.EqualTo(PityThreshold));
            Assert.That(restored.LastDishChoiceArchetypeId, Is.EqualTo("2"));
            Assert.That(restored.DishChoiceArchetypeMissStreak, Is.EqualTo(PityThreshold));
            Assert.That(restored.BeginDishChoiceArchetypePity("2", PityThreshold), Is.True);
        }

        [TestCase(null, 9, "", 0)]
        [TestCase("", 9, "", 0)]
        [TestCase("mixed", 9, "", 0)]
        [TestCase("3", 9, "", 0)]
        [TestCase("invalid", 9, "", 0)]
        [TestCase("1", -9, "1", 0)]
        public void FromSave_LegacyOrInvalidPityValues_AreSanitized(
            string savedArchetype,
            int savedStreak,
            string expectedArchetype,
            int expectedStreak)
        {
            RunSaveData save = CreateRun(_database).ToSaveData();
            save.LastDishChoiceArchetypeId = savedArchetype;
            save.DishChoiceArchetypeMissStreak = savedStreak;

            GameRun restored = GameRun.FromSaveData(_tables, _database, save);

            Assert.That(restored.LastDishChoiceArchetypeId, Is.EqualTo(expectedArchetype));
            Assert.That(restored.DishChoiceArchetypeMissStreak, Is.EqualTo(expectedStreak));
        }

        [TestCase("0")]
        [TestCase("1")]
        public void RollChoices_PositiveWeightDefinesMembership_AndMultiArchetypeDishHitsBothTargets(
            string targetArchetype)
        {
            int hidden = CurrentDishHiddenScore();
            GameplayDatabase database = CreateMembershipDatabase(hidden);
            GameRun run = CreateRun(database);
            Assert.That(run.AddBonusDish(targetArchetype == "0" ? Target0DishId : Target1DishId), Is.True);
            RecordMiss(run, targetArchetype);

            cfg.RewardSlot slot = CreateDishSlot(choiceCount: 2, requiredPickCount: 1);
            var context = new RewardContext(
                _tables,
                run,
                null,
                null,
                new Xoshiro256SS(10101UL));
            List<RewardChoice> choices = RewardPoolService.RollChoices(context, slot);

            Assert.That(choices.Select(choice => choice.Id), Does.Contain(MultiArchetypeDishId));
            Assert.That(database.GetDish(MultiArchetypeDishId).ArchetypeWeights[int.Parse(targetArchetype)],
                Is.GreaterThan(0f));
            Assert.That(run.DishChoiceArchetypeMissStreak, Is.Zero,
                "目标维度只要大于零就应视为命中，即使另一维权重更大。");
        }

        [Test]
        public void RollChoices_AllConfiguredEligibleSlots_RecordMiss()
        {
            string[] expectedIds = { "slot_base_dish", "slot_boss_dish", "dish_choice_3" };
            List<cfg.RewardSlot> eligibleSlots = _tables.TbRewardSlot.DataList
                .Where(IsPityEligibleSlot)
                .OrderBy(slot => slot.Id, StringComparer.Ordinal)
                .ToList();
            CollectionAssert.AreEquivalent(expectedIds, eligibleSlots.Select(slot => slot.Id).ToArray(),
                "新增配置型食物 N 选 1 槽时，必须纳入流派保底契约测试。");

            foreach (cfg.RewardSlot slot in eligibleSlots)
            {
                int hidden = HiddenScoreForSlot(_tables, slot);
                GameplayDatabase database = CreateGuaranteeDatabase(hidden);
                GameRun run = CreateRunWithTarget0(database);
                List<RewardChoice> choices = RewardPoolService.RollChoices(
                    new RewardContext(_tables, run, null, null, new Xoshiro256SS(20202UL)),
                    slot);

                Assert.That(choices, Has.Count.EqualTo(slot.ChoiceCount), slot.Id);
                Assert.That(choices.Select(choice => choice.Id).Distinct(StringComparer.Ordinal).Count(),
                    Is.EqualTo(choices.Count), slot.Id);
                Assert.That(choices.Any(choice => IsTargetChoice(choice, database, 0)), Is.False, slot.Id);
                Assert.That(run.LastDishChoiceArchetypeId, Is.EqualTo("0"), slot.Id);
                Assert.That(run.DishChoiceArchetypeMissStreak, Is.EqualTo(1), slot.Id);
            }
        }

        [TestCase("dish_grant_1")]
        [TestCase("flavored_dish_2")]
        [TestCase("flavored_dish_3")]
        public void RollChoices_ConfiguredNonEligibleDishSlots_DoNotChangePityState(string slotId)
        {
            cfg.RewardSlot slot = _tables.TbRewardSlot.Get(slotId);
            int hidden = HiddenScoreForSlot(_tables, slot);
            GameplayDatabase database = string.Equals(
                    slot.PoolId,
                    "pool_flavored_dish",
                    StringComparison.Ordinal)
                ? CreateNoFlavoredTargetDatabase(hidden)
                : CreateGuaranteeDatabase(hidden);
            GameRun run = CreateRunWithTarget0(database);
            RecordMiss(run, "0");

            List<RewardChoice> choices = RewardPoolService.RollChoices(
                new RewardContext(_tables, run, null, null, new Xoshiro256SS(30303UL)),
                slot);

            Assert.That(slot.Kind, Is.EqualTo(cfg.RewardKind.DishChoice));
            Assert.That(IsPityEligibleSlot(slot), Is.False, slot.Id);
            Assert.That(choices, Has.Count.EqualTo(slot.ChoiceCount), slot.Id);
            Assert.That(choices.Select(choice => choice.Id).Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(choices.Count), slot.Id);
            Assert.That(run.LastDishChoiceArchetypeId, Is.EqualTo("0"), slot.Id);
            Assert.That(run.DishChoiceArchetypeMissStreak, Is.EqualTo(1), slot.Id);
        }

        [Test]
        public void RollChoices_ArmedPity_IncludesTargetByRelaxingHiddenScore_WithoutDuplicates_AndIsReproducible()
        {
            Assert.That(_tables.TbGameBase.DishChoiceArchetypePityCount, Is.EqualTo(PityThreshold));

            PityRollResult first = RollThroughPity(40404UL);
            PityRollResult repeated = RollThroughPity(40404UL);
            RewardChoice guaranteed = first.ThirdChoices.Single(choice =>
                IsTargetChoice(choice, first.Database, targetIndex: 0));
            DishDef guaranteedDish = first.Database.GetDish(guaranteed.Id);

            Assert.That(first.FirstChoices.Any(choice =>
                IsTargetChoice(choice, first.Database, 0)), Is.False);
            Assert.That(first.SecondChoices.Any(choice =>
                IsTargetChoice(choice, first.Database, 0)), Is.False);
            Assert.That(first.ThirdChoices, Has.Count.EqualTo(3));
            Assert.That(first.ThirdChoices.Select(choice => choice.Id).Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(first.ThirdChoices.Count));
            Assert.That(guaranteedDish.CoversHiddenScore(first.HiddenScore), Is.False,
                "严格隐藏分池没有目标食物时，保底必须放宽隐藏分。");
            Assert.That(first.Run.LastDishChoiceArchetypeId, Is.EqualTo("0"));
            Assert.That(first.Run.DishChoiceArchetypeMissStreak, Is.Zero);
            CollectionAssert.AreEqual(
                first.ThirdChoices.Select(choice => choice.Id).ToArray(),
                repeated.ThirdChoices.Select(choice => choice.Id).ToArray(),
                "相同初始状态与随机种子应得到相同候选顺序。");
        }

        [Test]
        public void RollChoices_ArmedFlavoredPoolWithoutTarget_KeepsCandidatesAndPityPending()
        {
            int hidden = CurrentDishHiddenScore();
            GameplayDatabase database = CreateNoFlavoredTargetDatabase(hidden);
            cfg.RewardSlot slot = CreateDishSlot(
                choiceCount: 3,
                requiredPickCount: 1,
                poolId: "pool_flavored_dish");

            GameRun naturalRun = CreateRunWithTarget0(database);
            List<RewardChoice> naturalChoices = RewardPoolService.RollChoices(
                new RewardContext(_tables, naturalRun, null, null, new Xoshiro256SS(45454UL)),
                slot);

            GameRun armedRun = CreateRunWithTarget0(database);
            RecordMiss(armedRun, "0");
            RecordMiss(armedRun, "0");
            List<RewardChoice> armedChoices = RewardPoolService.RollChoices(
                new RewardContext(_tables, armedRun, null, null, new Xoshiro256SS(45454UL)),
                slot);

            Assert.That(database.AllDishes.Any(dish =>
                dish.HasFlavor && dish.ArchetypeWeights[0] > 0f), Is.False,
                "风味食物池内必须确实没有目标流派候选。");
            Assert.That(armedChoices, Has.Count.EqualTo(slot.ChoiceCount));
            Assert.That(armedChoices.Select(choice => choice.Id).Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(armedChoices.Count));
            Assert.That(armedChoices.Any(choice => IsTargetChoice(choice, database, 0)), Is.False);
            CollectionAssert.AreEqual(
                naturalChoices.Select(choice => choice.Id).ToArray(),
                armedChoices.Select(choice => choice.Id).ToArray(),
                "无法兑现保底时不得替换普通候选或额外消耗随机数。");
            Assert.That(armedRun.LastDishChoiceArchetypeId, Is.EqualTo("0"));
            Assert.That(armedRun.DishChoiceArchetypeMissStreak, Is.EqualTo(PityThreshold));
            Assert.That(armedRun.BeginDishChoiceArchetypePity("0", PityThreshold), Is.True,
                "本组无法兑现时，保底应留到下一符合条件的组。");
        }

        [Test]
        public void RollChoices_ConfiguredZeroThreshold_ClearsStateAndDoesNotInjectTarget()
        {
            cfg.Tables disabledTables = LoadTables(pityCountOverride: 0);
            cfg.RewardSlot slot = disabledTables.TbRewardSlot.Get("dish_choice_3");
            int hidden = HiddenScoreForSlot(disabledTables, slot);
            GameplayDatabase database = CreateGuaranteeDatabase(hidden);
            GameRun run = CreateRunWithTarget0(disabledTables, database);
            RecordMiss(run, "0");
            RecordMiss(run, "0");

            List<RewardChoice> choices = RewardPoolService.RollChoices(
                new RewardContext(disabledTables, run, null, null, new Xoshiro256SS(46464UL)),
                slot);

            Assert.That(disabledTables.TbGameBase.DishChoiceArchetypePityCount, Is.Zero);
            Assert.That(choices, Has.Count.EqualTo(slot.ChoiceCount));
            Assert.That(choices.Select(choice => choice.Id).Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(choices.Count));
            Assert.That(choices.Any(choice => IsTargetChoice(choice, database, 0)), Is.False);
            Assert.That(run.LastDishChoiceArchetypeId, Is.Empty);
            Assert.That(run.DishChoiceArchetypeMissStreak, Is.Zero);
        }

        [Test]
        public void RollChoices_ArchetypeChangesAfterMiss_TwoNewMissesThenGuaranteesNewTarget()
        {
            cfg.RewardSlot slot = _tables.TbRewardSlot.Get("dish_choice_3");
            int hidden = HiddenScoreForSlot(_tables, slot);
            GameplayDatabase database = CreateArchetypeChangeDatabase(hidden);
            GameRun run = CreateRunWithTarget0(database);
            var rng = new Xoshiro256SS(46969UL);
            var context = new RewardContext(_tables, run, null, null, rng);

            List<RewardChoice> archetype0Miss = RewardPoolService.RollChoices(context, slot);
            Assert.That(archetype0Miss.Any(choice => IsTargetChoice(choice, database, 0)), Is.False);
            Assert.That(run.LastDishChoiceArchetypeId, Is.EqualTo("0"));
            Assert.That(run.DishChoiceArchetypeMissStreak, Is.EqualTo(1));

            Assert.That(run.ReplaceRecipeDishAt(0, Target1DishId), Is.True);
            List<RewardChoice> archetype1FirstMiss = RewardPoolService.RollChoices(context, slot);
            Assert.That(archetype1FirstMiss.Any(choice => IsTargetChoice(choice, database, 1)), Is.False);
            Assert.That(run.LastDishChoiceArchetypeId, Is.EqualTo("1"));
            Assert.That(run.DishChoiceArchetypeMissStreak, Is.EqualTo(1));

            List<RewardChoice> archetype1SecondMiss = RewardPoolService.RollChoices(context, slot);
            Assert.That(archetype1SecondMiss.Any(choice => IsTargetChoice(choice, database, 1)), Is.False);
            Assert.That(run.DishChoiceArchetypeMissStreak, Is.EqualTo(PityThreshold));

            List<RewardChoice> archetype1Guaranteed = RewardPoolService.RollChoices(context, slot);
            Assert.That(archetype1Guaranteed, Has.Count.EqualTo(slot.ChoiceCount));
            Assert.That(archetype1Guaranteed.Select(choice => choice.Id).Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(archetype1Guaranteed.Count));
            Assert.That(archetype1Guaranteed.Any(choice => IsTargetChoice(choice, database, 1)), Is.True,
                "0 miss → 转为 1 → 两次 1 miss 后，下一组必须保底流派 1。");
            Assert.That(run.LastDishChoiceArchetypeId, Is.EqualTo("1"));
            Assert.That(run.DishChoiceArchetypeMissStreak, Is.Zero);
        }

        [Test]
        public void GenerateOffer_BossPackage_UsesConfiguredBossDishPitySlot()
        {
            cfg.GameAction bossAction = _tables.TbAction.Get("act_boss");
            var actionContext = new ActionExecutionContext(bossAction);
            cfg.RewardSlot bossDishSlot = _tables.TbRewardSlot.Get("slot_boss_dish");
            int hidden = HiddenScoreForSlot(_tables, bossDishSlot, actionContext);
            GameplayDatabase database = CreateGuaranteeDatabase(hidden);
            GameRun run = CreateRunWithTarget0(database);
            RecordMiss(run, "0");
            RecordMiss(run, "0");

            RewardOffer offer = RewardGranter.GenerateOffer(
                run,
                run.CurrentWeek,
                new Xoshiro256SS(47474UL),
                actionContext);
            RewardChoiceGroup bossDishGroup = offer.FixedGroups.Single(group =>
                string.Equals(group.SourceSlotId, bossDishSlot.Id, StringComparison.Ordinal));

            Assert.That(bossDishGroup.Choices, Has.Count.EqualTo(bossDishSlot.ChoiceCount));
            Assert.That(bossDishGroup.Choices.Any(choice =>
                IsTargetChoice(choice, database, 0)), Is.True);
            Assert.That(run.LastDishChoiceArchetypeId, Is.EqualTo("0"));
            Assert.That(run.DishChoiceArchetypeMissStreak, Is.Zero);
        }

        [Test]
        public void BuildConfigOffer_MultipleEligibleGroups_UpdatePityInGenerationOrder()
        {
            int hidden = CurrentDishHiddenScore();
            GameplayDatabase database = CreateGuaranteeDatabase(hidden);
            GameRun run = CreateRunWithTarget0(database);
            RecordMiss(run, "0");

            RewardOffer offer = RewardGranter.BuildConfigOffer(
                run,
                new Xoshiro256SS(50505UL),
                baseGold: 0,
                fixedSlotGroupIds: new[] { "base_dish", "dish_choice_3" },
                specificSlotGroupId: null);

            Assert.That(offer, Is.Not.Null);
            Assert.That(offer.FixedGroups, Has.Count.EqualTo(2));
            Assert.That(offer.FixedGroups[0].Choices.Any(choice =>
                IsTargetChoice(choice, database, 0)), Is.False,
                "第一组应先把 streak 从 1 推到 2。");
            Assert.That(offer.FixedGroups[1].Choices.Any(choice =>
                IsTargetChoice(choice, database, 0)), Is.True,
                "同批第二组应观察到第一组更新后的 armed 状态。");
            Assert.That(run.DishChoiceArchetypeMissStreak, Is.Zero);
        }

        private PityRollResult RollThroughPity(ulong seed)
        {
            int hidden = CurrentDishHiddenScore();
            GameplayDatabase database = CreateGuaranteeDatabase(hidden);
            GameRun run = CreateRunWithTarget0(database);
            cfg.RewardSlot slot = _tables.TbRewardSlot.Get("dish_choice_3");
            var rng = new Xoshiro256SS(seed);
            var context = new RewardContext(_tables, run, null, null, rng);

            List<RewardChoice> first = RewardPoolService.RollChoices(context, slot);
            List<RewardChoice> second = RewardPoolService.RollChoices(context, slot);
            List<RewardChoice> third = RewardPoolService.RollChoices(context, slot);

            return new PityRollResult(database, run, hidden, first, second, third);
        }

        private int CurrentDishHiddenScore()
        {
            return HiddenScoreService.DishHiddenScore(CreateRun(_database));
        }

        private int HiddenScoreForSlot(
            cfg.Tables tables,
            cfg.RewardSlot slot,
            ActionExecutionContext actionContext = null)
        {
            GameRun probeRun = CreateRun(tables, _database);
            var context = new RewardContext(
                tables,
                probeRun,
                null,
                null,
                new Xoshiro256SS(1UL),
                actionContext: actionContext);
            return RewardPoolService.ResolveHiddenScoreForSlot(context, slot);
        }

        private cfg.Tables LoadTables(int? pityCountOverride = null)
        {
            return new cfg.Tables(name =>
            {
                JSONNode json = JSON.Parse(File.ReadAllText(Path.Combine(_configDirectory, name + ".json")));
                if (pityCountOverride.HasValue
                    && string.Equals(name, "tbgamebase", StringComparison.OrdinalIgnoreCase))
                {
                    json[0]["dishChoiceArchetypePityCount"] = pityCountOverride.Value;
                }

                return json;
            });
        }

        private GameRun CreateRun(GameplayDatabase database)
        {
            return CreateRun(_tables, database);
        }

        private static GameRun CreateRun(cfg.Tables tables, GameplayDatabase database)
        {
            return new GameRun(
                tables,
                database,
                TestCharacterId,
                "dish-choice-pity-test",
                weekIndex: 1);
        }

        private GameRun CreateRunWithTarget0(GameplayDatabase database)
        {
            GameRun run = CreateRun(database);
            Assert.That(run.AddBonusDish(Target0DishId), Is.True);
            return run;
        }

        private static GameRun CreateRunWithTarget0(cfg.Tables tables, GameplayDatabase database)
        {
            GameRun run = CreateRun(tables, database);
            Assert.That(run.AddBonusDish(Target0DishId), Is.True);
            return run;
        }

        private static void RecordMiss(GameRun run, string archetypeId)
        {
            run.BeginDishChoiceArchetypePity(archetypeId, PityThreshold);
            run.CompleteDishChoiceArchetypePity(
                archetypeId,
                targetOffered: false,
                PityThreshold);
        }

        private static bool IsTargetChoice(
            RewardChoice choice,
            GameplayDatabase database,
            int targetIndex)
        {
            DishDef dish = choice?.Kind == cfg.RewardKind.DishChoice
                ? database.GetDish(choice.Id)
                : null;
            return dish?.ArchetypeWeights != null
                && targetIndex >= 0
                && targetIndex < dish.ArchetypeWeights.Count
                && dish.ArchetypeWeights[targetIndex] > 0f;
        }

        private static bool IsPityEligibleSlot(cfg.RewardSlot slot)
        {
            return slot != null
                && slot.Kind == cfg.RewardKind.DishChoice
                && slot.ChoiceCount > 1
                && slot.RequiredPickCount == 1;
        }

        private static GameplayDatabase CreateMembershipDatabase(int hidden)
        {
            return CreateDatabase(new[]
            {
                CreateDish(Target0DishId, hidden + 100, hidden + 100, new[] { 1f, 0f, 0f }),
                CreateDish(Target1DishId, hidden + 100, hidden + 100, new[] { 0f, 1f, 0f }),
                CreateDish(MultiArchetypeDishId, hidden, hidden, new[] { 0.25f, 10f, 0f }),
                CreateDish("pity_neutral", hidden, hidden, new[] { 0f, 0f, 0f }),
            });
        }

        private static GameplayDatabase CreateGuaranteeDatabase(int hidden)
        {
            return CreateDatabase(new[]
            {
                CreateDish(Target0DishId, hidden + 100, hidden + 100, new[] { 1f, 0f, 0f }),
                CreateDish("pity_non_target_a", hidden, hidden, new[] { 0f, 1f, 0f }),
                CreateDish("pity_non_target_b", hidden, hidden, new[] { 0f, 1f, 0f }),
                CreateDish("pity_non_target_c", hidden, hidden, new[] { 0f, 0f, 1f }),
                CreateDish("pity_non_target_d", hidden, hidden, new[] { 0f, 0f, 0f }),
            });
        }

        private static GameplayDatabase CreateArchetypeChangeDatabase(int hidden)
        {
            return CreateDatabase(new[]
            {
                CreateDish(Target0DishId, hidden + 100, hidden + 100, new[] { 1f, 0f, 0f }),
                CreateDish(Target1DishId, hidden + 100, hidden + 100, new[] { 0f, 1f, 0f }),
                CreateDish("pity_change_non_target_a", hidden, hidden, new[] { 0f, 0f, 1f }),
                CreateDish("pity_change_non_target_b", hidden, hidden, new[] { 0f, 0f, 1f }),
                CreateDish("pity_change_non_target_c", hidden, hidden, new[] { 0f, 0f, 0f }),
                CreateDish("pity_change_non_target_d", hidden, hidden, new[] { 0f, 0f, 0f }),
            });
        }

        private static GameplayDatabase CreateNoFlavoredTargetDatabase(int hidden)
        {
            return CreateDatabase(new[]
            {
                CreateDish(Target0DishId, hidden, hidden, new[] { 1f, 0f, 0f }),
                CreateDish("pity_flavored_non_target_a", hidden, hidden, new[] { 0f, 1f, 0f }, "flavor_test"),
                CreateDish("pity_flavored_non_target_b", hidden, hidden, new[] { 0f, 1f, 0f }, "flavor_test"),
                CreateDish("pity_flavored_non_target_c", hidden, hidden, new[] { 0f, 0f, 1f }, "flavor_test"),
                CreateDish("pity_flavored_non_target_d", hidden, hidden, new[] { 0f, 0f, 0f }, "flavor_test"),
            });
        }

        private static GameplayDatabase CreateDatabase(IEnumerable<DishDef> dishes)
        {
            return new GameplayDatabase(
                dishes,
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
        }

        private static DishDef CreateDish(
            string id,
            int hiddenMin,
            int hiddenMax,
            IReadOnlyList<float> archetypeWeights,
            string flavorId = null)
        {
            return new DishDef(
                id,
                id,
                deliciousness: 1,
                DishShape.FromRows(new[] { "X" }),
                hiddenMin,
                hiddenMax,
                baseWeight: 1f,
                Array.Empty<string>(),
                flavorId: flavorId ?? string.Empty,
                archetypeWeights: archetypeWeights);
        }

        private static cfg.RewardSlot CreateDishSlot(
            int choiceCount,
            int requiredPickCount,
            string poolId = "pool_dish")
        {
            JSONNode json = JSON.Parse($@"{{
                ""id"": ""pity_test_{poolId}_{choiceCount}_{requiredPickCount}"",
                ""groupId"": ""pity_test"",
                ""kind"": 2,
                ""choiceCount"": {choiceCount},
                ""requiredPickCount"": {requiredPickCount},
                ""weight"": 1,
                ""poolId"": ""{poolId}"",
                ""dishHiddenOffset"": [0, 0],
                ""itemLuckOffset"": [0, 0],
                ""fragmentHiddenOffset"": [0, 0],
                ""goldHiddenOffset"": [0, 0],
                ""name"": ""pity_test"",
                ""ruleTemplate"": """"
            }}");
            return cfg.RewardSlot.DeserializeRewardSlot(json);
        }

        private sealed class PityRollResult
        {
            public PityRollResult(
                GameplayDatabase database,
                GameRun run,
                int hiddenScore,
                List<RewardChoice> firstChoices,
                List<RewardChoice> secondChoices,
                List<RewardChoice> thirdChoices)
            {
                Database = database;
                Run = run;
                HiddenScore = hiddenScore;
                FirstChoices = firstChoices;
                SecondChoices = secondChoices;
                ThirdChoices = thirdChoices;
            }

            public GameplayDatabase Database { get; }

            public GameRun Run { get; }

            public int HiddenScore { get; }

            public List<RewardChoice> FirstChoices { get; }

            public List<RewardChoice> SecondChoices { get; }

            public List<RewardChoice> ThirdChoices { get; }
        }
    }
}
