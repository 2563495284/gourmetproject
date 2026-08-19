using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BreakInfinity;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta.BossDebuffs;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using UnityEditor;

namespace GourmetProject.Tests.EditMode
{
    public sealed class DishFlavorReworkTests
    {
        [Test]
        public void Fresh_StrictlyPrefersFittingFreshCandidate()
        {
            FlavorDef fresh = Flavor("t_fresh", FlavorEffectType.ServePriority, 0f);
            DishDef plain = DishDef("plain", DishShape.FromRows(new[] { "X" }));
            DishDef freshDish = DishDef("fresh", DishShape.FromRows(new[] { "X" }), "t_fresh");
            var slot = new RecipeSlot("slot", new[] { plain.Id, freshDish.Id });
            var session = new BattleSession(
                new DiningTable(1, 1),
                Database(new[] { plain, freshDish }, flavors: new[] { fresh }),
                new Xoshiro256SS(17UL),
                new[] { slot },
                requiredScore: 0);

            ServePrepareResult result = session.PrepareServe(0);

            Assert.That(result.Outcome, Is.EqualTo(ServePrepareOutcome.Prepared));
            Assert.That(result.PreparedDish.Dish.Def.Id, Is.EqualTo("fresh"));
        }

        [Test]
        public void Fresh_FallsBackWhenFreshCandidateDoesNotFit()
        {
            FlavorDef fresh = Flavor("t_fresh", FlavorEffectType.ServePriority, 0f);
            DishDef plain = DishDef("plain", DishShape.FromRows(new[] { "X" }));
            DishDef freshDish = DishDef("fresh", DishShape.FromRows(new[] { "XX" }), "t_fresh");
            var slot = new RecipeSlot("slot", new[] { freshDish.Id, plain.Id });
            var session = new BattleSession(
                new DiningTable(1, 1),
                Database(new[] { plain, freshDish }, flavors: new[] { fresh }),
                new Xoshiro256SS(18UL),
                new[] { slot },
                requiredScore: 0);

            ServePrepareResult result = session.PrepareServe(0);

            Assert.That(result.Outcome, Is.EqualTo(ServePrepareOutcome.Prepared));
            Assert.That(result.PreparedDish.Dish.Def.Id, Is.EqualTo("plain"));
        }

        [Test]
        public void Mantou_BossInsertionAndSpriteUseCanonicalId()
        {
            DishDef plain = DishDef("plain", DishShape.FromRows(new[] { "X" }));
            DishDef mantou = DishDef("mantou", DishShape.FromRows(new[] { "X" }));
            var slot = new RecipeSlot("slot", Enumerable.Repeat(plain.Id, 10));
            var session = new BattleSession(
                new DiningTable(5, 1),
                Database(new[] { plain, mantou }),
                new Xoshiro256SS(19UL),
                new[] { slot },
                requiredScore: 0);
            new CarbMealBossDebuffModel().ApplyToBattle(session);
            int inserted = 0;

            for (int i = 0; i < 5; i++)
            {
                ServePrepareResult prepared = session.PrepareServeAutomatically(0);
                Assert.That(prepared.Success, Is.True);
                if (prepared.PreparedDish.IsBossInsertedDish)
                {
                    inserted++;
                    Assert.That(prepared.PreparedDish.Definition.Id, Is.EqualTo("mantou"));
                }

                ServeResult placed = session.PreplacePreparedServe(prepared.PreparedDish.Placements[0]);
                Assert.That(placed.Success, Is.True);
                Assert.That(session.ConfirmPendingDish(placed.Dish.Id).Success, Is.True);
            }

            Assert.That(inserted, Is.EqualTo(1));
            Assert.That(
                AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(
                    "Assets/GameMain/Content/Resources/Sprites/Dishes/mantou.png"),
                Is.Not.Null);
        }

        [Test]
        public void Sour_UnservedLayersAddToEveryServedDishAndStack()
        {
            FlavorDef sour = Flavor("t_sour", FlavorEffectType.RecipeAddMultFlat, 0.5f);
            DishInstance first = Dish(1, "first", 10, 0, Array.Empty<string>(), Array.Empty<string>());
            DishInstance second = Dish(2, "second", 10, 1, Array.Empty<string>(), Array.Empty<string>());
            var table = new DiningTable(2, 1);
            table.Place(first);
            table.Place(second);
            GameplayDatabase db = Database(new[] { first.Def, second.Def }, flavors: new[] { sour });
            var unserved = new[]
            {
                new UnservedRecipeDish(0, first.Def.Id, new[] { "t_sour", "t_sour" }),
            };

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                db,
                unservedRecipeDishes: unserved);

