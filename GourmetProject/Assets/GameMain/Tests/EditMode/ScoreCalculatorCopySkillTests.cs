using System;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ScoreCalculatorCopySkillTests
    {
        [Test]
        public void OnSettleCopySkill_RecordsRequestAndResolvesCopiedSkillWithoutMutatingDish()
        {
            DishShape oneCell = DishShape.FromRows(new[] { "X" });
            SkillDef copySkill = Skill(
                "skill_copy",
                "复制",
                Rule("copy", "skill_copy", SkillActionType.CopySkill, SkillScope.Adjacent, 1f));
            SkillDef flatSkill = Skill(
                "skill_flat",
                "加分",
                Rule("flat", "skill_flat", SkillActionType.AddFlat, SkillScope.Self, 5f));
            DishDef copyDef = Dish("dish_copy", "复制菜", oneCell, "skill_copy");
            DishDef flatDef = Dish("dish_flat", "加分菜", oneCell, "skill_flat");
            GameplayDatabase db = Database(copyDef, flatDef, copySkill, flatSkill);
            DiningTable table = TableWith(copyDef, flatDef, oneCell, out DishInstance copyDish, out _);

            ScoreResult result = new ScoreCalculator().Calculate(table, db);

            Assert.That(result.CopySkillRequests.Count, Is.EqualTo(1));
            Assert.That(result.CopySkillRequests[0].TargetInstanceId, Is.EqualTo(copyDish.Id));
            Assert.That(result.CopySkillRequests[0].Candidates, Is.EquivalentTo(new[] { "skill_flat" }));
            Assert.That(result.CopySkillRequests[0].SelectedSkillIds, Is.EquivalentTo(new[] { "skill_flat" }));
            Assert.That(result.ScoreLines.Any(l =>
                l.DishInstanceId == copyDish.Id
                && l.Kind == ScoreLineKind.CopySkill
                && Math.Abs(l.Value - 1f) < 0.001f), Is.True);
            Assert.That(result.ScoreLines.Any(l =>
                l.DishInstanceId == copyDish.Id
                && l.Kind == ScoreLineKind.DishFlat
                && l.Source.Name == "复制菜<技能复制>"
                && Math.Abs(l.Value - 5f) < 0.001f), Is.True);
            Assert.AreEqual(30, result.Total);
            Assert.That(copyDish.SkillIds, Does.Not.Contain("skill_flat"));
        }

        [Test]
        public void SettleCopySkill_AppliesRequestWithSourceLabel()
        {
            DishShape oneCell = DishShape.FromRows(new[] { "X" });
            SkillDef copySkill = Skill(
                "skill_copy",
                "复制",
                Rule("copy", "skill_copy", SkillActionType.CopySkill, SkillScope.Adjacent, 1f));
            SkillDef flatSkill = Skill(
                "skill_flat",
                "加分",
                Rule("flat", "skill_flat", SkillActionType.AddFlat, SkillScope.Self, 5f));
            DishDef copyDef = Dish("dish_copy", "复制菜", oneCell, "skill_copy");
            DishDef flatDef = Dish("dish_flat", "加分菜", oneCell, "skill_flat");
            GameplayDatabase db = Database(copyDef, flatDef, copySkill, flatSkill);
            DiningTable table = TableWith(copyDef, flatDef, oneCell, out DishInstance copyDish, out _);
            var session = new BattleSession(table, db, Rng(), Array.Empty<RecipeSlot>(), requiredScore: 0);

            ScoreResult preview = session.PreviewScore();
            Assert.That(preview.CopySkillRequests.Count, Is.EqualTo(1));
            Assert.That(copyDish.SkillIds, Does.Not.Contain("skill_flat"));

            ScoreResult result = session.Settle();

            Assert.AreEqual(30, result.Total);
            Assert.That(copyDish.SkillIds, Does.Contain("skill_flat"));
            Assert.That(copyDish.GetSkillSource("skill_flat"), Is.EqualTo("复制菜<技能复制>"));
        }

        private static IRandomStream Rng(ulong seed = 0x9E3779B97F4A7C15UL) => new Xoshiro256SS(seed);

        private static SkillDef Skill(string id, string name, params SkillRuleDef[] rules)
        {
            return new SkillDef(id, name, string.Empty, Array.Empty<string>(), rules, rules.Select(_ => string.Empty).ToArray());
        }

        private static SkillRuleDef Rule(
            string id,
            string skillId,
            SkillActionType actionType,
            SkillScope actionScope,
            float actionValue)
        {
            return new SkillRuleDef(
                id,
                skillId,
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                actionType,
                actionScope,
                0,
                new[] { actionValue },
                Array.Empty<string>());
        }

        private static DishDef Dish(string id, string name, DishShape shape, string skillId)
        {
            return new DishDef(
                id,
                name,
                10,
                shape,
                0,
                0,
                1f,
                new[] { skillId },
                string.Empty,
                false);
        }

        private static GameplayDatabase Database(
            DishDef copyDef,
            DishDef flatDef,
            SkillDef copySkill,
            SkillDef flatSkill)
        {
            return new GameplayDatabase(
                new[] { copyDef, flatDef },
                new[] { copySkill, flatSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
        }

        private static DiningTable TableWith(
            DishDef copyDef,
            DishDef flatDef,
            DishShape oneCell,
            out DishInstance copyDish,
            out DishInstance flatDish)
        {
            var table = new DiningTable(2, 1);
            copyDish = new DishInstance(1, copyDef, MakePlacement(oneCell, 0, 0), copyDef.SkillIds, Array.Empty<string>());
            flatDish = new DishInstance(2, flatDef, MakePlacement(oneCell, 1, 0), flatDef.SkillIds, Array.Empty<string>());
            table.Place(copyDish);
            table.Place(flatDish);
            return table;
        }

        private static Placement MakePlacement(DishShape shape, int x, int y)
        {
            return new Placement(shape, 0, new GridPos(x, y));
        }
    }
}
