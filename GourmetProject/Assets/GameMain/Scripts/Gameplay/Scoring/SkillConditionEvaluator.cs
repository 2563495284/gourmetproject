using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
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
        /// <summary>默认「视为食物数」提供器：仅取实例静态定义 + 已持久化的运行时加成（无 live 上下文，避免递归）。</summary>
        private static readonly System.Func<DishInstance, int> DefaultCountAs = d => d.EffectiveCountAs;

        public static int Evaluate(SkillRuleDef rule, ScoreContext ctx, DishInstance self)
            => Evaluate(rule, ctx.Board, ctx.Snapshot.History, self, ctx.CurrentHappyCakeLayers, ctx.GetEffectiveCountAs, ctx.Db);

        public static int Evaluate(SkillRuleDef rule, GpBoard board, IScoreHistory history, DishInstance self, int happyCakeLayers = 0)
            => Evaluate(rule, board, history, self, happyCakeLayers, DefaultCountAs, null);

        private static int Evaluate(SkillRuleDef rule, GpBoard board, IScoreHistory history, DishInstance self, int happyCakeLayers, System.Func<DishInstance, int> countAsOf, GameplayDatabase db)
        {
            if (rule.CondType == SkillConditionType.None)
            {
                return 1;
            }

            int raw = RawValue(rule, board, history ?? EmptyScoreHistory.Instance, self, happyCakeLayers, countAsOf ?? DefaultCountAs, db);

            // 阶梯：condParam="tiers:t1|t2|t3"，返回满足的最高档序号（1-based），行为侧据此取对应 actionValue。
            if (TryParseTiers(rule.CondParam, out int[] tiers))
            {
                return HighestTier(raw, tiers);
            }

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

        private static int RawValue(SkillRuleDef rule, GpBoard board, IScoreHistory history, DishInstance self, int happyCakeLayers, System.Func<DishInstance, int> countAsOf, GameplayDatabase db)
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
                    return CountByUnit(ScopeDishes(board, self, rule.CondScope, IncludeSelf(rule)), rule.CondUnit, countAsOf);

                case SkillConditionType.DishSize:
                    return CountDishSize(ScopeDishes(board, self, rule.CondScope, IncludeSelf(rule)), rule, countAsOf);

                case SkillConditionType.ServeOrder:
                    return CountByUnit(ServeOrderDishes(board, self, rule.CondScope), rule.CondUnit, countAsOf);

                case SkillConditionType.SameDish:
                    return CountSameBase(ScopeDishes(board, self, rule.CondScope, IncludeSelf(rule)), self, rule.CondUnit, countAsOf);

                case SkillConditionType.SameKindInRun:
                    return history.RunSettledCount(self.Def.BaseId);

                case SkillConditionType.SameKindInMeal:
                    return rule.CondParam.IndexOf("settled", System.StringComparison.OrdinalIgnoreCase) >= 0
                        ? history.MealSettledCount(self.Def.BaseId)
                        : CountSameBase(ScopeDishes(board, self, SkillScope.All, includeSelf: true), self, rule.CondUnit, countAsOf);

                case SkillConditionType.TagCount:
                    return self.SkillIds.Count + (self.HasFlavor ? 1 : 0);

                case SkillConditionType.ShapeMatch:
                    return CountShapeMatch(ScopeDishes(board, self, rule.CondScope, IncludeSelf(rule)), rule.CondParam, rule.CondUnit, countAsOf);

                case SkillConditionType.RecipeCount:
                    return RecipeRaw(history, rule);

                case SkillConditionType.CategoryCount:
                    return CountByUnit(CategoryDishes(board, CategoryOf(rule.CondParam)), rule.CondUnit, countAsOf);

                case SkillConditionType.SkillTypeCount:
                    return CountSkillType(board, db, rule.CondParam, rule.CondUnit, countAsOf);

                case SkillConditionType.LayerCount:
                    return CapLayers(happyCakeLayers, rule.CondParam);

                case SkillConditionType.OccupiedCell:
                    return self.OccupiedCells.Count;

                default:
                    return 0;
            }
        }

        private static bool IncludeSelf(SkillRuleDef rule)
            => rule.CondParam.IndexOf("self", System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>层数封顶：condParam 含 cap:N 时返回 min(layers, N)（闪电泡芙「最多消耗3层」）。</summary>
        private static int CapLayers(int layers, string condParam)
        {
            if (!string.IsNullOrEmpty(condParam))
            {
                int idx = condParam.IndexOf("cap:", System.StringComparison.OrdinalIgnoreCase);
                if (idx >= 0 && int.TryParse(condParam.Substring(idx + 4).Split(';', ',', '|')[0], out int cap) && layers > cap)
                {
                    return cap;
                }
            }

            return layers;
        }

        // ---------- 阶梯 tiers ----------

        /// <summary>解析 condParam 中的 tiers:t1|t2|t3 阈值升序数组。无则返回 false。</summary>
        private static bool TryParseTiers(string condParam, out int[] tiers)
        {
            tiers = null;
            if (string.IsNullOrEmpty(condParam))
            {
                return false;
            }

            int idx = condParam.IndexOf("tiers:", System.StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return false;
            }

            string body = condParam.Substring(idx + "tiers:".Length).Split(';')[0];
            string[] parts = body.Split('|', ',');
            var list = new List<int>();
            foreach (string p in parts)
            {
                if (int.TryParse(p.Trim(), out int v))
                {
                    list.Add(v);
                }
            }

            if (list.Count == 0)
            {
                return false;
            }

            tiers = list.ToArray();
            return true;
        }

        /// <summary>返回 raw 满足的最高档序号（1-based）；均不满足返回 0。</summary>
        private static int HighestTier(int raw, int[] tiers)
        {
            int result = 0;
            for (int i = 0; i < tiers.Length; i++)
            {
                if (raw >= tiers[i])
                {
                    result = i + 1;
                }
            }

            return result;
        }

        // ---------- 带某行为类的食物数 ----------

        private static int CountSkillType(GpBoard board, GameplayDatabase db, string condParam, CountUnit unit, System.Func<DishInstance, int> countAsOf)
        {
            if (db == null || string.IsNullOrEmpty(condParam))
            {
                return 0;
            }

            string token = CategoryOf(condParam); // 复用「取非 tiers 段」：此处即行为类型名。
            if (!System.Enum.TryParse(token, ignoreCase: true, out SkillActionType actionType))
            {
                return 0;
            }

            var matched = new List<DishInstance>();
            foreach (DishInstance d in board.Dishes)
            {
                if (HasSkillOfType(db, d, actionType))
                {
                    matched.Add(d);
                }
            }

            return CountByUnit(matched, unit, countAsOf);
        }

        private static bool HasSkillOfType(GameplayDatabase db, DishInstance dish, SkillActionType actionType)
        {
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

        // ---------- 分类（category）集合与参数 ----------

        /// <summary>从 condParam 取「分类/类型名」段（去除 tiers:… 部分），如 "cake;tiers:3|5|8" → "cake"。</summary>
        public static string CategoryOf(string condParam)
        {
            if (string.IsNullOrEmpty(condParam))
            {
                return string.Empty;
            }

            foreach (string seg in condParam.Split(';'))
            {
                string s = seg.Trim();
                if (s.Length > 0 && !s.StartsWith("tiers:", System.StringComparison.OrdinalIgnoreCase))
                {
                    return s;
                }
            }

            return string.Empty;
        }

        /// <summary>棋盘上属于指定分类（如 cake）的全部菜（含自身若匹配）。</summary>
        public static List<DishInstance> CategoryDishes(GpBoard board, string category)
        {
            var result = new List<DishInstance>();
            if (string.IsNullOrEmpty(category))
            {
                return result;
            }

            foreach (DishInstance d in board.Dishes)
            {
                if (d.Def.IsCategory(category))
                {
                    result.Add(d);
                }
            }

            return result;
        }

        /// <summary>从 actionParams 解析分类定向的分类名，编码为 cat:xxx。未指定返回空串。</summary>
        public static string ParseCategoryParam(IEnumerable<string> actionParams)
        {
            if (actionParams == null)
            {
                return string.Empty;
            }

            foreach (string p in actionParams)
            {
                if (p == null)
                {
                    continue;
                }

                int idx = p.IndexOf("cat:", System.StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    return p.Substring(idx + 4).Split(';', ',', '|')[0].Trim();
                }
            }

            return string.Empty;
        }

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

                case SkillScope.Round:
                    CollectDishesFromCells(board, ScopeCells(board, self, scope), result);
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

        private static void CollectDishesFromCells(GpBoard board, IEnumerable<GridPos> cells, List<DishInstance> result)
        {
            var seen = new HashSet<int>();
            foreach (GridPos cell in cells)
            {
                DishInstance dish = board.DishAt(cell);
                if (dish != null && seen.Add(dish.Id))
                {
                    result.Add(dish);
                }
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

        private static int CountByUnit(List<DishInstance> dishes, CountUnit unit, System.Func<DishInstance, int> countAsOf)
        {
            if (unit != CountUnit.Kinds)
            {
                int sum = 0;
                foreach (DishInstance d in dishes) sum += countAsOf(d);
                return sum;
            }

            var kinds = new HashSet<string>();
            foreach (DishInstance d in dishes) kinds.Add(d.Def.BaseId);
            return kinds.Count;
        }

        private static int CountSameBase(List<DishInstance> dishes, DishInstance self, CountUnit unit, System.Func<DishInstance, int> countAsOf)
        {
            int count = 0;
            foreach (DishInstance d in dishes)
            {
                if (d.Id != self.Id && d.Def.BaseId == self.Def.BaseId) count += countAsOf(d);
            }

            if (unit == CountUnit.Kinds)
            {
                return count > 0 ? 1 : 0;
            }

            return count;
        }

        private static int CountDishSize(List<DishInstance> dishes, SkillRuleDef rule, System.Func<DishInstance, int> countAsOf)
        {
            int count = 0;
            foreach (DishInstance d in dishes)
            {
                if (rule.CondCompare.Evaluate(d.OccupiedCells.Count, rule.CondThreshold)) count += countAsOf(d);
            }

            return count;
        }

        private static int CountShapeMatch(List<DishInstance> dishes, string param, CountUnit unit, System.Func<DishInstance, int> countAsOf)
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

            return CountByUnit(matched, unit, countAsOf);
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

        /// <summary>作用域涉及的「存在格」集合（本行/本列/相邻/周围）。</summary>
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

                case SkillScope.Round:
                {
                    var selfCells = new HashSet<int>();
                    foreach (GridPos c in self.OccupiedCells) selfCells.Add(c.Y * board.Width + c.X);
                    foreach (GridPos c in self.OccupiedCells)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                if (dx == 0 && dy == 0)
                                {
                                    continue;
                                }

                                TryNeighbor(board, selfCells, cells, seen, c.Offset(dx, dy));
                            }
                        }
                    }
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
