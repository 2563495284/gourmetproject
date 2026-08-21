using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

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
            => Evaluate(rule, ctx.DiningTable, ctx.Snapshot.History, self, ctx.CurrentHappyCakeLayers, ctx.GetEffectiveCountAs, ctx.Db, ctx);

        public static int Evaluate(SkillRuleDef rule, GpTable board, IScoreHistory history, DishInstance self, int happyCakeLayers = 0)
            => Evaluate(rule, board, history, self, happyCakeLayers, DefaultCountAs, null, null);

        public static int Evaluate(
            SkillRuleDef rule,
            GpTable board,
            IScoreHistory history,
            DishInstance self,
            int happyCakeLayers,
            GameplayDatabase db)
            => Evaluate(rule, board, history, self, happyCakeLayers, DefaultCountAs, db, null);

        /// <summary>
        /// 条件是否真正依赖一片可用于摆位判断的餐桌空间。
        /// 边缘状态、结算顺序、装饰品/历史/层数/食谱/自身属性及全场统计等条件
        /// 虽然仍有 CondScope 配置占位，但它们没有应当绘制的空间条件范围。
        /// </summary>
        public static bool UsesSpatialScope(SkillRuleDef rule)
        {
            if (rule == null
                || rule.CondType == SkillConditionType.None
                || rule.CondScope == SkillScope.All)
            {
                return false;
            }

            switch (rule.CondType)
            {
                case SkillConditionType.PositionFilled:
                case SkillConditionType.EmptyCell:
                case SkillConditionType.DishCount:
                case SkillConditionType.DishSize:
                case SkillConditionType.SameDish:
                case SkillConditionType.SkillCount:
                case SkillConditionType.ShapeMatch:
                case SkillConditionType.SkillTypeCount:
                    return true;

                case SkillConditionType.TagCount:
                    return HasParam(rule.CondParam, "source:flavors");

                case SkillConditionType.CategoryCount:
                    return true;

                case SkillConditionType.Edge:
                case SkillConditionType.ServeOrder:
                case SkillConditionType.SameKindInRun:
                case SkillConditionType.SameKindInMeal:
                case SkillConditionType.RecipeCount:
                case SkillConditionType.LayerCount:
                case SkillConditionType.OccupiedCell:
                default:
                    return false;
            }
        }

        private static int Evaluate(SkillRuleDef rule, GpTable board, IScoreHistory history, DishInstance self, int happyCakeLayers, System.Func<DishInstance, int> countAsOf, GameplayDatabase db, ScoreContext ctx)
        {
            if (rule.CondType == SkillConditionType.None)
            {
                return 1;
            }

            int raw = RawValue(rule, board, history ?? EmptyScoreHistory.Instance, self, happyCakeLayers, countAsOf ?? DefaultCountAs, db, ctx);
            raw = ApplyDivisor(raw, rule.CondParam);

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
                    return SkillConditionParamParser.EvaluateComparison(rule.CondParam, raw, raw > 0) ? 1 : 0;
                case CountMode.Gate:
                default:
                    return raw > 0 ? 1 : 0;
            }
        }

        private static int RawValue(SkillRuleDef rule, GpTable board, IScoreHistory history, DishInstance self, int happyCakeLayers, System.Func<DishInstance, int> countAsOf, GameplayDatabase db, ScoreContext ctx)
        {
            switch (rule.CondType)
            {
                case SkillConditionType.PositionFilled:
                    return IsScopeFilled(board, self, rule.CondScope) ? 1 : 0;

                case SkillConditionType.EmptyCell:
                    return CountEmptyCells(board, self, rule.CondScope);

                case SkillConditionType.Edge:
                {
                    bool onEdge = IsOnEdge(board, self);
                    return HasParam(rule.CondParam, "not") ? (onEdge ? 0 : 1) : (onEdge ? 1 : 0);
                }

                case SkillConditionType.DishCount:
                {
                    int value = CountByUnit(ConditionScopeDishes(rule, board, self, ctx), rule.CondUnit, countAsOf);
                    if (rule.CondUnit == CountUnit.Instances && ctx != null)
                    {
                        value += ctx.EmptyCountAsInScope(self, rule.CondScope);
                    }
                    return value;
                }

                case SkillConditionType.DishSize:
                    return CountDishSize(ConditionScopeDishes(rule, board, self, ctx), rule, countAsOf);

                case SkillConditionType.ServeOrder:
                    if (HasParam(rule.CondParam, "first:settlement") && ctx != null)
                    {
                        return ctx.Snapshot.DishesInDefaultOrder.Count > 0
                            && ctx.Snapshot.DishesInDefaultOrder[0].Id == self.Id ? 1 : 0;
                    }
                    return CountByUnit(ServeOrderDishes(board, self, rule.CondScope), rule.CondUnit, countAsOf);

                case SkillConditionType.SameDish:
                    return CountSameBase(ScopeDishes(board, self, rule.CondScope), self, rule.CondUnit, countAsOf);

                case SkillConditionType.SameKindInRun:
                    return history.RunSettledCount(self.Def.BaseId);

                case SkillConditionType.SameKindInMeal:
                    return rule.CondParam.IndexOf("settled", System.StringComparison.OrdinalIgnoreCase) >= 0
                        ? history.MealSettledCount(self.Def.BaseId)
                        : CountSameBase(ScopeDishes(board, self, SkillScope.All), self, rule.CondUnit, countAsOf);

                case SkillConditionType.TagCount:
                    if (HasParam(rule.CondParam, "source:passive-items"))
                    {
                        return ctx?.Snapshot.PassiveItemCount ?? 0;
                    }
                    if (HasParam(rule.CondParam, "source:flavors"))
                    {
                        int flavors = 0;
                        foreach (DishInstance dish in ConditionScopeDishes(rule, board, self, ctx))
                        {
                            flavors += dish.FlavorIds.Count;
                        }
                        return flavors;
                    }
                    return CountSubSkills(self, db) + self.FlavorIds.Count;

                case SkillConditionType.SkillCount:
                    return CountSkills(ConditionScopeDishes(rule, board, self, ctx), db);

                case SkillConditionType.ShapeMatch:
                    return CountShapeMatch(ConditionScopeDishes(rule, board, self, ctx), rule.CondParam, rule.CondUnit, countAsOf);

                case SkillConditionType.RecipeCount:
                    return RecipeRaw(history, rule);

                case SkillConditionType.CategoryCount:
                {
                    string category = CategoryOf(rule.CondParam);
                    List<DishInstance> categoryDishes = ConditionScopeDishes(rule, board, self, ctx);
                    categoryDishes.RemoveAll(d => !(ctx?.IsCategory(d, category) ?? d.IsCategory(category)));
                    return CountByUnit(categoryDishes, rule.CondUnit, countAsOf);
                }

                case SkillConditionType.SkillTypeCount:
                    return CountSkillType(rule, board, db, self, countAsOf, ctx);

                case SkillConditionType.LayerCount:
                    return CapLayers(happyCakeLayers, rule.CondParam);

                case SkillConditionType.OccupiedCell:
                    return HasParam(rule.CondParam, "source:board")
                        ? board.ExistingCells().Count
                        : self.OccupiedCells.Count;

                default:
                    return 0;
            }
        }

        private static int CountSkills(IEnumerable<DishInstance> dishes, GameplayDatabase db)
        {
            int count = 0;
            var seen = new HashSet<int>();
            foreach (DishInstance dish in dishes)
            {
                if (dish != null && seen.Add(dish.Id))
                {
                    count += CountSubSkills(dish, db);
                }
            }

            return count;
        }

        /// <summary>
        /// “技能数”统一按可执行的子技能条目计数：本体 skill 中每条 rule 算 1，
        /// 甜蜜传递获得的每条 TransferredSkill 也算 1。
        /// </summary>
        internal static int CountSubSkills(DishInstance dish, GameplayDatabase db)
        {
            if (dish == null)
            {
                return 0;
            }

            int count = dish.TransferredSkills.Count;
            foreach (string skillId in dish.SkillIds)
            {
                SkillDef skill = db?.GetSkill(skillId);
                // 无数据库的旧式纯条件调用无法展开 rule，保留每个 skill 至少 1 条的兼容回退；
                // 正式结算始终传入数据库，因此使用真实子技能数。
                count += skill != null ? skill.Rules.Count : 1;
            }

            return count;
        }

        private static List<DishInstance> ConditionScopeDishes(
            SkillRuleDef rule,
            GpTable board,
            DishInstance self,
            ScoreContext ctx = null)
        {
            List<DishInstance> dishes = ScopeDishes(board, self, rule.CondScope);
            if (HasParam(rule.CondParam, "include:self") && dishes.TrueForAll(d => d.Id != self.Id))
            {
                dishes.Add(self);
            }

            string category = CategoryParamOf(rule.CondParam);
            if (!string.IsNullOrEmpty(category))
            {
                dishes.RemoveAll(d => !(ctx?.IsCategory(d, category) ?? d.IsCategory(category)));
            }

            return dishes;
        }

        private static int ApplyDivisor(int raw, string condParam)
        {
            int divisor = PositiveIntParam(condParam, "div", 1);
            return divisor <= 1 ? raw : raw / divisor;
        }

        private static int PositiveIntParam(string param, string key, int defaultValue)
        {
            if (string.IsNullOrEmpty(param))
            {
                return defaultValue;
            }

            string prefix = key + ":";
            foreach (string raw in param.Split(';'))
            {
                string segment = raw.Trim();
                if (segment.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(segment.Substring(prefix.Length), out int value)
                    && value > 0)
                {
                    return value;
                }
            }

            return defaultValue;
        }

        internal static bool HasParam(string param, string token)
            => !string.IsNullOrEmpty(param)
               && param.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0;

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

        private static int CountSkillType(
            SkillRuleDef rule,
            GpTable board,
            GameplayDatabase db,
            DishInstance self,
            System.Func<DishInstance, int> countAsOf,
            ScoreContext ctx)
        {
            if (db == null || string.IsNullOrEmpty(rule.CondParam))
            {
                return 0;
            }

            string token = SkillTypeParamOf(rule.CondParam);
            if (!System.Enum.TryParse(token, ignoreCase: true, out SkillActionType actionType))
            {
                return 0;
            }

            var matched = new List<DishInstance>();
            foreach (DishInstance d in ConditionScopeDishes(rule, board, self, ctx))
            {
                if (HasSkillOfType(db, d, actionType))
                {
                    matched.Add(d);
                }
            }

            return CountByUnit(matched, rule.CondUnit, countAsOf);
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

            foreach (TransferredSkill transferred in dish.TransferredSkills)
            {
                if (transferred?.Rule?.ActionType == actionType)
                {
                    return true;
                }
            }

            return false;
        }

        // ---------- 分类（category）集合与参数 ----------

        /// <summary>从 condParam 取「分类/类型名」段（去除控制参数），如 "cake;tiers:3|5|8" → "cake"。</summary>
        public static string CategoryOf(string condParam)
        {
            if (string.IsNullOrEmpty(condParam))
            {
                return string.Empty;
            }

            foreach (string seg in condParam.Split(';'))
            {
                string s = seg.Trim();
                if (s.Length > 0
                    && !s.StartsWith("tiers:", System.StringComparison.OrdinalIgnoreCase)
                    && !s.StartsWith("div:", System.StringComparison.OrdinalIgnoreCase)
                    && !s.StartsWith("include:", System.StringComparison.OrdinalIgnoreCase)
                    && !s.StartsWith("source:", System.StringComparison.OrdinalIgnoreCase)
                    && !s.StartsWith("skilltype:", System.StringComparison.OrdinalIgnoreCase)
                    && !s.StartsWith("cat:", System.StringComparison.OrdinalIgnoreCase))
                {
                    return s;
                }
            }

            return string.Empty;
        }

        private static string CategoryParamOf(string condParam)
        {
            if (string.IsNullOrEmpty(condParam))
            {
                return string.Empty;
            }

            foreach (string raw in condParam.Split(';'))
            {
                string segment = raw.Trim();
                if (segment.StartsWith("cat:", System.StringComparison.OrdinalIgnoreCase))
                {
                    return segment.Substring("cat:".Length).Trim();
                }
            }

            return string.Empty;
        }

        private static string SkillTypeParamOf(string condParam)
        {
            if (string.IsNullOrEmpty(condParam))
            {
                return string.Empty;
            }

            foreach (string raw in condParam.Split(';'))
            {
                string segment = raw.Trim();
                if (segment.StartsWith("skilltype:", System.StringComparison.OrdinalIgnoreCase))
                {
                    return segment.Substring("skilltype:".Length).Trim();
                }
            }

            return CategoryOf(condParam);
        }

        /// <summary>餐桌上属于指定分类（如 cake）的全部菜（含自身若匹配）。</summary>
        public static List<DishInstance> CategoryDishes(GpTable board, string category)
        {
            var result = new List<DishInstance>();
            if (string.IsNullOrEmpty(category))
            {
                return result;
            }

            foreach (DishInstance d in board.Dishes)
            {
                if (d.IsCategory(category))
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

        public static List<DishInstance> ScopeDishes(GpTable board, DishInstance self, SkillScope scope)
        {
            var result = new List<DishInstance>();
            switch (scope)
            {
                case SkillScope.Self:
                    result.Add(self);
                    return result;

                case SkillScope.Adjacent:
                    result.AddRange(board.GetAdjacentDishes(self));
                    return result;

                case SkillScope.Round:
                case SkillScope.RoundAndSelf:
                case SkillScope.Left:
                case SkillScope.Up:
                case SkillScope.Right:
                case SkillScope.Down:
                case SkillScope.LeftAndSelf:
                case SkillScope.UpAndSelf:
                case SkillScope.RightAndSelf:
                case SkillScope.DownAndSelf:
                    CollectDishesFromCells(board, ScopeCells(board, self, scope), result);
                    return result;

                case SkillScope.Row:
                case SkillScope.RowAndSelf:
                    CollectRowOrColumn(board, self, result, row: true, scope == SkillScope.RowAndSelf);
                    return result;

                case SkillScope.Column:
                case SkillScope.ColumnAndSelf:
                    CollectRowOrColumn(board, self, result, row: false, scope == SkillScope.ColumnAndSelf);
                    return result;

                case SkillScope.RowAndColumn:
                    CollectDishesFromCells(board, ScopeCells(board, self, scope), result);
                    result.RemoveAll(d => d.Id == self.Id);
                    return result;

                case SkillScope.Before:
                    foreach (DishInstance d in board.Dishes) if (d.Id < self.Id) result.Add(d);
                    return result;

                case SkillScope.After:
                    foreach (DishInstance d in board.Dishes) if (d.Id > self.Id) result.Add(d);
                    return result;

                case SkillScope.Other:
                    foreach (DishInstance d in board.Dishes)
                    {
                        if (d.Id != self.Id)
                        {
                            result.Add(d);
                        }
                    }
                    return result;

                case SkillScope.Edge:
                    foreach (DishInstance d in board.Dishes)
                    {
                        if (IsOnEdge(board, d)) result.Add(d);
                    }
                    return result;

                case SkillScope.CakeBuff:
                    return result;

                case SkillScope.All:
                default:
                    foreach (DishInstance d in board.Dishes)
                    {
                        result.Add(d);
                    }
                    return result;
            }
        }

        public static List<DishInstance> ScopeDishes(GpTable board, DishInstance self, SkillScope scope, bool includeSelf)
        {
            List<DishInstance> result = ScopeDishes(board, self, scope);
            bool hasSelf = result.Exists(d => d.Id == self.Id);
            if (includeSelf)
            {
                if (!hasSelf)
                {
                    result.Add(self);
                }
            }
            else if (hasSelf)
            {
                result.RemoveAll(d => d.Id == self.Id);
            }

            return result;
        }

        private static void CollectDishesFromCells(GpTable board, IEnumerable<GridPos> cells, List<DishInstance> result)
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

        private static void CollectRowOrColumn(GpTable board, DishInstance self, List<DishInstance> result, bool row, bool includeSelf)
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

        private static List<DishInstance> ServeOrderDishes(GpTable board, DishInstance self, SkillScope scope)
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
            if (unit == CountUnit.PhysicalInstances)
            {
                return dishes.Count;
            }

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
                if (d.Id != self.Id && d.Def.BaseId == self.Def.BaseId)
                {
                    count += unit == CountUnit.PhysicalInstances ? 1 : countAsOf(d);
                }
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
                if (SkillConditionParamParser.EvaluateComparison(rule.CondParam, d.OccupiedCells.Count, defaultValue: true))
                {
                    count += rule.CondUnit == CountUnit.PhysicalInstances ? 1 : countAsOf(d);
                }
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

            return SkillConditionParamParser.EvaluateComparison(rule.CondParam, value, value > 0) ? 1 : 0;
        }

        // ---------- 空格 / 填满 / 边缘 ----------

        private static int CountEmptyCells(GpTable board, DishInstance self, SkillScope scope)
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

        private static bool IsScopeFilled(GpTable board, DishInstance self, SkillScope scope)
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

        /// <summary>作用域涉及的「存在格」集合（同行/同列/相邻/周围/四方向及自身）。</summary>
        public static IEnumerable<GridPos> ScopeCells(GpTable board, DishInstance self, SkillScope scope)
        {
            var cells = new List<GridPos>();
            var seen = new HashSet<int>();
            void AddCell(GridPos p)
            {
                if (!board.Exists(p)) return;
                int key = p.Y * board.Width + p.X;
                if (seen.Add(key)) cells.Add(p);
            }

            void AddHorizontalDirection(bool left)
            {
                var edgeByRow = new Dictionary<int, int>();
                foreach (GridPos c in self.OccupiedCells)
                {
                    if (!edgeByRow.TryGetValue(c.Y, out int edge))
                    {
                        edgeByRow[c.Y] = c.X;
                    }
                    else
                    {
                        edgeByRow[c.Y] = left ? System.Math.Min(edge, c.X) : System.Math.Max(edge, c.X);
                    }
                }

                for (int y = 0; y < board.Height; y++)
                {
                    if (!edgeByRow.TryGetValue(y, out int edge))
                    {
                        continue;
                    }

                    int start = left ? 0 : edge + 1;
                    int end = left ? edge : board.Width;
                    for (int x = start; x < end; x++)
                    {
                        AddCell(new GridPos(x, y));
                    }
                }
            }

            void AddVerticalDirection(bool up)
            {
                var edgeByColumn = new Dictionary<int, int>();
                foreach (GridPos c in self.OccupiedCells)
                {
                    if (!edgeByColumn.TryGetValue(c.X, out int edge))
                    {
                        edgeByColumn[c.X] = c.Y;
                    }
                    else
                    {
                        edgeByColumn[c.X] = up ? System.Math.Min(edge, c.Y) : System.Math.Max(edge, c.Y);
                    }
                }

                for (int x = 0; x < board.Width; x++)
                {
                    if (!edgeByColumn.TryGetValue(x, out int edge))
                    {
                        continue;
                    }

                    int start = up ? 0 : edge + 1;
                    int end = up ? edge : board.Height;
                    for (int y = start; y < end; y++)
                    {
                        AddCell(new GridPos(x, y));
                    }
                }
            }

            switch (scope)
            {
                case SkillScope.Row:
                case SkillScope.RowAndSelf:
                {
                    var ys = new HashSet<int>();
                    foreach (GridPos c in self.OccupiedCells) ys.Add(c.Y);
                    foreach (int y in ys)
                        for (int x = 0; x < board.Width; x++) AddCell(new GridPos(x, y));
                    break;
                }

                case SkillScope.Column:
                case SkillScope.ColumnAndSelf:
                {
                    var xs = new HashSet<int>();
                    foreach (GridPos c in self.OccupiedCells) xs.Add(c.X);
                    foreach (int x in xs)
                        for (int y = 0; y < board.Height; y++) AddCell(new GridPos(x, y));
                    break;
                }

                case SkillScope.RowAndColumn:
                {
                    var xs = new HashSet<int>();
                    var ys = new HashSet<int>();
                    foreach (GridPos c in self.OccupiedCells)
                    {
                        xs.Add(c.X);
                        ys.Add(c.Y);
                    }

                    foreach (int y in ys)
                        for (int x = 0; x < board.Width; x++) AddCell(new GridPos(x, y));
                    foreach (int x in xs)
                        for (int y = 0; y < board.Height; y++) AddCell(new GridPos(x, y));
                    break;
                }

                case SkillScope.Round:
                case SkillScope.RoundAndSelf:
                {
                    var selfCells = new HashSet<int>();
                    foreach (GridPos c in self.OccupiedCells)
                    {
                        selfCells.Add(c.Y * board.Width + c.X);
                        if (scope == SkillScope.RoundAndSelf)
                        {
                            AddCell(c);
                        }
                    }
                    foreach (GridPos c in self.OccupiedCells)
                    {
                        TryNeighbor(board, selfCells, cells, seen, c.Offset(1, 0));
                        TryNeighbor(board, selfCells, cells, seen, c.Offset(-1, 0));
                        TryNeighbor(board, selfCells, cells, seen, c.Offset(0, 1));
                        TryNeighbor(board, selfCells, cells, seen, c.Offset(0, -1));
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

                case SkillScope.Left:
                    AddHorizontalDirection(left: true);
                    break;

                case SkillScope.LeftAndSelf:
                    AddHorizontalDirection(left: true);
                    foreach (GridPos cell in self.OccupiedCells) AddCell(cell);
                    break;

                case SkillScope.Up:
                    AddVerticalDirection(up: true);
                    break;

                case SkillScope.UpAndSelf:
                    AddVerticalDirection(up: true);
                    foreach (GridPos cell in self.OccupiedCells) AddCell(cell);
                    break;

                case SkillScope.Right:
                    AddHorizontalDirection(left: false);
                    break;

                case SkillScope.RightAndSelf:
                    AddHorizontalDirection(left: false);
                    foreach (GridPos cell in self.OccupiedCells) AddCell(cell);
                    break;

                case SkillScope.Down:
                    AddVerticalDirection(up: false);
                    break;

                case SkillScope.DownAndSelf:
                    AddVerticalDirection(up: false);
                    foreach (GridPos cell in self.OccupiedCells) AddCell(cell);
                    break;
            }

            return cells;
        }

        private static void TryNeighbor(GpTable board, HashSet<int> selfCells, List<GridPos> cells, HashSet<int> seen, GridPos p)
        {
            if (!board.Exists(p)) return;
            int key = p.Y * board.Width + p.X;
            if (selfCells.Contains(key)) return;
            if (seen.Add(key)) cells.Add(p);
        }

        /// <summary>
        /// 按棋盘实际轮廓判定边缘：菜品任一占用格的上、下、左、右四邻中，
        /// 只要存在一个「不存在格」（含越界），即视为处于边缘（闸门）。
        /// 禁用格仍属于餐桌轮廓，只影响摆放，不会在轮廓内部制造新的边缘。
        /// </summary>
        internal static bool IsOnEdge(GpTable board, DishInstance self)
        {
            if (board == null)
            {
                return false;
            }

            foreach (GridPos c in self.OccupiedCells)
            {
                if (!board.Exists(c.Offset(1, 0))
                    || !board.Exists(c.Offset(-1, 0))
                    || !board.Exists(c.Offset(0, 1))
                    || !board.Exists(c.Offset(0, -1)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
