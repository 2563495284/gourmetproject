using System;
using System.Collections.Generic;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class DiningTableSettlementOrderHintTests
    {
        [Test]
        public void BuildSettlementOrderHintCells_FiltersVoidAndDisabledCells_InRowMajorOrder()
        {
            var table = new DiningTable(
                4,
                3,
                new[]
                {
                    new GridPos(0, 0),
                    new GridPos(2, 0),
                    new GridPos(1, 1),
                    new GridPos(3, 1),
                    new GridPos(0, 2),
                });
            table.SetDisabled(new GridPos(1, 1), true);

            List<GridPos> actual = DiningTableView.BuildSettlementOrderHintCells(
                table,
                reverseOrder: false);

            CollectionAssert.AreEqual(
                new[]
                {
                    new GridPos(0, 0),
                    new GridPos(2, 0),
                    new GridPos(3, 1),
                    new GridPos(0, 2),
                },
                actual);
        }

        [Test]
        public void BuildSettlementOrderHintCells_ReverseOrder_IsExactInverseOfForwardOrder()
        {
            var table = new DiningTable(
                3,
                3,
                new[]
                {
                    new GridPos(0, 0),
                    new GridPos(2, 0),
                    new GridPos(1, 1),
                    new GridPos(0, 2),
                });
            table.SetDisabled(new GridPos(1, 1), true);

            List<GridPos> actual = DiningTableView.BuildSettlementOrderHintCells(
                table,
                reverseOrder: true);

            CollectionAssert.AreEqual(
                new[]
                {
                    new GridPos(0, 2),
                    new GridPos(2, 0),
                    new GridPos(0, 0),
                },
                actual);
        }
    }

    public sealed class DiningTableEdgeTests
    {
        [Test]
        public void EdgeCondition_TreatsDisabledNeighborAsTableBoundary()
        {
            var table = new DiningTable(3, 3);
            DishInstance dish = CreateSingleCellDish(1, new GridPos(1, 1));
            table.Place(dish);
            SkillRuleDef rule = CreateEdgeRule(SkillScope.Self);

            Assert.That(
                SkillConditionEvaluator.Evaluate(rule, table, history: null, self: dish),
                Is.Zero,
                "没有相邻禁用格时，中央食物不应属于边缘。");

            table.SetDisabled(new GridPos(2, 1), true);

            Assert.That(
                SkillConditionEvaluator.Evaluate(rule, table, history: null, self: dish),
                Is.EqualTo(1),
                "儿童餐禁用格应形成餐桌边界。被禁用格相邻的食物应属于边缘。");
        }

        [Test]
        public void EdgeScope_IncludesUsableCellsAdjacentToDisabledCell()
        {
            var table = new DiningTable(5, 5);
            DishInstance dish = CreateSingleCellDish(1, new GridPos(2, 1));
            table.Place(dish);
            table.SetDisabled(new GridPos(2, 2), true);
            SkillRuleDef rule = CreateEdgeRule(SkillScope.Edge);

            SkillScopeVisual visual = SkillScopeResolver.Resolve(
                db: null,
                board: table,
                self: dish,
                rule: rule,
                mode: SkillScopeVisualMode.CandidateScope);

            CollectionAssert.IsSubsetOf(
                new[]
                {
                    new GridPos(2, 1),
                    new GridPos(1, 2),
                    new GridPos(3, 2),
                    new GridPos(2, 3),
                },
                visual.ActionScopeCells,
                "禁用格四周的可用格都应纳入边缘作用域。");
            CollectionAssert.DoesNotContain(
                visual.ActionScopeCells,
                new GridPos(2, 2),
                "禁用格形成边界，但自身不是可用的边缘格。");
        }

        private static SkillRuleDef CreateEdgeRule(SkillScope actionScope)
        {
            return new SkillRuleDef(
                id: "edge_rule",
                skillId: "edge_skill",
                order: 0,
                trigger: SkillTrigger.OnSettle,
                condType: SkillConditionType.Edge,
                condScope: SkillScope.Self,
                condUnit: CountUnit.Instances,
                condMode: CountMode.Gate,
                condParam: string.Empty,
                actionType: SkillActionType.AddFlat,
                actionScope: actionScope,
                actionCount: 0,
                actionValues: new[] { 1f },
                actionParams: Array.Empty<string>());
        }

        private static DishInstance CreateSingleCellDish(int id, GridPos origin)
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            var def = new DishDef(
                id: $"dish_{id}",
                name: $"dish_{id}",
                deliciousness: 1,
                shape: shape,
                hiddenMin: 0,
                hiddenMax: 0,
                baseWeight: 1f,
                skillIds: Array.Empty<string>(),
                flavorId: string.Empty);
            return new DishInstance(
                id,
                def,
                new Placement(shape, rotationIndex: 0, origin: origin),
                Array.Empty<string>(),
                Array.Empty<string>());
        }
    }

    public sealed class DiningTableLayoutTests
    {
        [Test]
        public void ComputeInRect_IncludesPersistentlyVisibleRemovedCells()
        {
            var table = new DiningTable(
                4,
                4,
                new[]
                {
                    new GridPos(0, 0), new GridPos(1, 0), new GridPos(2, 0), new GridPos(3, 0),
                    new GridPos(0, 1), new GridPos(1, 1), new GridPos(2, 1), new GridPos(3, 1),
                    new GridPos(0, 2), new GridPos(1, 2), new GridPos(2, 2), new GridPos(3, 2),
                });
            var removedBottomRow = new[]
            {
                new GridPos(0, 3),
                new GridPos(1, 3),
                new GridPos(2, 3),
                new GridPos(3, 3),
            };

            BoardPlacement placement = DiningTableLayout.ComputeInRect(
                0f,
                4f,
                0f,
                3f,
                table,
                0f,
                removedBottomRow);

            Assert.That(placement.CellSize, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(placement.Position.x, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(placement.Position.y, Is.EqualTo(1.5f).Within(0.0001f));
        }

        [Test]
        public void FoodSettlementLayout_IncludesPersistentlyVisibleRemovedCells()
        {
            var table = new DiningTable(
                4,
                4,
                new[]
                {
                    new GridPos(0, 0), new GridPos(1, 0), new GridPos(2, 0), new GridPos(3, 0),
                    new GridPos(0, 1), new GridPos(1, 1), new GridPos(2, 1), new GridPos(3, 1),
                    new GridPos(0, 2), new GridPos(1, 2), new GridPos(2, 2), new GridPos(3, 2),
                });
            var removedBottomRow = new[]
            {
                new GridPos(0, 3),
                new GridPos(1, 3),
                new GridPos(2, 3),
                new GridPos(3, 3),
            };

            FoodSettlementBoardTween tween = FoodSettlementLayout.ComputeBoardTween(
                0f,
                4f,
                0f,
                3f,
                0f,
                4f,
                0f,
                3f,
                table,
                1f,
                removedBottomRow);

            Assert.That(tween.CellSize, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(tween.Scale, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(tween.Position.x, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(tween.Position.y, Is.EqualTo(1.5f).Within(0.0001f));
        }
    }
}
