using System.Collections.Generic;
using System.IO;
using System.Linq;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Analytics;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
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
        public void GeneratedConfiguration_ContainsValidNonNegativeThreeWayPointsAndThresholds()
        {
            Assert.That(_tables.TbGameBase.ArchetypePrimaryMinShare, Is.EqualTo(0.45f).Within(0.0001f));
            Assert.That(_tables.TbGameBase.ArchetypePrimaryMinLead, Is.EqualTo(0.10f).Within(0.0001f));
            foreach (cfg.DishBase dish in _tables.TbDishBase.DataList)
            {
                Assert.That(dish.ArchetypeWeights, Has.Count.EqualTo(3), dish.Id);
                Assert.That(dish.ArchetypeWeights, Is.All.GreaterThanOrEqualTo(0f), dish.Id);
            }

            Assert.That(_tables.TbDishBase.DataList.Any(dish => dish.ArchetypeWeights.All(value => value == 0f)), Is.True);
        }

        [Test]
        public void GeneratedSubSkills_UseDescriptionUnitsThatMatchCountingSemantics()
        {
            Assert.That(_tables.TbSubSkill.DataList, Has.Count.EqualTo(80));
            foreach (cfg.SubSkill subSkill in _tables.TbSubSkill.DataList)
            {
                var rule = new SkillRuleDef(
                    subSkill.Id,
                    string.Empty,
                    0,
                    (SkillTrigger)(int)subSkill.Trigger,
                    (SkillConditionType)(int)subSkill.CondType,
                    (SkillScope)(int)subSkill.CondScope,
                    (CountUnit)(int)subSkill.CondUnit,
                    (CountMode)(int)subSkill.CondMode,
                    subSkill.CondParam,
                    (SkillActionType)(int)subSkill.ActionType,
                    (SkillScope)(int)subSkill.ActionScope,
                    subSkill.ActionCount,
                    subSkill.ActionValue,
                    subSkill.ActionParam);
                string description = SkillDescComposer.ComposeComponent(subSkill.DescTemplate, rule, subSkill.Signed);
                if (!subSkill.DescTemplate.Contains("{unit}"))
                {
                    continue;
                }

                string expected = rule.CondUnit == CountUnit.Kinds
                    ? "种"
                    : rule.CondUnit == CountUnit.PhysicalInstances
                        || rule.CondType == SkillConditionType.SkillCount
                        || rule.CondType == SkillConditionType.SkillTypeCount
                        || rule.CondType == SkillConditionType.TagCount
                        || rule.CondType == SkillConditionType.EmptyCell
                        || rule.CondType == SkillConditionType.OccupiedCell
                            ? "个"
                            : "份";
                Assert.That(description, Does.Contain(expected), subSkill.Id);
            }

            cfg.SubSkill hawthorn = _tables.TbSubSkill.Get("sk_hawthorn_cake_1");
            cfg.SubSkill milleCrepe = _tables.TbSubSkill.Get("sk_mille_crepe_cake_1");
            cfg.SubSkill blackForest = _tables.TbSubSkill.Get("sk_black_forest_cake_2");
            Assert.That(hawthorn.CondUnit, Is.EqualTo(cfg.CountUnit.Instances));
            Assert.That(hawthorn.DescTemplate, Does.Contain("{unit}"));
            Assert.That(milleCrepe.CondUnit, Is.EqualTo(cfg.CountUnit.PhysicalInstances));
            Assert.That(milleCrepe.DescTemplate, Does.Contain("个蛋糕"));
            Assert.That(blackForest.CondUnit, Is.EqualTo(cfg.CountUnit.PhysicalInstances));
            Assert.That(blackForest.DescTemplate, Does.Contain("个时"));
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
        public void LegacyDishAndFlavorIdsMigrateAcrossRecipeAndPendingBoard()
        {
            var run = new GameRun(_tables, _database, "character_money", "legacy-content-id-test");
            RunSaveData data = run.ToSaveData();
            data.Recipe = new RunRecipeBookSaveData
            {
                DishIds = new List<string> { "custard_bun" },
                DishExtraFlavors = new List<RunRecipeDishFlavorSaveData>
                {
                    new RunRecipeDishFlavorSaveData { FlavorIds = new List<string> { "t_rust" } },
                },
            };
            data.PendingRewardBattleView = new PendingRewardBattleViewSaveData
            {
                Dishes = new List<PendingRewardBattleDishSaveData>
                {
                    new PendingRewardBattleDishSaveData
                    {
                        Id = 1,
                        DishId = "custard_bun",
                        FlavorIds = new List<string> { "t_rust" },
                    },
                },
            };

            GameRun restored = GameRun.FromSaveData(_tables, _database, data);

            Assert.That(_database.GetDish("custard_bun")?.Id, Is.EqualTo("mantou"));
            Assert.That(_database.GetFlavor("t_rust")?.Id, Is.EqualTo("t_fresh"));
            Assert.That(restored.RecipeEntries.Single().DishId, Is.EqualTo("mantou"));
            Assert.That(restored.RecipeEntries.Single().ExtraFlavorIds, Is.EqualTo(new[] { "t_fresh" }));
            PendingRewardBattleDishSaveData pending = restored.GetPendingRewardBattleView().Dishes.Single();
            Assert.That(pending.DishId, Is.EqualTo("mantou"));
            Assert.That(pending.FlavorIds, Is.EqualTo(new[] { "t_fresh" }));
        }

        [Test]
        public void Capture_SumsAbsolutePointsThenNormalizesRecipeShares()
        {
            DishDef first = ArchetypeDish("absolute_a", 2f, 0f, 0f);
            DishDef second = ArchetypeDish("absolute_b", 0f, 1f, 0f);
            var database = new GameplayDatabase(
                new[] { first, second },
                System.Array.Empty<SkillDef>(),
                System.Array.Empty<FlavorDef>(),
                System.Array.Empty<MaterialDef>(),
                System.Array.Empty<RecipeDef>());
            var seedRun = new GameRun(_tables, database, "character_money", "absolute-archetype-test");
            RunSaveData data = seedRun.ToSaveData();
            data.Recipe = new RunRecipeBookSaveData
            {
                DishIds = new List<string> { first.Id, second.Id },
            };

            ArchetypeVector result = ArchetypeService.Capture(GameRun.FromSaveData(_tables, database, data));

            Assert.That(result.Share0, Is.EqualTo(2d / 3d).Within(0.0001d));
            Assert.That(result.Share1, Is.EqualTo(1d / 3d).Within(0.0001d));
            Assert.That(result.Share2, Is.Zero.Within(0.0001d));
            Assert.That(result.Id, Is.EqualTo("0"));
        }

        [Test]
        public void Capture_AllZeroRecipeIsMixed()
        {
            DishDef zero = ArchetypeDish("absolute_zero", 0f, 0f, 0f);
            var database = new GameplayDatabase(
                new[] { zero },
                System.Array.Empty<SkillDef>(),
                System.Array.Empty<FlavorDef>(),
                System.Array.Empty<MaterialDef>(),
                System.Array.Empty<RecipeDef>());
            var seedRun = new GameRun(_tables, database, "character_money", "zero-archetype-test");
            RunSaveData data = seedRun.ToSaveData();
            data.Recipe = new RunRecipeBookSaveData { DishIds = new List<string> { zero.Id } };

            Assert.That(ArchetypeService.Capture(GameRun.FromSaveData(_tables, database, data)).Id, Is.EqualTo("mixed"));
        }

        private static DishDef ArchetypeDish(string id, params float[] points)
            => new DishDef(
                id,
                id,
                10,
                DishShape.FromRows(new[] { "X" }),
                0,
                0,
                1f,
                System.Array.Empty<string>(),
                string.Empty,
                archetypeWeights: points);

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
