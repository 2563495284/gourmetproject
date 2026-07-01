using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GpBoard = GourmetProject.Gameplay.Board.Board;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>
    /// 上菜时（OnServe）规则的轻量结算子流程：只处理刚上桌那道菜的 OnServe 规则。
    /// 上菜阶段没有分数累加器，只处理会直接改运行时状态的行为（层数/技能传递）与金币积累。
    /// </summary>
    public static class ServeRuleResolver
    {
        /// <summary>对刚上桌的实例执行其 OnServe 规则，返回本次上菜产生的金币增量。</summary>
        public static float ResolveOnServe(GpBoard board, GameplayDatabase db, IScoreHistory history, DishInstance served)
        {
            if (served == null)
            {
                return 0f;
            }

            float gold = 0f;
            foreach (string skillId in served.SkillIds)
            {
                SkillDef skill = db.GetSkill(skillId);
                if (skill == null || !skill.HasRules)
                {
                    continue;
                }

                foreach (SkillRuleDef rule in skill.Rules)
                {
                    if (rule.Trigger != SkillTrigger.OnServe)
                    {
                        continue;
                    }

                    int count = SkillConditionEvaluator.Evaluate(rule, board, history, served);
                    if (count <= 0)
                    {
                        continue;
                    }

                    gold += ApplyServeAction(board, db, rule, served, count);
                }
            }

            return gold;
        }

        private static float ApplyServeAction(GpBoard board, GameplayDatabase db, SkillRuleDef rule, DishInstance self, int count)
        {
            float value = rule.ActionValue;
            switch (rule.ActionType)
            {
                case SkillActionType.AddLayer:
                {
                    bool mult = rule.HasActionParam("mult");
                    foreach (DishInstance t in Targets(board, self, rule))
                    {
                        if (mult)
                        {
                            t.SetLayers((int)System.Math.Round(t.Layers * System.Math.Pow(value, count), System.MidpointRounding.AwayFromZero));
                        }
                        else
                        {
                            t.AddLayers((int)(value * count));
                        }
                    }

                    return 0f;
                }

                case SkillActionType.ConsumeLayer:
                    foreach (DishInstance t in Targets(board, self, rule)) t.AddLayers(-(int)(value * count));
                    return 0f;

                case SkillActionType.TransferSkills:
                {
                    List<string> skills = SkillsToTransfer(db, self, rule);
                    foreach (DishInstance t in Targets(board, self, rule))
                    {
                        if (t.Id == self.Id) continue;
                        foreach (string s in skills) t.AddSkill(s);
                    }

                    return 0f;
                }

                case SkillActionType.GrantGold:
                    return value * count;

                default:
                    // 分数类行为在上菜阶段无意义（无累加器），忽略。
                    return 0f;
            }
        }

        private static List<string> SkillsToTransfer(GameplayDatabase db, DishInstance self, SkillRuleDef rule)
        {
            bool keepTransfer = rule.HasActionParam("keep_transfer");
            var result = new List<string>();
            foreach (string skillId in self.SkillIds)
            {
                if (!keepTransfer && IsTransferSkill(db, skillId)) continue;
                result.Add(skillId);
            }

            return result;
        }

        private static bool IsTransferSkill(GameplayDatabase db, string skillId)
        {
            SkillDef def = db.GetSkill(skillId);
            if (def == null || !def.HasRules) return false;
            for (int i = 0; i < def.Rules.Count; i++)
            {
                if (def.Rules[i].ActionType == SkillActionType.TransferSkills) return true;
            }

            return false;
        }

        private static IReadOnlyList<DishInstance> Targets(GpBoard board, DishInstance self, SkillRuleDef rule)
        {
            if (rule.ActionScope == SkillScope.Self)
            {
                return new[] { self };
            }

            return SkillConditionEvaluator.ScopeDishes(board, self, rule.ActionScope, includeSelf: false);
        }
    }
}
