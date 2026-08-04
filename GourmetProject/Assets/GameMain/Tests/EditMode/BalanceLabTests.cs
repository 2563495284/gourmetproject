using System.Collections.Generic;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Balance;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BalanceLabTests
    {
        [Test]
        public void Statistics_UsesInterpolatedQuantilesAndPassRate()
        {
            var checkpoint = new BuildCheckpoint { Name = "W1", Week = 1, RequiredScore = 25 };
            var samples = new List<BalanceSampleResult>
            {
                Valid(10), Valid(20), Valid(30), Valid(40),
                new BalanceSampleResult { IsValid = false, FailureReason = "invalid" },
            };

            BalanceStatistics result = BalanceStatisticsCalculator.Calculate("test", checkpoint, samples, roundTo: 10);

            Assert.That(result.ValidRate, Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(result.P50, Is.EqualTo(25f).Within(0.0001f));
            Assert.That(result.CurrentPassRate, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(result.SuggestedNormal % 10, Is.Zero);
            Assert.That(result.Warnings.Any(v => v.Contains("无效样本")), Is.True);
        }

        [Test]
        public void Simulation_SameSeedProducesSameFormalPreviewScore()
        {
            var config = new ConfigService();
            config.LoadAll();
            cfg.Tables tables = config.Tables;
            GameplayDatabase database = GameplayContentBuilder.BuildDatabase(tables);
            cfg.Character character = tables.TbCharacter.DataList[0];
            var baseRun = new GameRun(tables, database, character.Id, "balance-test");
            DiningTable table = BattleSessionFactory.BuildTablePreviewForBalance(baseRun, string.Empty, new Xoshiro256SS(7));
            DishDef dish = database.AllDishes.First(d => table.FindValidPlacements(d).Count > 0);
            Placement placement = table.FindValidPlacements(dish)[0];
            var checkpoint = new BuildCheckpoint
            {
                Name = "deterministic",
                CharacterId = character.Id,
                Week = 1,
                RequiredScore = 1,
                Dishes = new List<BuildReplayStep>
                {
                    new BuildReplayStep
                    {
                        DishId = dish.Id,
                        X = placement.Origin.X,
                        Y = placement.Origin.Y,
                        Rotation = placement.RotationIndex,
                    },
                },
            };
            var service = new BalanceSimulationService(tables, database);

            BalanceSampleResult first = service.RunSample(checkpoint, 12345);
            BalanceSampleResult second = service.RunSample(checkpoint, 12345);

            Assert.That(first.IsValid, Is.True, first.FailureReason);
            Assert.That(second.IsValid, Is.True, second.FailureReason);
            Assert.That(second.TotalScore, Is.EqualTo(first.TotalScore));
            Assert.That(second.GoldDelta, Is.EqualTo(first.GoldDelta));
            Assert.That(second.Dishes.Select(v => v.Score), Is.EqualTo(first.Dishes.Select(v => v.Score)));
        }

        [Test]
        public void AutoRun_SmokeTwentySeeds_IsDeterministicAndReportsRealBattles()
        {
            var config = new ConfigService();
            config.LoadAll();
            cfg.Tables tables = config.Tables;
            GameplayDatabase database = GameplayContentBuilder.BuildDatabase(tables);
            cfg.Character character = tables.TbCharacter.DataList[0];
            var simulator = new HeadlessRunSimulator(tables, database);
            var traces = new List<AutoRunTrace>();
            for (int seed = 1001; seed < 1021; seed++)
            {
                var request = new AutoRunRequest { CharacterId = character.Id, PlayerLevel = AutoPlayerLevel.Normal, Seed = seed };
                AutoRunTrace first = simulator.Run(request);
                AutoRunTrace second = simulator.Run(request);
                Assert.That(first.FailureReason, Is.EqualTo(second.FailureReason), $"seed={seed}");
                Assert.That(first.Stages.Select(v => v.Score), Is.EqualTo(second.Stages.Select(v => v.Score)), $"seed={seed}");
                Assert.That(first.Stages.Select(v => v.GoldBalance), Is.EqualTo(second.Stages.Select(v => v.GoldBalance)), $"seed={seed}");
                Assert.That(first.Stages.All(v => v.MealPasses <= v.MealBattles), Is.True, $"seed={seed}");
                Assert.That(first.Stages.All(v => !v.BossPassed || v.BossReached), Is.True, $"seed={seed}");
                traces.Add(first);
            }

            int stages = traces.Sum(v => v.Stages.Count);
            int meals = traces.SelectMany(v => v.Stages).Sum(v => v.MealBattles);
            int bosses = traces.SelectMany(v => v.Stages).Count(v => v.BossReached);
            string summary = $"auto-smoke runs={traces.Count} completed={traces.Count(v => v.Completed)} stages={stages} meals={meals} bosses={bosses}";
            TestContext.Progress.WriteLine(summary);
            UnityEngine.Debug.Log(summary);
            foreach (var failure in traces.GroupBy(v => v.FailureReason ?? string.Empty))
                UnityEngine.Debug.Log($"auto-smoke failure count={failure.Count()} reason={failure.Key}");
            foreach (var week in traces.SelectMany(v => v.Stages).GroupBy(v => v.Week))
                UnityEngine.Debug.Log($"auto-smoke week={week.Key} scoreAvg={week.Average(v => v.Score):0} reqMaxAvg={week.Average(v => v.RequiredScore):0} mealPass={week.Sum(v => v.MealPasses)}/{week.Sum(v => v.MealBattles)} active={week.Sum(v => v.ActiveItemsUsed.Count)} rewards={week.Sum(v => v.Rewards.Count)}");
            Assert.That(stages, Is.GreaterThan(0));
            Assert.That(meals, Is.GreaterThan(0));
            Assert.That(traces.All(v => string.IsNullOrEmpty(v.FailureReason) || v.FailureReason == "红心耗尽"), Is.True,
                "自动模拟不应吞掉运行时异常。");
        }

        private static BalanceSampleResult Valid(int score) => new BalanceSampleResult { IsValid = true, TotalScore = score };
    }
}
