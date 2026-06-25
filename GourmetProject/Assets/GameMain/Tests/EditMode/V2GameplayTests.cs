using System.Collections.Generic;
using System.IO;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Gameplay;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;

namespace GourmetProject.Tests
{
    public class V2GameplayTests
    {
        [Test]
        public void ActionSchedule_SatisfiesConfiguredWindows_AndAvoidsAdjacentRepeats()
        {
            GameRun run = NewRun(week: 1);
            var rng = new RandomService();
            rng.Init(123UL);

            ActionScheduleService.RollSchedule(run, rng.Stream("schedule"));

            Assert.GreaterOrEqual(run.ScheduledActionSteps.Count, 12);
            AssertHasGroupInRange(run.ScheduledActionSteps, "grp_reward", startInclusive: 2, endInclusive: 5);
            AssertHasGroupInRange(run.ScheduledActionSteps, "grp_reward", startInclusive: 9, endInclusive: 11);

            int eventCount = CountGroupInRange(run.ScheduledActionSteps, "grp_event_food", startInclusive: 1, endInclusive: 7);
            Assert.That(eventCount, Is.InRange(1, 2));

            for (int i = 1; i < run.ScheduledActionSteps.Count; i++)
            {
                Assert.AreNotEqual(run.ScheduledActionSteps[i - 1].GroupId, run.ScheduledActionSteps[i].GroupId);
            }
        }

        [Test]
        public void HiddenScore_HardActionProducesHigherTargetAndRewardHidden()
        {
            GameRun run = NewRun(week: 2);
            run.CurrentDay = 3;
            run.RestoreActionSchedule(null, 4);

            cfg.GameAction normal = run.Tables.TbAction.Get("act_food_dish");
            cfg.GameAction hard = run.Tables.TbAction.Get("act_food_hard_passive");

            var normalContext = new ActionExecutionContext(normal);
            var hardContext = new ActionExecutionContext(hard);

            Assert.Greater(HiddenScoreService.TargetScore(run, hardContext), HiddenScoreService.TargetScore(run, normalContext));
            Assert.Greater(HiddenScoreService.PassiveItemHiddenScore(run, hardContext), HiddenScoreService.DishHiddenScore(run, normalContext));
        }

        [Test]
        public void GoldRewardRange_UsesActionDifficultyAndCurve()
        {
            GameRun run = NewRun(week: 1);
            cfg.GameAction normal = run.Tables.TbAction.Get("act_food_dish");
            cfg.GameAction hard = run.Tables.TbAction.Get("act_food_hard_passive");
            GoldRange normalRange = HiddenScoreService.GoldRewardRange(run, new ActionExecutionContext(normal), run.Tables.TbRewardPackage.Get(normal.RewardPackageId));
            GoldRange hardRange = HiddenScoreService.GoldRewardRange(run, new ActionExecutionContext(hard), run.Tables.TbRewardPackage.Get(hard.RewardPackageId));

            Assert.Greater(hardRange.Min, normalRange.Min);
            Assert.Greater(hardRange.Max, hardRange.Min);
        }

        [Test]
        public void HiddenScoreWeight_PrefersCloserHiddenMean()
        {
            float close = RewardPoolService.HiddenScoreWeight(10f, hiddenMean: 20f, requiredHidden: 20, distanceFloor: 5);
            float far = RewardPoolService.HiddenScoreWeight(10f, hiddenMean: 40f, requiredHidden: 20, distanceFloor: 5);

            Assert.Greater(close, far);
        }

        private static GameRun NewRun(int week)
        {
            cfg.Tables tables = LoadTables();
            GameplayDatabase database = GameplayContentBuilder.BuildDatabase(tables);
            return new GameRun(tables, database, "glutton_dog", "v2-test", week);
        }

        private static cfg.Tables LoadTables()
        {
            string root = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "StreamingAssets", "Config");
            return new cfg.Tables(name =>
            {
                string path = Path.Combine(root, name + ".json");
                return JSON.Parse(File.ReadAllText(path));
            });
        }

        private static void AssertHasGroupInRange(IReadOnlyList<ActionScheduleStep> steps, string groupId, int startInclusive, int endInclusive)
        {
            Assert.Greater(CountGroupInRange(steps, groupId, startInclusive, endInclusive), 0, $"{groupId} missing in configured range.");
        }

        private static int CountGroupInRange(IReadOnlyList<ActionScheduleStep> steps, string groupId, int startInclusive, int endInclusive)
        {
            int count = 0;
            int end = System.Math.Min(endInclusive, steps.Count - 1);
            for (int i = startInclusive; i <= end; i++)
            {
                if (steps[i].GroupId == groupId)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
