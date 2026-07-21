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
    public sealed class ScoreCalculatorSweetTransferTests
    {
        [Test]
        public void OnSettleTransferSkills_ResolvesTargetEffectImmediately()
        {
            DishShape oneCell = DishShape.FromRows(new[] { "X" });
            SkillRuleDef addFlat = Rule("sweet_add", 0, SkillActionType.AddFlat, SkillScope.Self, 5f);
            SkillRuleDef transfer = Rule("sweet_transfer", 1, SkillActionType.TransferSkills, SkillScope.Other, 0f);
            var sweetSkill = new SkillDef(
                "skill_sweet",
                "甜蜜",
                string.Empty,
                Array.Empty<string>(),
                new[] { addFlat, transfer },
                new[] { "美味 +5", "甜蜜传递" });

            DishDef sourceDef = Dish("dish_a", "A", oneCell, "skill_sweet");
            DishDef targetDef = Dish("dish_b", "B", oneCell);
            var db = new GameplayDatabase(
                new[] { sourceDef, targetDef },
                new[] { sweetSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var table = new DiningTable(2, 1);
            var source = new DishInstance(1, sourceDef, MakePlacement(oneCell, 0, 0), sourceDef.SkillIds, Array.Empty<string>());
            var target = new DishInstance(2, targetDef, MakePlacement(oneCell, 1, 0), targetDef.SkillIds, Array.Empty<string>());
            table.Place(source);
            table.Place(target);

            ScoreResult result = new ScoreCalculator().Calculate(table, db);
            ScoreLine[] lines = result.ScoreLines.ToArray();

            int sourceAddIndex = Array.FindIndex(lines, l =>
                l.DishInstanceId == source.Id
                && l.Kind == ScoreLineKind.DishFlat
                && l.Source.Name == "甜蜜");
            int transferredAddIndex = Array.FindIndex(lines, l =>
                l.DishInstanceId == target.Id
                && l.Kind == ScoreLineKind.DishFlat
                && l.Source.Name == "A<甜蜜传递>");
            int targetBaseIndex = Array.FindIndex(lines, l =>
                l.DishInstanceId == target.Id
                && l.Kind == ScoreLineKind.DishBase);

            Assert.That(sourceAddIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(transferredAddIndex, Is.GreaterThan(sourceAddIndex));
            Assert.That(targetBaseIndex, Is.GreaterThan(transferredAddIndex));
            Assert.AreEqual(30f, result.RawSum);
            Assert.AreEqual(30, result.Total);
            Assert.AreEqual(1, result.SkillTransfers.Count);
            Assert.AreEqual(target.Id, result.SkillTransfers[0].TargetInstanceId);
        }

        [Test]
        public void OnSettleTransferSkills_OrdersRowTargetsByBoardPosition()
        {
            DishShape oneCell = DishShape.FromRows(new[] { "X" });
            DishShape tallShape = DishShape.FromRows(new[] { "X", "X", "X" });
            SkillRuleDef addFlat = Rule("sweet_add", 0, SkillActionType.AddFlat, SkillScope.Self, 5f);
            SkillRuleDef transfer = Rule("sweet_transfer", 1, SkillActionType.TransferSkills, SkillScope.Row, 0f);
            var sweetSkill = new SkillDef(
                "skill_sweet",
                "甜蜜",
                string.Empty,
                Array.Empty<string>(),
                new[] { addFlat, transfer },
                new[] { "美味 +5", "甜蜜传递" });

            DishDef sourceDef = Dish("dish_a", "A", tallShape, "skill_sweet");
            DishDef topTargetDef = Dish("dish_top", "Top", oneCell);
            DishDef bottomTargetDef = Dish("dish_bottom", "Bottom", oneCell);
            var db = new GameplayDatabase(
                new[] { sourceDef, topTargetDef, bottomTargetDef },
                new[] { sweetSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var table = new DiningTable(3, 3);
            var source = new DishInstance(1, sourceDef, MakePlacement(tallShape, 1, 0), sourceDef.SkillIds, Array.Empty<string>());
            var bottomTarget = new DishInstance(2, bottomTargetDef, MakePlacement(oneCell, 0, 2), bottomTargetDef.SkillIds, Array.Empty<string>());
            var topTarget = new DishInstance(3, topTargetDef, MakePlacement(oneCell, 0, 0), topTargetDef.SkillIds, Array.Empty<string>());
            table.Place(source);
            table.Place(bottomTarget);
            table.Place(topTarget);

            ScoreResult result = new ScoreCalculator().Calculate(table, db);
            ScoreLine[] lines = result.ScoreLines.ToArray();

            int topIndex = Array.FindIndex(lines, l =>
                l.DishInstanceId == topTarget.Id
                && l.Kind == ScoreLineKind.DishFlat
                && l.Source.Name == "A<甜蜜传递>");
            int bottomIndex = Array.FindIndex(lines, l =>
                l.DishInstanceId == bottomTarget.Id
                && l.Kind == ScoreLineKind.DishFlat
                && l.Source.Name == "A<甜蜜传递>");

            Assert.That(topIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(bottomIndex, Is.GreaterThan(topIndex));
        }

        [Test]
        public void OnSettleTransferSkills_DoesNotCarryCopySkill()
        {
            DishShape oneCell = DishShape.FromRows(new[] { "X" });
            DishShape twoCell = DishShape.FromRows(new[] { "XX" });
            SkillRuleDef copy = RuleWithCondition(
                "sweet_copy",
                0,
                SkillActionType.CopySkill,
                SkillScope.All,
                1f,
                SkillConditionType.OccupiedCell,
                CountMode.Reach,
                "gte:2");
            SkillRuleDef addFlat = Rule("sweet_add", 1, SkillActionType.AddFlat, SkillScope.Self, 5f);
            SkillRuleDef transfer = Rule("sweet_transfer", 2, SkillActionType.TransferSkills, SkillScope.Other, 0f);
            var sweetSkill = new SkillDef(
                "skill_sweet",
                "甜蜜",
                string.Empty,
                Array.Empty<string>(),
                new[] { copy, addFlat, transfer },
                new[] { "复制技能", "美味 +5", "甜蜜传递" });
            SkillRuleDef candidateFlat = Rule("candidate_add", 0, SkillActionType.AddFlat, SkillScope.Self, 3f, "skill_candidate");
            var candidateSkill = new SkillDef(
                "skill_candidate",
                "候选",
                string.Empty,
                Array.Empty<string>(),
                new[] { candidateFlat },
                new[] { "美味 +3" });

            DishDef sourceDef = Dish("dish_a", "A", oneCell, "skill_sweet");
            DishDef targetDef = Dish("dish_b", "B", twoCell);
            DishDef candidateDef = Dish("dish_c", "C", oneCell, "skill_candidate");
            var db = new GameplayDatabase(
                new[] { sourceDef, targetDef, candidateDef },
                new[] { sweetSkill, candidateSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var table = new DiningTable(4, 1);
            var source = new DishInstance(1, sourceDef, MakePlacement(oneCell, 0, 0), sourceDef.SkillIds, Array.Empty<string>());
            var target = new DishInstance(2, targetDef, MakePlacement(twoCell, 1, 0), targetDef.SkillIds, Array.Empty<string>());
            var candidate = new DishInstance(3, candidateDef, MakePlacement(oneCell, 3, 0), candidateDef.SkillIds, Array.Empty<string>());
            table.Place(source);
            table.Place(target);
            table.Place(candidate);

            ScoreResult result = new ScoreCalculator().Calculate(table, db);

            Assert.That(result.CopySkillRequests.Count, Is.EqualTo(0));
            Assert.That(result.ScoreLines.Any(l =>
                l.DishInstanceId == target.Id
                && l.Kind == ScoreLineKind.DishFlat
                && l.Source.Name == "A<甜蜜传递>"
                && Math.Abs(l.Value - 5f) < 0.001f), Is.True);
            Assert.That(result.ScoreLines.Any(l => l.Kind == ScoreLineKind.CopySkill), Is.False);
        }

        [Test]
        public void ExtraSettlement_ReplaysTargetDishSkillsOnce()
        {
            DishShape oneCell = DishShape.FromRows(new[] { "X" });
            SkillRuleDef extra = Rule("extra_trigger", 0, SkillActionType.ExtraSettlement, SkillScope.Other, 1f, "skill_extra");
            var extraSkill = new SkillDef(
                "skill_extra",
                "额外触发",
                string.Empty,
                Array.Empty<string>(),
                new[] { extra },
                new[] { "目标技能额外触发 +1" });
            SkillRuleDef addFlat = Rule("target_add", 0, SkillActionType.AddFlat, SkillScope.Self, 5f, "skill_target");
            var targetSkill = new SkillDef(
                "skill_target",
                "目标加分",
                string.Empty,
                Array.Empty<string>(),
                new[] { addFlat },
                new[] { "美味 +5" });

            DishDef sourceDef = Dish("dish_a", "A", oneCell, "skill_extra");
            DishDef targetDef = Dish("dish_b", "B", oneCell, "skill_target");
            var db = new GameplayDatabase(
                new[] { sourceDef, targetDef },
                new[] { extraSkill, targetSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var table = new DiningTable(2, 1);
            var source = new DishInstance(1, sourceDef, MakePlacement(oneCell, 0, 0), sourceDef.SkillIds, Array.Empty<string>());
            var target = new DishInstance(2, targetDef, MakePlacement(oneCell, 1, 0), targetDef.SkillIds, Array.Empty<string>());
            table.Place(source);
            table.Place(target);

            ScoreResult result = new ScoreCalculator().Calculate(table, db);

            Assert.That(result.ScoreLines.Count(l =>
                l.DishInstanceId == target.Id
                && l.Kind == ScoreLineKind.DishFlat
                && l.Source.Name == "目标加分"
                && Math.Abs(l.Value - 5f) < 0.001f), Is.EqualTo(2));
            Assert.That(result.RawSum, Is.EqualTo(30f));
        }

        [Test]
        public void ExtraSettlement_ReplaysSkillWithoutRecursingExtraSettlement()
        {
            DishShape oneCell = DishShape.FromRows(new[] { "X" });
            SkillRuleDef extraSelf = Rule("extra_self", 0, SkillActionType.ExtraSettlement, SkillScope.Self, 1f, "skill_sweet");
            SkillRuleDef transfer = Rule("sweet_transfer", 1, SkillActionType.TransferSkills, SkillScope.Other, 0f);
            var sweetSkill = new SkillDef(
                "skill_sweet",
                "甜蜜",
                string.Empty,
                Array.Empty<string>(),
                new[] { extraSelf, transfer },
                new[] { "技能额外触发 +1", "甜蜜传递" });

            SkillRuleDef addFlat = Rule("sweet_add", 0, SkillActionType.AddFlat, SkillScope.Self, 5f, "skill_bonus");
            var bonusSkill = new SkillDef(
                "skill_bonus",
                "甜蜜加分",
                string.Empty,
                Array.Empty<string>(),
                new[] { addFlat },
                new[] { "美味 +5" });

            DishDef sourceDef = Dish("dish_a", "A", oneCell, "skill_sweet");
            DishDef targetDef = Dish("dish_b", "B", oneCell, "skill_bonus");
            var db = new GameplayDatabase(
                new[] { sourceDef, targetDef },
                new[] { sweetSkill, bonusSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var table = new DiningTable(2, 1);
            var source = new DishInstance(1, sourceDef, MakePlacement(oneCell, 0, 0), sourceDef.SkillIds, Array.Empty<string>());
            var target = new DishInstance(2, targetDef, MakePlacement(oneCell, 1, 0), targetDef.SkillIds, Array.Empty<string>());
            table.Place(source);
            table.Place(target);

            ScoreResult result = new ScoreCalculator().Calculate(table, db);

            Assert.That(result.ScoreLines.Count(l =>
                l.DishInstanceId == source.Id
                && l.Kind == ScoreLineKind.ExtraSettlement
                && l.Source.Name == "甜蜜"), Is.EqualTo(1));
            Assert.That(result.ScoreLines.Count(l =>
                l.DishInstanceId == target.Id
                && l.Kind == ScoreLineKind.ExtraSettlement
                && l.Source.Name == "A<甜蜜传递>"
                && Math.Abs(l.Value - 1f) < 0.001f), Is.EqualTo(2));
            Assert.That(result.ScoreLines.Count(l =>
                l.DishInstanceId == target.Id
                && l.Kind == ScoreLineKind.DishFlat
                && l.Source.Name == "甜蜜加分"
                && Math.Abs(l.Value - 5f) < 0.001f), Is.EqualTo(3));
            Assert.That(result.RawSum, Is.EqualTo(35f));
        }

        [Test]
        public void ExtraSettlement_RerollsTransferTargetsPerTrigger()
        {
            DishShape oneCell = DishShape.FromRows(new[] { "X" });
            SkillRuleDef extraSelf = Rule("extra_self", 0, SkillActionType.ExtraSettlement, SkillScope.Self, 1f, "skill_sweet");
            SkillRuleDef transfer = RuleWithActionCount("sweet_transfer", 1, SkillActionType.TransferSkills, SkillScope.Other, 0f, 1, "skill_sweet");
            var sweetSkill = new SkillDef(
                "skill_sweet",
                "甜蜜",
                string.Empty,
                Array.Empty<string>(),
                new[] { extraSelf, transfer },
                new[] { "技能额外触发 +1", "甜蜜传递" });

            DishDef sourceDef = Dish("dish_a", "A", oneCell, "skill_sweet");
            DishDef targetOneDef = Dish("dish_b", "B", oneCell);
            DishDef targetTwoDef = Dish("dish_c", "C", oneCell);
            var db = new GameplayDatabase(
                new[] { sourceDef, targetOneDef, targetTwoDef },
                new[] { sweetSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var table = new DiningTable(3, 1);
            var source = new DishInstance(1, sourceDef, MakePlacement(oneCell, 0, 0), sourceDef.SkillIds, Array.Empty<string>());
            var targetOne = new DishInstance(2, targetOneDef, MakePlacement(oneCell, 1, 0), targetOneDef.SkillIds, Array.Empty<string>());
            var targetTwo = new DishInstance(3, targetTwoDef, MakePlacement(oneCell, 2, 0), targetTwoDef.SkillIds, Array.Empty<string>());
            table.Place(source);
            table.Place(targetOne);
            table.Place(targetTwo);

            int call = 0;
            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                db,
                transferTargetSelector: (candidates, count) =>
                {
                    call++;
                    return new[] { call == 1 ? targetOne.Id : targetTwo.Id };
                });

            Assert.That(call, Is.EqualTo(2));
            Assert.That(result.SkillTransfers.Select(t => t.TargetInstanceId).ToArray(), Is.EqualTo(new[] { targetOne.Id, targetTwo.Id }));
        }

        [Test]
        public void TransferredSkillTipsKeepDuplicateEntriesInRevealOrder()
        {
            DishShape oneCell = DishShape.FromRows(new[] { "X" });
            SkillRuleDef nougatExtra = Rule("nougat_extra", 0, SkillActionType.ExtraSettlement, SkillScope.Self, 1f, "skill_nougat");
            SkillRuleDef chocoMult = Rule("choco_mult", 0, SkillActionType.AddMultFlat, SkillScope.Self, 3f, "skill_choco");
            var nougatSkill = new SkillDef(
                "skill_nougat",
                "牛轧糖",
                string.Empty,
                Array.Empty<string>(),
                new[] { nougatExtra },
                new[] { "技能额外触发 +1" });
            var chocoSkill = new SkillDef(
                "skill_choco",
                "巧克力棒",
                string.Empty,
                Array.Empty<string>(),
                new[] { chocoMult },
                new[] { "倍率 +3" });
            DishDef cookieDef = Dish("dish_cookie", "曲奇", oneCell);
            var db = new GameplayDatabase(
                new[] { cookieDef },
                new[] { nougatSkill, chocoSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var table = new DiningTable(1, 1);
            var cookie = new DishInstance(1, cookieDef, MakePlacement(oneCell, 0, 0), cookieDef.SkillIds, Array.Empty<string>());
            table.Place(cookie);

            cookie.AddTransferredSkill(new SkillEffect(nougatExtra, "技能额外触发 +1"), "牛轧糖<甜蜜传递>", 4);
            cookie.AddTransferredSkill(new SkillEffect(nougatExtra, "技能额外触发 +1"), "牛轧糖<甜蜜传递>", 4);
            cookie.AddTransferredSkill(new SkillEffect(chocoMult, "倍率 +3"), "巧克力棒<甜蜜传递>", 3);

            FoodTipsData revealed = FoodTipsDataFactory.BuildRevealed(
                cookie,
                table,
                db,
                new FoodTipsReveal(cookie.BaseScoreBeforeSettlement, cookie.BaseMultiplierBeforeSettlement, -1, 2));
            FoodTipsData full = FoodTipsDataFactory.Build(cookie, table, db);

            Assert.That(cookie.TransferredSkills.Count, Is.EqualTo(3));
            Assert.That(revealed.TransferredSubSkills.Select(e => e.Title).ToArray(), Is.EqualTo(new[] { "牛轧糖<甜蜜传递>", "牛轧糖<甜蜜传递>" }));
            Assert.That(full.TransferredSubSkills.Select(e => e.Title).ToArray(), Is.EqualTo(new[] { "牛轧糖<甜蜜传递>", "牛轧糖<甜蜜传递>", "巧克力棒<甜蜜传递>" }));
        }

        [Test]
        public void TriggerSweetTransfer_MarksTarget_ExtraTransferRerollsWhenTargetSettles()
        {
            DishShape oneCell = DishShape.FromRows(new[] { "X" });

            // 来源 S：自身 +5，甜蜜传递给同行 1 个食物。
            SkillRuleDef sourceAdd = Rule("s_add", 0, SkillActionType.AddFlat, SkillScope.Self, 5f, "skill_source");
            SkillRuleDef sourceTransfer = RuleWithActionCount(
                "s_transfer", 1, SkillActionType.TransferSkills, SkillScope.Row, 0f, 1, "skill_source");
            var sourceSkill = new SkillDef(
                "skill_source",
                "来源",
                string.Empty,
                Array.Empty<string>(),
                new[] { sourceAdd, sourceTransfer },
                new[] { "美味 +5", "甜蜜传递" });

            // 枫糖：使 1 个带甜蜜传递的食物，在其结算甜蜜传递时额外再传 1 次（重掷目标）。
            var mapleTrigger = new SkillRuleDef(
                "m_trigger",
                "skill_maple",
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                SkillActionType.TriggerSweetTransfer,
                SkillScope.All,
                1,
                new[] { 1f },
                new[] { "skilltype:TransferSkills" });
            var mapleSkill = new SkillDef(
                "skill_maple",
                "枫糖",
                string.Empty,
                Array.Empty<string>(),
                new[] { mapleTrigger },
                new[] { "额外甜蜜传递" });

            DishDef mapleDef = Dish("dish_maple", "枫糖", oneCell, "skill_maple");
            DishDef sourceDef = Dish("dish_source", "S", oneCell, "skill_source");
            DishDef targetADef = Dish("dish_a", "A", oneCell);
            DishDef targetBDef = Dish("dish_b", "B", oneCell);
            var db = new GameplayDatabase(
                new[] { mapleDef, sourceDef, targetADef, targetBDef },
                new[] { mapleSkill, sourceSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());

            // 枫糖先结算并挂标记，再轮到来源结算做甜蜜传递。
            var table = new DiningTable(4, 1);
            var maple = new DishInstance(1, mapleDef, MakePlacement(oneCell, 0, 0), mapleDef.SkillIds, Array.Empty<string>());
            var source = new DishInstance(2, sourceDef, MakePlacement(oneCell, 1, 0), sourceDef.SkillIds, Array.Empty<string>());
            var targetA = new DishInstance(3, targetADef, MakePlacement(oneCell, 2, 0), targetADef.SkillIds, Array.Empty<string>());
            var targetB = new DishInstance(4, targetBDef, MakePlacement(oneCell, 3, 0), targetBDef.SkillIds, Array.Empty<string>());
            table.Place(maple);
            table.Place(source);
            table.Place(targetA);
            table.Place(targetB);

            int transferRoll = 0;
            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                db,
                transferTargetSelector: (candidates, count) =>
                {
                    // 仅来源有甜蜜传递时，枫糖挂载不必走 selector；两次传递分别重掷到 A / B。
                    transferRoll++;
                    int pick = transferRoll == 1 ? targetA.Id : targetB.Id;
                    Assert.That(candidates, Does.Contain(pick));
                    return new[] { pick };
                });

            Assert.That(transferRoll, Is.EqualTo(2));
            Assert.That(result.SkillTransfers.Select(t => t.TargetInstanceId).ToArray(), Is.EqualTo(new[] { targetA.Id, targetB.Id }));
            Assert.That(result.ScoreLines.Count(l =>
                l.DishInstanceId == targetA.Id
                && l.Kind == ScoreLineKind.DishFlat
                && l.Source.Name == "S<甜蜜传递>"), Is.EqualTo(1));
            Assert.That(result.ScoreLines.Count(l =>
                l.DishInstanceId == targetB.Id
                && l.Kind == ScoreLineKind.DishFlat
                && l.Source.Name == "S<甜蜜传递>"), Is.EqualTo(1));
        }

        private static SkillRuleDef Rule(
            string id,
            int order,
            SkillActionType actionType,
            SkillScope actionScope,
            float actionValue,
            string skillId = "skill_sweet")
        {
            return new SkillRuleDef(
                id,
                skillId,
                order,
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

        private static SkillRuleDef RuleWithActionCount(
            string id,
            int order,
            SkillActionType actionType,
            SkillScope actionScope,
            float actionValue,
            int actionCount,
            string skillId = "skill_sweet")
        {
            return new SkillRuleDef(
                id,
                skillId,
                order,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                actionType,
                actionScope,
                actionCount,
                new[] { actionValue },
                Array.Empty<string>());
        }

        private static SkillRuleDef RuleWithCondition(
            string id,
            int order,
            SkillActionType actionType,
            SkillScope actionScope,
            float actionValue,
            SkillConditionType condType,
            CountMode condMode,
            string condParam)
        {
            return new SkillRuleDef(
                id,
                "skill_sweet",
                order,
                SkillTrigger.OnSettle,
                condType,
                SkillScope.Self,
                CountUnit.Instances,
                condMode,
                condParam,
                actionType,
                actionScope,
                0,
                new[] { actionValue },
                Array.Empty<string>());
        }

        private static DishDef Dish(string id, string name, DishShape shape, string skillId = null)
        {
            return new DishDef(
                id,
                name,
                10,
                shape,
                0,
                0,
                1f,
                string.IsNullOrEmpty(skillId) ? Array.Empty<string>() : new[] { skillId },
                string.Empty,
                false);
        }

        private static Placement MakePlacement(DishShape shape, int x, int y)
        {
            return new Placement(shape, 0, new GridPos(x, y));
        }
    }
}
