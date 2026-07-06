using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using GpBoard = GourmetProject.Gameplay.Board.Board;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>
    /// 技能前提求值器：把一条规则的前提在当前局面折算成非负整数 count。
    /// 纯逻辑、无随机，语义见 docs/design/技能.md 第 3 节。
    /// </summary>
    public static class SkillConditionEvaluator
    {
        public static int Evaluate(SkillRuleDef rule, ScoreContext ctx, DishInstance self)
            => Evaluate(rule, ctx.Board, ctx.Snapshot.History, self);

        public static int Evaluate(SkillRuleDef rule, GpBoard board, IScoreHistory history, DishInstance self)
        {
            if (rule.CondType == SkillConditionType.None)
            {
                return 1;
            }

            int raw = RawValue(rule, board, history ?? EmptyScoreHistory.Instance, self);
            switch (rule.CondMode)
            {
                case CountMode.Per:
                    return raw < 0 ? 0 : raw;
                case CountMode.Reach:
                    return rule.CondCompare.Evaluate(raw, rule.CondThreshold) ? 1 : 0;
                case CountMode.Gate:
                default:
                    return raw > 0 ? 1 : 0;
            }
        }

        private static int RawValue(SkillRuleDef rule, GpBoard board, IScoreHistory history, DishInstance self)
        {
            switch (rule.CondType)
            {
                case SkillConditionType.PositionFilled:
                    return IsScopeFilled(board, self, rule.CondScope) ? 1 : 0;

                case SkillConditionType.EmptyCell:
                    return CountEmptyCells(board, self, rule.CondScope);

                case SkillConditionType.Edge:
                    return IsOnEdge(board, self) ? 1 : 0;

                case SkillConditionType.DishCount:
                    return CountByUnit(ScopeDishes(board, self, rule.CondScope, IncludeSelf(rule)), rule.CondUnit);

                case SkillConditionType.DishSize:
                    return CountDishSize(ScopeDishes(board, self, rule.CondScope, IncludeSelf(rule)), rule);

                case SkillConditionType.ServeOrder:
                    return CountByUnit(ServeOrderDishes(board, self, rule.CondScope), rule.CondUnit);

                case SkillConditionType.SameDish:
                    return CountSameBase(ScopeDishes(board, self, rule.CondScope, IncludeSelf(rule)), self, rule.CondUnit);

                case SkillConditionType.SameKindInRun:
                    return history.RunSettledCount(self.Def.BaseId);

                case SkillConditionType.SameKindInMeal:
                    return rule.CondParam.IndexOf("settled", System.StringComparison.OrdinalIgnoreCase) >= 0
                        ? history.MealSettledCount(self.Def.BaseId)
                        : CountSameBase(ScopeDishes(board, self, SkillScope.All, includeSelf: true), self, rule.CondUnit);

                case SkillConditionType.TagCount:
                    return self.SkillIds.Count + (self.HasFlavor ? 1 : 0);

                case SkillConditionType.ShapeMatch:
                    return CountShapeMatch(ScopeDishes(board, self, rule.CondScope, IncludeSelf(rule)), rule.CondParam, rule.CondUnit);

                case SkillConditionType.RecipeCount:
                    return RecipeRaw(history, rule);

                case SkillConditionType.LayerCount:
                    return self.Layers;

                case SkillConditionType.OccupiedCell:
                    return self.OccupiedCells.Count;

                default:
                    return 0;
            }
        }

        private static bool IncludeSelf(SkillRuleDef rule)
            => rule.CondParam.IndexOf("self", System.StringComparison.OrdinalIgnoreCase) >= 0;

        // ---------- 作用域内的菜集合 ----------

        public static List<DishInstance> ScopeDishes(GpBoard board, DishInstance self, SkillScope scope, bool includeSelf)
        {
            var result = new List<DishInstance>();
            switch (scope)
            {
                case SkillScope.Self:
                    result.Add(self);
                    return result;

                case SkillScope.Adjacent:
                    result.AddRange(board.GetAdjacentDishes(self));
                    if (includeSelf) result.Add(self);
                    return result;

                case SkillScope.Row:
                    CollectRowOrColumn(board, self, result, row: true, includeSelf);
                    return result;

                case SkillScope.Column:
                    CollectRowOrColumn(board, self, result, row: false, includeSelf);
                    return result;

                case SkillScope.Before:
                    foreach (DishInstance d in board.Dishes) if (d.Id < self.Id) result.Add(d);
                    return result;

                case SkillScope.After:
                    foreach (DishInstance d in board.Dishes) if (d.Id > self.Id) result.Add(d);
                    return result;

                case SkillScope.All:
                default:
                    foreach (DishInstance d in board.Dishes)
                    {
                        if (d.Id == self.Id && !includeSelf) continue;
                        result.Add(d);
                    }
                    return result;
            }
        }

        private static void CollectRowOrColumn(GpBoard board, DishInstance self, List<DishInstance> result, bool row, bool includeSelf)
        {
            var lines = new HashSet<int>();
            foreach (GridPos c in self.OccupiedCells)
            {
                lines.Add(row ? c.Y : c.X);
            }

            var seen = new HashSet<int>();
            foreach (DishInstance d in board.Dishes)
            {
                if (d.Id == self.Id)
                {
                    if (includeSelf && seen.Add(d.Id)) result.Add(d);
                    continue;
                }

                foreach (GridPos c in d.OccupiedCells)
                {
                    if (lines.Contains(row ? c.Y : c.X))
                    {
                        if (seen.Add(d.Id)) result.Add(d);
                        break;
                    }
                }
            }
        }

        private static List<DishInstance> ServeOrderDishes(GpBoard board, DishInstance self, SkillScope scope)
        {
            var result = new List<DishInstance>();
            foreach (DishInstance d in board.Dishes)
            {
                if (scope == SkillScope.Before && d.Id < self.Id) result.Add(d);
                else if (scope == SkillScope.After && d.Id > self.Id) result.Add(d);
                else if (scope != SkillScope.Before && scope != SkillScope.After && d.Id != self.Id) result.Add(d);
            }

            return result;
        }

        // ---------- 计数工具 ----------

        private static int CountByUnit(List<DishInstance> dishes, CountUnit unit)
        {
            if (unit != CountUnit.Kinds)
            {
                return dishes.Count;
            }

            var kinds = new HashSet<string>();
            foreach (DishInstance d in dishes) kinds.Add(d.Def.BaseId);
            return kinds.Count;
        }

        private static int CountSameBase(List<DishInstance> dishes, DishInstance self, CountUnit unit)
        {
            int count = 0;
            foreach (DishInstance d in dishes)
            {
                if (d.Id != self.Id && d.Def.BaseId == self.Def.BaseId) count++;
            }

            if (unit == CountUnit.Kinds)
            {
                return count > 0 ? 1 : 0;
            }

            return count;
        }

        private static int CountDishSize(List<DishInstance> dishes, SkillRuleDef rule)
        {
            int count = 0;
            foreach (DishInstance d in dishes)
            {
                if (rule.CondCompare.Evaluate(d.OccupiedCells.Count, rule.CondThreshold)) count++;
            }

            return count;
        }

        private static int CountShapeMatch(List<DishInstance> dishes, string param, CountUnit unit)
        {
            int w = 1, h = 1;
            if (!string.IsNullOrEmpty(param))
            {
                string[] parts = param.ToLowerInvariant().Split('x');
                if (parts.Length == 2)
                {
                    int.TryParse(parts[0], out w);
                    int.TryParse(parts[1], out h);
                }
            }

            var matched = new List<DishInstance>();
            foreach (DishInstance d in dishes)
            {
                int dw = d.Placement.Orientation.Width;
                int dh = d.Placement.Orientation.Height;
                if ((dw == w && dh == h) || (dw == h && dh == w))
                {
                    matched.Add(d);
                }
            }

            return CountByUnit(matched, unit);
        }

        private static int RecipeRaw(IScoreHistory history, SkillRuleDef rule)
        {
            IReadOnlyList<string> recipe = history.RecipeBaseIds;
            int value;
            if (rule.CondUnit == CountUnit.Kinds)
            {
                var kinds = new HashSet<string>(recipe);
                value = kinds.Count;
            }
            else
            {
                value = recipe.Count;
            }

            return rule.CondCompare.Evaluate(value, rule.CondThreshold) ? 1 : 0;
        }

        // ---------- 空格 / 填满 / 边缘 ----------

        private static int CountEmptyCells(GpBoard board, DishInstance self, SkillScope scope)
        {
            if (scope == SkillScope.All || scope == SkillScope.Empty)
            {
                return board.EmptyCellCount;
            }

            int count = 0;
            foreach (GridPos c in ScopeCells(board, self, scope))
            {
                if (board.IsEmpty(c)) count++;
            }

            return count;
        }

        private static bool IsScopeFilled(GpBoard board, DishInstance self, SkillScope scope)
        {
            if (scope == SkillScope.All)
            {
                return board.EmptyCellCount == 0;
            }

            bool any = false;
            foreach (GridPos c in ScopeCells(board, self, scope))
            {
                any = true;
                if (board.IsEmpty(c)) return false;
            }

            return any;
        }

        /// <summary>作用域涉及的「存在格」集合（本行/本列/相邻）。</summary>
        private static IEnumerable<GridPos> ScopeCells(GpBoard board, DishInstance self, SkillScope scope)
        {
            var cells = new List<GridPos>();
            var seen = new HashSet<int>();
            void AddCell(GridPos p)
            {
                if (!board.Exists(p)) return;
                int key = p.Y * board.Width + p.X;
                if (seen.Add(key)) cells.Add(p);
            }

            switch (scope)
            {
                case SkillScope.Row:
                {
                    var ys = new HashSet<int>();
                    foreach (GridPos c in self.OccupiedCells) ys.Add(c.Y);
                    foreach (int y in ys)
                        for (int x = 0; x < board.Width; x++) AddCell(new GridPos(x, y));
                    break;
                }

                case SkillScope.Column:
                {
                    var xs = new HashSet<int>();
                    foreach (GridPos c in self.OccupiedCells) xs.Add(c.X);
                    foreach (int x in xs)
                        for (int y = 0; y < board.Height; y++) AddCell(new GridPos(x, y));
                    break;
                }

                case SkillScope.Adjacent:
                {
                    var selfCells = new HashSet<int>();
                    foreach (GridPos c in self.OccupiedCells) selfCells.Add(c.Y * board.Width + c.X);
                    foreach (GridPos c in self.OccupiedCells)
                    {
                        TryNeighbor(board, selfCells, cells, seen, c.Offset(1, 0));
                        TryNeighbor(board, selfCells, cells, seen, c.Offset(-1, 0));
                        TryNeighbor(board, selfCells, cells, seen, c.Offset(0, 1));
                        TryNeighbor(board, selfCells, cells, seen, c.Offset(0, -1));
                    }
                    break;
                }
            }

            return cells;
        }

        private static void TryNeighbor(GpBoard board, HashSet<int> selfCells, List<GridPos> cells, HashSet<int> seen, GridPos p)
        {
            if (!board.Exists(p)) return;
            int key = p.Y * board.Width + p.X;
            if (selfCells.Contains(key)) return;
            if (seen.Add(key)) cells.Add(p);
        }

        private static bool IsOnEdge(GpBoard board, DishInstance self)
        {
            if (!board.TryGetExistingBounds(out int minX, out int minY, out int maxX, out int maxY))
            {
                return false;
            }

            foreach (GridPos c in self.OccupiedCells)
            {
                if (c.X == minX || c.X == maxX || c.Y == minY || c.Y == maxY) return true;
            }

            return false;
        }
    }
}