            Assert.That(result.DishScores.Select(score => score.Multiplier.ToDouble()),
                Is.All.EqualTo(2d).Within(0.0001d));
            Assert.That(result.Total.ToDouble(), Is.EqualTo(40d).Within(0.0001d));
        }

        [Test]
        public void Sour_AlreadyServedDoesNotApplyRecipeBonus()
        {
            FlavorDef sour = Flavor("t_sour", FlavorEffectType.RecipeAddMultFlat, 0.5f);
            DishInstance served = Dish(1, "served", 10, 0, Array.Empty<string>(), new[] { "t_sour" });
            var table = new DiningTable(1, 1);
            table.Place(served);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                Database(new[] { served.Def }, flavors: new[] { sour }));

            Assert.That(result.DishScores.Single().Multiplier.ToDouble(), Is.EqualTo(1d).Within(0.0001d));
            Assert.That(result.Total.ToDouble(), Is.EqualTo(10d).Within(0.0001d));
        }

        [Test]
        public void Salty_EachLayerRollsOnceAndExtraSettlementIsIndependentAndComplete()
        {
            FlavorDef salty = Flavor("t_salty", FlavorEffectType.ExtraSettlementChance, 0.2f);
            FlavorDef sweet = Flavor("t_sweet", FlavorEffectType.AddFlat, 2f);
            MaterialDef material = new MaterialDef(
                "material",
                "material",
                string.Empty,
                MaterialEffectType.AddFlat,
                new[] { 3f },
                Array.Empty<string>(),
                string.Empty);
            SkillRuleDef rule = new SkillRuleDef(
                "rule",
                "skill",
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.AddFlat,
                SkillScope.All,
                0,
                new[] { 5f },
                Array.Empty<string>());
            SkillDef skill = new SkillDef("skill", "skill", string.Empty, Array.Empty<string>(), new[] { rule });
            DishInstance source = Dish(1, "source", 10, 0, new[] { "skill" }, new[] { "t_salty", "t_salty", "t_sweet" });
            DishInstance target = Dish(2, "target", 10, 1, Array.Empty<string>(), Array.Empty<string>());
            var materials = new Dictionary<GridPos, IReadOnlyList<string>>
            {
                [new GridPos(0, 0)] = new[] { "material" },
            };
            var table = new DiningTable(2, 1, null, materials);
            table.Place(source);
            table.Place(target);
            GameplayDatabase db = Database(
                new[] { source.Def, target.Def },
                new[] { skill },
                new[] { salty, sweet },
                new[] { material });
            int rollCount = 0;

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                db,
                randomIntegerSelector: (_, _) => rollCount++ == 0 ? 0 : 9999);

            DishScore sourceScore = result.DishScores.Single(score => score.DishInstanceId == source.Id);
            DishScore targetScore = result.DishScores.Single(score => score.DishInstanceId == target.Id);
            Assert.That(rollCount, Is.EqualTo(2), "额外结算不得递归触发新的咸味掷骰");
            Assert.That(sourceScore.ExtraSettlementCount, Is.EqualTo(1));
            Assert.That(sourceScore.ExtraSettlementContribution.ToDouble(), Is.EqualTo(20d).Within(0.0001d));
            Assert.That(sourceScore.Contribution.ToDouble(), Is.EqualTo(40d).Within(0.0001d));
            Assert.That(targetScore.Contribution.ToDouble(), Is.EqualTo(20d).Within(0.0001d),
                "技能对其他食物的影响应在额外结算中再次执行");
            Assert.That(result.Total.ToDouble(), Is.EqualTo(60d).Within(0.0001d));
            Assert.That(result.ScoreLines.Count(line => line.Kind == ScoreLineKind.ExtraSettlement), Is.EqualTo(1));

            List<ScoreLine> orderedLines = result.ScoreLines.ToList();
            int firstSkillIndex = orderedLines.FindIndex(line =>
                line.Source?.Type == ScoreSourceType.DishSkill
                && line.DishInstanceId == target.Id);
            int materialIndex = orderedLines.FindIndex(line =>
                line.Source?.Type == ScoreSourceType.Material);
            int extraSettlementIndex = orderedLines.FindIndex(line =>
                line.Kind == ScoreLineKind.ExtraSettlement);
            int repeatedSkillIndex = orderedLines.FindLastIndex(line =>
                line.Source?.Type == ScoreSourceType.DishSkill
                && line.DishInstanceId == target.Id);
            Assert.That(firstSkillIndex, Is.LessThan(materialIndex), "主轮技能应先于材质结算");
            Assert.That(materialIndex, Is.LessThan(extraSettlementIndex), "额外结算提示应在主轮材质之后");
            Assert.That(extraSettlementIndex, Is.LessThan(repeatedSkillIndex), "额外结算提示应先于第二轮技能");
        }

        [Test]
        public void Salty_PreviewWithoutRandomSelectorExcludesRandomGain()
        {
            FlavorDef salty = Flavor("t_salty", FlavorEffectType.ExtraSettlementChance, 1f);
            DishInstance dish = Dish(1, "dish", 10, 0, Array.Empty<string>(), new[] { "t_salty" });
            var table = new DiningTable(1, 1);
            table.Place(dish);
            GameplayDatabase db = Database(new[] { dish.Def }, flavors: new[] { salty });

            ScoreResult preview = new ScoreCalculator().Calculate(table, db);
            ScoreResult settled = new ScoreCalculator().Calculate(table, db, randomIntegerSelector: (_, _) => 0);

            Assert.That(preview.Total.ToDouble(), Is.EqualTo(10d).Within(0.0001d));
            Assert.That(preview.DishScores.Single().ExtraSettlementCount, Is.Zero);
            Assert.That(settled.Total.ToDouble(), Is.EqualTo(20d).Within(0.0001d));
            Assert.That(settled.DishScores.Single().ExtraSettlementCount, Is.EqualTo(1));
        }

        [Test]
        public void HawthornCake_CountsEffectiveServingsOfTwoCellDishes()
        {
            SkillRuleDef rule = new SkillRuleDef(
                "sk_hawthorn_cake_1",
                "sk_hawthorn_cake",
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.DishSize,
                SkillScope.All,
                CountUnit.Instances,
                CountMode.Per,
                "eq:2",
                SkillActionType.AddFlat,
                SkillScope.All,
                0,
                new[] { 8f },
                new[] { "size:2" });
            SkillDef skill = new SkillDef(
                "sk_hawthorn_cake",
                "山楂糕",
                string.Empty,
                Array.Empty<string>(),
                new[] { rule });
            DishInstance hawthorn = Dish(1, "hawthorn", 0, 0, new[] { skill.Id }, Array.Empty<string>());
            DishShape twoCells = DishShape.FromRows(new[] { "XX" });
            DishDef targetDef = new DishDef(
                "two_cells",
                "two_cells",
                10,
                twoCells,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                countAs: 3);
            var target = new DishInstance(
                2,
                targetDef,
                new Placement(twoCells, 0, new GridPos(1, 0)),
                Array.Empty<string>(),
                Array.Empty<string>());
            var table = new DiningTable(3, 1);
            table.Place(hawthorn);
            table.Place(target);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                Database(new[] { hawthorn.Def, target.Def }, new[] { skill }));

            DishScore targetScore = result.DishScores.Single(score => score.DishInstanceId == target.Id);
            Assert.That(targetScore.FlatBonus.ToDouble(), Is.EqualTo(24d).Within(0.0001d));
        }

        [Test]
        public void DishCountInstances_SumsEffectiveServings()
        {
            var rule = new SkillRuleDef(
                "serving_count_rule",
                "serving_count_skill",
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.DishCount,
                SkillScope.All,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                SkillActionType.AddFlat,
                SkillScope.Self,
                0,
                new[] { 1f },
                Array.Empty<string>());
            DishShape shape = DishShape.FromRows(new[] { "X" });
            DishDef sourceDef = new DishDef(
                "serving_count_source",
                "serving_count_source",
                0,
                shape,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                countAs: 1);
            DishDef targetDef = new DishDef(
                "serving_count_target",
                "serving_count_target",
                0,
                shape,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                countAs: 3);
            var source = new DishInstance(
                1,
                sourceDef,
                new Placement(shape, 0, new GridPos(0, 0)),
                Array.Empty<string>(),
                Array.Empty<string>());
            var target = new DishInstance(
                2,
                targetDef,
                new Placement(shape, 0, new GridPos(1, 0)),
                Array.Empty<string>(),
                Array.Empty<string>());
            var table = new DiningTable(2, 1);
            table.Place(source);
            table.Place(target);

            Assert.That(
                SkillConditionEvaluator.Evaluate(rule, table, EmptyScoreHistory.Instance, source),
                Is.EqualTo(4));
        }

        [TestCase(CountUnit.Instances, "份")]
        [TestCase(CountUnit.PhysicalInstances, "个")]
        [TestCase(CountUnit.Kinds, "种")]
        public void SkillDescription_UsesConfiguredDishCountUnit(CountUnit unit, string expected)
        {
            var rule = new SkillRuleDef(
                "rule",
                "skill",
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.DishCount,
                SkillScope.All,
                unit,
                CountMode.Per,
                string.Empty,
                SkillActionType.AddFlat,
                SkillScope.Self,
                0,
                new[] { 1f },
                Array.Empty<string>());

            Assert.That(
                SkillDescComposer.ComposeComponent("每有 1 {unit}食物，分数 {0}", rule, signed: true),
                Does.Contain($"1 {expected}食物"));
        }

        [Test]
        public void RecipeReadonlyBookDisplayOrder_KeepsRecipeOrderWithinSameBase()
        {
            var dishes = new[]
            {
                DisplayDish("base_b_t_fresh", "base_b", 20, "t_fresh"),
                DisplayDish("base_a_t_bitter", "base_a", 10, "t_bitter"),
                DisplayDish("base_a", "base_a", 10),
                DisplayDish("base_a_t_sweet", "base_a", 10, "t_sweet"),
                DisplayDish("base_b", "base_b", 20),
            };
            GameplayDatabase database = Database(dishes);
            var entries = new List<RecipeBookSlot>
            {
                new RecipeBookSlot(dishes[0].Id),
                new RecipeBookSlot(dishes[1].Id),
                new RecipeBookSlot(dishes[2].Id),
                new RecipeBookSlot(dishes[3].Id),
                new RecipeBookSlot(dishes[4].Id),
            };
            var gameObject = new UnityEngine.GameObject(
                "RecipeReadonlyBookDisplayOrderTest");

            try
            {
                var view = gameObject.AddComponent<RecipeReadonlyBookView>();
                FieldInfo databaseField = typeof(RecipeReadonlyBookView)
                    .GetField(
                        "_database",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo buildOrder = typeof(RecipeReadonlyBookView)
                    .GetMethod(
                        "BuildDishDisplayOrder",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.That(databaseField, Is.Not.Null);
                Assert.That(buildOrder, Is.Not.Null);
                databaseField.SetValue(view, database);
                var order = (List<int>)buildOrder.Invoke(
                    view,
                    new object[] { entries });

                Assert.That(order, Is.EqualTo(new[] { 1, 2, 3, 0, 4 }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void RecipeFlavorDisplayOrder_FollowsAcquisitionTime()
        {
            DishDef dish = DisplayDish(
                "base_a_t_sour",
                "base_a",
                10,
                "t_sour");
            var slot = new RecipeBookSlot(dish.Id);
            slot.AddFlavor("t_salty");
            slot.AddFlavor("t_sweet");
            MethodInfo composeFlavorIds = typeof(RecipeReadonlyBookView)
                .GetMethod(
                    "ComposeFlavorIds",
                    BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(composeFlavorIds, Is.Not.Null);
            var flavorIds = (List<string>)composeFlavorIds.Invoke(
                null,
                new object[] { dish, slot.ExtraFlavorIds });

            Assert.That(
                flavorIds,
                Is.EqualTo(new[] { "t_sour", "t_salty", "t_sweet" }));
        }

        private static FlavorDef Flavor(string id, FlavorEffectType type, float value)
            => new FlavorDef(id, id, string.Empty, type, new[] { value }, Array.Empty<string>(), string.Empty);

        private static DishDef DishDef(string id, DishShape shape, string flavorId = "")
            => new DishDef(id, id, 10, shape, 0, 0, 1f, Array.Empty<string>(), flavorId);

        private static DishDef DisplayDish(
            string id,
            string baseId,
            int sortOrder,
            string flavorId = "")
            => new DishDef(
                id,
                id,
                10,
                DishShape.FromRows(new[] { "X" }),
                0,
                0,
                1f,
                Array.Empty<string>(),
                flavorId,
                baseId,
                sortOrder: sortOrder);

        private static DishInstance Dish(
            int instanceId,
            string id,
            int deliciousness,
            int x,
            IReadOnlyList<string> skillIds,
            IReadOnlyList<string> flavorIds)
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            DishDef def = new DishDef(id, id, deliciousness, shape, 0, 0, 1f, skillIds, string.Empty);
            return new DishInstance(instanceId, def, new Placement(shape, 0, new GridPos(x, 0)), skillIds, flavorIds);
        }

        private static GameplayDatabase Database(
            IReadOnlyList<DishDef> dishes,
            IReadOnlyList<SkillDef> skills = null,
            IReadOnlyList<FlavorDef> flavors = null,
            IReadOnlyList<MaterialDef> materials = null)
            => new GameplayDatabase(
                dishes,
                skills ?? Array.Empty<SkillDef>(),
                flavors ?? Array.Empty<FlavorDef>(),
                materials ?? Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
    }
}
