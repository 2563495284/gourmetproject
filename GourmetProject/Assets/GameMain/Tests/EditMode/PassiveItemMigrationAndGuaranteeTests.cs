using System.Collections.Generic;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class PassiveItemMigrationAndGuaranteeTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void FromSaveData_SkipsUnknownItemsAndClearsOrphanedGlobalCounters()
        {
            GameRun run = CreateRun();
            RunSaveData save = run.ToSaveData();
            int goldBeforeLoad = save.Gold;
            save.Items = new List<RunItemSaveData>
            {
                new RunItemSaveData
                {
                    ItemId = "item_removed_from_config",
                    Count = 99,
                    StateJson = "count:99",
                },
            };
            save.MealBonusRemaining = 7;
            save.ScoreToOneRemaining = 3;

            GameRun restored = GameRun.FromSaveData(_tables, _database, save);

            Assert.That(restored.Items, Is.Empty);
            Assert.That(restored.Gold, Is.EqualTo(goldBeforeLoad), "未知条目不应折算或补偿金币");
            Assert.That(restored.MealBonusRemaining, Is.Zero);
            Assert.That(restored.ScoreToOneRemaining, Is.Zero);
            Assert.That(restored.ToSaveData().Items, Is.Empty, "再次保存时不应保留幽灵装饰品和消耗品");
        }

        [Test]
        public void Loan_GrantsConfiguredGoldOnlyAfterRepaymentNodeWasCreated()
        {
            ItemDefinition loan = ItemDefinition.Get(_tables, "item_loan", cfg.ItemKind.Passive);
            Assert.That(loan, Is.Not.Null);

            GameRun withoutTimeline = CreateRun();
            int noTimelineGold = withoutTimeline.Gold;
            withoutTimeline.AcquireItem(loan.Id, 0);
            Assert.That(withoutTimeline.Gold, Is.EqualTo(noTimelineGold));
            Assert.That(withoutTimeline.RuntimeTimelineNodes, Is.Empty);

            GameRun withTimeline = CreateRun();
            withTimeline.BeginTimeline("loan-test", 7f);
            int beforeGold = withTimeline.Gold;
            withTimeline.AcquireItem(loan.Id, 0);

            Assert.That(withTimeline.Gold, Is.EqualTo(beforeGold + (int)loan.EffectValue));
            RuntimeTimelineNode node = withTimeline.RuntimeTimelineNodes.Single(
                candidate => candidate.ActionId == "act_loan_repay");
            Assert.That(node.SourceItemId, Is.EqualTo(loan.Id));
            Assert.That(node.WeekEndAnchored, Is.True);
            Assert.That(node.Day, Is.EqualTo(7));
        }

        [Test]
        public void WeeklyTimelinePassives_UseModelConfigurationAndAreIdempotent()
        {
            GameRun run = CreateRun();
            run.BeginTimeline("week-one", 7f);
            using (RunPersistence.SuppressSave())
            {
                run.AcquireItem("item_extra_day", 0);
                run.AcquireItem("item_extra_interest", 0);
            }

            run.IncrementWeek();
            run.BeginTimeline("week-two", 7f);
            var runtime = new ItemRuntime(run);
            runtime.ApplyWeekTimelinePassives();
            runtime.ApplyWeekTimelinePassives();

            int configuredDays = (int)ItemDefinition
                .Get(_tables, "item_extra_day", cfg.ItemKind.Passive)
                .EffectValue;
            int configuredInterestNodes = (int)ItemDefinition
                .Get(_tables, "item_extra_interest", cfg.ItemKind.Passive)
                .EffectValue;
            Assert.That(run.TimelineLengthDays, Is.EqualTo(configuredDays));
            Assert.That(
                run.RuntimeTimelineNodes.Count(node =>
                    node.SourceItemId == "item_extra_interest"
                    && node.ActionId == "act_interest"
                    && node.WeekEndAnchored),
                Is.EqualTo(configuredInterestNodes));
            Assert.That(
                run.RuntimeTimelineNodes
                    .Where(node => node.SourceItemId == "item_extra_interest")
                    .All(node => node.Day == configuredDays),
                Is.True);

            RuntimeTimelineNode interestNode = run.RuntimeTimelineNodes.First(
                node => node.SourceItemId == "item_extra_interest");
            Assert.That(run.RemoveRuntimeTimelineNode(interestNode.Id), Is.True);
            runtime.ApplyWeekTimelinePassives();
            Assert.That(
                run.RuntimeTimelineNodes.Count(node => node.SourceItemId == "item_extra_interest"),
                Is.EqualTo(System.Math.Max(0, configuredInterestNodes - 1)),
                "同一条周时间轴已经应用过后，即使节点被删除也不应再次补建");

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            new ItemRuntime(restored).ApplyWeekTimelinePassives();
            Assert.That(
                restored.RuntimeTimelineNodes.Count(node => node.SourceItemId == "item_extra_interest"),
                Is.EqualTo(System.Math.Max(0, configuredInterestNodes - 1)),
                "周内幂等标记应随存档恢复");
        }

        [Test]
        public void LuckyEventGuarantee_UsesFourNaturalDrawsThenForcesFifth()
        {
            GameRun run = CreateRun();
            run.AcquireItem("item_lucky_guarantee", 0);
            PassiveItemModel guarantee = GuaranteeModel(run);
            var rng = new FixedWeightedIndexRandomStream(1);

            for (int draw = 1; draw <= 4; draw++)
            {
                cfg.GameEvent natural = EventService.RollActionEventWithGuarantee(run, rng);
                Assert.That(natural, Is.Not.Null);
                Assert.That(natural.HasEventType(cfg.ActionBehavior.Reward), Is.True,
                    "测试固定抽取一个自然 Reward 成员，验证它不会提前重置保底");
                Assert.That(guarantee.EventGuaranteeStreak, Is.EqualTo(draw));
            }

            cfg.GameEvent fifth = EventService.RollActionEventWithGuarantee(run, rng);
            Assert.That(fifth, Is.Not.Null);
            Assert.That(fifth.HasEventType(cfg.ActionBehavior.Reward), Is.True);
            Assert.That(guarantee.EventGuaranteeStreak, Is.Zero);
        }

        [Test]
        public void LuckyEventGuarantee_ForcedQueueHasPriorityAndDefersDueGuarantee()
        {
            GameRun run = CreateRun();
            run.AcquireItem("item_lucky_guarantee", 0);
            PassiveItemModel guarantee = GuaranteeModel(run);
            var rng = new FixedWeightedIndexRandomStream(0);
            for (int draw = 0; draw < 4; draw++)
            {
                Assert.That(EventService.RollActionEventWithGuarantee(run, rng), Is.Not.Null);
            }

            Assert.That(guarantee.EventGuaranteeStreak, Is.EqualTo(4));
            const string forcedEventId = "ev_midnight_tasting";
            run.QueueForcedEvent(forcedEventId);

            cfg.GameEvent queued = EventService.RollActionEventWithGuarantee(run, rng);
            Assert.That(queued.Id, Is.EqualTo(forcedEventId));
            Assert.That(guarantee.EventGuaranteeStreak, Is.EqualTo(4), "强制队列不消费已到期保底");

            cfg.GameEvent deferredGuarantee = EventService.RollActionEventWithGuarantee(run, rng);
            Assert.That(deferredGuarantee.HasEventType(cfg.ActionBehavior.Reward), Is.True);
            Assert.That(guarantee.EventGuaranteeStreak, Is.Zero);
        }

        private PassiveItemModel GuaranteeModel(GameRun run)
            => run.PassiveModels.Single(model => model.ItemId == "item_lucky_guarantee");

        private GameRun CreateRun()
        {
            string characterId = _tables.TbCharacter.DataList.First().Id;
            return new GameRun(_tables, _database, characterId, "passive-migration-guarantee-tests");
        }

        private sealed class FixedWeightedIndexRandomStream : IRandomStream
        {
            private readonly int _index;

            public FixedWeightedIndexRandomStream(int index)
            {
                _index = index;
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
                => weights.Count == 0 ? 0 : System.Math.Min(_index, weights.Count - 1);
        }
    }
}
