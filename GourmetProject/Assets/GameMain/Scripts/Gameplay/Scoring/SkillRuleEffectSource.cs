using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>把带「前提×行为」规则的菜品技能转换为结算效果条目（OnSettle 触发）。</summary>
    public sealed class SkillRuleEffectSource : IScoreEffectSource
    {
        public void CollectEffects(ScoreSnapshot snapshot, ScoreEffectCollector collector)
        {
            foreach (DishInstance dish in snapshot.DishesInDefaultOrder)
            {
                if (dish.SkillsDisabled)
                {
                    continue;
                }

                int boardOrder = dish.Placement.Origin.Y * snapshot.DiningTable.Width + dish.Placement.Origin.X;
                foreach (string skillId in dish.SkillIds)
                {
                    SkillDef skill = snapshot.Db.GetSkill(skillId);
                    if (skill == null || !skill.HasRules)
                    {
                        continue;
                    }

                    string sourceLabel = dish.GetSkillSource(skillId);
                    ScoreSource source = string.IsNullOrEmpty(sourceLabel)
                        ? ScoreSource.DishSkill(skill, dish)
                        : ScoreSource.TransferredDishSkill(skill, dish, sourceLabel);

                    foreach (SkillRuleDef rule in skill.Rules)
                    {
                        if (rule.Trigger != SkillTrigger.OnSettle)
                        {
                            continue;
                        }

                        collector.Add(new ScoreEffectEntry(
                            ScorePhase.DishSkills,
                            source,
                            new SkillRuleEffect(rule, dish),
                            dish,
                            null,
                            null,
                            rule.Order,
                            boardOrder));
                    }
                }

                // 甜蜜传递获得的外来子技能：与目标自身技能一并结算（作用域相对目标计算，tips 显示来源标签）。
                foreach (TransferredSkill transferred in dish.TransferredSkills)
                {
                    SkillRuleDef rule = transferred.Effect.Rule;
                    if (rule == null || rule.Trigger != SkillTrigger.OnSettle)
                    {
                        continue;
                    }

                    SkillDef parent = snapshot.Db.GetSkill(rule.SkillId);
                    collector.Add(new ScoreEffectEntry(
                        ScorePhase.DishSkills,
                        ScoreSource.TransferredDishSkill(parent, dish, transferred.SourceLabel),
                        new SkillRuleEffect(rule, dish),
                        dish,
                        null,
                        null,
                        rule.Order,
                        boardOrder));
                }
            }
        }
    }

    /// <summary>单条技能规则的结算行为：先求前提 count，再按 count 派发行为到目标作用域。</summary>
    public sealed class SkillRuleEffect : IScoreEffect
    {
        private readonly SkillRuleDef _rule;
        private readonly DishInstance _self;

        public SkillRuleEffect(SkillRuleDef rule, DishInstance self)
        {
            _rule = rule;
            _self = self;
        }

        public void Apply(ScoreContext ctx)
        {
            int count = SkillConditionEvaluator.Evaluate(_rule, ctx, _self);
            if (count <= 0)
            {
                return;
            }

            Dispatch(ctx, count);
        }

        private void Dispatch(ScoreContext ctx, int count)
        {
            // 阶梯规则：count 为满足档序号（1-based），取对应 actionValue 并按「触发一次」应用。
            float value;
            if (IsTiered())
            {
                value = TierValue(count);
                count = 1;
            }
            else
            {
                value = _rule.ActionValue;
            }

            switch (_rule.ActionType)
            {
                case SkillActionType.AddFlat:
                    foreach (DishInstance t in Targets(ctx)) ctx.AddFlatTo(t, value * count);
                    break;

                case SkillActionType.AddMult:
                {
                    float factor = (float)Math.Pow(value, count);
                    foreach (DishInstance t in Targets(ctx)) ctx.MultiplyTo(t, factor);
                    break;
                }

                case SkillActionType.AddMultFlat:
                    foreach (DishInstance t in Targets(ctx)) ctx.AddMultFlatTo(t, value * count);
                    break;

                case SkillActionType.TransferScore:
                    foreach (DishInstance t in Targets(ctx)) ctx.TransferScore(_self, t, value);
                    break;

                case SkillActionType.ExtraSettlement:
                {
                    int times = (int)Math.Round(value * count, MidpointRounding.AwayFromZero);
                    foreach (DishInstance t in Targets(ctx)) ctx.AddExtraSettlement(t, times);
                    break;
                }

                case SkillActionType.AddLayer:
                {
                    // 全局欢乐蛋糕层数：无论作用域，统一改一次全局计数器。
                    bool mult = IsMultLayer(_rule);
                    ctx.AddHappyCakeLayers(mult ? value : value * count, mult, ParseFloor(_rule));
                    break;
                }

                case SkillActionType.ConsumeLayer:
                    ctx.AddHappyCakeLayers(-(value * count), mult: false);
                    break;

                case SkillActionType.TransferSkills:
                {
                    IReadOnlyList<SkillEffect> effects = EffectsToTransfer(ctx.Db, _rule);
                    if (effects.Count > 0)
                    {
                        foreach (DishInstance t in Targets(ctx))
                        {
                            if (t.Id == _self.Id)
                            {
                                continue;
                            }

                            IReadOnlyList<SkillEffect> newEffects = NewTransferredEffects(t, effects);
                            if (newEffects.Count == 0)
                            {
                                continue;
                            }

                            ctx.RecordSkillTransfer(t, newEffects, _self.Def.Name);
                            ResolveTransferredEffects(ctx, t, newEffects);
                        }
                    }
                    break;
                }

                case SkillActionType.GrantGold:
                    ctx.GrantGold(value * count);
                    break;

                case SkillActionType.PermanentAddFlat:
                    foreach (DishInstance t in Targets(ctx)) ctx.AddPermanentFlatTo(t, value * count);
                    break;

                case SkillActionType.PermanentAddMult:
                    foreach (DishInstance t in Targets(ctx)) ctx.AddPermanentMultTo(t, value);
                    break;

                case SkillActionType.AddCountAs:
                    // 「视为食物数」由 ScoreContext 结算前 live 预计算（ComputeLiveCountAs）当次生效，
                    // 此处不再作为延迟副作用累加，避免与 live 值重复计入。
                    break;

                case SkillActionType.None:
                default:
                    break;
            }
        }

        private bool IsTiered() => IsTiered(_rule);

        private float TierValue(int tier) => TierValue(_rule, tier);

        private static IReadOnlyList<SkillEffect> NewTransferredEffects(DishInstance target, IReadOnlyList<SkillEffect> effects)
        {
            var result = new List<SkillEffect>();
            foreach (SkillEffect effect in effects)
            {
                if (!HasTransferredRule(target, effect?.Rule))
                {
                    result.Add(effect);
                }
            }

            return result;
        }

        private static bool HasTransferredRule(DishInstance target, SkillRuleDef rule)
        {
            if (target == null || rule == null)
            {
                return false;
            }

            foreach (TransferredSkill transferred in target.TransferredSkills)
            {
                if (ReferenceEquals(transferred.Effect.Rule, rule))
                {
                    return true;
                }
            }

            return false;
        }

        private void ResolveTransferredEffects(ScoreContext ctx, DishInstance target, IReadOnlyList<SkillEffect> effects)
        {
            SkillDef parent = ctx.Db.GetSkill(_rule.SkillId);
            string sourceLabel = $"{_self.Def.Name}<甜蜜传递>";
            int boardOrder = target.Placement.Origin.Y * ctx.DiningTable.Width + target.Placement.Origin.X;
            foreach (SkillEffect effect in effects)
            {
                SkillRuleDef rule = effect.Rule;
                if (rule == null || rule.Trigger != SkillTrigger.OnSettle)
                {
                    continue;
                }

                var entry = new ScoreEffectEntry(
                    ScorePhase.DishSkills,
                    ScoreSource.TransferredDishSkill(parent, target, sourceLabel),
                    new SkillRuleEffect(rule, target),
                    target,
                    null,
                    null,
                    rule.Order,
                    boardOrder);
                ctx.SubmitCommand(new ResolveScoreEffectCommand(entry));
            }
        }

        /// <summary>规则是否为阶梯：condParam 含 tiers:…（阈值）且 actionParam 含 tiervals:…（各档值）。</summary>
        internal static bool IsTiered(SkillRuleDef rule)
            => rule.CondParam.IndexOf("tiers:", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>取满足档（1-based）对应的行为数值（来自 actionParam 的 tiervals:a|b|c）；越界钳到边界。</summary>
        internal static float TierValue(SkillRuleDef rule, int tier)
        {
            float[] values = ParseTierValues(rule.ActionParams);
            if (values == null || values.Length == 0)
            {
                return rule.ActionValue;
            }

            int i = tier - 1;
            if (i < 0) i = 0;
            if (i >= values.Length) i = values.Length - 1;
            return values[i];
        }

        private static float[] ParseTierValues(IReadOnlyList<string> actionParams)
        {
            foreach (string p in actionParams)
            {
                if (p == null) continue;
                int idx = p.IndexOf("tiervals:", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;

                string body = p.Substring(idx + "tiervals:".Length).Split(';')[0];
                string[] parts = body.Split('|', ',');
                var list = new List<float>();
                foreach (string s in parts)
                {
                    if (float.TryParse(s.Trim(), out float v)) list.Add(v);
                }

                return list.ToArray();
            }

            return null;
        }

        /// <summary>层数是否按乘法：任一 actionParam 以 mult 开头（含 multfloor:N）。</summary>
        private static bool IsMultLayer(SkillRuleDef rule)
        {
            foreach (string p in rule.ActionParams)
            {
                if (p != null && p.StartsWith("mult", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>解析层数乘法的「至少净增」下限，编码在 actionParam 的 floor:N（可与 mult 合写为 multfloor:N）。</summary>
        internal static int ParseFloor(SkillRuleDef rule)
        {
            foreach (string p in rule.ActionParams)
            {
                if (p == null)
                {
                    continue;
                }

                int idx = p.IndexOf("floor:", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0 && int.TryParse(p.Substring(idx + "floor:".Length), out int n))
                {
                    return n;
                }
            }

            return 0;
        }

        /// <summary>
        /// 甜蜜传递载荷：取传递子技能「所在 skill」内除传递外的所有子技能(rule)，配上各自描述片段。
        /// 目标获得后随其结算一并施加，作用域相对目标计算。
        /// </summary>
        internal static IReadOnlyList<SkillEffect> EffectsToTransfer(GameplayDatabase db, SkillRuleDef transferRule)
        {
            var result = new List<SkillEffect>();
            SkillDef parent = db.GetSkill(transferRule.SkillId);
            if (parent == null || !parent.HasRules)
            {
                return result;
            }

            for (int i = 0; i < parent.Rules.Count; i++)
            {
                SkillRuleDef rule = parent.Rules[i];
                if (rule.ActionType == SkillActionType.TransferSkills)
                {
                    continue;
                }

                string desc = i < parent.RuleDescs.Count ? parent.RuleDescs[i] : string.Empty;
                result.Add(new SkillEffect(rule, desc));
            }

            return result;
        }

        private IReadOnlyList<DishInstance> Targets(ScoreContext ctx)
        {
            if (_rule.ActionScope == SkillScope.Self)
            {
                return new[] { _self };
            }

            if (_rule.ActionScope == SkillScope.Category)
            {
                // 分类定向（如「所有蛋糕」）：取餐桌上匹配 cat:xxx 的全部菜（含自身若匹配）。
                return SkillConditionEvaluator.CategoryDishes(ctx.DiningTable, SkillConditionEvaluator.ParseCategoryParam(_rule.ActionParams));
            }

            List<DishInstance> dishes = SkillConditionEvaluator.ScopeDishes(ctx.DiningTable, _self, _rule.ActionScope);

            // skilltype:X 过滤：只作用于「带某行为类技能」的食物（巧克力「此类食物」= 带甜蜜传递的食物）。
            string skillTypeToken = ParseSkillTypeParam(_rule.ActionParams);
            if (!string.IsNullOrEmpty(skillTypeToken)
                && Enum.TryParse(skillTypeToken, ignoreCase: true, out SkillActionType filterType))
            {
                dishes = dishes.Where(d => HasSkillOfType(ctx.Db, d, filterType)).ToList();
            }

            if (_rule.ActionCount > 0 && dishes.Count > _rule.ActionCount)
            {
                // 无随机流时以餐桌顺序取前 N，保证确定性可复现。
                dishes = dishes
                    .OrderBy(d => d.Placement.Origin.Y)
                    .ThenBy(d => d.Placement.Origin.X)
                    .ThenBy(d => d.Id)
                    .Take(_rule.ActionCount)
                    .ToList();
            }

            return dishes;
        }

        private static string ParseSkillTypeParam(IReadOnlyList<string> actionParams)
        {
            foreach (string p in actionParams)
            {
                if (p == null) continue;
                int idx = p.IndexOf("skilltype:", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    return p.Substring(idx + "skilltype:".Length).Split(';', ',', '|')[0].Trim();
                }
            }

            return string.Empty;
        }

        private static bool HasSkillOfType(GameplayDatabase db, DishInstance dish, SkillActionType actionType)
        {
            if (db == null) return false;
            foreach (string skillId in dish.SkillIds)
            {
                SkillDef def = db.GetSkill(skillId);
                if (def == null || !def.HasRules) continue;
                for (int i = 0; i < def.Rules.Count; i++)
                {
                    if (def.Rules[i].ActionType == actionType) return true;
                }
            }

            return false;
        }
    }
}
