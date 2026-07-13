using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>
    /// 上菜时（OnServe）规则的轻量结算子流程：只处理刚上桌那道菜的 OnServe 规则。
    /// 上菜阶段没有分数累加器，只处理会直接改运行时状态的行为（层数/技能传递）与金币积累。
    /// </summary>
    public static class ServeRuleResolver
    {
        /// <summary>上菜结算产物：金币/层数增量、技能复制请求、以及临时复制请求（均需 RNG 落地）。</summary>
        public readonly struct ServeResolveResult
        {
            public ServeResolveResult(
                float gold, int happyCakeLayerDelta,
                IReadOnlyList<CopySkillRequest> copySkillRequests,
                IReadOnlyList<int> tempCopySourceIds,
                IReadOnlyList<SkillTransferRequest> transferRequests)
            {
                Gold = gold;
                HappyCakeLayerDelta = happyCakeLayerDelta;
                CopySkillRequests = copySkillRequests ?? System.Array.Empty<CopySkillRequest>();
                TempCopySourceIds = tempCopySourceIds ?? System.Array.Empty<int>();
                TransferRequests = transferRequests ?? System.Array.Empty<SkillTransferRequest>();
            }

            public float Gold { get; }

            public int HappyCakeLayerDelta { get; }

            /// <summary>技能复制请求：由 BattleSession 用注入的随机流从候选池挑选并加到目标实例。</summary>
            public IReadOnlyList<CopySkillRequest> CopySkillRequests { get; }

            /// <summary>临时复制请求：需被克隆到空格的源实例 Id（BattleSession 用随机流找空格落地）。</summary>
            public IReadOnlyList<int> TempCopySourceIds { get; }

            /// <summary>甜蜜传递请求：由 BattleSession 用随机流在候选目标中均权取 N 个并追加技能（带来源标签）。</summary>
            public IReadOnlyList<SkillTransferRequest> TransferRequests { get; }
        }

        /// <summary>对刚上桌的实例执行其 OnServe 规则，返回本次上菜产生的金币/全局层数增量与技能复制请求。</summary>
        public static ServeResolveResult ResolveOnServe(
            GpTable board, GameplayDatabase db, IScoreHistory history, DishInstance served, int currentHappyCakeLayers)
        {
            if (served == null)
            {
                return new ServeResolveResult(0f, 0, null, null, null);
            }

            float gold = 0f;
            int layerDelta = 0;
            List<CopySkillRequest> copyRequests = null;
            List<int> tempCopyIds = null;
            List<SkillTransferRequest> transferRequests = null;
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

                    int running = System.Math.Max(0, currentHappyCakeLayers + layerDelta);
                    int count = SkillConditionEvaluator.Evaluate(rule, board, history, served, running);
                    if (count <= 0)
                    {
                        continue;
                    }

                    ApplyServeAction(board, db, history, rule, served, count, running, ref gold, ref layerDelta, ref copyRequests, ref tempCopyIds, ref transferRequests);
                }
            }

            return new ServeResolveResult(gold, layerDelta, copyRequests, tempCopyIds, transferRequests);
        }

        private static void ApplyServeAction(
            GpTable board, GameplayDatabase db, IScoreHistory history, SkillRuleDef rule, DishInstance self, int count,
            int runningLayers, ref float gold, ref int layerDelta, ref List<CopySkillRequest> copyRequests,
            ref List<int> tempCopyIds, ref List<SkillTransferRequest> transferRequests)
        {
            // 阶梯规则：count 为满足档序号，取对应档值并按触发一次应用。
            float value;
            if (SkillRuleEffect.IsTiered(rule))
            {
                value = SkillRuleEffect.TierValue(rule, count);
                count = 1;
            }
            else
            {
                value = rule.ActionValue;
            }

            switch (rule.ActionType)
            {
                case SkillActionType.AddLayer:
                {
                    bool mult = IsMultLayer(rule);
                    int before = runningLayers;
                    int after;
                    if (mult)
                    {
                        after = (int)System.Math.Round(before * value, System.MidpointRounding.AwayFromZero);
                        int floor = SkillRuleEffect.ParseFloor(rule);
                        if (floor > 0 && after < before + floor)
                        {
                            after = before + floor;
                        }
                    }
                    else
                    {
                        after = before + (int)(value * count);
                    }

                    if (after < 0) after = 0;
                    layerDelta += after - before;
                    break;
                }

                case SkillActionType.ConsumeLayer:
                {
                    int before = runningLayers;
                    int after = System.Math.Max(0, before - (int)(value * count));
                    layerDelta += after - before;
                    break;
                }

                case SkillActionType.TransferSkills:
                {
                    // 甜蜜传递：把「本子技能所在 skill 内的其它子技能」打包为外来子技能载荷，
                    // 收集候选（作用域内有食物的其它菜），落地随机取 N 由 BattleSession 用 RNG 执行。
                    IReadOnlyList<SkillEffect> effects = SkillRuleEffect.EffectsToTransfer(db, rule);
                    if (effects.Count > 0)
                    {
                        var candidateIds = new List<int>();
                        foreach (DishInstance t in ScopeDishesForTransfer(board, self, rule))
                        {
                            if (t.Id != self.Id) candidateIds.Add(t.Id);
                        }

                        if (candidateIds.Count > 0)
                        {
                            transferRequests ??= new List<SkillTransferRequest>();
                            transferRequests.Add(new SkillTransferRequest(self.Id, self.Def.Name, candidateIds, effects, rule.ActionCount));
                        }
                    }

                    break;
                }

                case SkillActionType.TriggerSweetTransfer:
                {
                    foreach (DishInstance source in TriggerTransferSources(board, db, self, rule))
                    {
                        AppendTransferRequestsFromSource(board, db, history, source, runningLayers, ref transferRequests);
                    }

                    break;
                }

                case SkillActionType.GrantGold:
                    gold += value * count;
                    break;

                case SkillActionType.TempCopyDish:
                {
                    // 临时复制本菜品至空格：克隆源实例，避免临时克隆再触发临时复制。
                    if (!self.IsTemporary)
                    {
                        tempCopyIds ??= new List<int>();
                        tempCopyIds.Add(self.Id);
                    }

                    break;
                }

                case SkillActionType.CopySkill:
                {
                    List<string> candidates = BuildCopyCandidates(board, db, rule, self);
                    if (candidates.Count > 0)
                    {
                        int n = System.Math.Max(1, (int)System.Math.Round(value, System.MidpointRounding.AwayFromZero));
                        copyRequests ??= new List<CopySkillRequest>();
                        copyRequests.Add(new CopySkillRequest(self.Id, candidates, n));
                    }

                    break;
                }

                default:
                    // 分数类行为在上菜阶段无意义（无累加器），忽略。
                    break;
            }
        }

        private static void AppendTransferRequestsFromSource(
            GpTable board,
            GameplayDatabase db,
            IScoreHistory history,
            DishInstance source,
            int runningLayers,
            ref List<SkillTransferRequest> transferRequests)
        {
            foreach (string skillId in source.SkillIds)
            {
                SkillDef skill = db.GetSkill(skillId);
                if (skill == null || !skill.HasRules)
                {
                    continue;
                }

                foreach (SkillRuleDef transferRule in skill.Rules)
                {
                    if (transferRule.Trigger != SkillTrigger.OnServe
                        || transferRule.ActionType != SkillActionType.TransferSkills)
                    {
                        continue;
                    }

                    int count = SkillConditionEvaluator.Evaluate(transferRule, board, history, source, runningLayers);
                    if (count <= 0)
                    {
                        continue;
                    }

                    IReadOnlyList<SkillEffect> effects = SkillRuleEffect.EffectsToTransfer(db, transferRule);
                    if (effects.Count == 0)
                    {
                        continue;
                    }

                    var candidateIds = new List<int>();
                    foreach (DishInstance target in ScopeDishesForTransfer(board, source, transferRule))
                    {
                        if (target.Id != source.Id)
                        {
                            candidateIds.Add(target.Id);
                        }
                    }

                    if (candidateIds.Count == 0)
                    {
                        continue;
                    }

                    transferRequests ??= new List<SkillTransferRequest>();
                    transferRequests.Add(new SkillTransferRequest(source.Id, source.Def.Name, candidateIds, effects, transferRule.ActionCount));
                }
            }
        }

        private static IReadOnlyList<DishInstance> TriggerTransferSources(
            GpTable board,
            GameplayDatabase db,
            DishInstance self,
            SkillRuleDef rule)
        {
            var result = new List<DishInstance>();
            var seen = new HashSet<int>();
            void Add(DishInstance dish)
            {
                if (dish == null || dish.Id == self.Id || !seen.Add(dish.Id))
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

                return result;
            }

            foreach (DishInstance dish in SkillConditionEvaluator.ScopeDishes(board, self, rule.ActionScope, includeSelf: false))
            {
                Add(dish);
            }

            return result;
        }

        /// <summary>
        /// 技能复制的候选池：actionParam 含 cat:xxx 时取该分类全部菜品定义的技能（如「蛋糕技能」）；
        /// 否则取作用域内其它实例的运行时技能。剔除自身已有技能与复制类技能（避免复制「复制」造成循环）。
        /// </summary>
        private static List<string> BuildCopyCandidates(GpTable board, GameplayDatabase db, SkillRuleDef rule, DishInstance self)
        {
            var candidates = new List<string>();
            void Add(string s)
            {
                if (!string.IsNullOrEmpty(s) && !candidates.Contains(s)) candidates.Add(s);
            }

            string category = SkillConditionEvaluator.ParseCategoryParam(rule.ActionParams);
            if (!string.IsNullOrEmpty(category))
            {
                foreach (DishDef def in db.AllDishes)
                {
                    if (def.IsCategory(category))
                    {
                        foreach (string s in def.SkillIds) Add(s);
                    }
                }
            }
            else
            {
                foreach (DishInstance t in Targets(board, self, rule))
                {
                    if (t.Id == self.Id) continue;
                    foreach (string s in t.SkillIds) Add(s);
                }
            }

            candidates.RemoveAll(s => self.SkillIds.Contains(s) || IsCopySkill(db, s));
            return candidates;
        }

        private static bool IsCopySkill(GameplayDatabase db, string skillId)
        {
            SkillDef def = db.GetSkill(skillId);
            if (def == null || !def.HasRules) return false;
            for (int i = 0; i < def.Rules.Count; i++)
            {
                if (def.Rules[i].ActionType == SkillActionType.CopySkill) return true;
            }

            return false;
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
            foreach (string param in rule.ActionParams)
            {
                if (param != null && param.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsMultLayer(SkillRuleDef rule)
        {
            foreach (string p in rule.ActionParams)
            {
                if (p != null && p.StartsWith("mult", System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>甜蜜传递的候选目标：作用域内「有食物」的其它菜（不做 ActionCount 截断，随机取 N 交由 BattleSession）。</summary>
        private static IReadOnlyList<DishInstance> ScopeDishesForTransfer(GpTable board, DishInstance self, SkillRuleDef rule)
        {
            if (rule.ActionScope == SkillScope.Self)
            {
                return System.Array.Empty<DishInstance>();
            }

            if (rule.ActionScope == SkillScope.Category)
            {
                return SkillConditionEvaluator.CategoryDishes(board, SkillConditionEvaluator.ParseCategoryParam(rule.ActionParams));
            }

            return SkillConditionEvaluator.ScopeDishes(board, self, rule.ActionScope, includeSelf: false);
        }

        private static IReadOnlyList<DishInstance> Targets(GpTable board, DishInstance self, SkillRuleDef rule)
        {
            if (rule.ActionScope == SkillScope.Self)
            {
                return new[] { self };
            }

            if (rule.ActionScope == SkillScope.Category)
            {
                return SkillConditionEvaluator.CategoryDishes(board, SkillConditionEvaluator.ParseCategoryParam(rule.ActionParams));
            }

            List<DishInstance> dishes = SkillConditionEvaluator.ScopeDishes(board, self, rule.ActionScope, includeSelf: false);
            if (rule.ActionCount > 0 && dishes.Count > rule.ActionCount)
            {
                // 与 SkillRuleEffect 一致：无随机流时以餐桌顺序取前 N，保证确定性可复现。
                dishes = dishes
                    .OrderBy(d => d.Placement.Origin.Y)
                    .ThenBy(d => d.Placement.Origin.X)
                    .ThenBy(d => d.Id)
                    .Take(rule.ActionCount)
                    .ToList();
            }

            return dishes;
        }
    }

    /// <summary>技能复制请求：把 Candidates 中随机 Count 个技能加到目标实例。RNG 落地由 BattleSession 执行。</summary>
    public sealed class CopySkillRequest
    {
        public CopySkillRequest(int targetInstanceId, IReadOnlyList<string> candidates, int count)
        {
            TargetInstanceId = targetInstanceId;
            Candidates = candidates ?? System.Array.Empty<string>();
            Count = count;
        }

        public int TargetInstanceId { get; }

        public IReadOnlyList<string> Candidates { get; }

        public int Count { get; }
    }

    /// <summary>
    /// 甜蜜传递请求：把外来子技能(Effects) 追加给候选目标中随机 Count 个（0=全部）实例，并标注来源。RNG 落地由 BattleSession 执行。
    /// </summary>
    public sealed class SkillTransferRequest
    {
        public SkillTransferRequest(int sourceInstanceId, string sourceName, IReadOnlyList<int> candidateTargetIds, IReadOnlyList<SkillEffect> effects, int count)
        {
            SourceInstanceId = sourceInstanceId;
            SourceName = sourceName ?? string.Empty;
            CandidateTargetIds = candidateTargetIds ?? System.Array.Empty<int>();
            Effects = effects ?? System.Array.Empty<SkillEffect>();
            Count = count;
        }

        public int SourceInstanceId { get; }

        public string SourceName { get; }

        public IReadOnlyList<int> CandidateTargetIds { get; }

        public IReadOnlyList<SkillEffect> Effects { get; }

        /// <summary>随机取的目标数（0=全部候选）。</summary>
        public int Count { get; }
    }
}
