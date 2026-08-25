using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ActiveItemPreparedDishFlavorTests
    {
        private const string DishId = "prepared_flavor_target";
        private const string NumbFlavorId = "t_numb";
        private cfg.Tables _tables;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
        }

        [Test]
        public void EnumerateSourceBackedBattleDishes_IncludesServingOutletDish()
        {
            (GameRun run, BattleSession session) = CreatePreparedBattle();
            DishInstance prepared = session.PreparedServe.Dish;

            IReadOnlyList<ActiveTarget> targets =
                BattleUseContext.EnumerateSourceBackedBattleDishes(session, run);

            Assert.That(targets, Has.Count.EqualTo(1));
            Assert.That(targets[0].TargetKind, Is.EqualTo(cfg.ItemTargetKind.DiningTableDish));
            Assert.That(targets[0].Id, Is.EqualTo(prepared.Id.ToString()));
        }

        [Test]
        public void AddFlavorToDish_ServingOutletDish_UpdatesRecipeAndPreparedOrientation()
        {
            (GameRun run, BattleSession session) = CreatePreparedBattle();
            DishInstance prepared = session.PreparedServe.Dish;
            var target = new ActiveTarget(
                prepared.Id.ToString(),
                prepared.Placement.Origin.X,
                prepared.Placement.Origin.Y,
                cfg.ItemTargetKind.DiningTableDish);
            var context = new BattleUseContext(session, run);

            bool applied = context.AddFlavorToDish(target, NumbFlavorId);

            Assert.That(applied, Is.True);
            Assert.That(run.GetRecipeFlavorIds(0), Does.Contain(NumbFlavorId));
            Assert.That(prepared.FlavorIds, Does.Contain(NumbFlavorId));
            Assert.That(prepared.Placement.RotationIndex, Is.EqualTo(3));
            Assert.That(
                session.FindPreparedServePlacements().All(placement => placement.RotationIndex == 3),
                Is.True);
        }

        [Test]
        public void ServingOutletNumbPreview_UsesCounterClockwiseRotationFromOldToNewOrientation()
        {
            Assert.That(
                DishIconRenderTexturePreview.CounterClockwiseStepsBetween(0, 3),
                Is.EqualTo(1));
            Assert.That(
                DishIconRenderTexturePreview.CounterClockwiseStepsBetween(3, 2),
                Is.EqualTo(1));
            Assert.That(
                DishIconRenderTexturePreview.CounterClockwiseStepsBetween(2, 2),
                Is.Zero);
        }

        private (GameRun Run, BattleSession Session) CreatePreparedBattle()
        {
            var dish = new DishDef(
                DishId,
                "出餐口测试食物",
                deliciousness: 10,
                DishShape.FromRows(new[] { "X", "X" }),
                hiddenMin: 0,
                hiddenMax: 0,
                baseWeight: 1f,
                skillIds: Array.Empty<string>(),
                flavorId: string.Empty);
            var numb = new FlavorDef(
                NumbFlavorId,
                "麻",
                string.Empty,
                FlavorEffectType.Rotate,
                new[] { 1f },
                Array.Empty<string>(),
                string.Empty);
            var database = new GameplayDatabase(
                new[] { dish },
                Array.Empty<SkillDef>(),
                new[] { numb },
                Array.Empty<RecipeDef>());
            var run = new GameRun(
                _tables,
                database,
                string.Empty,
                "prepared-dish-flavor-test");
            Assert.That(run.AddBonusDish(dish.Id), Is.True);

            var entry = new RecipeSlotEntry(
                dish.Id,
                extraFlavorIds: null,
                extraSkillIds: null,
                scoreMultiplier: 1f,
                scoreFlatBonus: 0f,
                sourceBookIndex: 0,
                sourceDishIndex: 0);
            var session = new BattleSession(
                new DiningTable(2, 2),
                database,
                new Xoshiro256SS(20260824UL),
                new[] { new RecipeSlot("prepared-flavor-slot", new[] { entry }) },
                requiredScore: 0);
            ServePrepareResult prepared = session.PrepareServe(0);
            Assert.That(prepared.Success, Is.True);
            return (run, session);
        }
    }
}
