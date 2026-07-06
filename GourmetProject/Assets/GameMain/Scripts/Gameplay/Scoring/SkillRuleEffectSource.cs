using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GpBoard = GourmetProject.Gameplay.Board.Board;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>把带「前提×行为」规则的菜品技能转换为结算效果条目（OnSettle 触发）。</summary>
    public sealed class SkillRuleEffectSource : IScoreEffectSource
    {
        public void CollectEffects(ScoreSnapshot snapshot, ScoreEffectCollector collector)
        {
            foreach (DishInstance dish in snapshot.DishesInDefaultOrder)
            {
                int boardOrder = dish.Placement.Origin.Y * snapshot.Board.Width + dish.Placement.Origin.X;
                foreach (string skillId in dish.SkillIds)
                {
                    SkillDef skill = snapshot.Db.GetSkill(skillId);
                    if (skill == null || !skill.HasRules)
                    {
                        continue;
                    }

                    foreach (SkillRuleDef rule in skill.Rules)
                    {
                        if (rule.Trigger != SkillTrigger.OnSettle)
                        {
                            continue;
                        }

                        collector.Add(new ScoreEffectEntry(
                            ScorePhase.DishSkills,
                            ScoreSource.DishSkill(skill, dish),
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
            float value = _rule.ActionValue;
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
                    bool mult = _rule.HasActionParam("mult");
                    foreach (DishInstance t in Targets(ctx))
                        ctx.ChangeLayers(t, mult ? (float)Math.Pow(value, count) : value * count, mult);
                    break;
                }

                case SkillActionType.ConsumeLayer:
                    foreach (DishInstance t in Targets(ctx)) ctx.ChangeLayers(t, -(value * count), mult: false);
                    break;

                case SkillActionType.TransferSkills:
                {
                    IReadOnlyList<string> skills = SkillsToTransfer(ctx);
                    if (skills.Count > 0)
                    {
                        foreach (DishInstance t in Targets(ctx))
                        {
                            if (t.Id != _self.Id) ctx.RecordSkillTransfer(t, skills);
                        }
                    }
                    break;
                }

                case SkillActionType.GrantGold:
                    ctx.GrantGold(value * count);
                    break;

                case SkillActionType.None:
                default:
                    break;
            }
        }

        private IReadOnlyList<string> SkillsToTransfer(ScoreContext ctx)
        {
            bool keepTransfer = _rule.HasActionParam("keep_transfer");
            var result = new List<string>();
            foreach (string skillId in _self.SkillIds)
            {
                if (!keepTransfer && IsTransferSkill(ctx.Db, skillId))
                {
                    continue;
                }

                result.Add(skillId);
            }

            return result;
        }

        private static bool IsTransferSkill(GameplayDatabase db, string skillId)
        {
            SkillDef def = db.GetSkill(skillId);
            if (def == null || !def.HasRules)
            {
                return false;
            }

            for (int i = 0; i < def.Rules.Count; i++)
            {
                if (def.Rules[i].ActionType == SkillActionType.TransferSkills)
                {
                    return true;
                }
            }

            return false;
        }

        private IReadOnlyList<DishInstance> Targets(ScoreContext ctx)
        {
            if (_rule.ActionScope == SkillScope.Self)
            {
                return new[] { _self };
            }

            List<DishInstance> dishes = SkillConditionEvaluator.ScopeDishes(ctx.Board, _self, _rule.ActionScope, includeSelf: false);
            if (_rule.ActionCount > 0 && dishes.Count > _rule.ActionCount)
            {
                // 无随机流时以棋盘顺序取前 N，保证确定性可复现。
                dishes = dishes
                    .OrderBy(d => d.Placement.Origin.Y)
                    .ThenBy(d => d.Placement.Origin.X)
                    .ThenBy(d => d.Id)
                    .Take(_rule.ActionCount)
                    .ToList();
            }

            return dishes;
        }
    }
}
