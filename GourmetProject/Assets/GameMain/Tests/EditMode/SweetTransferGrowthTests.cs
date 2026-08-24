using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SweetTransferGrowthTests
    {
        private const double Tolerance = 0.0001d;
        private cfg.Tables _tables;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
        }

        [Test]
        public void LargeCandyPlate_AddsPointFourWithoutWritingBackToRecipe()
        {
            const string skillId = "skill_transfer_target_growth_test";
            SkillDef skill = TransferSkill(skillId, SkillTrigger.OnSettle, SkillScope.Other, actionCount: 1);
            DishDef sourceDef = DishDef("dish_transfer_source", skillId);
            DishDef targetDef = DishDef("dish_transfer_target");
            GameplayDatabase db = Database(new[] { sourceDef, targetDef }, skill);
            var table = new DiningTable(2, 1);
            DishInstance source = Dish(1, sourceDef, 0, sourceDishIndex: 0);
            DishInstance target = Dish(2, targetDef, 1, sourceDishIndex: 1);
            target.MultiplyPermanentMult(2f);
            table.Place(source);
            table.Place(target);
            BattleSession session = Session(table, db);
            session.SweetTransferTargetMultiplier = 0.4f;

            ScoreResult preview = session.PreviewScore();

            Assert.That(preview.SkillTransfers, Has.Count.EqualTo(1));
            Assert.That(target.PermanentMultBonus.ToDouble(), Is.EqualTo(2d).Within(Tolerance));
            Assert.That(session.LastRecipeScoreMultiplierDeltas, Is.Empty);

            session.Settle();

            Assert.That(target.PermanentMultBonus.ToDouble(), Is.EqualTo(2.4d).Within(Tolerance));
            Assert.That(session.LastRecipeScoreMultiplierDeltas, Is.Empty);

            GameRun run = Run(db, sourceDef.Id, targetDef.Id);
            Assert.That(run.MultiplyRecipeScore(1, 2f), Is.True);
            Assert.That(BattleSettlementApplier.ApplyRecipeGrowth(run, session), Is.True);
            Assert.That(run.RecipeEntries[1].ScoreMultiplier.ToDouble(), Is.EqualTo(2d).Within(Tolerance));
            Assert.That(BattleSettlementApplier.ApplyRecipeGrowth(run, session), Is.False);
            Assert.That(run.RecipeEntries[1].ScoreMultiplier.ToDouble(), Is.EqualTo(2d).Within(Tolerance));
        }

        [Test]
        public void LongSpoutSyrupPot_TwoTargetsAddOneWithoutRecipeWriteback()
        {
            const string skillId = "skill_transfer_source_growth_test";
            SkillDef skill = TransferSkill(skillId, SkillTrigger.OnSettle, SkillScope.Other, actionCount: 0);
            DishDef sourceDef = DishDef("dish_transfer_source_all", skillId);
            DishDef firstTargetDef = DishDef("dish_transfer_target_first");
            DishDef secondTargetDef = DishDef("dish_transfer_target_second");
            GameplayDatabase db = Database(new[] { sourceDef, firstTargetDef, secondTargetDef }, skill);
            var table = new DiningTable(3, 1);
            DishInstance source = Dish(1, sourceDef, 0, sourceDishIndex: 0);
            DishInstance firstTarget = Dish(2, firstTargetDef, 1, sourceDishIndex: 1);
            DishInstance secondTarget = Dish(3, secondTargetDef, 2, sourceDishIndex: 2);
            source.MultiplyPermanentMult(2f);
            table.Place(source);
            table.Place(firstTarget);
            table.Place(secondTarget);
            BattleSession session = Session(table, db);
            session.SweetTransferSourceMultiplier = 0.5f;

            session.Settle();

            Assert.That(source.PermanentMultBonus.ToDouble(), Is.EqualTo(3d).Within(Tolerance));
            Assert.That(session.LastRecipeScoreMultiplierDeltas, Is.Empty);

            GameRun run = Run(db, sourceDef.Id, firstTargetDef.Id, secondTargetDef.Id);
            Assert.That(run.MultiplyRecipeScore(0, 2f), Is.True);
            Assert.That(BattleSettlementApplier.ApplyRecipeGrowth(run, session), Is.True);
            Assert.That(run.RecipeEntries[0].ScoreMultiplier.ToDouble(), Is.EqualTo(2d).Within(Tolerance));
        }

        [Test]
        public void FailedTransferWithNoLegalTarget_DoesNotCreateGrowth()
        {
            const string skillId = "skill_transfer_no_target_test";
            SkillDef skill = TransferSkill(skillId, SkillTrigger.OnSettle, SkillScope.Adjacent, actionCount: 1);
            DishDef sourceDef = DishDef("dish_transfer_isolated_source", skillId);
            DishDef targetDef = DishDef("dish_transfer_isolated_target");
            GameplayDatabase db = Database(new[] { sourceDef, targetDef }, skill);
            var table = new DiningTable(1, 1);
            DishInstance source = Dish(1, sourceDef, 0, sourceDishIndex: 0);
            DishInstance target = Dish(2, targetDef, 0, sourceDishIndex: 1);
            source.MultiplyPermanentMult(2f);
            target.MultiplyPermanentMult(2f);
            table.Place(source);
            BattleSession session = Session(table, db);
            session.SweetTransferTargetMultiplier = 0.4f;
            session.SweetTransferSourceMultiplier = 0.5f;

            ScoreResult result = session.Settle();

            Assert.That(result.SkillTransfers, Is.Empty);
            Assert.That(result.ScoreLines.Any(line => line.Kind == ScoreLineKind.SweetTransferFailed), Is.True);
            Assert.That(source.PermanentMultBonus.ToDouble(), Is.EqualTo(2d).Within(Tolerance));
            Assert.That(target.PermanentMultBonus.ToDouble(), Is.EqualTo(2d).Within(Tolerance));
            Assert.That(session.LastRecipeScoreMultiplierDeltas, Is.Empty);
        }

        [Test]
        public void InstancesWithoutRecipeSource_DoNotCreateWritebackGrowth()
        {
            const string skillId = "skill_transfer_no_recipe_source_test";
            SkillDef skill = TransferSkill(skillId, SkillTrigger.OnSettle, SkillScope.Other, actionCount: 1);
            DishDef sourceDef = DishDef("dish_transfer_untracked_source", skillId);
            DishDef targetDef = DishDef("dish_transfer_untracked_target");
            GameplayDatabase db = Database(new[] { sourceDef, targetDef }, skill);
            var table = new DiningTable(2, 1);
            DishInstance source = Dish(1, sourceDef, 0);
            DishInstance target = Dish(2, targetDef, 1);
            target.MultiplyPermanentMult(2f);
            table.Place(source);
            table.Place(target);
            BattleSession session = Session(table, db);
            session.SweetTransferTargetMultiplier = 0.4f;

            session.Settle();

            Assert.That(target.PermanentMultBonus.ToDouble(), Is.EqualTo(2.4d).Within(Tolerance));
            Assert.That(session.LastRecipeScoreMultiplierDeltas, Is.Empty);
        }

        [Test]
        public void OnServeMultiplier_RemainsInCurrentBattleWithoutRecipeWriteback()
        {
            const string skillId = "skill_transfer_on_serve_growth_test";
            SkillDef skill = TransferSkill(skillId, SkillTrigger.OnServe, SkillScope.Other, actionCount: 1);
            DishDef sourceDef = DishDef("dish_transfer_on_serve_source", skillId);
            DishDef targetDef = DishDef("dish_transfer_on_serve_target");
            GameplayDatabase db = Database(new[] { sourceDef, targetDef }, skill);
            var table = new DiningTable(2, 1);
            DishInstance target = Dish(10, targetDef, 1, sourceDishIndex: 1);
            target.MultiplyPermanentMult(2f);
            table.Place(target);
            var entry = new RecipeSlotEntry(
                sourceDef.Id,
                extraFlavorIds: null,
                extraSkillIds: null,
                scoreMultiplier: new BigDouble(2),
                sourceBookIndex: 0,
                sourceDishIndex: 0);
            var slot = new RecipeSlot("slot_transfer_on_serve", new[] { entry });
            var session = new BattleSession(
                table,
                db,
                new Xoshiro256SS(20260824UL),
                new[] { slot },
                requiredScore: 0);
            session.SweetTransferTargetMultiplier = 0.4f;
            session.SweetTransferSourceMultiplier = 0.5f;

            ServePrepareResult prepared = session.PrepareServe(0);
            Assert.That(prepared.Success, Is.True);
            ServeResult served = session.CommitPreparedServe(prepared.PreparedDish.Placements[0]);

            Assert.That(served.Success, Is.True);
            Assert.That(served.Dish.PermanentMultBonus.ToDouble(), Is.EqualTo(2.5d).Within(Tolerance));
            Assert.That(target.PermanentMultBonus.ToDouble(), Is.EqualTo(2.4d).Within(Tolerance));
            Assert.That(session.LastRecipeScoreMultiplierDeltas, Is.Empty);

            session.Settle();

            Assert.That(served.Dish.PermanentMultBonus.ToDouble(), Is.EqualTo(2.5d).Within(Tolerance));
            Assert.That(target.PermanentMultBonus.ToDouble(), Is.EqualTo(2.4d).Within(Tolerance));
            Assert.That(session.LastRecipeScoreMultiplierDeltas, Is.Empty);
        }

        [Test]
        public void TransferMultiplierItems_ConfigKeepsValuesWithoutPermanentClaim()
        {
            cfg.PassiveItem target = _tables.TbPassiveItem.Get("item_transfer_target_mult");
            cfg.PassiveItem source = _tables.TbPassiveItem.Get("item_transfer_source_mult");

            StringAssert.DoesNotContain("永久", target.Desc);
            StringAssert.DoesNotContain("永久", source.Desc);
            Assert.That(target.EffectValue, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(source.EffectValue, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void TransferGrowthItems_DoNotShowCounterText()
        {
            PassiveItemModel[] models =
            {
                new TransferTargetFlatModel(),
                new TransferSourceFlatModel(),
                new TransferTargetMultModel(),
                new TransferSourceMultModel(),
            };

            foreach (PassiveItemModel model in models)
            {
                Assert.That(model.InfoText, Is.Empty, model.GetType().Name);
            }
        }

        private GameRun Run(GameplayDatabase db, params string[] dishIds)
        {
            var run = new GameRun(_tables, db, string.Empty, "sweet-transfer-growth-test");
            foreach (string dishId in dishIds)
            {
                Assert.That(run.AddBonusDish(dishId), Is.True);
            }

            return run;
        }

        private static BattleSession Session(DiningTable table, GameplayDatabase db)
        {
            return new BattleSession(
                table,
                db,
                new Xoshiro256SS(20260824UL),
                Array.Empty<RecipeSlot>(),
                requiredScore: 0);
        }

        private static GameplayDatabase Database(IReadOnlyList<DishDef> dishes, params SkillDef[] skills)
        {
            return new GameplayDatabase(
                dishes,
                skills ?? Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
        }

        private static SkillDef TransferSkill(
            string skillId,
            SkillTrigger trigger,
            SkillScope targetScope,
            int actionCount)
        {
            var payload = new SkillRuleDef(
                id: skillId + "_payload",
                skillId: skillId,
                order: 0,
                trigger: SkillTrigger.OnSettle,
                condType: SkillConditionType.None,
                condScope: SkillScope.Self,
                condUnit: CountUnit.Instances,
                condMode: CountMode.Gate,
                condParam: string.Empty,
                actionType: SkillActionType.AddFlat,
                actionScope: SkillScope.Self,
                actionCount: 0,
                actionValues: new[] { 1f },
                actionParams: Array.Empty<string>());
            var transfer = new SkillRuleDef(
                id: skillId + "_transfer",
                skillId: skillId,
                order: 1,
                trigger: trigger,
                condType: SkillConditionType.None,
                condScope: SkillScope.Self,
                condUnit: CountUnit.Instances,
                condMode: CountMode.Gate,
                condParam: string.Empty,
                actionType: SkillActionType.TransferSkills,
                actionScope: targetScope,
                actionCount: actionCount,
                actionValues: new[] { 0f },
                actionParams: Array.Empty<string>());
            return new SkillDef(
                skillId,
                skillId,
                string.Empty,
                Array.Empty<string>(),
                new[] { payload, transfer },
                new[] { "测试载荷", "测试传递" });
        }

        private static DishDef DishDef(string id, params string[] skillIds)
        {
            return new DishDef(
                id,
                id,
                deliciousness: 10,
                shape: DishShape.FromRows(new[] { "X" }),
                hiddenMin: 0,
                hiddenMax: 0,
                baseWeight: 1f,
                skillIds: skillIds ?? Array.Empty<string>(),
                flavorId: string.Empty);
        }

        private static DishInstance Dish(
            int id,
            DishDef def,
            int x,
            int sourceDishIndex = -1)
        {
            var placement = new Placement(def.Shape, rotationIndex: 0, origin: new GridPos(x, 0));
            var dish = new DishInstance(
                id,
                def,
                placement,
                def.SkillIds,
                Array.Empty<string>());
            if (sourceDishIndex >= 0)
            {
                dish.SetSourceRecipeIndex(0, sourceDishIndex);
            }

            return dish;
        }
    }
}
