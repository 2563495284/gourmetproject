using System;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SkillScopeResolverTests
    {
        [Test]
        public void JellyCandidateScopeKeepsConditionRowAndFullActionArea()
        {
            DishShape oneCell = DishShape.FromRows(new[] { "X" });
            DishDef jellyDef = Dish("jelly", "果冻", oneCell, "sk_jelly");
            DishDef neighborDef = Dish("neighbor", "相邻食物", oneCell);
            var db = new GameplayDatabase(
                new[] { jellyDef, neighborDef },
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var board = new DiningTable(5, 4);
            DishInstance jelly = Place(board, 1, jellyDef, oneCell, 2, 1);
            Place(board, 2, neighborDef, oneCell, 3, 1);
            var rule = new SkillRuleDef(
                "ss_multflat_per_dish_all_adj_v0p2",
                "sk_jelly",
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.DishCount,
                SkillScope.RowAndSelf,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                SkillActionType.AddMultFlat,
                SkillScope.RoundAndSelf,
                0,
                new[] { 0.2f },
                Array.Empty<string>());

            SkillScopeVisual visual = SkillScopeResolver.Resolve(
                db,
                board,
                jelly,
                rule,
                SkillScopeVisualMode.CandidateScope);

            Assert.That(visual.ConditionCells, Has.Count.EqualTo(5));
            Assert.That(visual.ConditionCells.All(c => c.Y == 1), Is.True);
            Assert.That(visual.VisualTargetCells, Has.Count.EqualTo(5));
            Assert.That(visual.VisualTargetCells, Does.Contain(new GridPos(1, 1)));
            Assert.That(visual.VisualTargetCells, Has.No.Member(new GridPos(3, 2)));
            Assert.That(visual.VisualTargetDishInstanceIds, Is.EquivalentTo(new[] { 1, 2 }));
        }

        private static DishInstance Place(
            DiningTable board,
            int id,
            DishDef def,
            DishShape shape,
            int x,
            int y)
        {
            var dish = new DishInstance(
                id,
                def,
                new Placement(shape, 0, new GridPos(x, y)),
                def.SkillIds,
                Array.Empty<string>());
            board.Place(dish);
            return dish;
        }

        private static DishDef Dish(string id, string name, DishShape shape, params string[] skillIds)
        {
            return new DishDef(
                id,
                name,
                1,
                shape,
                0,
                0,
                1f,
                skillIds,
                string.Empty,
                false);
        }
    }
}
