using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Gameplay.Scoring
{
    public enum SkillScopeVisualMode
    {
        CandidateScope = 0,
        ResolvedTargets = 1,
    }

    public sealed class SkillScopeVisual
    {
        public static readonly SkillScopeVisual Empty = new SkillScopeVisual(null, null, null);

        public SkillScopeVisual(
            IReadOnlyList<int> visualTargetDishInstanceIds,
            IReadOnlyList<GridPos> visualTargetCells,
            IReadOnlyList<GridPos> conditionCells)
        {
            VisualTargetDishInstanceIds = visualTargetDishInstanceIds ?? System.Array.Empty<int>();
            VisualTargetCells = visualTargetCells ?? System.Array.Empty<GridPos>();
            ConditionCells = conditionCells ?? System.Array.Empty<GridPos>();
        }

        public IReadOnlyList<int> VisualTargetDishInstanceIds { get; }

        public IReadOnlyList<GridPos> VisualTargetCells { get; }

        public IReadOnlyList<GridPos> ConditionCells { get; }
    }

    /// <summary>技能 scope 的单一解析入口，供结算目标选择和表现层高亮共用。</summary>
    public static class SkillScopeResolver
    {
        public static SkillScopeVisual Resolve(
            GameplayDatabase db,
            GpTable board,
            DishInstance self,
            SkillRuleDef rule,
            SkillScopeVisualMode mode)
        {
            if (board == null || self == null || rule == null)
            {
                return SkillScopeVisual.Empty;
            }

            IReadOnlyList<DishInstance> targetDishes = ResolveVisualActionDishes(db, board, self, rule, mode);
            List<int> targetIds = targetDishes.Select(d => d.Id).Distinct().ToList();
            List<GridPos> targetCells = mode == SkillScopeVisualMode.CandidateScope
                ? VisualCellsForScope(db, board, self, rule, rule.ActionScope, isActionScope: true)
                : CellsForDishes(targetDishes);
            if (targetCells.Count == 0)
            {
                targetCells = VisualCellsForScope(db, board, self, rule, rule.ActionScope, isActionScope: true);
            }

            List<GridPos> conditionCells = VisualCellsForScope(db, board, self, rule, rule.CondScope, isActionScope: false);
            return new SkillScopeVisual(targetIds, targetCells, conditionCells);
        }

        public static IReadOnlyList<DishInstance> ResolveActionTargetDishes(
            GameplayDatabase db,
            GpTable board,
            DishInstance self,
            SkillRuleDef rule,
            SkillScopeVisualMode mode)
        {
            return ResolveVisualActionDishes(db, board, self, rule, mode);
        }

        private static IReadOnlyList<DishInstance> ResolveVisualActionDishes(
            GameplayDatabase db,
            GpTable board,
            DishInstance self,
            SkillRuleDef rule,
            SkillScopeVisualMode mode)
        {
            if (rule.ActionType == SkillActionType.CopySkill)
            {
                return ResolveCopyCandidateDishes(db, board, self, rule, mode);
            }

            if (rule.ActionType == SkillActionType.TriggerSweetTransfer
                || rule.ActionType == SkillActionType.ExtraSweetTransfer)
            {
                return ResolveSweetTransferSources(db, board, self, rule);
            }

            if (rule.ActionType == SkillActionType.AddLayer
                || rule.ActionType == SkillActionType.ConsumeLayer
                || rule.ActionType == SkillActionType.GrantGold)
            {
                return System.Array.Empty<DishInstance>();
            }

            List<DishInstance> dishes = ResolveScopeDishes(db, board, self, rule, includeSelfForSelfScope: true);
            if (rule.ActionType == SkillActionType.TransferSkills)
            {
                dishes.RemoveAll(d => d.Id == self.Id);
            }

            return ApplyActionCount(dishes, rule, mode);
        }

        private static List<DishInstance> ResolveCopyCandidateDishes(
            GameplayDatabase db,
            GpTable board,
            DishInstance self,
            SkillRuleDef rule,
            SkillScopeVisualMode mode)
        {
            string category = SkillConditionEvaluator.ParseCategoryParam(rule.ActionParams);
            List<DishInstance> dishes = !string.IsNullOrEmpty(category)
                ? SkillConditionEvaluator.CategoryDishes(board, category)
                : ResolveScopeDishes(db, board, self, rule, includeSelfForSelfScope: false);

            dishes.RemoveAll(d => d.Id == self.Id || !HasCopyableSkill(db, self, d));
            return ApplyActionCount(dishes, rule, mode);
        }

        private static List<DishInstance> ResolveScopeDishes(
            GameplayDatabase db,
            GpTable board,
            DishInstance self,
            SkillRuleDef rule,
            bool includeSelfForSelfScope)
        {
            if (rule.ActionScope == SkillScope.Self)
            {
                return includeSelfForSelfScope ? new List<DishInstance> { self } : new List<DishInstance>();
            }

            if (rule.ActionScope == SkillScope.Category)
            {
                return SkillConditionEvaluator.CategoryDishes(board, SkillConditionEvaluator.ParseCategoryParam(rule.ActionParams));
            }

            List<DishInstance> dishes = SkillConditionEvaluator.ScopeDishes(board, self, rule.ActionScope);
            string skillTypeToken = ParseSkillTypeParam(rule.ActionParams);
            if (!string.IsNullOrEmpty(skillTypeToken)
                && System.Enum.TryParse(skillTypeToken, ignoreCase: true, out SkillActionType filterType))
            {
                dishes = dishes.Where(d => HasSkillOfType(db, d, filterType)).ToList();
            }

            return dishes;
        }

        private static List<DishInstance> ApplyActionCount(
            List<DishInstance> dishes,
            SkillRuleDef rule,
            SkillScopeVisualMode mode)
        {
            dishes = dishes
                .OrderBy(BoardTop)
                .ThenBy(BoardLeft)
                .ThenBy(d => d.Id)
                .ToList();

            if (mode == SkillScopeVisualMode.ResolvedTargets
                && rule.ActionCount > 0
                && dishes.Count > rule.ActionCount)
            {
                dishes = dishes.Take(rule.ActionCount).ToList();
            }

            return dishes;
        }

        private static List<DishInstance> ResolveSweetTransferSources(
            GameplayDatabase db,
            GpTable board,
            DishInstance self,
            SkillRuleDef rule)
        {
            var result = new List<DishInstance>();
            var seen = new HashSet<int>();
            void Add(DishInstance dish)
            {
                if (dish == null || dish.SkillsDisabled || dish.Id == self.Id || !seen.Add(dish.Id))
                {
                    return;
                }

                if (HasSkillOfType(db, dish, SkillActionType.TransferSkills))
                {
                    result.Add(dish);
                }
            }

            if (HasActionParam(rule, "axis:rowcol"))
            {
                foreach (DishInstance dish in SkillConditionEvaluator.ScopeDishes(board, self, SkillScope.Row, includeSelf: false))
                {
                    Add(dish);
                }

                foreach (DishInstance dish in SkillConditionEvaluator.ScopeDishes(board, self, SkillScope.Column, includeSelf: false))
                {
                    Add(dish);
                }

                return result
                    .OrderBy(BoardTop)
                    .ThenBy(BoardLeft)
                    .ThenBy(d => d.Id)
                    .ToList();
            }

            foreach (DishInstance dish in SkillConditionEvaluator.ScopeDishes(board, self, rule.ActionScope, includeSelf: false))
            {
                Add(dish);
            }

            return result
                .OrderBy(BoardTop)
                .ThenBy(BoardLeft)
                .ThenBy(d => d.Id)
                .ToList();
        }

        private static List<GridPos> VisualCellsForScope(
            GameplayDatabase db,
            GpTable board,
            DishInstance self,
            SkillRuleDef rule,
            SkillScope scope,
            bool isActionScope)
        {
            if (board == null || self == null)
            {
                return new List<GridPos>();
            }

            switch (scope)
            {
                case SkillScope.Self:
                    return UniqueCells(self.OccupiedCells);

                case SkillScope.All:
                case SkillScope.Empty:
                    return board.ExistingCells();

                case SkillScope.Category:
                {
                    string category = isActionScope && rule != null
                        ? SkillConditionEvaluator.ParseCategoryParam(rule.ActionParams)
                        : SkillConditionEvaluator.CategoryOf(rule?.CondParam);
                    return CellsForDishes(SkillConditionEvaluator.CategoryDishes(board, category));
                }

                case SkillScope.Before:
                case SkillScope.After:
                case SkillScope.Other:
                    return CellsForDishes(SkillConditionEvaluator.ScopeDishes(board, self, scope));

                case SkillScope.Edge:
                    return EdgeCells(board);

                default:
                {
                    List<GridPos> cells = SkillConditionEvaluator.ScopeCells(board, self, scope).ToList();
                    if (cells.Count > 0)
                    {
                        return cells;
                    }

                    return CellsForDishes(SkillConditionEvaluator.ScopeDishes(board, self, scope));
                }
            }
        }

        private static List<GridPos> EdgeCells(GpTable board)
        {
            var result = new List<GridPos>();
            if (board == null || !board.TryGetExistingBounds(out int minX, out int minY, out int maxX, out int maxY))
            {
                return result;
            }

            foreach (GridPos cell in board.ExistingCells())
            {
                if (cell.X == minX || cell.X == maxX || cell.Y == minY || cell.Y == maxY)
                {
                    result.Add(cell);
                }
            }

            return result;
        }

        private static List<GridPos> CellsForDishes(IEnumerable<DishInstance> dishes)
        {
            var result = new List<GridPos>();
            var seen = new HashSet<GridPos>();
            if (dishes == null)
            {
                return result;
            }

            foreach (DishInstance dish in dishes)
            {
                if (dish == null)
                {
                    continue;
                }

                foreach (GridPos cell in dish.OccupiedCells)
                {
                    if (seen.Add(cell))
                    {
                        result.Add(cell);
                    }
                }
            }

            return result;
        }

        private static List<GridPos> UniqueCells(IEnumerable<GridPos> cells)
        {
            var result = new List<GridPos>();
            var seen = new HashSet<GridPos>();
            if (cells == null)
            {
                return result;
            }

            foreach (GridPos cell in cells)
            {
                if (seen.Add(cell))
                {
                    result.Add(cell);
                }
            }

            return result;
        }

        private static bool HasCopyableSkill(GameplayDatabase db, DishInstance self, DishInstance candidate)
        {
            if (db == null || self == null || candidate == null)
            {
                return false;
            }

            foreach (string skillId in candidate.SkillIds)
            {
                if (!string.IsNullOrEmpty(skillId)
                    && !self.SkillIds.Contains(skillId)
                    && !IsCopySkill(db, skillId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCopySkill(GameplayDatabase db, string skillId)
        {
            SkillDef def = db?.GetSkill(skillId);
            if (def == null || !def.HasRules)
            {
                return false;
            }

            for (int i = 0; i < def.Rules.Count; i++)
            {
                if (def.Rules[i].ActionType == SkillActionType.CopySkill)
                {
                    return true;
                }
            }

            return false;
        }

        private static string ParseSkillTypeParam(IReadOnlyList<string> actionParams)
        {
            if (actionParams == null)
            {
                return string.Empty;
            }

            foreach (string p in actionParams)
            {
                if (p == null) continue;
                int idx = p.IndexOf("skilltype:", System.StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    return p.Substring(idx + "skilltype:".Length).Split(';', ',', '|')[0].Trim();
                }
            }

            return string.Empty;
        }

        private static bool HasSkillOfType(GameplayDatabase db, DishInstance dish, SkillActionType actionType)
        {
            if (db == null || dish == null)
            {
                return false;
            }

            foreach (string skillId in dish.SkillIds)
            {
                SkillDef def = db.GetSkill(skillId);
                if (def == null || !def.HasRules)
                {
                    continue;
                }

                for (int i = 0; i < def.Rules.Count; i++)
                {
                    if (def.Rules[i].ActionType == actionType)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasActionParam(SkillRuleDef rule, string token)
        {
            if (rule?.ActionParams == null)
            {
                return false;
            }

            foreach (string param in rule.ActionParams)
            {
                if (param != null && param.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static int BoardTop(DishInstance dish)
        {
            int top = int.MaxValue;
            foreach (GridPos cell in dish.OccupiedCells)
            {
                if (cell.Y < top)
                {
                    top = cell.Y;
                }
            }

            return top == int.MaxValue ? dish.Placement.Origin.Y : top;
        }

        private static int BoardLeft(DishInstance dish)
        {
            int left = int.MaxValue;
            foreach (GridPos cell in dish.OccupiedCells)
            {
                if (cell.X < left)
                {
                    left = cell.X;
                }
            }

            return left == int.MaxValue ? dish.Placement.Origin.X : left;
        }
    }
}
