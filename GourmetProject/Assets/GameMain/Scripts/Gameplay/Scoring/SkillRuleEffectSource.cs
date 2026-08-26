using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BreakInfinity;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>把带「前提×行为」规则的食物技能转换为结算效果条目（OnSettle 触发）。</summary>
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
                            boardOrder,
                            snapshot.CaptureDiagnostics
                                ? SkillExecutionTrace.Create(
                                    snapshot.Db,
                                    snapshot.DiningTable,
                                    dish,
                                    dish,
                                    skill,
                                    rule,
                                    TraceKindForSourceLabel(sourceLabel),
                                    sourceLabel,
                                    SkillScopeVisualMode.ResolvedTargets)
                                : null));
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
                    DishInstance owner = FindDish(snapshot.DiningTable, transferred.SourceInstanceId);
                    collector.Add(new ScoreEffectEntry(
                        ScorePhase.DishSkills,
                        ScoreSource.TransferredDishSkill(parent, dish, transferred.SourceLabel),
                        new SkillRuleEffect(rule, dish),
                        dish,
                        null,
                        null,
                        rule.Order,
                        boardOrder,
                        !snapshot.CaptureDiagnostics
                            ? null
                            : owner != null
                                ? SkillExecutionTrace.Create(
                                    snapshot.Db,
                                    snapshot.DiningTable,
                                    owner,
                                    dish,
                                    parent,
                                    rule,
                                    SkillExecutionKind.SweetTransfer,
                                    transferred.SourceLabel,
                                    SkillScopeVisualMode.ResolvedTargets)
                                : SkillExecutionTrace.CreateWithOwnerFallback(
                                    snapshot.Db,
                                    snapshot.DiningTable,
                                    transferred.SourceInstanceId,
                                    SourceNameWithoutTag(transferred.SourceLabel),
                                    dish,
                                    parent,
                                    rule,
                                    SkillExecutionKind.SweetTransfer,
                                    transferred.SourceLabel,
                                    SkillScopeVisualMode.ResolvedTargets)));
                }
            }
        }

        internal static SkillExecutionKind TraceKindForSourceLabel(string sourceLabel)
        {
            if (string.IsNullOrEmpty(sourceLabel))
            {
                return SkillExecutionKind.NativeSkill;
            }

            return sourceLabel.IndexOf("技能复制", StringComparison.OrdinalIgnoreCase) >= 0
                ? SkillExecutionKind.CopiedSkill
                : SkillExecutionKind.SweetTransfer;
        }

        private static DishInstance FindDish(GpTable board, int instanceId)
        {
            if (board == null || instanceId <= 0)
            {
                return null;
            }

            foreach (DishInstance dish in board.Dishes)
            {
                if (dish.Id == instanceId)
                {
                    return dish;
                }
            }

            return null;
        }

        private static string SourceNameWithoutTag(string sourceLabel)
        {
            if (string.IsNullOrEmpty(sourceLabel))
            {
                return string.Empty;
            }

            int index = sourceLabel.IndexOf('<');
            return index > 0 ? sourceLabel.Substring(0, index) : sourceLabel;
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
                if (count == 0 && _rule.CondMode == CountMode.Per)
                {
                    DispatchZeroCountScoreCue(ctx);
                }

                return;
            }

            Dispatch(ctx, count);
        }

        private void DispatchZeroCountScoreCue(ScoreContext ctx)
        {
            switch (_rule.ActionType)
            {
                case SkillActionType.AddFlat:
                {
                    float value = _rule.ActionValue * 0f;
                    foreach (DishInstance target in Targets(ctx))
                    {
                        ctx.AddFlatTo(target, value);
                    }

                    break;
                }

                case SkillActionType.AddMultFlat:
                    foreach (DishInstance target in Targets(ctx))
                    {
                        ctx.AddMultFlatTo(target, 0f);
                    }

                    break;

                case SkillActionType.AddMult:
                    foreach (DishInstance target in Targets(ctx))
                    {
                        ctx.MultiplyTo(target, 1f);
                    }

                    break;
            }
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

            // 甜蜜传递修饰器在自身真正轮到结算时，才把 Buff 挂到当前作用域目标。
            // 后续 TransferSkills 只读取已经登记的 Buff，从而严格遵守结算优先级。
            if (IsSweetTransferModifier(_rule))
            {
                ctx.RegisterSweetTransferBuff(_self, _rule, SweetTransferBuffTargets(ctx), count);
                return;
            }

            switch (_rule.ActionType)
            {
                case SkillActionType.AddFlat:
                    foreach (DishInstance t in Targets(ctx)) ctx.AddFlatTo(t, value * count);
                    break;

                case SkillActionType.AddMult:
                {
                    float factor = HasActionParam(_rule, "linear")
                        ? 1f + value * count
                        : (float)Math.Pow(value, count);
                    foreach (DishInstance t in Targets(ctx)) ctx.MultiplyTo(t, factor);
                    break;
                }

                case SkillActionType.AddMultFlat:
                    foreach (DishInstance t in Targets(ctx))
                    {
                        float amount = HasActionParam(_rule, "source:target-countas")
                            ? value * ctx.GetEffectiveCountAs(t)
                            : value * count;
                        ctx.AddMultFlatTo(t, amount);
                    }
                    break;

                case SkillActionType.AddCurrentMult:
                {
                    BigDouble sourceMultiplier = BigDouble.Zero;
                    foreach (DishInstance source in CurrentMultiplierSources(ctx))
                    {
                        sourceMultiplier += ctx.GetCurrentMultiplier(source);
                    }
                    sourceMultiplier *= count;
                    foreach (DishInstance t in Targets(ctx)) ctx.AddMultFlatTo(t, sourceMultiplier);
                    break;
                }

                case SkillActionType.AddCurrentScore:
                {
                    // 先读来源快照，再统一写目标；目标包含自身时不会改变后续目标获得的数值。
                    BigDouble sourceScore;
                    if (HasActionParam(_rule, "source:row-max"))
                    {
                        sourceScore = BigDouble.Zero;
                        foreach (DishInstance source in SkillConditionEvaluator.ScopeDishes(
                            ctx.DiningTable,
                            _self,
                            SkillScope.RowAndSelf))
                        {
                            BigDouble candidate = ctx.GetCurrentScore(source);
                            if (candidate > sourceScore)
                            {
                                sourceScore = candidate;
                            }
                        }
                    }
                    else
                    {
                        sourceScore = ctx.GetCurrentScore(_self);
                    }

                    sourceScore *= value * count;
                    foreach (DishInstance t in Targets(ctx)) ctx.AddFlatTo(t, sourceScore);
                    break;
                }

                case SkillActionType.AddLayer:
                {
                    // 全局欢乐蛋糕层数：无论作用域，统一改一次全局计数器。
                    bool mult = IsMultLayer(_rule);
                    if (TryParseRandomIntegerRange(_rule, out int min, out int max))
                    {
                        value = ctx.Snapshot.RandomIntegerSelector != null
                            ? ctx.Snapshot.RandomIntegerSelector(min, max)
                            : min;
                        count = 1;
                    }

                    float layerValue = mult ? value : value * count;
                    if (!mult && HasActionParam(_rule, "round:ceil"))
                    {
                        layerValue = (float)Math.Ceiling(layerValue);
                    }
                    ctx.AddHappyCakeLayers(layerValue, mult, ParseFloor(_rule));
                    break;
                }

                case SkillActionType.ConsumeLayer:
                    ctx.AddHappyCakeLayers(-(value * count), mult: false);
                    break;

                case SkillActionType.TransferSkills:
                {
                    ExecuteOneSweetTransfer(ctx);
                    break;
                }

                case SkillActionType.CopySkill:
                {
                    List<string> candidates = ServeRuleResolver.BuildCopyCandidates(ctx.DiningTable, ctx.Db, _rule, _self);
                    if (candidates.Count > 0)
                    {
                        int n = Math.Max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero));
                        ctx.RecordCopySkill(_self, candidates, n, CurrentSkillSourceName(ctx));
                    }

                    break;
                }

                case SkillActionType.TriggerSweetTransfer:
                {
                    // 只代执行来源的 TransferSkills 子技能：把其他可传递子技能交给目标，
                    // 不重跑来源技能中的加分/倍率等其他子技能。
                    IReadOnlyList<DishInstance> sources = SweetTransferSources(ctx);
                    ctx.RecordTriggerSweetTransfer(_self, sources);
                    for (int i = 0; i < sources.Count; i++)
                    {
                        DishInstance source = sources[i];
                        ctx.RecordTriggeredSweetTransferSource(source, i + 1, sources.Count);
                        ExecuteSweetTransfersFrom(ctx, source);
                    }

                    break;
                }

                case SkillActionType.GrantGold:
                    ctx.GrantGold(value * count);
                    break;

                case SkillActionType.PermanentAddFlat:
                    foreach (DishInstance t in Targets(ctx)) ctx.AddPermanentFlatTo(t, value * count);
                    break;

                case SkillActionType.AddCountAs:
                    // 所有子技能均主动触发，只在自身执行到时影响后续规则。
                    ctx.ApplyLiveCountAs(_rule, _self, count, value, Targets(ctx));
                    break;

                case SkillActionType.RequestRecipeRemoval:
                    ctx.RequestRecipeRemoval(_self, value * count);
                    break;

                case SkillActionType.AddEmptyCountAs:
                    ctx.AddEmptyCountAsPerCell(Math.Max(0, (int)Math.Round(value * count, MidpointRounding.AwayFromZero)));
                    break;

                case SkillActionType.AddTemporaryCategory:
                {
                    string category = SkillConditionEvaluator.ParseCategoryParam(_rule.ActionParams);
                    string categoryName = string.Equals(category, "cake", StringComparison.OrdinalIgnoreCase)
                        ? "蛋糕"
                        : category;
                    foreach (DishInstance target in Targets(ctx))
                    {
                        ctx.AddTemporaryCategory(
                            target,
                            category,
                            CurrentSkillSourceName(ctx),
                            $"视为{categoryName}");
                    }
                    break;
                }

                case SkillActionType.None:
                default:
                    break;
            }
        }

        private bool IsTiered() => IsTiered(_rule);

        private float TierValue(int tier) => TierValue(_rule, tier);

        /// <summary>执行一轮甜蜜传递：每次调用都会重新解析/随机目标。</summary>
        private void ExecuteOneSweetTransfer(ScoreContext ctx)
        {
            IReadOnlyList<SkillEffect> effects = EffectsToTransfer(ctx.Db, _rule);
            if (effects.Count == 0)
            {
                return;
            }

            IReadOnlyList<DishInstance> candidates = TransferCandidates(ctx);
            if (candidates.Count == 0)
            {
                ctx.RecordSweetTransferFailed(_self);
                return;
            }

            IReadOnlyList<SweetTransferBuffRegistration> buffs = ctx.SweetTransferBuffsFor(_self);
            int itemExtraTargetCount = Math.Max(0, ctx.Snapshot.SweetTransferExtraTargetCount);
            int fixedBuffExtraTargetCount = FixedExtraTargetCount(buffs);
            int chanceRollCount = _rule.ActionCount <= 0
                ? 0
                : _rule.ActionCount + itemExtraTargetCount + fixedBuffExtraTargetCount;
            IReadOnlyDictionary<SweetTransferBuffRegistration, int> resolvedBuffExtraTargets =
                ResolveBuffExtraTargetCounts(ctx, buffs, chanceRollCount);
            int extraTargetCount = itemExtraTargetCount + resolvedBuffExtraTargets.Values.Sum();
            IReadOnlyList<DishInstance> targets = SelectTransferTargets(ctx, candidates, extraTargetCount);
            if (targets.Count == 0)
            {
                ctx.RecordSweetTransferFailed(_self);
                return;
            }

            ApplyRegisteredSweetTransferBuffs(ctx, buffs, targets, resolvedBuffExtraTargets);

            string sourceName = CurrentSkillSourceName(ctx);
            int handoffExecutionGroupId = ctx.CurrentExecutionGroupId;
            foreach (DishInstance target in targets)
            {
                ctx.RecordSkillTransfer(target, effects, sourceName, _self.Id);
                ResolveTransferredEffects(
                    ctx,
                    target,
                    ctx.TransferredSkillsForCurrentCalculation(target),
                    _self.Id,
                    handoffExecutionGroupId,
                    _rule.SkillId,
                    effects.Count);
            }
        }

        private string CurrentSkillSourceName(ScoreContext ctx)
        {
            if (ctx?.Source?.Type == ScoreSourceType.DishSkill
                && !string.IsNullOrEmpty(ctx.Source.Name))
            {
                return ctx.Source.Name;
            }

            return _self?.Def?.Name ?? string.Empty;
        }

        private static void ExecuteSweetTransfersFrom(ScoreContext ctx, DishInstance source)
        {
            if (ctx?.Db == null || source == null || source.SkillsDisabled)
            {
                return;
            }

            foreach (string skillId in source.SkillIds)
            {
                SkillDef skill = ctx.Db.GetSkill(skillId);
                if (skill == null || !skill.HasRules)
                {
                    continue;
                }

                foreach (SkillRuleDef transferRule in skill.Rules)
                {
                    if (transferRule.Trigger == SkillTrigger.OnSettle
                        && transferRule.ActionType == SkillActionType.TransferSkills)
                    {
                        string sourceLabel = source.GetSkillSource(skillId);
                        ScoreSource scoreSource = string.IsNullOrEmpty(sourceLabel)
                            ? ScoreSource.DishSkill(skill, source)
                            : ScoreSource.TransferredDishSkill(skill, source, sourceLabel);
                        int boardOrder = source.Placement.Origin.Y * ctx.DiningTable.Width
                            + source.Placement.Origin.X;
                        var entry = new ScoreEffectEntry(
                            ScorePhase.DishSkills,
                            scoreSource,
                            new SkillRuleEffect(transferRule, source),
                            source,
                            null,
                            null,
                            transferRule.Order,
                            boardOrder,
                            ctx.CaptureDiagnostics
                                ? SkillExecutionTrace.Create(
                                    ctx.Db,
                                    ctx.DiningTable,
                                    source,
                                    source,
                                    skill,
                                    transferRule,
                                    SkillRuleEffectSource.TraceKindForSourceLabel(sourceLabel),
                                    sourceLabel,
                                    SkillScopeVisualMode.CandidateScope)
                                : null);
                        ctx.ResolveTransferredEffect(entry);
                    }
                }
            }
        }

        private IReadOnlyList<DishInstance> TransferCandidates(ScoreContext ctx)
        {
            IReadOnlyList<DishInstance> candidates = SkillScopeResolver.ResolveActionTargetDishes(
                ctx.Db,
                ctx.DiningTable,
                _self,
                _rule,
                SkillScopeVisualMode.CandidateScope,
                ctx.IsCategory);
            var result = new List<DishInstance>();
            var seen = new HashSet<int>();
            foreach (DishInstance dish in candidates)
            {
                if (dish != null && dish.Id != _self.Id && seen.Add(dish.Id))
                {
                    result.Add(dish);
                }
            }

            return result;
        }

        private IReadOnlyList<DishInstance> SelectTransferTargets(
            ScoreContext ctx,
            IReadOnlyList<DishInstance> candidates,
            int extraTargetCount)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return Array.Empty<DishInstance>();
            }

            var candidateIds = candidates.Select(dish => dish.Id).ToList();

            int requested = _rule.ActionCount <= 0
                ? candidateIds.Count
                : _rule.ActionCount + Math.Max(0, extraTargetCount);
            int count = Math.Min(requested, candidateIds.Count);
            IReadOnlyList<int> selectedIds = ctx.Snapshot.TransferTargetSelector != null
                ? ctx.Snapshot.TransferTargetSelector(candidateIds, count)
                : candidateIds.Take(count).ToArray();
            if (selectedIds == null || selectedIds.Count == 0)
            {
                return Array.Empty<DishInstance>();
            }

            var result = new List<DishInstance>();
            foreach (int selectedId in selectedIds)
            {
                if (selectedId == _self.Id || !candidateIds.Contains(selectedId) || result.Any(d => d.Id == selectedId))
                {
                    continue;
                }

                DishInstance dish = candidates.FirstOrDefault(d => d.Id == selectedId);
                if (dish != null)
                {
                    result.Add(dish);
                }

                if (result.Count >= count)
                {
                    break;
                }
            }

            return result;
        }

        private static int FixedExtraTargetCount(IReadOnlyList<SweetTransferBuffRegistration> buffs)
        {
            int extra = 0;
            if (buffs == null)
            {
                return extra;
            }

            foreach (SweetTransferBuffRegistration buff in buffs)
            {
                SkillRuleDef rule = buff?.Rule;
                if (rule == null
                    || rule.ActionType != SkillActionType.TriggerSweetTransfer
                    || !HasActionParam(rule, "modifier:add-targets")
                    || HasActionParam(rule, "chance:"))
                {
                    continue;
                }

                extra += Math.Max(
                    0,
                    (int)Math.Round(
                        rule.ActionValue * buff.ConditionCount,
                        MidpointRounding.AwayFromZero));
            }

            return extra;
        }

        private static IReadOnlyDictionary<SweetTransferBuffRegistration, int> ResolveBuffExtraTargetCounts(
            ScoreContext ctx,
            IReadOnlyList<SweetTransferBuffRegistration> buffs,
            int chanceRollCount)
        {
            var resolved = new Dictionary<SweetTransferBuffRegistration, int>();
            if (buffs == null)
            {
                return resolved;
            }

            foreach (SweetTransferBuffRegistration buff in buffs)
            {
                SkillRuleDef rule = buff?.Rule;
                if (rule == null
                    || rule.ActionType != SkillActionType.TriggerSweetTransfer
                    || !HasActionParam(rule, "modifier:add-targets"))
                {
                    continue;
                }

                int amountPerSuccess = Math.Max(
                    0,
                    (int)Math.Round(
                        rule.ActionValue * buff.ConditionCount,
                        MidpointRounding.AwayFromZero));
                if (!HasActionParam(rule, "chance:"))
                {
                    resolved[buff] = amountPerSuccess;
                    continue;
                }

                TryParseChance(rule, out float chance);
                int successExtra = 0;
                if (amountPerSuccess > 0
                    && chanceRollCount > 0
                    && ctx.Snapshot.RandomIntegerSelector != null)
                {
                    int threshold = Math.Max(
                        0,
                        Math.Min(10000, (int)Math.Round(chance * 10000f)));
                    for (int i = 0; i < chanceRollCount; i++)
                    {
                        if (ctx.Snapshot.RandomIntegerSelector(0, 9999) < threshold)
                        {
                            successExtra += amountPerSuccess;
                        }
                    }
                }

                resolved[buff] = successExtra;
            }

            return resolved;
        }

        private void ApplyRegisteredSweetTransferBuffs(
            ScoreContext ctx,
            IReadOnlyList<SweetTransferBuffRegistration> buffs,
            IReadOnlyList<DishInstance> transferTargets,
            IReadOnlyDictionary<SweetTransferBuffRegistration, int> resolvedBuffExtraTargets)
        {
            buffs ??= Array.Empty<SweetTransferBuffRegistration>();

            // 先表现已兑现的目标数修饰，再结算软糖倍率响应。
            foreach (SweetTransferBuffRegistration buff in buffs)
            {
                SkillRuleDef rule = buff?.Rule;
                if (rule == null
                    || rule.ActionType != SkillActionType.TriggerSweetTransfer
                    || !HasActionParam(rule, "modifier:add-targets"))
                {
                    continue;
                }

                int extra = resolvedBuffExtraTargets != null
                    && resolvedBuffExtraTargets.TryGetValue(buff, out int resolved)
                        ? resolved
                        : 0;
                if (extra <= 0)
                {
                    continue;
                }

                ResolveSweetTransferBuffTrigger(ctx, buff, transferTargets, extra, SkillActionType.None);
            }

            foreach (SweetTransferBuffRegistration buff in buffs)
            {
                SkillRuleDef rule = buff?.Rule;
                if (rule == null
                    || !HasActionParam(rule, "when:transfer")
                    || (rule.ActionType != SkillActionType.AddMult
                        && rule.ActionType != SkillActionType.AddMultFlat))
                {
                    continue;
                }

                IReadOnlyList<DishInstance> resultTargets;
                if (HasActionParam(rule, "resultscope:BuffTargets"))
                {
                    resultTargets = RegisteredBuffTargets(ctx, buff);
                }
                else if (HasActionParam(rule, "resultscope:TransferSource"))
                {
                    resultTargets = new[] { _self };
                }
                else
                {
                    resultTargets = SkillConditionEvaluator.ScopeDishes(
                        ctx.DiningTable,
                        buff.Owner,
                        ParseResultScope(rule, SkillScope.RowAndSelf));
                }
                float value = rule.ActionType == SkillActionType.AddMultFlat
                    ? rule.ActionValue * buff.ConditionCount
                    : HasActionParam(rule, "linear")
                        ? 1f + rule.ActionValue * buff.ConditionCount
                        : (float)Math.Pow(rule.ActionValue, buff.ConditionCount);
                ResolveSweetTransferBuffTrigger(ctx, buff, resultTargets, value, rule.ActionType);
            }

            foreach (SweetTransferBuffRegistration buff in ctx.SweetTransferReceiverBuffsFor(transferTargets))
            {
                SkillRuleDef rule = buff.Rule;
                if (rule.ActionType != SkillActionType.AddFlat)
                {
                    continue;
                }

                int receivedCount = transferTargets.Count(target =>
                    target != null && buff.TargetDishInstanceIds.Contains(target.Id));
                if (receivedCount <= 0)
                {
                    continue;
                }

                bool applyToBuffTargets = HasActionParam(rule, "resultscope:BuffTargets");
                IReadOnlyList<DishInstance> resultTargets = applyToBuffTargets
                    ? RegisteredBuffTargets(ctx, buff)
                    : transferTargets
                        .Where(target => buff.TargetDishInstanceIds.Contains(target.Id))
                        .ToArray();
                ResolveSweetTransferBuffTrigger(
                    ctx,
                    buff,
                    resultTargets,
                    rule.ActionValue * buff.ConditionCount * (applyToBuffTargets ? receivedCount : 1),
                    SkillActionType.AddFlat);
            }
        }

        /// <summary>
        /// 返回 Buff 登记时锁定的食物实例。范围不会随之后的移动或新上菜扩张，
        /// 但已不在餐桌上的实例不会继续获得收益。
        /// </summary>
        private static IReadOnlyList<DishInstance> RegisteredBuffTargets(
            ScoreContext ctx,
            SweetTransferBuffRegistration buff)
        {
            if (ctx?.DiningTable == null || buff == null || buff.TargetDishInstanceIds.Count == 0)
            {
                return Array.Empty<DishInstance>();
            }

            var targetIds = new HashSet<int>(buff.TargetDishInstanceIds);
            return ctx.DiningTable.Dishes
                .Where(target => target != null && targetIds.Contains(target.Id))
                .OrderBy(BoardTop)
                .ThenBy(BoardLeft)
                .ThenBy(target => target.Id)
                .ToArray();
        }

        private void ResolveSweetTransferBuffTrigger(
            ScoreContext ctx,
            SweetTransferBuffRegistration buff,
            IReadOnlyList<DishInstance> targets,
            float value,
            SkillActionType applyType)
        {
            if (ctx == null || buff?.Owner == null || buff.Rule == null)
            {
                return;
            }

            var targetList = targets?.Where(target => target != null).Distinct().ToArray()
                ?? Array.Empty<DishInstance>();
            var ids = targetList.Select(target => target.Id).ToArray();
            var cells = targetList.SelectMany(target => target.OccupiedCells).Distinct().ToArray();
            SkillExecutionTrace trace = ctx.CaptureDiagnostics
                ? buff.Trace?.WithRuntimeContext(buff.Owner, _self, ids, cells)
                : null;
            int boardOrder = buff.Owner.Placement.Origin.Y * ctx.DiningTable.Width
                + buff.Owner.Placement.Origin.X;
            var entry = new ScoreEffectEntry(
                ScorePhase.DishSkills,
                buff.Source,
                new SweetTransferBuffTriggerEffect(buff, _self, targetList, value, applyType),
                buff.Owner,
                null,
                null,
                buff.Rule.Order,
                boardOrder,
                trace);
            ctx.ResolveTransferredEffect(entry);
        }

        private static SkillScope ParseResultScope(SkillRuleDef rule, SkillScope fallback)
        {
            foreach (string param in rule.ActionParams)
            {
                if (string.IsNullOrEmpty(param))
                {
                    continue;
                }

                int index = param.IndexOf("resultscope:", StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    continue;
                }

                string value = param.Substring(index + "resultscope:".Length)
                    .Split(';', ',', '|')[0]
                    .Trim();
                if (Enum.TryParse(value, ignoreCase: true, out SkillScope scope))
                {
                    return scope;
                }
            }

            return fallback;
        }

        private static bool IsSweetTransferModifier(SkillRuleDef rule)
            => HasActionParam(rule, "when:transfer")
               || HasActionParam(rule, "when:receive-transfer")
               || HasActionParam(rule, "modifier:add-targets");

        private void ResolveTransferredEffects(
            ScoreContext ctx,
            DishInstance target,
            IReadOnlyList<TransferredSkill> transferredSkills,
            int handoffSourceDishInstanceId,
            int handoffExecutionGroupId,
            string handoffSkillId,
            int handoffPayloadCount)
        {
            int boardOrder = target.Placement.Origin.Y * ctx.DiningTable.Width + target.Placement.Origin.X;
            foreach (TransferredSkill transferred in transferredSkills)
            {
                SkillRuleDef rule = transferred?.Rule;
                if (rule == null || rule.Trigger != SkillTrigger.OnSettle)
                {
                    continue;
                }

                SkillDef parent = ctx.Db.GetSkill(rule.SkillId);
                string sourceLabel = transferred.SourceLabel;
                DishInstance owner = ctx.DiningTable.Dishes.FirstOrDefault(
                    dish => dish.Id == transferred.SourceInstanceId);
                SkillExecutionTrace trace = null;
                if (ctx.CaptureDiagnostics)
                {
                    trace = owner != null
                        ? SkillExecutionTrace.Create(
                            ctx.Db,
                            ctx.DiningTable,
                            owner,
                            target,
                            parent,
                            rule,
                            SkillExecutionKind.SweetTransfer,
                            sourceLabel,
                            SkillScopeVisualMode.ResolvedTargets)
                        : SkillExecutionTrace.CreateWithOwnerFallback(
                            ctx.Db,
                            ctx.DiningTable,
                            transferred.SourceInstanceId,
                            SourceNameWithoutTag(sourceLabel),
                            target,
                            parent,
                            rule,
                            SkillExecutionKind.SweetTransfer,
                            sourceLabel,
                            SkillScopeVisualMode.ResolvedTargets);
                    trace = trace?.WithSweetTransferHandoff(
                        handoffSourceDishInstanceId,
                        handoffExecutionGroupId,
                        handoffSkillId,
                        handoffPayloadCount);
                }

                var entry = new ScoreEffectEntry(
                    ScorePhase.DishSkills,
                    ScoreSource.TransferredDishSkill(parent, target, sourceLabel),
                    new SkillRuleEffect(rule, target),
                    target,
                    null,
                    null,
                    rule.Order,
                    boardOrder,
                    trace);
                ctx.ResolveTransferredEffect(entry);
            }
        }

        private static string SourceNameWithoutTag(string sourceLabel)
        {
            if (string.IsNullOrEmpty(sourceLabel))
            {
                return string.Empty;
            }

            int index = sourceLabel.IndexOf('<');
            return index > 0 ? sourceLabel.Substring(0, index) : sourceLabel;
        }

        /// <summary>
        /// 甜蜜传递来源候选：作用域内「带甜蜜传递」的其它食物。
        /// <c>actionCount&gt;0</c> 时均权无放回取 N 个来源；这不是传递接收者选择，
        /// 因此不使用按甜蜜传递流派/占格数加权的 TransferTargetSelector。
        /// </summary>
        private IReadOnlyList<DishInstance> SweetTransferSources(ScoreContext ctx)
        {
            var qualified = new List<DishInstance>();
            var seen = new HashSet<int>();

            void TryAdd(DishInstance dish)
            {
                if (dish == null || dish.SkillsDisabled || dish.Id == _self.Id || !seen.Add(dish.Id))
                {
                    return;
                }

                if (HasSkillOfType(ctx.Db, dish, SkillActionType.TransferSkills))
                {
                    qualified.Add(dish);
                }
            }

            foreach (DishInstance dish in SkillConditionEvaluator.ScopeDishes(ctx.DiningTable, _self, _rule.ActionScope, includeSelf: false))
            {
                TryAdd(dish);
            }

            List<DishInstance> ordered = qualified
                .OrderBy(BoardTop)
                .ThenBy(BoardLeft)
                .ThenBy(d => d.Id)
                .ToList();

            if (_rule.ActionCount <= 0 || ordered.Count <= _rule.ActionCount)
            {
                return ordered;
            }

            int count = _rule.ActionCount;
            var selected = new List<DishInstance>(count);
            if (ctx.Snapshot.RandomIntegerSelector == null)
            {
                selected.AddRange(ordered.Take(count));
            }
            else
            {
                var remaining = new List<DishInstance>(ordered);
                while (selected.Count < count && remaining.Count > 0)
                {
                    int index = ctx.Snapshot.RandomIntegerSelector(0, remaining.Count - 1);
                    index = Math.Max(0, Math.Min(index, remaining.Count - 1));
                    selected.Add(remaining[index]);
                    remaining.RemoveAt(index);
                }
            }

            return selected
                .OrderBy(BoardTop)
                .ThenBy(BoardLeft)
                .ThenBy(dish => dish.Id)
                .ToArray();
        }

        private static bool HasActionParam(SkillRuleDef rule, string token)
        {
            foreach (string param in rule.ActionParams)
            {
                if (param != null && param.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseChance(SkillRuleDef rule, out float chance)
        {
            chance = 0f;
            if (rule?.ActionParams == null)
            {
                return false;
            }

            foreach (string param in rule.ActionParams)
            {
                if (string.IsNullOrEmpty(param))
                {
                    continue;
                }

                int index = param.IndexOf("chance:", StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    continue;
                }

                string value = param.Substring(index + "chance:".Length)
                    .Split(';', ',', '|')[0]
                    .Trim();
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out chance))
                {
                    chance = Math.Max(0f, Math.Min(1f, chance));
                    return true;
                }
            }

            return false;
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
        /// 甜蜜传递载荷：取传递子技能「所在 skill」内除传递/复制外的子技能(rule)，配上各自描述片段。
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
                if (rule.ActionType == SkillActionType.TransferSkills
                    || rule.ActionType == SkillActionType.CopySkill
                    || IsSweetTransferModifier(rule))
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
            bool randomTargets = HasActionParam(_rule, "target:random");
            IEnumerable<DishInstance> targets;
            if (_rule.ActionScope == SkillScope.Category
                && _rule.ActionType != SkillActionType.AddTemporaryCategory)
            {
                targets = ctx.DiningTable.Dishes;
            }
            else
            {
                targets = SkillScopeResolver.ResolveActionTargetDishes(
                    ctx.Db,
                    ctx.DiningTable,
                    _self,
                    _rule,
                    SkillScopeVisualMode.ResolvedTargets,
                    ctx.IsCategory);
            }

            string category = SkillConditionEvaluator.ParseCategoryParam(_rule.ActionParams);
            if (!string.IsNullOrEmpty(category)
                && _rule.ActionType != SkillActionType.AddTemporaryCategory)
            {
                targets = targets.Where(target => ctx.IsCategory(target, category));
            }

            if (TryParseTargetSizeComparison(_rule, out string sizeComparison))
            {
                targets = targets.Where(target => SkillConditionParamParser.EvaluateComparison(
                    sizeComparison,
                    target.OccupiedCells.Count,
                    defaultValue: false));
            }

            if (HasActionParam(_rule, "exclude:source") || HasActionParam(_rule, "exclude:self"))
            {
                targets = targets.Where(target => target.Id != _self.Id);
            }

            var resolved = targets
                .Distinct()
                .OrderBy(BoardTop)
                .ThenBy(BoardLeft)
                .ThenBy(dish => dish.Id)
                .ToList();
            if (!randomTargets || _rule.ActionCount <= 0 || resolved.Count <= _rule.ActionCount)
            {
                ctx.UpdateTraceVisualTargets(resolved);
                return resolved;
            }

            // 正式结算复用会话随机整数选择器；预览未注入选择器时稳定取第一个。
            // 每抽中一个候选就从池中移除，保证按食物实例均权且无放回。
            var selected = new List<DishInstance>(_rule.ActionCount);
            for (int i = 0; i < _rule.ActionCount && resolved.Count > 0; i++)
            {
                int index = ctx.Snapshot.RandomIntegerSelector != null
                    ? ctx.Snapshot.RandomIntegerSelector(0, resolved.Count - 1)
                    : 0;
                index = Math.Max(0, Math.Min(index, resolved.Count - 1));
                selected.Add(resolved[index]);
                resolved.RemoveAt(index);
            }

            ctx.UpdateTraceVisualTargets(selected);
            return selected;
        }

        private static bool TryParseTargetSizeComparison(
            SkillRuleDef rule,
            out string comparison)
        {
            comparison = string.Empty;
            if (rule?.ActionParams == null)
            {
                return false;
            }

            const string prefix = "size:";
            foreach (string param in rule.ActionParams)
            {
                if (string.IsNullOrEmpty(param)) continue;
                foreach (string raw in param.Split(';'))
                {
                    string segment = raw.Trim();
                    if (!segment.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                    string value = segment.Substring(prefix.Length).Trim();
                    if (int.TryParse(
                        value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int exactSize))
                    {
                        comparison = $"eq:{exactSize}";
                        return true;
                    }

                    if (SkillConditionParamParser.TryGetComparison(value, out _, out _))
                    {
                        comparison = value;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryParseRandomIntegerRange(SkillRuleDef rule, out int min, out int max)
        {
            min = 0;
            max = 0;
            if (rule?.ActionParams == null)
            {
                return false;
            }

            foreach (string param in rule.ActionParams)
            {
                if (string.IsNullOrEmpty(param)) continue;
                int index = param.IndexOf("random:int:", StringComparison.OrdinalIgnoreCase);
                if (index < 0) continue;
                string[] parts = param.Substring(index + "random:int:".Length)
                    .Split(';')[0]
                    .Split('|', ',');
                if (parts.Length >= 2
                    && int.TryParse(parts[0].Trim(), out min)
                    && int.TryParse(parts[1].Trim(), out max))
                {
                    if (max < min) (min, max) = (max, min);
                    return true;
                }
            }

            return false;
        }

        private IReadOnlyList<DishInstance> SweetTransferBuffTargets(ScoreContext ctx)
        {
            return SkillConditionEvaluator.ScopeDishes(
                ctx.DiningTable,
                _self,
                _rule.ActionScope);
        }

        /// <summary>
        /// 动态倍率来源。默认取技能运行时自身；<c>actionParam=source:one-cell</c> 时，
        /// 取餐桌上全部实际占 1 格的食物。倍率在行为执行前统一快照，避免目标包含来源时边加边读。
        /// </summary>
        private IReadOnlyList<DishInstance> CurrentMultiplierSources(ScoreContext ctx)
        {
            if (!HasActionParam(_rule, "source:one-cell"))
            {
                return new[] { _self };
            }

            return ctx.DiningTable.Dishes
                .Where(dish => dish.OccupiedCells.Count == 1)
                .OrderBy(BoardTop)
                .ThenBy(BoardLeft)
                .ThenBy(dish => dish.Id)
                .ToArray();
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

            foreach (TransferredSkill transferred in dish.TransferredSkills)
            {
                if (transferred?.Rule?.ActionType == actionType)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>在一次成功甜蜜传递中，以 Buff 所有者为来源生成独立 trigger 明细与倍率结果。</summary>
    internal sealed class SweetTransferBuffTriggerEffect : IScoreEffect
    {
        private readonly SweetTransferBuffRegistration _registration;
        private readonly DishInstance _transferSource;
        private readonly IReadOnlyList<DishInstance> _targets;
        private readonly float _value;
        private readonly SkillActionType _applyType;

        public SweetTransferBuffTriggerEffect(
            SweetTransferBuffRegistration registration,
            DishInstance transferSource,
            IReadOnlyList<DishInstance> targets,
            float value,
            SkillActionType applyType)
        {
            _registration = registration;
            _transferSource = transferSource;
            _targets = targets ?? Array.Empty<DishInstance>();
            _value = value;
            _applyType = applyType;
        }

        public void Apply(ScoreContext ctx)
        {
            if (ctx == null || _registration == null || _transferSource == null)
            {
                return;
            }

            ctx.RecordSweetTransferBuffTriggered(_registration, _transferSource, _value, _targets);
            if (_applyType == SkillActionType.None)
            {
                return;
            }

            foreach (DishInstance target in _targets)
            {
                if (_applyType == SkillActionType.AddFlat)
                {
                    ctx.AddFlatTo(target, _value);
                }
                else if (_applyType == SkillActionType.AddMultFlat)
                {
                    ctx.AddMultFlatTo(target, _value);
                }
                else
                {
                    ctx.MultiplyTo(target, _value);
                }
            }
        }
    }
}
