using System;
using System.Linq;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ActiveFoodSkillPresentationTests
    {
        [Test]
        public void CountAsSkill_IsNotIncludedInPassiveTipsPreview()
        {
            SkillRuleDef rule = Rule(
                "egg_yolk_count_as",
                SkillActionType.AddCountAs,
                SkillScope.Self,
                2f);
            SkillDef skill = Skill("egg_yolk_skill", "蛋黄酥", rule);
            DishDef definition = DishDefinition(
                "egg_yolk_pastry",
                "蛋黄酥",
                new[] { skill.Id },
                countAs: 1);
            GameplayDatabase db = Database(new[] { definition }, new[] { skill });

            int previewCountAs = FoodTipsDataFactory.ResolveIntrinsicCountAs(definition, db);

            Assert.That(previewCountAs, Is.EqualTo(1), "主动技能不应在摆放或 Tips 预览时提前生效");
        }

        [Test]
        public void CountAsSkill_SettlementProducesRevealLine()
        {
            SkillRuleDef rule = Rule(
                "egg_yolk_count_as",
                SkillActionType.AddCountAs,
                SkillScope.Self,
                2f);
            SkillDef skill = Skill("egg_yolk_skill", "蛋黄酥", rule);
            DishDef definition = DishDefinition(
                "egg_yolk_pastry",
                "蛋黄酥",
                new[] { skill.Id },
                countAs: 1);
            DishInstance dish = Dish(1, definition, 0);
            var table = new DiningTable(1, 1);
            table.Place(dish);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                Database(new[] { definition }, new[] { skill }));

            DishScore dishScore = result.DishScores.Single();
            ScoreLine countAsLine = result.ScoreLines.Single(line => line.Kind == ScoreLineKind.DishCountAs);
            Assert.That(dishScore.EffectiveCountAs, Is.EqualTo(3));
            Assert.That(countAsLine.DishInstanceId, Is.EqualTo(dish.Id));
            Assert.That(countAsLine.Before.ToDouble(), Is.EqualTo(1d));
            Assert.That(countAsLine.After.ToDouble(), Is.EqualTo(3d));
        }

        [Test]
        public void TemporaryCakeCategory_PreviewShowsAllLeftCandidates()
        {
            SkillRuleDef rule = Rule(
                "red_velvet_category",
                SkillActionType.AddTemporaryCategory,
                SkillScope.Left,
                1f,
                actionCount: 1,
                actionParams: new[] { "cat:cake;target:random" });
            SkillDef skill = Skill("red_velvet_skill", "红丝绒蛋糕", rule);
            DishDef leftA = DishDefinition("left_a", "左侧食物甲");
            DishDef leftB = DishDefinition("left_b", "左侧食物乙");
            DishDef sourceDefinition = DishDefinition("red_velvet", "红丝绒蛋糕", new[] { skill.Id }, category: "cake");
            DishInstance first = Dish(1, leftA, 0);
            DishInstance second = Dish(2, leftB, 1);
            DishInstance source = Dish(3, sourceDefinition, 3);
            var table = new DiningTable(4, 1);
            table.Place(first);
            table.Place(second);
            table.Place(source);
            GameplayDatabase db = Database(new[] { leftA, leftB, sourceDefinition }, new[] { skill });

            SkillScopeVisual preview = SkillScopeResolver.Resolve(
                db,
                table,
                source,
                rule,
                SkillScopeVisualMode.CandidateScope);

            Assert.That(preview.VisualTargetDishInstanceIds, Is.EquivalentTo(new[] { first.Id, second.Id }));
            Assert.That(preview.ActionScopeCells, Does.Contain(new GridPos(0, 0)));
            Assert.That(preview.ActionScopeCells, Does.Contain(new GridPos(1, 0)));
            Assert.That(preview.ActionScopeCells, Does.Contain(new GridPos(2, 0)), "空的左侧格也属于预览范围");
        }

        [Test]
        public void TemporaryCakeCategory_KeepsSourceAndEffectForTips()
        {
            SkillRuleDef rule = Rule(
                "red_velvet_category",
                SkillActionType.AddTemporaryCategory,
                SkillScope.Left,
                1f,
                actionCount: 1,
                actionParams: new[] { "cat:cake;target:random" });
            SkillDef skill = Skill("red_velvet_skill", "红丝绒蛋糕", rule);
            DishDef targetDefinition = DishDefinition("target", "目标食物");
            DishDef sourceDefinition = DishDefinition("red_velvet", "红丝绒蛋糕", new[] { skill.Id }, category: "cake");
            DishInstance target = Dish(1, targetDefinition, 0);
            DishInstance source = Dish(2, sourceDefinition, 1);
            var table = new DiningTable(2, 1);
            table.Place(target);
            table.Place(source);
            GameplayDatabase db = Database(new[] { targetDefinition, sourceDefinition }, new[] { skill });

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                db,
                randomIntegerSelector: (_, _) => 0);
            TemporaryCategorySideEffect sideEffect = result.TemporaryCategories.Single();
            target.AddTemporaryCategory(
                sideEffect.Category,
                sideEffect.SourceName,
                sideEffect.EffectDescription);
            FoodTipsData tips = FoodTipsDataFactory.Build(target, table, db);

            Assert.That(sideEffect.DishInstanceId, Is.EqualTo(target.Id));
            Assert.That(sideEffect.SourceName, Is.EqualTo("红丝绒蛋糕"));
            Assert.That(sideEffect.EffectDescription, Is.EqualTo("视为蛋糕"));
            Assert.That(tips.TransferredSubSkills.Single().Title, Does.Contain("红丝绒蛋糕"));
            Assert.That(tips.TransferredSubSkills.Single().Desc, Is.EqualTo("视为蛋糕"));
        }

        [Test]
        public void SettlementReveal_HidesActiveCountAndTemporaryEffectUntilTheirCues()
        {
            DishDef definition = DishDefinition("target", "目标食物");
            DishInstance dish = Dish(1, definition, 0);
            var revealState = new SettlementRevealState();
            revealState.CaptureBaseline(dish);
            dish.AddTemporaryCategory("cake", "红丝绒蛋糕", "视为蛋糕");

            Assert.That(revealState.TryBuildReveal(dish, out FoodTipsReveal baseline), Is.True);
            Assert.That(baseline.CountAs, Is.EqualTo(1));
            Assert.That(baseline.MaxTemporaryEffects, Is.Zero);

            revealState.RevealCountAs(dish.Id, 3);
            revealState.RevealTemporaryEffects(dish.Id, 1);

            Assert.That(revealState.TryBuildReveal(dish, out FoodTipsReveal revealed), Is.True);
            Assert.That(revealed.CountAs, Is.EqualTo(3));
            Assert.That(revealed.MaxTemporaryEffects, Is.EqualTo(1));
        }

        private static SkillRuleDef Rule(
            string id,
            SkillActionType actionType,
            SkillScope actionScope,
            float actionValue,
            int actionCount = 0,
            string[] actionParams = null)
        {
            return new SkillRuleDef(
                id,
                id + "_skill",
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                actionType,
                actionScope,
                actionCount,
                new[] { actionValue },
                actionParams ?? Array.Empty<string>());
        }

        private static SkillDef Skill(string id, string name, SkillRuleDef rule)
        {
            SkillRuleDef boundRule = new SkillRuleDef(
                rule.Id,
                id,
                rule.Order,
                rule.Trigger,
                rule.CondType,
                rule.CondScope,
                rule.CondUnit,
                rule.CondMode,
                rule.CondParam,
                rule.ActionType,
                rule.ActionScope,
                rule.ActionCount,
                rule.ActionValues,
                rule.ActionParams,
                rule.TermIds);
            return new SkillDef(id, name, string.Empty, Array.Empty<string>(), new[] { boundRule });
        }

        private static DishDef DishDefinition(
            string id,
            string name,
            string[] skillIds = null,
            string category = null,
            int countAs = 1)
        {
            return new DishDef(
                id,
                name,
                10,
                DishShape.FromRows(new[] { "X" }),
                0,
                0,
                1f,
                skillIds ?? Array.Empty<string>(),
                string.Empty,
                category: category,
                countAs: countAs);
        }

        private static DishInstance Dish(int id, DishDef definition, int x)
        {
            return new DishInstance(
                id,
                definition,
                new Placement(definition.Shape, 0, new GridPos(x, 0)),
                definition.SkillIds,
                Array.Empty<string>());
        }

        private static GameplayDatabase Database(DishDef[] dishes, SkillDef[] skills)
        {
            return new GameplayDatabase(
                dishes,
                skills,
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
        }
    }
}
