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
    public sealed class BossPassiveArchetypePityTests
    {
        private const string BossPassiveSlotId = "slot_boss_passive";

        private cfg.Tables _tables;
        private GameplayDatabase _database;
        private string _configDirectory;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            _configDirectory = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = LoadTables(clearArchetypeTags: false);
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void Configuration_ExplicitArchetypeTagsMatchDesignedItems()
        {
            AssertTaggedIds(
                cfg.ItemArchetypeTag.Count,
                "item_count_le_mult",
                "item_count_as_all",
                "item_count_as_plus1",
                "item_flavored_count_as",
                "item_count_ge_mult",
                "item_self_count_flat",
                "item_self_count_mult",
                "item_edge_count_as");
            AssertTaggedIds(
                cfg.ItemArchetypeTag.Cake,
                "item_cake_retain",
                "item_cake_init_bonus",
                "item_cake_accel",
                "item_cake_to_gold",
                "item_cake_req_minus",
                "item_cake_req_minus_30",
                "item_random_two_as_cake",
                "item_cake_on_settle",
                "item_count_as_cake");
            AssertTaggedIds(
                cfg.ItemArchetypeTag.SweetTransfer,
                "item_transfer_target_mult",
                "item_transfer_source_mult",
                "item_gold_on_transfer",
                "item_transfer_extra_targets",
                "item_transfer_target_flat",
                "item_transfer_source_flat");
        }

        [Test]
        public void StateMachine_FirstHitDoesNotArm_AndFirstMissArmsOnlySecond()
        {
            GameRun hitRun = CreateRun(_tables, _database, "pity-state-hit");
            Assert.That(hitRun.BeginBossPassiveArchetypePity(), Is.False);
            hitRun.CompleteBossPassiveArchetypePity(archetypeOffered: true);
            Assert.That(hitRun.BossPassiveArchetypeRewardCount, Is.EqualTo(1));
            Assert.That(hitRun.BossPassiveArchetypePityArmed, Is.False);
            Assert.That(hitRun.BeginBossPassiveArchetypePity(), Is.False);

            GameRun missRun = CreateRun(_tables, _database, "pity-state-miss");
            missRun.CompleteBossPassiveArchetypePity(archetypeOffered: false);
            Assert.That(missRun.BossPassiveArchetypePityArmed, Is.True);
            Assert.That(missRun.BeginBossPassiveArchetypePity(), Is.True);
            missRun.CompleteBossPassiveArchetypePity(archetypeOffered: false);
            Assert.That(missRun.BossPassiveArchetypeRewardCount, Is.EqualTo(2));
            Assert.That(missRun.BossPassiveArchetypePityArmed, Is.False);
            Assert.That(missRun.BeginBossPassiveArchetypePity(), Is.False,
                "第二次无论是否有可用流派候选，都应消费本周保底。");
        }

        [Test]
        public void StateMachine_SaveRoundTripPreservesArmedSecondOffer_AndNewWeekResets()
        {
            GameRun run = CreateRun(_tables, _database, "pity-save");
            run.CompleteBossPassiveArchetypePity(archetypeOffered: false);

            RunSaveData save = run.ToSaveData();
            GameRun restored = GameRun.FromSaveData(_tables, _database, save);

            Assert.That(save.BossPassiveArchetypePityWeekIndex, Is.EqualTo(1));
            Assert.That(save.BossPassiveArchetypeRewardCount, Is.EqualTo(1));
            Assert.That(save.BossPassiveArchetypePityArmed, Is.True);
            Assert.That(restored.BeginBossPassiveArchetypePity(), Is.True);

            restored.IncrementWeek();
            Assert.That(restored.BossPassiveArchetypeRewardCount, Is.Zero);
            Assert.That(restored.BossPassiveArchetypePityArmed, Is.False);
            Assert.That(restored.BeginBossPassiveArchetypePity(), Is.False);
        }

        [Test]
        public void BossRoll_FirstMissMakesSecondOfferContainArchetype_WithoutDuplicates_AndIsDeterministic()
        {
            ulong seed = FindSeedWhoseFirstBossOfferMisses();
            RollPair first = RollBossPair(_tables, _database, seed);
            RollPair repeated = RollBossPair(_tables, _database, seed);

            Assert.That(ContainsArchetype(first.First, _tables), Is.False);
            Assert.That(ContainsArchetype(first.Second, _tables), Is.True);
            Assert.That(first.Second, Has.Count.EqualTo(3));
            Assert.That(first.Second.Select(choice => choice.Id).Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(first.Second.Count));
            Assert.That(first.Run.BossPassiveArchetypeRewardCount, Is.EqualTo(2));
            Assert.That(first.Run.BossPassiveArchetypePityArmed, Is.False);
            CollectionAssert.AreEqual(
                first.First.Select(choice => choice.Id).ToArray(),
                repeated.First.Select(choice => choice.Id).ToArray());
            CollectionAssert.AreEqual(
                first.Second.Select(choice => choice.Id).ToArray(),
                repeated.Second.Select(choice => choice.Id).ToArray());
        }

        [Test]
        public void BossRoll_ArmedWithoutEligibleArchetypeFallsBackToUnchangedNormalRollAndConsumesPity()
        {
            cfg.Tables taglessTables = LoadTables(clearArchetypeTags: true);
            GameplayDatabase taglessDatabase = GameplayContentBuilder.BuildDatabase(taglessTables);
            cfg.RewardSlot slot = taglessTables.TbRewardSlot.Get(BossPassiveSlotId);

            GameRun normalRun = CreateRun(taglessTables, taglessDatabase, "pity-no-candidate-normal");
            List<RewardChoice> normal = RewardPoolService.RollChoices(
                CreateContext(taglessTables, normalRun, 70707UL),
                slot);

            GameRun armedRun = CreateRun(taglessTables, taglessDatabase, "pity-no-candidate-armed");
            armedRun.CompleteBossPassiveArchetypePity(archetypeOffered: false);
            List<RewardChoice> armed = RewardPoolService.RollChoices(
                CreateContext(taglessTables, armedRun, 70707UL),
                slot);

            CollectionAssert.AreEqual(
                normal.Select(choice => choice.Id).ToArray(),
                armed.Select(choice => choice.Id).ToArray(),
                "无可用流派候选时应沿用普通随机结果和随机数消费。");
            Assert.That(armedRun.BossPassiveArchetypeRewardCount, Is.EqualTo(2));
            Assert.That(armedRun.BossPassiveArchetypePityArmed, Is.False);
        }

        [Test]
        public void BossRoll_ArmedExcludesHeldAndLockedItems()
        {
            GameRun run = CreateRun(_tables, _database, "pity-eligibility");
            ItemDefinition held = ItemDefinition.All(_tables, cfg.ItemKind.Passive)
                .First(item => item.ArchetypeTags.Count > 0
                    && MetaProgressService.IsItemUnlockedForPool(_tables, item, run.MetaProgress)
                    && ItemPoolService.CanEnterPool(run, item));
            Assert.That(run.AcquireItem(held.Id, fallbackGold: 0, fireOnAcquire: false).Outcome,
                Is.EqualTo(ItemAcquireOutcome.Added));
            run.CompleteBossPassiveArchetypePity(archetypeOffered: false);

            List<RewardChoice> choices = RewardPoolService.RollChoices(
                CreateContext(_tables, run, 80808UL),
                _tables.TbRewardSlot.Get(BossPassiveSlotId));

            Assert.That(ContainsArchetype(choices, _tables), Is.True);
            Assert.That(choices.Select(choice => choice.Id), Does.Not.Contain(held.Id));
            Assert.That(choices.All(choice =>
                MetaProgressService.IsItemUnlockedForPool(
                    _tables,
                    ItemDefinition.Get(_tables, choice.Id, cfg.ItemKind.Passive),
                    run.MetaProgress)), Is.True);
        }

        [Test]
        public void NonBossPassiveSlotDoesNotReadOrAdvanceBossPity()
        {
            GameRun run = CreateRun(_tables, _database, "pity-non-boss");
            run.CompleteBossPassiveArchetypePity(archetypeOffered: false);

            RewardPoolService.RollChoices(
                CreateContext(_tables, run, 90909UL),
                _tables.TbRewardSlot.Get("passive_choice_3"));

            Assert.That(run.BossPassiveArchetypeRewardCount, Is.EqualTo(1));
            Assert.That(run.BossPassiveArchetypePityArmed, Is.True);
        }

        [Test]
        public void PendingOffer_ReopenUsesCachedCandidatesWithoutAdvancingPityAgain()
        {
            GameRun run = CreateRun(_tables, _database, "pity-pending-cache");
            RewardOffer generated = RewardGranter.BuildConfigOffer(
                run,
                new Xoshiro256SS(100101UL),
                _tables.TbRewardSlot.Get(BossPassiveSlotId));
            run.SetPendingRewardOffer("pity-cache-key", generated);

            RewardOffer firstOpen = run.GetPendingRewardOffer("pity-cache-key");
            RewardOffer secondOpen = run.GetPendingRewardOffer("pity-cache-key");

            Assert.That(firstOpen, Is.Not.Null);
            Assert.That(secondOpen, Is.Not.Null);
            Assert.That(run.BossPassiveArchetypeRewardCount, Is.EqualTo(1));
            CollectionAssert.AreEqual(
                firstOpen.MainChoices.Select(choice => choice.Id).ToArray(),
                secondOpen.MainChoices.Select(choice => choice.Id).ToArray());
        }

        private void AssertTaggedIds(cfg.ItemArchetypeTag tag, params string[] expectedIds)
        {
            string[] actual = ItemDefinition.All(_tables, cfg.ItemKind.Passive)
                .Where(item => item.HasArchetypeTag(tag))
                .Select(item => item.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            CollectionAssert.AreEquivalent(expectedIds, actual);
        }

        private ulong FindSeedWhoseFirstBossOfferMisses()
        {
            cfg.RewardSlot slot = _tables.TbRewardSlot.Get(BossPassiveSlotId);
            for (ulong seed = 1; seed <= 512; seed++)
            {
                GameRun run = CreateRun(_tables, _database, $"pity-seed-{seed}");
                List<RewardChoice> choices = RewardPoolService.RollChoices(
                    CreateContext(_tables, run, seed),
                    slot);
                if (!ContainsArchetype(choices, _tables))
                {
                    return seed;
                }
            }

            Assert.Fail("在前 512 个确定性种子中未找到第一次星评装饰品未命中流派的样本。");
            return 0;
        }

        private static RollPair RollBossPair(cfg.Tables tables, GameplayDatabase database, ulong seed)
        {
            GameRun run = CreateRun(tables, database, $"pity-pair-{seed}");
            cfg.RewardSlot slot = tables.TbRewardSlot.Get(BossPassiveSlotId);
            RewardContext context = CreateContext(tables, run, seed);
            List<RewardChoice> first = RewardPoolService.RollChoices(context, slot);
            List<RewardChoice> second = RewardPoolService.RollChoices(context, slot);
            return new RollPair(run, first, second);
        }

        private static bool ContainsArchetype(IReadOnlyList<RewardChoice> choices, cfg.Tables tables)
        {
            return choices.Any(choice =>
                choice.Kind == cfg.RewardKind.PassiveItemChoice
                && ItemDefinition.Get(tables, choice.Id, cfg.ItemKind.Passive)?.ArchetypeTags.Count > 0);
        }

        private cfg.Tables LoadTables(bool clearArchetypeTags)
        {
            return new cfg.Tables(name =>
            {
                JSONNode json = JSON.Parse(File.ReadAllText(Path.Combine(_configDirectory, name + ".json")));
                if (clearArchetypeTags
                    && string.Equals(name, "tbpassiveitem", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (JSONNode row in json.Children)
                    {
                        row["archetypeTags"] = JSON.Parse("[]");
                    }
                }

                return json;
            });
        }

        private static RewardContext CreateContext(cfg.Tables tables, GameRun run, ulong seed)
        {
            return new RewardContext(
                tables,
                run,
                run.CurrentWeek,
                package: null,
                rng: new Xoshiro256SS(seed));
        }

        private static GameRun CreateRun(
            cfg.Tables tables,
            GameplayDatabase database,
            string seed)
        {
            return new GameRun(
                tables,
                database,
                "glutton_dog",
                seed,
                weekIndex: 1,
                execution: RunExecutionEnvironment.CreateIsolated(tables, seed));
        }

        private sealed class RollPair
        {
            public RollPair(
                GameRun run,
                List<RewardChoice> first,
                List<RewardChoice> second)
            {
                Run = run;
                First = first;
                Second = second;
            }

            public GameRun Run { get; }
            public List<RewardChoice> First { get; }
            public List<RewardChoice> Second { get; }
        }
    }
}
