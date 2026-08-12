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
        /// <summary>上菜结算产物：金币/层数增量、技能复制请求与甜蜜传递请求。</summary>
        public readonly struct ServeResolveResult
        {
            public ServeResolveResult(
                float gold, int happyCakeLayerDelta,
                IReadOnlyList<CopySkillRequest> copySkillRequests,
                IReadOnlyList<SkillTransferRequest> transferRequests)
            {
                Gold = gold;
                HappyCakeLayerDelta = happyCakeLayerDelta;
                CopySkillRequests = copySkillRequests ?? System.Array.Empty<CopySkillRequest>();
                TransferRequests = transferRequests ?? System.Array.Empty<SkillTransferRequest>();
            }

            public float Gold { get; }

            public int HappyCakeLayerDelta { get; }

            /// <summary>技能复制请求：由 BattleSession 用注入的随机流从候选池挑选并加到目标实例。</summary>
            public IReadOnlyList<CopySkillRequest> CopySkillRequests { get; }

            /// <summary>甜蜜传递请求：由 BattleSession 用随机流在候选目标中均权取 N 个并追加技能（带来源标签）。</summary>
            public IReadOnlyList<SkillTransferRequest> TransferRequests { get; }
        }

        /// <summary>对刚上桌的实例执行其 OnServe 规则，返回本次上菜产生的金币/全局层数增量与技能复制请求。</summary>
        public static ServeResolveResult ResolveOnServe(
            GpTable board,
            GameplayDatabase db,
            IScoreHistory history,
            DishInstance served,
            int currentHappyCakeLayers,
            int itemExtraTargetCount = 0)
        {
            if (served == null)
            {
                return new ServeResolveResult(0f, 0, null, null);
            }

            float gold = 0f;
            int layerDelta = 0;
            List<CopySkillRequest> copyRequests = null;
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
                    int count = SkillConditionEvaluator.Evaluate(rule, board, history, served, running, db);
                    if (count <= 0)
                    {
                        continue;
                    }

                    string sourceName = ScoreSource.DishSkillSourceName(
                        served,
                        served.GetSkillSource(skillId),
                        skill.Name);
                    ApplyServeAction(
                        board,
                        db,
                        history,
                        rule,
                        served,
                        sourceName,
                        count,
                        running,
                        itemExtraTargetCount,
                        ref gold,
                        ref layerDelta,
                        ref copyRequests,
                        ref transferRequests);
                }
            }

            return new ServeResolveResult(gold, layerDelta, copyRequests, transferRequests);
        }

        private static void ApplyServeAction(
            GpTable board, GameplayDatabase db, IScoreHistory history, SkillRuleDef rule, DishInstance self,
            string sourceName, int count,
            int runningLayers, int itemExtraTargetCount,
            ref float gold, ref int layerDelta, ref List<CopySkillRequest> copyRequests,
            ref List<SkillTransferRequest> transferRequests)
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

            if (IsSweetTransferModifier(rule))
            {
                return;
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
                    // 收集全场其它食物作为候选，落地随机取 N 由 BattleSession 用 RNG 执行。
                    IReadOnlyList<SkillEffect> effects = SkillRuleEffect.EffectsToTransfer(db, rule);
                    if (effects.Count > 0)
                    {
                        var candidateIds = new List<int>();
                        foreach (DishInstance t in ScopeDishesForTransfer(board, db, self, rule))
                        {
                            if (t.Id != self.Id) candidateIds.Add(t.Id);
                        }

                        if (candidateIds.Count > 0)
                        {
                            int effectiveTargetCount = EffectiveTransferTargetCount(
                                rule.ActionCount,
                                SweetTransferExtraTargetCount(
                                    board,
                                    db,
                                    history,
                                    self,
                                    runningLayers,
                                    rule.Trigger) + System.Math.Max(0, itemExtraTargetCount));
                            transferRequests ??= new List<SkillTransferRequest>();
                            transferRequests.Add(new SkillTransferRequest(
                                self.Id,
                                sourceName,
                                candidateIds,
                                effects,
                                effectiveTargetCount));
                        }
                    }

                    break;
                }

                case SkillActionType.TriggerSweetTransfer:
                {
                    foreach (DishInstance source in TriggerTransferSources(board, db, self, rule))
                    {
                        AppendTransferRequestsFromSource(
                            board,
                            db,
                            history,
                            source,
                            sourceName,
                            runningLayers,
                            itemExtraTargetCount,
                            ref transferRequests);
                    }

                    break;
                }

                case SkillActionType.GrantGold:
                    gold += value * count;
                    break;

                case SkillActionType.CopySkill:
                {
                    List<string> candidates = BuildCopyCandidates(board, db, rule, self);
                    if (candidates.Count > 0)
                    {
                        int n = System.Math.Max(1, (int)System.Math.Round(value, System.MidpointRounding.AwayFromZero));
                        copyRequests ??= new List<CopySkillRequest>();
                        copyRequests.Add(new CopySkillRequest(self.Id, candidates, n, sourceName));
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
            string sourceName,
            int runningLayers,
            int itemExtraTargetCount,
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

                    int count = SkillConditionEvaluator.Evaluate(
                        transferRule,
                        board,
                        history,
                        source,
                        runningLayers,
                        db);
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
                    foreach (DishInstance target in ScopeDishesForTransfer(board, db, source, transferRule))
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

                    int effectiveTargetCount = EffectiveTransferTargetCount(
                        transferRule.ActionCount,
                        SweetTransferExtraTargetCount(
                            board,
                            db,
                            history,
                            source,
                            runningLayers,
                            transferRule.Trigger) + System.Math.Max(0, itemExtraTargetCount));
                    transferRequests ??= new List<SkillTransferRequest>();
                    transferRequests.Add(new SkillTransferRequest(
                        source.Id,
                        sourceName,
                        candidateIds,
                        effects,
                        effectiveTargetCount));
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
        /// 技能复制的候选池：actionParam 含 cat:xxx 时取该分类全部食物定义的技能（如「蛋糕技能」）；
        /// 否则取作用域内其它实例的运行时技能。剔除自身已有技能与复制类技能（避免复制「复制」造成循环）。
        /// </summary>
        internal static List<string> BuildCopyCandidates(GpTable board, GameplayDatabase db, SkillRuleDef rule, DishInstance self)
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
                foreach (DishInstance t in Targets(board, db, self, rule))
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

            foreach (TransferredSkill transferred in dish.TransferredSkills)
            {
                if (transferred?.Rule?.ActionType == actionType)
                {
                    return true;
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

        /// <summary>甜蜜传递的候选目标：全场其它食物（不做 ActionCount 截断，随机取 N 交由 BattleSession）。</summary>
        private static IReadOnlyList<DishInstance> ScopeDishesForTransfer(
            GpTable board,
            GameplayDatabase db,
            DishInstance self,
            SkillRuleDef rule)
        {
            return SkillScopeResolver.ResolveActionTargetDishes(
                db,
                board,
                self,
                rule,
                SkillScopeVisualMode.CandidateScope);
        }

        private static IReadOnlyList<DishInstance> Targets(
            GpTable board,
            GameplayDatabase db,
            DishInstance self,
            SkillRuleDef rule)
        {
            return SkillScopeResolver.ResolveActionTargetDishes(
                db,
                board,
                self,
                rule,
                SkillScopeVisualMode.ResolvedTargets);
        }

        private static int SweetTransferExtraTargetCount(
            GpTable board,
            GameplayDatabase db,
            IScoreHistory history,
            DishInstance source,
            int runningLayers,
            SkillTrigger trigger)
        {
            int extra = 0;
            foreach (DishInstance owner in board.Dishes)
            {
                if (owner == null || owner.SkillsDisabled)
                {
                    continue;
                }

                foreach (SkillRuleDef modifier in RulesOf(db, owner))
                {
                    if (modifier == null
                        || modifier.Trigger != trigger
                        || modifier.ActionType != SkillActionType.TriggerSweetTransfer
                        || !HasActionParam(modifier, "modifier:add-targets"))
                    {
                        continue;
                    }

                    IReadOnlyList<DishInstance> targets =
                        SkillScopeResolver.ResolveActionTargetDishes(
                            db,
                            board,
                            owner,
                            modifier,
                            SkillScopeVisualMode.ResolvedTargets);
                    if (targets.All(dish => dish.Id != source.Id))
                    {
                        continue;
                    }

                    int count = SkillConditionEvaluator.Evaluate(
                        modifier,
                        board,
                        history,
                        owner,
                        runningLayers,
                        db);
                    if (count > 0)
                    {
                        extra += System.Math.Max(
                            0,
                            (int)System.Math.Round(
                                modifier.ActionValue * count,
                                System.MidpointRounding.AwayFromZero));
                    }
                }
            }

            return extra;
        }

        private static int EffectiveTransferTargetCount(int configured, int extra)
            => configured <= 0 ? 0 : configured + System.Math.Max(0, extra);

        private static IEnumerable<SkillRuleDef> RulesOf(GameplayDatabase db, DishInstance dish)
        {
            foreach (string skillId in dish.SkillIds)
            {
                SkillDef skill = db?.GetSkill(skillId);
                if (skill == null || !skill.HasRules)
                {
                    continue;
                }

                foreach (SkillRuleDef rule in skill.Rules)
                {
                    yield return rule;
                }
            }

            foreach (TransferredSkill transferred in dish.TransferredSkills)
            {
                if (transferred?.Rule != null)
                {
                    yield return transferred.Rule;
                }
            }
        }

        private static bool IsSweetTransferModifier(SkillRuleDef rule)
            => HasActionParam(rule, "when:transfer")
               || HasActionParam(rule, "modifier:add-targets");
    }

    /// <summary>技能复制请求：把 Candidates 中随机 Count 个技能加到目标实例。RNG 落地由 BattleSession 执行。</summary>
    public sealed class CopySkillRequest
    {
        public CopySkillRequest(
            int targetInstanceId,
            IReadOnlyList<string> candidates,
            int count,
            string sourceName = null,
            IReadOnlyList<string> selectedSkillIds = null)
        {
            TargetInstanceId = targetInstanceId;
            Candidates = candidates ?? System.Array.Empty<string>();
            Count = count;
            SourceName = sourceName ?? string.Empty;
            SelectedSkillIds = selectedSkillIds ?? System.Array.Empty<string>();
        }

        public int TargetInstanceId { get; }

        public IReadOnlyList<string> Candidates { get; }

        public int Count { get; }

        public string SourceName { get; }

        /// <summary>结算阶段已选中的具体技能。为空时由 BattleSession 落地时随机选择（上菜阶段请求）。</summary>
        public IReadOnlyList<string> SelectedSkillIds { get; }
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
