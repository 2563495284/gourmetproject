using System;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BattleTemporaryAreaTests
    {
        private static cfg.Tables _tables;
        private static GameplayDatabase _configuredDatabase;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _configuredDatabase = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void BattleUseContext_AddNumbFlavor_RotatesAndMovesDishToTemporaryArea()
        {
            string characterId = _tables.TbCharacter.DataList.First().Id;
            var run = new GameRun(_tables, _configuredDatabase, characterId, "temporary-area-tests");
            Assert.That(run.RecipeDishes.Count, Is.GreaterThan(0));

            DishDef definition = _configuredDatabase.GetDish(run.RecipeDishes[0]);
            Assert.That(definition, Is.Not.Null);
            var table = new DiningTable(12, 12);
            var placement = new Placement(
                definition.Shape.RotatedBy(definition.RotationIndex),
                definition.RotationIndex,
                new GridPos(0, 0));
            var dish = new DishInstance(
                77,
                definition,
                placement,
                definition.SkillIds,
                string.IsNullOrEmpty(definition.FlavorId)
                    ? Array.Empty<string>()
                    : new[] { definition.FlavorId });
            dish.SetSourceRecipeIndex(0, 0);
            table.Place(dish);

            var session = new BattleSession(
                table,
                _configuredDatabase,
                new Xoshiro256SS(1UL),
                Array.Empty<RecipeSlot>(),
                requiredScore: 0);
            var context = new BattleUseContext(session, run);
            var target = new ActiveTarget(
                dish.Id.ToString(),
                0,
                0,
                cfg.ItemTargetKind.DiningTableDish);

            bool applied = context.AddFlavorToDish(target, "t_numb");

            Assert.That(applied, Is.True);
            Assert.That(run.RecipeEntries[0].ExtraFlavorIds, Does.Contain("t_numb"));
            Assert.That(dish.FlavorIds, Does.Contain("t_numb"));
            Assert.That(table.DishCount, Is.Zero);
            Assert.That(session.TemporaryAreaDishes, Is.EqualTo(new[] { dish }));

            int expectedRotation =
                ((definition.RotationIndex - (int)_configuredDatabase.GetFlavor("t_numb").EffectValue) % 4 + 4) % 4;
            Assert.That(dish.Placement.RotationIndex, Is.EqualTo(expectedRotation));
        }

        [Test]
        public void TemporaryAreaDish_CanReturnWhileOutletWaits_WithoutServingAgain()
        {
            BattleSession session = CreateSession(entryCount: 2);
            int servedEvents = 0;
            session.Served += (_, _) => servedEvents++;

            PreparedServeDish firstPrepared = session.PrepareServe(0).PreparedDish;
            ServeResult firstServe = session.CommitPreparedServe(firstPrepared.Placements[0]);
            Assert.That(firstServe.Success, Is.True);
            Assert.That(session.ServesUsed, Is.EqualTo(1));
            Assert.That(servedEvents, Is.EqualTo(1));

            Assert.That(
                session.MoveDishToTemporaryAreaAfterRotate(firstServe.Dish.Id, 1),
                Is.True);
            Assert.That(session.PreviewScore().Total, Is.Zero);

            PreparedServeDish outletDish = session.PrepareServe(0).PreparedDish;
            Assert.That(outletDish, Is.Not.Null);
            Placement returnPlacement =
                session.FindTemporaryAreaDishPlacements(firstServe.Dish.Id).First();

            Assert.That(
                session.CommitTemporaryAreaDish(firstServe.Dish.Id, returnPlacement),
                Is.True);
            Assert.That(session.PreparedServe, Is.SameAs(outletDish));
            Assert.That(session.ServesUsed, Is.EqualTo(1));
            Assert.That(servedEvents, Is.EqualTo(1));
            Assert.That(session.TemporaryAreaDishes, Is.Empty);
            Assert.That(session.DiningTable.Dishes, Does.Contain(firstServe.Dish));
        }

        [Test]
        public void TemporaryArea_SupportsMultipleDishes_InvalidReturnStaysTemporary_AndSettleExcludesThem()
        {
            var table = new DiningTable(8, 5);
            BattleSession session = CreateSession(table, entryCount: 0);
            DishDef definition = session.Database.GetDish("dish");
            DishInstance first = CreateDish(definition, 101, new GridPos(0, 0));
            DishInstance second = CreateDish(definition, 102, new GridPos(4, 0));
            table.Place(first);
            table.Place(second);

            Assert.That(session.MoveDishToTemporaryAreaAfterRotate(first.Id, 1), Is.True);
            Assert.That(session.MoveDishToTemporaryAreaAfterRotate(second.Id, 1), Is.True);
            Assert.That(session.TemporaryAreaDishes.Count, Is.EqualTo(2));
            Assert.That(table.DishCount, Is.Zero);

            var invalidPlacement = new Placement(
                first.Placement.Orientation,
                first.Placement.RotationIndex,
                new GridPos(-1, 0));
            Assert.That(
                session.CommitTemporaryAreaDish(first.Id, invalidPlacement),
                Is.False);
            Assert.That(session.TemporaryAreaDishes.Count, Is.EqualTo(2));

            ScoreResult result = session.Settle();
            Assert.That(result.Total, Is.Zero);
            Assert.That(session.TemporaryAreaDishes.Count, Is.EqualTo(2));
        }

        [Test]
        public void RecipeNumbFlavor_StillForcesConfiguredServeRotation()
        {
            var table = new DiningTable(8, 5);
            BattleSession session = CreateSession(
                table,
                new RecipeSlotEntry("dish", new[] { "t_numb" }));

            PreparedServeDish prepared = session.PrepareServe(0).PreparedDish;

            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared.Dish.Placement.RotationIndex, Is.EqualTo(3));
            Assert.That(prepared.Placements.All(p => p.RotationIndex == 3), Is.True);
        }

        private static BattleSession CreateSession(int entryCount)
        {
            return CreateSession(new DiningTable(8, 5), entryCount);
        }

        private static BattleSession CreateSession(DiningTable table, int entryCount)
        {
            var entries = Enumerable.Range(0, entryCount)
                .Select(_ => new RecipeSlotEntry("dish"))
                .ToArray();
            return CreateSession(table, entries);
        }

        private static BattleSession CreateSession(
            DiningTable table,
            params RecipeSlotEntry[] entries)
        {
            DishDef definition = CreateDishDefinition();
            var numb = new FlavorDef(
                "t_numb",
                "麻",
                string.Empty,
                FlavorEffectType.Rotate,
                new[] { 1f },
                Array.Empty<string>(),
                "t_numb");
            var database = new GameplayDatabase(
                new[] { definition },
                Array.Empty<SkillDef>(),
                new[] { numb },
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            return new BattleSession(
                table,
                database,
                new Xoshiro256SS(1UL),
                entries.Length > 0
                    ? new[] { new RecipeSlot("recipe", entries) }
                    : Array.Empty<RecipeSlot>(),
                requiredScore: 0);
        }

        private static DishDef CreateDishDefinition()
        {
            return new DishDef(
                "dish",
                "测试菜",
                10,
                DishShape.FromRows(new[] { "XXX", "X.." }),
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                allowRotate: false);
        }

        private static DishInstance CreateDish(
            DishDef definition,
            int id,
            GridPos origin)
        {
            var placement = new Placement(
                definition.Shape,
                definition.RotationIndex,
                origin);
            return new DishInstance(
                id,
                definition,
                placement,
                Array.Empty<string>(),
                Array.Empty<string>());
        }
    }
}
