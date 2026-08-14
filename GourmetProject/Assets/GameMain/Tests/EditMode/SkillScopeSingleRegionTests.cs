using System;
using System.Collections.Generic;
using System.IO;
using GourmetProject.Game.Adapter;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SkillScopeSingleRegionTests
    {
        private DiningTable _board;
        private DishInstance _self;

        [SetUp]
        public void SetUp()
        {
            _board = new DiningTable(5, 5);
            _self = CreateDish(1, new GridPos(2, 2));
            _board.Place(_self);
        }

        [TestCase(SkillConditionType.Edge, SkillScope.Self, "", false)]
        [TestCase(SkillConditionType.ServeOrder, SkillScope.Self, "first:settlement", false)]
        [TestCase(SkillConditionType.ServeOrder, SkillScope.Before, "gte:2", false)]
        [TestCase(SkillConditionType.TagCount, SkillScope.Self, "source:passive-items", false)]
        [TestCase(SkillConditionType.CategoryCount, SkillScope.All, "cake;tiers:3|5|8", false)]
        [TestCase(SkillConditionType.OccupiedCell, SkillScope.All, "source:board", false)]
        [TestCase(SkillConditionType.LayerCount, SkillScope.Self, "gte:3", false)]
        [TestCase(SkillConditionType.RecipeCount, SkillScope.Recipe, "gte:2", false)]
        [TestCase(SkillConditionType.SameKindInRun, SkillScope.All, "", false)]
        [TestCase(SkillConditionType.SameKindInMeal, SkillScope.All, "", false)]
        [TestCase((SkillConditionType)999, SkillScope.Self, "future:self-property", false)]
        [TestCase(SkillConditionType.PositionFilled, SkillScope.RoundAndSelf, "", true)]
        [TestCase(SkillConditionType.PositionFilled, SkillScope.All, "", false)]
        [TestCase(SkillConditionType.EmptyCell, SkillScope.All, "", false)]
        [TestCase(SkillConditionType.DishSize, SkillScope.All, "eq:2", false)]
        [TestCase(SkillConditionType.DishCount, SkillScope.RowAndSelf, "gte:2", true)]
        [TestCase(SkillConditionType.SkillCount, SkillScope.RoundAndSelf, "", true)]
        [TestCase(SkillConditionType.TagCount, SkillScope.RowAndSelf, "source:flavors", true)]
        [TestCase(SkillConditionType.CategoryCount, SkillScope.ColumnAndSelf, "cake", true)]
        public void UsesSpatialScope_ClassifiesConditionSemantics(
            SkillConditionType conditionType,
            SkillScope conditionScope,
            string conditionParam,
            bool expected)
        {
            SkillActionType actionType = conditionType == SkillConditionType.CategoryCount
                                         && conditionScope == SkillScope.All
                                         && conditionParam.Contains("tiers:")
                ? SkillActionType.AddLayer
                : SkillActionType.AddFlat;
            SkillScope actionScope = actionType == SkillActionType.AddLayer
                ? SkillScope.CakeBuff
                : conditionScope;
            SkillRuleDef rule = Rule(conditionType, conditionScope, actionType, actionScope, conditionParam);

            Assert.That(SkillConditionEvaluator.UsesSpatialScope(rule), Is.EqualTo(expected));
        }

        [Test]
        public void Resolve_ConditionOnly_UsesConditionRegion()
        {
            SkillRuleDef rule = Rule(
                SkillConditionType.SkillCount,
                SkillScope.RoundAndSelf,
                SkillActionType.AddLayer,
                SkillScope.CakeBuff);

            SkillScopeVisual visual = Resolve(rule);

            Assert.That(visual.ScopeRegionKind, Is.EqualTo(SkillScopeRegionKind.Condition));
            AssertCells(
                visual.ScopeRegionCells,
                new GridPos(2, 2),
                new GridPos(1, 2),
                new GridPos(3, 2),
                new GridPos(2, 1),
                new GridPos(2, 3));
        }

        [Test]
        public void Resolve_ActionOnly_UsesActionRegion()
        {
            SkillRuleDef rule = Rule(
                SkillConditionType.None,
                SkillScope.Self,
                SkillActionType.AddFlat,
                SkillScope.RightAndSelf);

            SkillScopeVisual visual = Resolve(rule);

            Assert.That(visual.ScopeRegionKind, Is.EqualTo(SkillScopeRegionKind.Action));
            AssertCells(visual.ScopeRegionCells, new GridPos(2, 2), new GridPos(3, 2), new GridPos(4, 2));
        }

        [Test]
        public void Resolve_TriggerSweetTransferRowColumn_UsesCrossActionRegion()
        {
            SkillRuleDef rule = Rule(
                SkillConditionType.None,
                SkillScope.All,
                SkillActionType.TriggerSweetTransfer,
                SkillScope.RowAndColumn,
                actionParams: new[] { "skilltype:TransferSkills" });

            SkillScopeVisual visual = Resolve(rule);

            Assert.That(visual.ScopeRegionKind, Is.EqualTo(SkillScopeRegionKind.Action));
            AssertCells(
                visual.ScopeRegionCells,
                new GridPos(0, 2),
                new GridPos(1, 2),
                new GridPos(2, 2),
                new GridPos(3, 2),
                new GridPos(4, 2),
                new GridPos(2, 0),
                new GridPos(2, 1),
                new GridPos(2, 3),
                new GridPos(2, 4));
        }

        [Test]
        public void Resolve_MatchingConditionAndAction_MergesUnifiedRegion()
        {
            SkillRuleDef rule = Rule(
                SkillConditionType.PositionFilled,
                SkillScope.RoundAndSelf,
                SkillActionType.AddMultFlat,
                SkillScope.RoundAndSelf);

            SkillScopeVisual visual = Resolve(rule);

            Assert.That(visual.ScopeRegionKind, Is.EqualTo(SkillScopeRegionKind.Unified));
            Assert.That(visual.ScopeRegionCells, Is.EquivalentTo(visual.ActionScopeCells));
            Assert.That(visual.ScopeRegionCells, Is.EquivalentTo(visual.ConditionCells));
        }

        [Test]
        public void Resolve_GlobalActionHidden_FallsBackToSpatialCondition()
        {
            SkillRuleDef rule = Rule(
                SkillConditionType.EmptyCell,
                SkillScope.RoundAndSelf,
                SkillActionType.AddLayer,
                SkillScope.CakeBuff);

            SkillScopeVisual visual = Resolve(rule);

            Assert.That(visual.ScopeRegionKind, Is.EqualTo(SkillScopeRegionKind.Condition));
            Assert.That(visual.ScopeRegionCells, Has.Count.EqualTo(5));
        }

        [TestCase(SkillConditionType.PositionFilled, SkillScope.All, "")]
        [TestCase(SkillConditionType.EmptyCell, SkillScope.All, "")]
        [TestCase(SkillConditionType.DishSize, SkillScope.All, "eq:2")]
        [TestCase(SkillConditionType.Edge, SkillScope.Self, "")]
        [TestCase(SkillConditionType.ServeOrder, SkillScope.Self, "first:settlement")]
        [TestCase(SkillConditionType.TagCount, SkillScope.Self, "source:passive-items")]
        [TestCase(SkillConditionType.OccupiedCell, SkillScope.All, "source:board")]
        public void Resolve_NonRenderableConditionAndGlobalAction_HidesBoardRegion(
            SkillConditionType conditionType,
            SkillScope conditionScope,
            string conditionParam)
        {
            SkillRuleDef rule = Rule(
                conditionType,
                conditionScope,
                SkillActionType.AddMultFlat,
                SkillScope.All,
                conditionParam);

            SkillScopeVisual visual = Resolve(rule);

            Assert.That(visual.ScopeRegionKind, Is.EqualTo(SkillScopeRegionKind.None));
            Assert.That(visual.ScopeRegionCells, Is.Empty);
        }

        [Test]
        public void Resolve_SpecialCondition_PrefersRenderableActionRegion()
        {
            SkillRuleDef rule = Rule(
                SkillConditionType.Edge,
                SkillScope.Self,
                SkillActionType.AddFlat,
                SkillScope.Edge);

            SkillScopeVisual visual = Resolve(rule);

            Assert.That(visual.ScopeRegionKind, Is.EqualTo(SkillScopeRegionKind.Action));
            Assert.That(visual.ScopeRegionCells, Has.Count.EqualTo(16));
        }

        [Test]
        public void Resolve_ConflictingSpatialRegions_SelectsOnlyActionAndReportsAuditError()
        {
            SkillRuleDef rule = Rule(
                SkillConditionType.DishCount,
                SkillScope.RowAndSelf,
                SkillActionType.AddFlat,
                SkillScope.ColumnAndSelf);

            SkillScopeVisual visual = Resolve(rule);

            Assert.That(visual.ScopeRegionKind, Is.EqualTo(SkillScopeRegionKind.Action));
            Assert.That(visual.ScopeRegionCells, Is.EquivalentTo(visual.ActionScopeCells));
            Assert.That(
                SkillScopeResolver.HasConflictingSpatialRegions(
                    rule,
                    visual.ActionScopeCells,
                    visual.ConditionCells),
                Is.True);
        }

        [Test]
        public void FormalSubSkillConfig_HasNoConflictingRenderableSpatialRegions()
        {
            GameplayDatabase database = LoadFormalDatabase();
            var conflicts = new List<string>();

            foreach (SkillDef skill in database.AllSkills)
            {
                foreach (SkillRuleDef configuredRule in skill.Rules)
                {
                    // 审计范围几何，不让目标筛选参数把作用域缩成当前命中的食物格。
                    SkillRuleDef rule = CopyWithoutActionFilters(configuredRule);
                    SkillScopeVisual visual = SkillScopeResolver.Resolve(
                        database,
                        _board,
                        _self,
                        rule,
                        SkillScopeVisualMode.CandidateScope);
                    if (SkillScopeResolver.HasConflictingSpatialRegions(
                            rule,
                            visual.ActionScopeCells,
                            visual.ConditionCells))
                    {
                        conflicts.Add(configuredRule.Id);
                    }
                }
            }

            Assert.That(conflicts, Is.Empty, "配置了两个不同空间范围的子技能：" + string.Join(", ", conflicts));
        }

        [Test]
        public void FormalFoodExamples_ResolveExpectedSingleRegionKinds()
        {
            GameplayDatabase database = LoadFormalDatabase();
            var board = new DiningTable(5, 5);
            DishInstance cake = CreateDish(10, new GridPos(2, 2), "cake");
            board.Place(cake);

            AssertFormalKind(database, board, cake, "sk_cupcake", 0, SkillScopeRegionKind.Condition);
            AssertFormalKind(database, board, cake, "sk_ice_cream", 0, SkillScopeRegionKind.Action);
            AssertFormalKind(database, board, cake, "sk_strawberry_sundae", 0, SkillScopeRegionKind.Action);
            AssertFormalKind(database, board, cake, "sk_black_forest_cake", 0, SkillScopeRegionKind.Action);
            AssertFormalKind(database, board, cake, "sk_black_forest_cake", 1, SkillScopeRegionKind.None);
            AssertFormalKind(database, board, cake, "sk_fruit_cake", 0, SkillScopeRegionKind.Action);
            AssertFormalKind(database, board, cake, "sk_fruit_cake", 1, SkillScopeRegionKind.None);
            AssertFormalKind(database, board, cake, "sk_caramel_pudding", 0, SkillScopeRegionKind.None);
            AssertFormalKind(database, board, cake, "sk_coconut_milk_jelly", 0, SkillScopeRegionKind.None);
            AssertFormalKind(database, board, cake, "sk_cream_puff", 0, SkillScopeRegionKind.None);
            AssertFormalKind(database, board, cake, "sk_hawthorn_cake", 0, SkillScopeRegionKind.None);
            AssertFormalKind(database, board, cake, "sk_tree_ring_cake", 0, SkillScopeRegionKind.None);
            AssertFormalKind(database, board, cake, "sk_jelly", 0, SkillScopeRegionKind.Unified);
            AssertFormalKind(database, board, cake, "sk_eggtart", 0, SkillScopeRegionKind.Unified);
            AssertFormalKind(database, board, cake, "sk_big_lollipop", 0, SkillScopeRegionKind.Action);
        }

        [Test]
        public void FormalCreamCake_CategoryFilterKeepsFullColumnGeometry()
        {
            GameplayDatabase database = LoadFormalDatabase();
            DishDef definition = database.GetDish("cream_cake");
            SkillDef skill = database.GetSkill("sk_cream_cake");
            var board = new DiningTable(5, 5);
            var creamCake = new DishInstance(
                20,
                definition,
                new Placement(definition.Shape, 0, new GridPos(1, 1)),
                definition.SkillIds,
                Array.Empty<string>());
            board.Place(creamCake);

            SkillScopeVisual visual = SkillScopeResolver.Resolve(
                database,
                board,
                creamCake,
                skill.Rules[1],
                SkillScopeVisualMode.CandidateScope);

            Assert.That(visual.ScopeRegionKind, Is.EqualTo(SkillScopeRegionKind.Action));
            Assert.That(visual.ScopeRegionCells, Has.Count.EqualTo(10));
            Assert.That(visual.ActionScopeCells, Has.Count.EqualTo(10));
            Assert.That(visual.VisualTargetDishInstanceIds, Is.EquivalentTo(new[] { creamCake.Id }));
        }

        private SkillScopeVisual Resolve(SkillRuleDef rule)
        {
            return SkillScopeResolver.Resolve(
                null,
                _board,
                _self,
                rule,
                SkillScopeVisualMode.CandidateScope);
        }

        private static SkillRuleDef Rule(
            SkillConditionType conditionType,
            SkillScope conditionScope,
            SkillActionType actionType,
            SkillScope actionScope,
            string conditionParam = "",
            IReadOnlyList<string> actionParams = null)
        {
            return new SkillRuleDef(
                "test-rule",
                "test-skill",
                0,
                SkillTrigger.OnSettle,
                conditionType,
                conditionScope,
                CountUnit.Instances,
                CountMode.Gate,
                conditionParam,
                actionType,
                actionScope,
                0,
                new[] { 1f },
                actionParams ?? Array.Empty<string>());
        }

        private static SkillRuleDef CopyWithoutActionFilters(SkillRuleDef source)
        {
            return new SkillRuleDef(
                source.Id,
                source.SkillId,
                source.Order,
                source.Trigger,
                source.CondType,
                source.CondScope,
                source.CondUnit,
                source.CondMode,
                source.CondParam,
                source.ActionType,
                source.ActionScope,
                source.ActionCount,
                source.ActionValues,
                Array.Empty<string>(),
                source.TermIds);
        }

        private static DishInstance CreateDish(int id, GridPos origin, string category = "")
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            var def = new DishDef(
                "test-dish-" + id,
                "test",
                1,
                shape,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                category: category);
            return new DishInstance(
                id,
                def,
                new Placement(shape, 0, origin),
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        private static GameplayDatabase LoadFormalDatabase()
        {
            string configDirectory = Path.Combine(Application.streamingAssetsPath, "Config");
            var tables = new cfg.Tables(tableName =>
            {
                string json = File.ReadAllText(Path.Combine(configDirectory, tableName + ".json"));
                return JSON.Parse(json);
            });
            return GameplayContentBuilder.BuildDatabase(tables);
        }

        private static void AssertFormalKind(
            GameplayDatabase database,
            DiningTable board,
            DishInstance self,
            string skillId,
            int ruleIndex,
            SkillScopeRegionKind expectedKind)
        {
            SkillDef skill = database.GetSkill(skillId);
            Assert.That(skill, Is.Not.Null, skillId);
            Assert.That(skill.Rules.Count, Is.GreaterThan(ruleIndex), skillId);
            SkillScopeVisual visual = SkillScopeResolver.Resolve(
                database,
                board,
                self,
                skill.Rules[ruleIndex],
                SkillScopeVisualMode.CandidateScope);

            Assert.That(visual.ScopeRegionKind, Is.EqualTo(expectedKind), skill.Rules[ruleIndex].Id);
            Assert.That(
                visual.ScopeRegionCells.Count,
                expectedKind == SkillScopeRegionKind.None ? Is.EqualTo(0) : Is.GreaterThan(0),
                skill.Rules[ruleIndex].Id);
        }

        private static void AssertCells(IReadOnlyList<GridPos> actual, params GridPos[] expected)
        {
            Assert.That(actual, Is.EquivalentTo(expected));
        }
    }
}
