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
        public static readonly SkillScopeVisual Empty = new SkillScopeVisual(null, null, null, null);

        public SkillScopeVisual(
            IReadOnlyList<int> visualTargetDishInstanceIds,
            IReadOnlyList<GridPos> visualTargetCells,
            IReadOnlyList<GridPos> actionScopeCells,
            IReadOnlyList<GridPos> conditionCells)
        {
            VisualTargetDishInstanceIds = visualTargetDishInstanceIds ?? System.Array.Empty<int>();
            VisualTargetCells = visualTargetCells ?? System.Array.Empty<GridPos>();
            ActionScopeCells = actionScopeCells ?? System.Array.Empty<GridPos>();
            ConditionCells = conditionCells ?? System.Array.Empty<GridPos>();
        }

        public IReadOnlyList<int> VisualTargetDishInstanceIds { get; }

        public IReadOnlyList<GridPos> VisualTargetCells { get; }

        /// <summary>
        /// 行为配置对应的语义作用域格。与实际命中的食物占格分离，避免多格食物
        /// 只用一格接触作用域时把整块食物扩进范围轮廓。
        /// </summary>
        public IReadOnlyList<GridPos> ActionScopeCells { get; }

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

            IReadOnlyList<DishInstance> targetDishes = ResolveVisualActionDishes(db, board, self, rule, mode, null);
            List<int> targetIds = targetDishes.Select(d => d.Id).Distinct().ToList();
            List<GridPos> actionScopeCells = VisualCellsForScope(
                db,
                board,
                self,
                rule,
                rule.ActionScope,
                isActionScope: true);
            List<GridPos> targetCells = rule.ActionType == SkillActionType.TransferSkills
                ? board.ExistingCells()
                : mode == SkillScopeVisualMode.CandidateScope
                    ? new List<GridPos>(actionScopeCells)
                    : CellsForDishes(targetDishes);
            if (targetCells.Count == 0)
            {
                targetCells = new List<GridPos>(actionScopeCells);
            }

            List<GridPos> conditionCells = VisualCellsForScope(db, board, self, rule, rule.CondScope, isActionScope: false);
            return new SkillScopeVisual(targetIds, targetCells, actionScopeCells, conditionCells);
        }

        public static IReadOnlyList<DishInstance> ResolveActionTargetDishes(
            GameplayDatabase db,
            GpTable board,
            DishInstance self,
            SkillRuleDef rule,
            SkillScopeVisualMode mode,
            System.Func<DishInstance, string, bool> categoryMatcher = null)
        {
            return ResolveVisualActionDishes(db, board, self, rule, mode, categoryMatcher);
        }

        private static IReadOnlyList<DishInstance> ResolveVisualActionDishes(
            GameplayDatabase db,
            GpTable board,
            DishInstance self,
            SkillRuleDef rule,
            SkillScopeVisualMode mode,
            System.Func<DishInstance, string, bool> categoryMatcher)
        {
            if (rule.ActionType == SkillActionType.CopySkill)
            {
                return ResolveCopyCandidateDishes(db, board, self, rule, mode, categoryMatcher);
            }

            if (rule.ActionType == SkillActionType.TriggerSweetTransfer)
            {
                return ResolveSweetTransferSources(db, board, self, rule);
            }

            // “甜蜜传递 X 个食物”的接收者固定从全场其它食物中随机选择。
            // 即使旧配置误把第一个子技能的作用域复制到传递规则，也不能限制候选池。
            if (rule.ActionType == SkillActionType.TransferSkills)
            {
                return ApplyActionCount(
                    SkillConditionEvaluator.ScopeDishes(board, self, SkillScope.Other),
                    rule,
                    mode);
            }

            if (rule.ActionType == SkillActionType.AddLayer
                || rule.ActionType == SkillActionType.ConsumeLayer
                || rule.ActionType == SkillActionType.GrantGold)
            {
                return System.Array.Empty<DishInstance>();
            }

            List<DishInstance> dishes = ResolveScopeDishes(
                db,
                board,
                self,
                rule,
                includeSelfForSelfScope: true,
                categoryMatcher);
            return ApplyActionCount(dishes, rule, mode);
        }

        private static List<DishInstance> ResolveCopyCandidateDishes(
            GameplayDatabase db,
            GpTable board,
            DishInstance self,
            SkillRuleDef rule,
            SkillScopeVisualMode mode,
            System.Func<DishInstance, string, bool> categoryMatcher)
        {
            string category = TargetFilterCategory(rule);
            List<DishInstance> dishes = !string.IsNullOrEmpty(category)
                ? board.Dishes.Where(d => MatchesCategory(d, category, categoryMatcher)).ToList()
                : ResolveScopeDishes(
                    db,
                    board,
                    self,
                    rule,
                    includeSelfForSelfScope: false,
                    categoryMatcher);

            dishes.RemoveAll(d => d.Id == self.Id || !HasCopyableSkill(db, self, d));
            return ApplyActionCount(dishes, rule, mode);
        }

        private static List<DishInstance> ResolveScopeDishes(
            GameplayDatabase db,
            GpTable board,
            DishInstance self,
            SkillRuleDef rule,
            bool includeSelfForSelfScope,
            System.Func<DishInstance, string, bool> categoryMatcher = null)
        {
            List<DishInstance> dishes;
            if (rule.ActionScope == SkillScope.Self)
            {
                dishes = includeSelfForSelfScope ? new List<DishInstance> { self } : new List<DishInstance>();
            }
            else if (rule.ActionScope == SkillScope.Category)
            {
                string scopedCategory = TargetFilterCategory(rule);
                dishes = board.Dishes
                    .Where(d => MatchesCategory(d, scopedCategory, categoryMatcher))
                    .ToList();
            }
            else
            {
                dishes = SkillConditionEvaluator.ScopeDishes(board, self, rule.ActionScope);
            }

            if (HasActionParam(rule, "include:self") && dishes.All(d => d.Id != self.Id))
            {
                dishes.Add(self);
            }

            string category = TargetFilterCategory(rule);
            if (!string.IsNullOrEmpty(category))
            {
                dishes = dishes.Where(d => MatchesCategory(d, category, categoryMatcher)).ToList();
            }

            if (HasActionParam(rule, "position:non-edge"))
            {
                dishes = dishes.Where(d => !SkillConditionEvaluator.IsOnEdge(board, d)).ToList();
            }

            int size = ParseIntActionParam(rule.ActionParams, "size", 0);
            if (size > 0)
            {
                dishes = dishes.Where(d => d.OccupiedCells.Count == size).ToList();
            }

            string skillTypeToken = ParseSkillTypeParam(rule.ActionParams);
            if (!string.IsNullOrEmpty(skillTypeToken)
                && System.Enum.TryParse(skillTypeToken, ignoreCase: true, out SkillActionType filterType))
            {
                dishes = dishes.Where(d => HasSkillOfType(db, d, filterType)).ToList();
            }

            return dishes;
        }

        private static bool MatchesCategory(
            DishInstance dish,
            string category,
            System.Func<DishInstance, string, bool> categoryMatcher)
            => categoryMatcher != null
                ? categoryMatcher(dish, category)
                : dish != null && dish.IsCategory(category);

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
                && !HasActionParam(rule, "target:random")
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

            if (isActionScope && rule != null)
            {
                string category = TargetFilterCategory(rule);
                string skillType = ParseSkillTypeParam(rule.ActionParams);
                if (!string.IsNullOrEmpty(category)
                    || !string.IsNullOrEmpty(skillType)
                    || HasActionParam(rule, "include:self"))
                {
                    List<DishInstance> filtered = ResolveScopeDishes(
                        db,
                        board,
                        self,
                        rule,
                        includeSelfForSelfScope: true);
                    if (rule.ActionType == SkillActionType.TransferSkills)
                    {
                        filtered.RemoveAll(d => d.Id == self.Id);
                    }

                    return CellsForDishes(filtered);
                }
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
                    if (!isActionScope
                        && rule != null
                        && SkillConditionEvaluator.HasParam(rule.CondParam, "include:self"))
                    {
                        foreach (GridPos cell in self.OccupiedCells)
                        {
                            if (!cells.Contains(cell))
                            {
                                cells.Add(cell);
                            }
                        }
                    }

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

        private static int ParseIntActionParam(IReadOnlyList<string> actionParams, string key, int defaultValue)
        {
            if (actionParams == null)
            {
                return defaultValue;
            }

            string prefix = key + ":";
            foreach (string param in actionParams)
            {
                if (string.IsNullOrEmpty(param)) continue;
                foreach (string raw in param.Split(';'))
                {
                    string segment = raw.Trim();
                    if (segment.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(segment.Substring(prefix.Length), out int value))
                    {
                        return value;
                    }
                }
            }

            return defaultValue;
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

        /// <summary>
        /// cat:xxx 通常表示“只选指定分类目标”；AddTemporaryCategory 中它表示“赋予什么分类”，
        /// 不能反过来过滤已经是该分类的食物。结算、hover 预览和结算范围演出统一走这个解析入口。
        /// </summary>
        private static string TargetFilterCategory(SkillRuleDef rule)
        {
            return rule != null && rule.ActionType == SkillActionType.AddTemporaryCategory
                ? string.Empty
                : SkillConditionEvaluator.ParseCategoryParam(rule?.ActionParams);
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
