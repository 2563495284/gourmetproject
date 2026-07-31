using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ActionScheduleGuaranteeTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;
        private FirstPositiveRandomStream _rng;

        [SetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = GameplayContentBuilder.BuildDatabase(_tables);
            _rng = new FirstPositiveRandomStream();
        }

        [Test]
        public void MinimumCandidatesTakePriorityOverMaximumCandidates()
        {
            ResetGroupRules();
            cfg.ActionLargeGroup minimum = Group("lg_normal");
            cfg.ActionLargeGroup maximumOnly = Group("lg_normalHard");
            SetWeight(minimum, 0f);
            SetWeight(maximumOnly, 100f);
            SetBounds(minimum.MinGuaranteeCounts, new[] { 1 });
            SetBounds(minimum.MaxGuaranteeCounts, new[] { 1 });
            SetBounds(maximumOnly.MinGuaranteeCounts, new[] { 0 });
            SetBounds(maximumOnly.MaxGuaranteeCounts, new[] { 1 });

            string picked = ActionScheduleService.EnsureCurrentGroup(CreateRun(), _rng);

            Assert.That(picked, Is.EqualTo(minimum.Id));
        }

        [Test]
        public void SatisfiedMinimumUsesOnlyGroupsBelowMaximum()
        {
            ResetGroupRules();
            cfg.ActionLargeGroup reachedMaximum = Group("lg_normal");
            cfg.ActionLargeGroup belowMaximum = Group("lg_normalHard");
            SetWeight(reachedMaximum, 100f);
            SetWeight(belowMaximum, 100f);
            SetBounds(reachedMaximum.MinGuaranteeCounts, new[] { 0, 0 });
            SetBounds(reachedMaximum.MaxGuaranteeCounts, new[] { 1, 1 });
            SetBounds(belowMaximum.MinGuaranteeCounts, new[] { 0, 0 });
            SetBounds(belowMaximum.MaxGuaranteeCounts, new[] { 1, 1 });
            GameRun run = CreateRun();
            AppendCompletedAction(run, reachedMaximum.Id);

            string picked = ActionScheduleService.EnsureCurrentGroup(run, _rng);

            Assert.That(picked, Is.EqualTo(belowMaximum.Id));
        }

        [Test]
        public void AllMaximumsReachedFallsBackToFullWeightedPool()
        {
            ResetGroupRules();
            cfg.ActionLargeGroup weighted = Group("lg_normal");
            cfg.ActionLargeGroup zeroWeight = Group("lg_normalHard");
            SetWeight(weighted, 100f);
            SetWeight(zeroWeight, 0f);
            SetBounds(weighted.MinGuaranteeCounts, new[] { 0 });
            SetBounds(weighted.MaxGuaranteeCounts, new[] { 0 });
            SetBounds(zeroWeight.MinGuaranteeCounts, new[] { 0 });
            SetBounds(zeroWeight.MaxGuaranteeCounts, new[] { 0 });

            string picked = ActionScheduleService.EnsureCurrentGroup(CreateRun(), _rng);

            Assert.That(picked, Is.EqualTo(weighted.Id));
        }

        [Test]
        public void ActionThirteenUsesFullWeightedReplacementAndAllowsConsecutiveGroup()
        {
            ResetGroupRules();
            cfg.ActionLargeGroup weighted = Group("lg_normal");
            SetWeight(weighted, 100f);
            SetBounds(weighted.MinGuaranteeCounts, Enumerable.Repeat(0, 12).ToArray());
            SetBounds(weighted.MaxGuaranteeCounts, Enumerable.Repeat(0, 12).ToArray());
            GameRun run = CreateRun();
            for (int index = 0; index < 12; index++)
            {
                AppendCompletedAction(run, weighted.Id);
            }

            string action13 = ActionScheduleService.EnsureCurrentGroup(run, _rng);
            run.AdvanceActionStep();
            string action14 = ActionScheduleService.EnsureCurrentGroup(run, _rng);

            Assert.That(action13, Is.EqualTo(weighted.Id));
            Assert.That(action14, Is.EqualTo(weighted.Id));
            Assert.That(run.ActionGroupSequence[12], Is.EqualTo(run.ActionGroupSequence[13]));
        }

        [Test]
        public void ZeroWeightGroupCanBeForcedByMinimumButNotByFreeRandom()
        {
            ResetGroupRules();
            cfg.ActionLargeGroup forced = Group("lg_normal");
            cfg.ActionLargeGroup weighted = Group("lg_normalHard");
            SetWeight(forced, 0f);
            SetWeight(weighted, 100f);
            SetBounds(forced.MinGuaranteeCounts, new[] { 1 });
            SetBounds(forced.MaxGuaranteeCounts, new[] { 1 });

            string guaranteed = ActionScheduleService.EnsureCurrentGroup(CreateRun(), _rng);

            SetBounds(forced.MinGuaranteeCounts, new[] { -1 });
            SetBounds(forced.MaxGuaranteeCounts, new[] { -1 });
            string free = ActionScheduleService.EnsureCurrentGroup(CreateRun(), _rng);

            Assert.That(guaranteed, Is.EqualTo(forced.Id));
            Assert.That(free, Is.EqualTo(weighted.Id));
        }

        [Test]
        public void WeekChangeResetsCountsAndUsesThatWeeksBounds()
        {
            ResetGroupRules();
            cfg.ActionLargeGroup forcedOnWeekTwo = Group("lg_normal");
            cfg.ActionLargeGroup freeWeighted = Group("lg_normalHard");
            SetWeight(forcedOnWeekTwo, 0f, 0f);
            SetWeight(freeWeighted, 100f, 100f);
            SetBounds(
                forcedOnWeekTwo.MinGuaranteeCounts,
                new[] { -1 },
                new[] { 1 });
            SetBounds(
                forcedOnWeekTwo.MaxGuaranteeCounts,
                new[] { -1 },
                new[] { 1 });
            GameRun run = CreateRun();
            AppendCompletedAction(run, forcedOnWeekTwo.Id);
            run.SetWeekIndex(2);
            run.BeginTimeline("week-2", 20f);

            string picked = ActionScheduleService.EnsureCurrentGroup(run, _rng);

            Assert.That(picked, Is.EqualTo(forcedOnWeekTwo.Id));
        }

        [Test]
        public void SameStepIsIdempotentAndLegacyWeekPlanIsIgnored()
        {
            ResetGroupRules();
            cfg.ActionLargeGroup weighted = Group("lg_normal");
            cfg.ActionLargeGroup legacyPlanned = Group("lg_normalHard");
            SetWeight(weighted, 100f);
            SetWeight(legacyPlanned, 0f);
            GameRun run = CreateRun();
            run.SetActionWeekPlan(1, 0, new[] { legacyPlanned.Id });

            string first = ActionScheduleService.EnsureCurrentGroup(run, _rng);
            string second = ActionScheduleService.EnsureCurrentGroup(run, _rng);

            Assert.That(first, Is.EqualTo(weighted.Id));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(run.ActionGroupSequence.Count, Is.EqualTo(1));
        }

        [Test]
        public void ShippedConfigurationHasAlignedWeeklyBoundsAndValidRanges()
        {
            foreach (cfg.ActionLargeGroup group in _tables.TbActionLargeGroup.DataList)
            {
                Assert.That(group.FallbackWeights.Count, Is.EqualTo(4), $"{group.Id} 必须配置 4 周权重");
                Assert.That(group.MinGuaranteeCounts.Count, Is.EqualTo(4), $"{group.Id} 最小保底必须保留 4 周索引");
                Assert.That(group.MaxGuaranteeCounts.Count, Is.EqualTo(4), $"{group.Id} 最大保底必须保留 4 周索引");
                Assert.That(group.MinGuaranteeCounts[0].Count, Is.EqualTo(12), $"{group.Id} 第一周最小保底应配置前 12 次行动");
                Assert.That(group.MaxGuaranteeCounts[0].Count, Is.EqualTo(12), $"{group.Id} 第一周最大保底应配置前 12 次行动");

                for (int week = 0; week < 4; week++)
                {
                    IReadOnlyList<int> minimum = group.MinGuaranteeCounts[week];
                    IReadOnlyList<int> maximum = group.MaxGuaranteeCounts[week];
                    int actionCount = Math.Max(minimum.Count, maximum.Count);
                    for (int action = 0; action < actionCount; action++)
                    {
                        if (action >= minimum.Count || action >= maximum.Count)
                        {
                            continue;
                        }

                        int min = minimum[action];
                        int max = maximum[action];
                        if (min >= 0 && max >= 0)
                        {
                            Assert.That(min, Is.LessThanOrEqualTo(max), $"{group.Id} 第{week + 1}周第{action + 1}次行动下限不能高于上限");
                        }
                    }
                }
            }
        }

        private GameRun CreateRun()
        {
            string characterId = _tables.TbCharacter.DataList.First().Id;
            var run = new GameRun(_tables, _database, characterId, "action-schedule-guarantee-tests");
            run.BeginTimeline("test", 20f);
            return run;
        }

        private cfg.ActionLargeGroup Group(string id)
        {
            return _tables.TbActionLargeGroup.Get(id);
        }

        private void ResetGroupRules()
        {
            foreach (cfg.ActionLargeGroup group in _tables.TbActionLargeGroup.DataList)
            {
                SetWeight(group, 0f);
                SetBounds(group.MinGuaranteeCounts, new[] { -1 });
                SetBounds(group.MaxGuaranteeCounts, new[] { -1 });
            }
        }

        private static void SetWeight(cfg.ActionLargeGroup group, params float[] weights)
        {
            group.FallbackWeights.Clear();
            group.FallbackWeights.AddRange(weights);
        }

        private static void SetBounds(List<List<int>> destination, params int[][] weeks)
        {
            destination.Clear();
            foreach (int[] week in weeks)
            {
                destination.Add(new List<int>(week));
            }
        }

        private static void AppendCompletedAction(GameRun run, string groupId)
        {
            run.AppendActionGroup(groupId);
            run.AdvanceActionStep();
        }

        private sealed class FirstPositiveRandomStream : IRandomStream
        {
            public RngState State { get; set; }

            public uint NextUInt() => 0;

            public ulong NextULong() => 0;

            public int Range(int minInclusive, int maxExclusive) => minInclusive;

            public float Range(float minInclusive, float maxExclusive) => minInclusive;

            public float NextFloat() => 0f;

            public double NextDouble() => 0.0;

            public bool NextBool(double probability = 0.5) => probability > 0.0;

            public void Shuffle<T>(IList<T> list)
            {
            }

            public T Pick<T>(IReadOnlyList<T> list) => list[0];

            public int WeightedPickIndex(IReadOnlyList<float> weights)
            {
                for (int index = 0; index < weights.Count; index++)
                {
                    if (weights[index] > 0f)
                    {
                        return index;
                    }
                }

                return 0;
            }
        }
    }
}
