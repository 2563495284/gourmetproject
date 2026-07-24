using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class NewFoodSkillExecutorTests
    {
        [Test]
        public void SkillCount_UsesConfiguredScopeAndCountsEverySkill()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            SkillDef frosting = Skill(
                "sk_frosting",
                Rule(
                    "sk_frosting_1",
                    "sk_frosting",
                    SkillConditionType.SkillCount,
                    SkillScope.RoundAndSelf,
                    CountMode.Per,
                    null,
                    SkillActionType.AddLayer,
                    SkillScope.CakeBuff,
                    2f));
            DishDef selfDef = Dish("frosting", cell, new[] { frosting.Id });
            DishDef adjacentDef = Dish("adjacent", cell, new[] { "marker_a", "marker_b" });
            DishDef offScopeDef = Dish("off_scope", cell, new[] { "marker_c" });
            GameplayDatabase db = Database(new[] { selfDef, adjacentDef, offScopeDef }, frosting);
            var table = new DiningTable(4, 1);
            Place(
                table,
                Instance(1, selfDef, cell, 1, 0),
                Instance(2, adjacentDef, cell, 2, 0),
                Instance(3, offScopeDef, cell, 3, 0));

            ScoreResult result = new ScoreCalculator().Calculate(table, db);

            Assert.That(result.HappyCakeLayerDelta, Is.EqualTo(6));
        }

        [Test]
        public void DishCount_DividesAfterCombiningDirectionalScopeWithSelf()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            SkillDef mochiSkill = Skill(
                "sk_mochi",
                Rule(
                    "sk_mochi_1",
                    "sk_mochi",
                    SkillConditionType.DishCount,
                    SkillScope.DownAndSelf,
                    CountMode.Per,
                    "div:2",
                    SkillActionType.AddMultFlat,
                    SkillScope.DownAndSelf,
                    0.8f,
                    null));
            DishDef mochiDef = Dish("mochi", cell, new[] { mochiSkill.Id });
            DishDef plainDef = Dish("plain", cell);
            GameplayDatabase db = Database(new[] { mochiDef, plainDef }, mochiSkill);
            var table = new DiningTable(1, 3);
            DishInstance mochi = Instance(1, mochiDef, cell, 0, 0);
            DishInstance below1 = Instance(2, plainDef, cell, 0, 1);
            DishInstance below2 = Instance(3, plainDef, cell, 0, 2);
            Place(table, mochi, below1, below2);

            ScoreResult result = new ScoreCalculator().Calculate(table, db);

            foreach (DishInstance dish in new[] { mochi, below1, below2 })
            {
                Assert.That(
                    result.DishScores.Single(score => score.DishInstanceId == dish.Id).Multiplier,
                    Is.EqualTo(1.8f).Within(0.0001f));
            }
        }

        [Test]
        public void SpatialActionScope_CanBeFilteredByCategory()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            SkillDef creamCakeSkill = Skill(
                "sk_cream_cake",
                Rule(
                    "sk_cream_cake_1",
                    "sk_cream_cake",
                    SkillConditionType.None,
                    SkillScope.Self,
                    CountMode.Gate,
                    null,
                    SkillActionType.AddFlat,
                    SkillScope.ColumnAndSelf,
                    30f,
                    "cat:cake"));
            DishDef ownerDef = Dish("cream_cake", cell, new[] { creamCakeSkill.Id }, "cake");
            DishDef cakeDef = Dish("cake", cell, null, "cake");
            DishDef plainDef = Dish("plain", cell);
            GameplayDatabase db = Database(new[] { ownerDef, cakeDef, plainDef }, creamCakeSkill);
            var table = new DiningTable(2, 3);
            DishInstance owner = Instance(1, ownerDef, cell, 0, 0);
            DishInstance cakeInColumn = Instance(2, cakeDef, cell, 0, 1);
            DishInstance plainInColumn = Instance(3, plainDef, cell, 0, 2);
            DishInstance cakeOffColumn = Instance(4, cakeDef, cell, 1, 1);
            Place(table, owner, cakeInColumn, plainInColumn, cakeOffColumn);

            ScoreResult result = new ScoreCalculator().Calculate(table, db);

            AssertFlat(result, owner, 30f);
            AssertFlat(result, cakeInColumn, 30f);
            AssertFlat(result, plainInColumn, 0f);
            AssertFlat(result, cakeOffColumn, 0f);
        }

        [Test]
        public void OccupiedCell_SourceBoardCountsRealExistingCells()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            SkillDef fruitCakeSkill = Skill(
                "sk_fruit_cake",
                Rule(
                    "sk_fruit_cake_1",
                    "sk_fruit_cake",
                    SkillConditionType.OccupiedCell,
                    SkillScope.All,
                    CountMode.Per,
                    "source:board",
                    SkillActionType.AddLayer,
                    SkillScope.CakeBuff,
                    1f));
            DishDef fruitCakeDef = Dish("fruit_cake", cell, new[] { fruitCakeSkill.Id }, "cake");
            GameplayDatabase db = Database(new[] { fruitCakeDef }, fruitCakeSkill);
            var existing = new[]
            {
                new GridPos(0, 0),
                new GridPos(1, 0),
                new GridPos(2, 0),
                new GridPos(0, 1),
                new GridPos(2, 1),
            };
            var table = new DiningTable(3, 2, existing, null);
            table.Place(Instance(1, fruitCakeDef, cell, 0, 0));

            ScoreResult result = new ScoreCalculator().Calculate(table, db);

            Assert.That(result.HappyCakeLayerDelta, Is.EqualTo(existing.Length));
        }

        [Test]
        public void AddCountAs_TargetOccupiedCellsUsesEachTargetsOwnSize()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            DishShape twoCells = DishShape.FromRows(new[] { "XX" });
            SkillDef parfaitSkill = Skill(
                "sk_parfait",
                Rule(
                    "sk_parfait_1",
                    "sk_parfait",
                    SkillConditionType.None,
                    SkillScope.Self,
                    CountMode.Gate,
                    null,
                    SkillActionType.AddCountAs,
                    SkillScope.All,
                    1f,
                    "target:occupiedcells"));
            SkillDef counterSkill = Skill(
                "sk_counter",
                Rule(
                    "sk_counter_1",
                    "sk_counter",
                    SkillConditionType.DishCount,
                    SkillScope.All,
                    CountMode.Per,
                    null,
                    SkillActionType.AddFlat,
                    SkillScope.Self,
                    1f));
            DishDef parfaitDef = Dish("parfait", cell, new[] { parfaitSkill.Id });
            DishDef counterDef = Dish("counter", cell, new[] { counterSkill.Id });
            DishDef wideDef = Dish("wide", twoCells);
            GameplayDatabase db = Database(
                new[] { parfaitDef, counterDef, wideDef },
                parfaitSkill,
                counterSkill);
            var table = new DiningTable(4, 1);
            DishInstance parfait = Instance(1, parfaitDef, cell, 0, 0);
            DishInstance counter = Instance(2, counterDef, cell, 1, 0);
            DishInstance wide = Instance(3, wideDef, twoCells, 2, 0);
            Place(table, parfait, counter, wide);

            ScoreResult result = new ScoreCalculator().Calculate(table, db);

            // 1格菜：基础1 +1；另一个1格菜同样为2；2格菜：基础1 +2。合计7。
            AssertFlat(result, counter, 7f);
        }

        [Test]
        public void SweetTransfer_WhenTransferMultiplierOnlyBuffsTheActualSource()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            SkillDef gummySkill = Skill(
                "sk_gummy",
                Rule(
                    "sk_gummy_1",
                    "sk_gummy",
                    SkillConditionType.None,
                    SkillScope.Self,
                    CountMode.Gate,
                    null,
                    SkillActionType.AddMult,
                    SkillScope.RowAndSelf,
                    1.5f,
                    "when:transfer"));
            SkillDef transferSkill = Skill(
                "sk_transfer",
                Rule(
                    "sk_transfer_1",
                    "sk_transfer",
                    SkillConditionType.None,
                    SkillScope.Self,
                    CountMode.Gate,
                    null,
                    SkillActionType.AddFlat,
                    SkillScope.Self,
                    5f),
                Rule(
                    "sk_transfer_2",
                    "sk_transfer",
                    SkillConditionType.None,
                    SkillScope.Self,
                    CountMode.Gate,
                    null,
                    SkillActionType.TransferSkills,
                    SkillScope.Other,
                    0f,
                    null,
                    1));
            DishDef gummyDef = Dish("gummy", cell, new[] { gummySkill.Id });
            DishDef sourceDef = Dish("source", cell, new[] { transferSkill.Id });
            DishDef targetDef = Dish("target", cell);
            GameplayDatabase db = Database(
                new[] { gummyDef, sourceDef, targetDef },
                gummySkill,
                transferSkill);
            var table = new DiningTable(3, 1);
            DishInstance gummy = Instance(1, gummyDef, cell, 0, 0);
            DishInstance source = Instance(2, sourceDef, cell, 1, 0);
            DishInstance target = Instance(3, targetDef, cell, 2, 0);
            Place(table, gummy, source, target);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                db,
                transferTargetSelector: (candidates, count) => new[] { target.Id });

            Assert.That(
                result.DishScores.Single(score => score.DishInstanceId == source.Id).Multiplier,
                Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(
                result.DishScores.Single(score => score.DishInstanceId == gummy.Id).Multiplier,
                Is.EqualTo(1f).Within(0.0001f),
                "软糖的被动声明不能在自身正常结算时直接乘倍率");
        }

        [Test]
        public void SweetTransfer_AddTargetModifierIncreasesSelectedTargetCount()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            SkillDef marshmallowSkill = Skill(
                "sk_marshmallow",
                Rule(
                    "sk_marshmallow_1",
                    "sk_marshmallow",
                    SkillConditionType.None,
                    SkillScope.Self,
                    CountMode.Gate,
                    null,
                    SkillActionType.TriggerSweetTransfer,
                    SkillScope.Down,
                    2f,
                    "modifier:add-targets"));
            SkillDef transferSkill = Skill(
                "sk_transfer",
                Rule(
                    "sk_transfer_1",
                    "sk_transfer",
                    SkillConditionType.None,
                    SkillScope.Self,
                    CountMode.Gate,
                    null,
                    SkillActionType.AddFlat,
                    SkillScope.Self,
                    5f),
                Rule(
                    "sk_transfer_2",
                    "sk_transfer",
                    SkillConditionType.None,
                    SkillScope.Self,
                    CountMode.Gate,
                    null,
                    SkillActionType.TransferSkills,
                    SkillScope.Other,
                    0f,
                    null,
                    1));
            DishDef marshmallowDef = Dish("marshmallow", cell, new[] { marshmallowSkill.Id });
            DishDef sourceDef = Dish("source", cell, new[] { transferSkill.Id });
            DishDef targetDef = Dish("target", cell);
            GameplayDatabase db = Database(
                new[] { marshmallowDef, sourceDef, targetDef },
                marshmallowSkill,
                transferSkill);
            var table = new DiningTable(4, 2);
            DishInstance marshmallow = Instance(1, marshmallowDef, cell, 0, 0);
            DishInstance source = Instance(2, sourceDef, cell, 0, 1);
            DishInstance target1 = Instance(3, targetDef, cell, 1, 1);
            DishInstance target2 = Instance(4, targetDef, cell, 2, 1);
            DishInstance target3 = Instance(5, targetDef, cell, 3, 1);
            Place(table, marshmallow, source, target1, target2, target3);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                db,
                transferTargetSelector: (candidates, count) => candidates.Take(count).ToArray());

            Assert.That(
                result.SkillTransfers.Count(transfer => transfer.SourceInstanceId == source.Id),
                Is.EqualTo(3));
        }

        private static void AssertFlat(ScoreResult result, DishInstance dish, float expected)
        {
            Assert.That(
                result.DishScores.Single(score => score.DishInstanceId == dish.Id).FlatBonus,
                Is.EqualTo(expected).Within(0.0001f));
        }

        private static SkillRuleDef Rule(
            string id,
            string skillId,
            SkillConditionType condType,
            SkillScope condScope,
            CountMode condMode,
            string condParam,
            SkillActionType actionType,
            SkillScope actionScope,
            float value,
            string actionParam = null,
            int actionCount = 0)
        {
            return new SkillRuleDef(
                id,
                skillId,
                0,
                SkillTrigger.OnSettle,
                condType,
                condScope,
                CountUnit.Instances,
                condMode,
                condParam,
                actionType,
                actionScope,
                actionCount,
                new[] { value },
                string.IsNullOrEmpty(actionParam)
                    ? Array.Empty<string>()
                    : new[] { actionParam });
        }

        private static SkillDef Skill(string id, params SkillRuleDef[] rules)
        {
            return new SkillDef(
                id,
                id,
                string.Empty,
                Array.Empty<string>(),
                rules,
                rules.Select(rule => rule.Id).ToArray());
        }

        private static DishDef Dish(
            string id,
            DishShape shape,
            IReadOnlyList<string> skillIds = null,
            string category = null)
        {
            return new DishDef(
                id,
                id,
                10,
                shape,
                0,
                0,
                1f,
                skillIds ?? Array.Empty<string>(),
                string.Empty,
                false,
                category: category);
        }

        private static DishInstance Instance(
            int id,
            DishDef def,
            DishShape shape,
            int x,
            int y)
        {
            return new DishInstance(
                id,
                def,
                new Placement(shape, 0, new GridPos(x, y)),
                def.SkillIds,
                Array.Empty<string>());
        }

        private static GameplayDatabase Database(
            DishDef[] dishes,
            params SkillDef[] skills)
        {
            return new GameplayDatabase(
                dishes,
                skills,
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
        }

        private static void Place(DiningTable table, params DishInstance[] dishes)
        {
            foreach (DishInstance dish in dishes)
            {
                table.Place(dish);
            }
        }
    }
}
