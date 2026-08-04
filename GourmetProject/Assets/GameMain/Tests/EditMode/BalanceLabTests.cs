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

        private static BalanceSampleResult Valid(int score) => new BalanceSampleResult { IsValid = true, TotalScore = score };
    }
}
