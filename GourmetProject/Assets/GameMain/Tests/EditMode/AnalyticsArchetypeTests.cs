using System.IO;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Analytics;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class AnalyticsArchetypeTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            string configDirectory = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name =>
                JSON.Parse(File.ReadAllText(Path.Combine(configDirectory, name + ".json"))));
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void GeneratedConfiguration_ContainsValidThreeWayWeightsAndThresholds()
        {
            Assert.That(_tables.TbGameBase.ArchetypePrimaryMinShare, Is.EqualTo(0.45f).Within(0.0001f));
            Assert.That(_tables.TbGameBase.ArchetypePrimaryMinLead, Is.EqualTo(0.10f).Within(0.0001f));
            foreach (cfg.DishBase dish in _tables.TbDishBase.DataList)
            {
                Assert.That(dish.ArchetypeWeights, Has.Count.EqualTo(3), dish.Id);
                Assert.That(
                    dish.ArchetypeWeights[0] + dish.ArchetypeWeights[1] + dish.ArchetypeWeights[2],
                    Is.EqualTo(1f).Within(0.001f),
                    dish.Id);
            }
        }

        [TestCase(0.60, 0.25, 0.15, "0")]
        [TestCase(0.20, 0.62, 0.18, "1")]
        [TestCase(0.10, 0.20, 0.70, "2")]
        [TestCase(0.44, 0.30, 0.26, "mixed")]
        [TestCase(0.46, 0.38, 0.16, "mixed")]
        [TestCase(0.45, 0.35, 0.20, "0")]
        [TestCase(0.50, 0.50, 0.00, "mixed")]
        public void Classify_UsesConfiguredShareAndLeadRules(
            double share0,
            double share1,
            double share2,
            string expected)
        {
            ArchetypeVector result = ArchetypeService.Classify(share0, share1, share2, 0.45, 0.10);
            Assert.That(result.Id, Is.EqualTo(expected));
        }

        [Test]
        public void Classify_AbnormalVectorsAreMixed()
        {
            Assert.That(ArchetypeService.Classify(double.NaN, 0.5d, 0.5d, 0.45d, 0.10d).Id, Is.EqualTo("mixed"));
            Assert.That(ArchetypeService.Classify(double.PositiveInfinity, 0d, 0d, 0.45d, 0.10d).Id, Is.EqualTo("mixed"));
            Assert.That(ArchetypeService.Classify(-0.1d, 0.6d, 0.5d, 0.45d, 0.10d).Id, Is.EqualTo("mixed"));
            Assert.That(ArchetypeService.Classify(0.4d, 0.3d, 0.2d, 0.45d, 0.10d).Id, Is.EqualTo("mixed"));
        }

        [Test]
        public void RunId_RoundTrips_AndOldSaveGetsMigrated()
        {
            var run = new GameRun(_tables, _database, "character_money", "analytics-test");
            RunSaveData data = run.ToSaveData();
            GameRun restored = GameRun.FromSaveData(_tables, _database, data);
            Assert.That(restored.RunId, Is.EqualTo(run.RunId));

            data.RunId = null;
            GameRun migrated = GameRun.FromSaveData(_tables, _database, data);
            Assert.That(migrated.RunId, Is.Not.Null.And.Not.Empty);
            Assert.That(migrated.RunId, Is.Not.EqualTo(run.SeedText));
        }

        [Test]
        public void PendingActionChoiceRevision_IncrementsOnlyForSameOffer()
        {
            var run = new GameRun(_tables, _database, "character_money", "analytics-reroll-test");
            var first = ActionScheduleService.GenerateChoices(run, new GourmetProject.Core.Rng.Xoshiro256SS(7UL));
            run.SetPendingActionChoices("offer-a", first);
            Assert.That(run.PendingActionChoiceRevision, Is.Zero);

            var second = ActionScheduleService.RerollChoices(run, new GourmetProject.Core.Rng.Xoshiro256SS(8UL));
            run.SetPendingActionChoices("offer-a", second);
            Assert.That(run.PendingActionChoiceRevision, Is.EqualTo(1));

            run.SetPendingActionChoices("offer-b", first);
            Assert.That(run.PendingActionChoiceRevision, Is.Zero);
        }
    }
}
