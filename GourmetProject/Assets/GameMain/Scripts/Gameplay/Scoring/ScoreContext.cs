using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>
    /// 结算过程中的可变上下文与累加器。
    /// 每个食物有独立累加器（加法区/倍率），支持跨菜改分；
    /// 所有食物在全部阶段跑完后统一定稿（deferred finalization），因此技能可以改到别的食物。
    /// 层数/金币/技能传递等对外副作用只累积，不在结算中直接改实例（保证预览安全、纯计算）。
    /// </summary>
    public class ScoreContext
    {
        private const int MaxCommandsPerCalculation = 2048;

        private sealed class DishAccumulator
        {
            public DishInstance Dish;
            public float Base;
            public float Flat;
            public float Mult = 1f;
        }

        private readonly Dictionary<int, DishAccumulator> _accums = new Dictionary<int, DishAccumulator>();
        private readonly List<int> _order = new List<int>();
        private readonly List<DishScore> _dishScores = new List<DishScore>();
        private readonly List<ScoreLine> _lines = new List<ScoreLine>();
        private readonly List<ScoreEvent> _events = new List<ScoreEvent>();
        private readonly Queue<PendingScoreCommand> _commands = new Queue<PendingScoreCommand>();
        private int _happyCakeLayerDelta;
        private int _silverItemRolls;
        private readonly List<SkillTransferSideEffect> _skillTransfers = new List<SkillTransferSideEffect>();
        private readonly List<SweetTransferBuffRegistration> _sweetTransferBuffs = new List<SweetTransferBuffRegistration>();
        private readonly List<CopySkillRequest> _copySkillRequests = new List<CopySkillRequest>();
        private readonly Dictionary<int, float> _permanentFlatDeltas = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _permanentMultDeltas = new Dictionary<int, float>();
        private readonly Dictionary<int, int> _liveCountAs = new Dictionary<int, int>();
        private DishAccumulator _current;
        private bool _initialFinalModifiersRecorded;
        private bool _finalized;
        private bool _isResolvingCommands;
        private int _commandsExecuted;
        private int _nextExecutionGroupId;
        private int _currentExecutionGroupId;

        public ScoreContext(ScoreSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            DiningTable = snapshot.DiningTable;
            Db = snapshot.Db;
            FinalFlat = snapshot.InitialFinalFlat;
            FinalMultiplier = snapshot.InitialFinalMultiplier;
            InitialHappyCakeLayers = snapshot.InitialHappyCakeLayers;

            // 预建全部菜的累加器，保证「A 改 B 的分」无论 B 是否已开始都有效。
            foreach (DishInstance dish in snapshot.DishesInDefaultOrder)
            {
                EnsureAccumulator(dish);
            }

            // 预计算本次结算的「视为食物数」live 值（含条件/定向 AddCountAs），供计数前提读取。
            ComputeLiveCountAs(snapshot);
        }

        /// <summary>
        /// 本次结算每个食物的「视为食物数」实际值（下限 1）：静态定义 + 已持久化运行时加成 + 本次结算即时生效的 AddCountAs 规则。
        /// AddCountAs 走 live（每次结算按当前局面重算），不做跨结算持久，故条件类「视为N」在当次结算即生效。
        /// </summary>
        public int GetEffectiveCountAs(DishInstance dish)
        {
            if (dish == null)
            {
                return 1;
            }

            return _liveCountAs.TryGetValue(dish.Id, out int v) ? v : Math.Max(1, dish.EffectiveCountAs);
        }

        private void ComputeLiveCountAs(ScoreSnapshot snapshot)
        {
            var extra = new Dictionary<int, int>();
            IScoreHistory history = snapshot.History;
            foreach (DishInstance src in snapshot.DishesInDefaultOrder)
            {
                if (src.SkillsDisabled)
                {
                    continue;
                }

                foreach (string skillId in src.SkillIds)
                {
                    SkillDef skill = Db?.GetSkill(skillId);
                    if (skill == null || !skill.HasRules)
                    {
                        continue;
                    }

                    foreach (SkillRuleDef rule in skill.Rules)
                    {
                        if (rule.Trigger != SkillTrigger.OnSettle || rule.ActionType != SkillActionType.AddCountAs)
                        {
                            continue;
                        }

                        // 用默认（非 live）计数评估条件，避免 countAs 递归依赖 countAs。
                        int count = SkillConditionEvaluator.Evaluate(rule, DiningTable, history, src, InitialHappyCakeLayers);
                        if (count <= 0)
                        {
                            continue;
                        }

                        foreach (DishInstance t in CountAsTargets(rule, src))
                        {
                            float basis = HasActionParam(rule, "target:occupiedcells")
                                ? t.OccupiedCells.Count
                                : 1f;
                            int value = (int)Math.Round(
                                rule.ActionValue * count * basis,
                                MidpointRounding.AwayFromZero);
                            if (value == 0)
                            {
                                continue;
                            }

                            extra.TryGetValue(t.Id, out int cur);
                            extra[t.Id] = cur + value;
                        }
                    }
                }
            }

            int itemBonus = Math.Max(0, snapshot.ExtraCountAsPerDish);
            foreach (DishInstance d in snapshot.DishesInDefaultOrder)
            {
                extra.TryGetValue(d.Id, out int e);
                _liveCountAs[d.Id] = Math.Max(1, d.Def.CountAs + d.RuntimeCountAsBonus + e + itemBonus);
            }
        }

        private List<DishInstance> CountAsTargets(SkillRuleDef rule, DishInstance self)
        {
            return SkillScopeResolver.ResolveActionTargetDishes(
                    Db,
                    DiningTable,
                    self,
                    rule,
                    SkillScopeVisualMode.ResolvedTargets)
                .ToList();
        }

        private static bool HasActionParam(SkillRuleDef rule, string token)
        {
            if (rule?.ActionParams == null)
            {
                return false;
            }

            foreach (string param in rule.ActionParams)
            {
                if (param != null
                    && param.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public ScoreSnapshot Snapshot { get; }

        public GpTable DiningTable { get; }

        public GameplayDatabase Db { get; }

        public DishInstance Dish { get; private set; }

        public GridPos? CurrentCell { get; private set; }

        public ScoreSource Source { get; private set; }

        public ScorePhase Phase { get; private set; }

        /// <summary>当前正在结算的风味或材质定义；非配置定义来源时为空。</summary>
        public IEffectDef EffectDef { get; set; }

        public SkillExecutionTrace Trace { get; private set; }

        public float FlatBonus => _current?.Flat ?? 0f;

        public float Multiplier => _current?.Mult ?? 1f;

        public float RawSum { get; private set; }

        public float FinalFlat { get; private set; }

        public float FinalMultiplier { get; private set; } = 1f;

        /// <summary>本次结算产生的金币增量（副作用，由 Game 层在正式结算后入账）。</summary>
        public float GoldDelta { get; private set; }

        public IReadOnlyList<DishScore> DishScores => _dishScores;

        public IReadOnlyList<ScoreLine> Lines => _lines;

        public IReadOnlyList<ScoreEvent> Events => _events;

        /// <summary>本场经营挑战开始时的全局欢乐蛋糕层数。</summary>
        public int InitialHappyCakeLayers { get; }

        /// <summary>本次结算产生的全局欢乐蛋糕层数增量（正式结算后由 Game 层写回经营挑战状态）。</summary>
        public int HappyCakeLayerDelta => _happyCakeLayerDelta;

        /// <summary>结算过程中「当前」的全局欢乐蛋糕层数（初始 + 已产生增量）。</summary>
        public int CurrentHappyCakeLayers => Math.Max(0, InitialHappyCakeLayers + _happyCakeLayerDelta);

        /// <summary>本次结算登记的「银材质」1/2 获得消耗品掷骰请求次数（正式结算后由 Game 层掷骰发放）。</summary>
        public int SilverItemRollRequests => _silverItemRolls;

        public IReadOnlyList<SkillTransferSideEffect> SkillTransfers => _skillTransfers;

        public IReadOnlyList<CopySkillRequest> CopySkillRequests => _copySkillRequests;

        /// <summary>本次结算登记的永久加法分增量（实例 Id → 累加值）。正式结算后写回实例。</summary>
        public IReadOnlyDictionary<int, float> PermanentFlatDeltas => _permanentFlatDeltas;

        /// <summary>本次结算登记的永久倍率增量（实例 Id → 累乘倍数）。正式结算后写回实例。</summary>
        public IReadOnlyDictionary<int, float> PermanentMultDeltas => _permanentMultDeltas;

        public void EmitEvent(ScoreEventType type, string message)
        {
            int dishInstanceId = Dish != null ? Dish.Id : 0;
            string dishId = Dish != null ? Dish.Def.Id : string.Empty;
            _events.Add(new ScoreEvent(
                type,
                Phase,
                Source,
                dishInstanceId,
                dishId,
                CurrentCell,
                message,
                Trace,
                _currentExecutionGroupId));
        }

        public void BeginDish(DishInstance dish)
        {
            Dish = dish ?? throw new ArgumentNullException(nameof(dish));
            _current = EnsureAccumulator(dish);
            CurrentCell = null;
            Source = ScoreSource.Dish(dish);
            Phase = ScorePhase.BeforeDish;
            EffectDef = null;
            EmitEvent(ScoreEventType.DishStarted, $"开始结算 {dish.Def.Name}");
        }

        public void RecordDishBase()
        {
            if (Dish == null)
            {
                return;
            }

            Phase = ScorePhase.DishBase;
            Source = ScoreSource.Dish(Dish);
            float baseScore = Dish.BaseScoreBeforeSettlement;
            _lines.Add(new ScoreLine(
                Phase,
                ScoreLineKind.DishBase,
                Source,
                Dish.Id,
                Dish.Def.Id,
                null,
                baseScore,
                0f,
                baseScore,
                $"{Dish.Def.Name} 基础分数 {baseScore}"));
        }

        public void Apply(ScoreEffectEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            DishInstance previousDish = Dish;
            DishAccumulator previousCurrent = _current;
            ScorePhase previousPhase = Phase;
            ScoreSource previousSource = Source;
            GridPos? previousCell = CurrentCell;
            IEffectDef previousEffectDef = EffectDef;
            SkillExecutionTrace previousTrace = Trace;
            int previousExecutionGroupId = _currentExecutionGroupId;
            if (entry.Dish != null)
            {
                Dish = entry.Dish;
                _current = EnsureAccumulator(entry.Dish);
            }

            Phase = entry.Phase;
            Source = entry.Source;
            CurrentCell = entry.Cell;
            EffectDef = entry.EffectDef;
            Trace = entry.Trace;
            _currentExecutionGroupId = ++_nextExecutionGroupId;
            string effectName = Source.Name;
            EmitEvent(ScoreEventType.EffectStarted, $"开始效果 {effectName}");
            try
            {
                entry.Effect.Apply(this);
                ResolveCommandQueue();
                EmitEvent(ScoreEventType.EffectFinished, $"结束效果 {effectName}");
            }
            finally
            {
                Dish = previousDish;
                _current = previousCurrent;
                Phase = previousPhase;
                Source = previousSource;
                CurrentCell = previousCell;
                EffectDef = previousEffectDef;
                Trace = previousTrace;
                _currentExecutionGroupId = previousExecutionGroupId;
            }
        }

        // ------- 兼容旧 API：作用于「当前菜」 -------

        public void AddFlat(float value)
        {
            int id = Dish != null ? Dish.Id : 0;
            SubmitCommand(new AddDishFlatCommand(id, value));
        }

        public void MultiplyBy(float value)
        {
            int id = Dish != null ? Dish.Id : 0;
            SubmitCommand(new MultiplyDishCommand(id, value));
        }

        // ------- 跨菜 / 副作用 API -------

        public void AddFlatTo(DishInstance target, float value)
        {
            if (target == null)
            {
                return;
            }

            SubmitCommand(new AddDishFlatCommand(target.Id, value));
        }

        public void MultiplyTo(DishInstance target, float value)
        {
            if (target == null)
            {
                return;
            }

            SubmitCommand(new MultiplyDishCommand(target.Id, value));
        }

        /// <summary>目标食物「倍率区」加法（倍率+X），区别于乘法的 MultiplyTo。</summary>
        public void AddMultFlatTo(DishInstance target, float value)
        {
            if (target == null)
            {
                return;
            }

            SubmitCommand(new AddDishMultFlatCommand(target.Id, value));
        }

        /// <summary>读取目标食物结算到当前时刻的倍率（含固化倍率与此前已执行的倍率效果）。</summary>
        public float GetCurrentMultiplier(DishInstance target)
        {
            if (target == null || !_accums.TryGetValue(target.Id, out DishAccumulator accumulator))
            {
                return 0f;
            }

            return accumulator.Mult;
        }

        /// <summary>读取目标食物结算到当前时刻的分数（基础分 + 此前已执行的固定加分，不含倍率）。</summary>
        public float GetCurrentScore(DishInstance target)
        {
            if (target == null || !_accums.TryGetValue(target.Id, out DishAccumulator accumulator))
            {
                return 0f;
            }

            return accumulator.Base + accumulator.Flat;
        }

        public void GrantGold(float value)
        {
            SubmitCommand(new GrantGoldCommand(value));
        }

        /// <summary>
        /// 登记一次「1/2 概率获得消耗品」的掷骰请求（银材质）。结算层只累计请求数、不掷骰，
        /// 保证 PreviewScore 纯净；正式 Settle 后由 Game 层用注入的随机流掷骰并发放装饰品和消耗品。
        /// </summary>
        public void RequestSilverItemRoll()
        {
            SubmitCommand(new RequestSilverItemRollCommand());
        }

        public void AddFinalFlat(float value)
        {
            SubmitCommand(new AddFinalFlatCommand(value));
        }

        public void MultiplyFinalBy(float value)
        {
            SubmitCommand(new MultiplyFinalCommand(value));
        }

        /// <summary>全局「欢乐蛋糕层数」改动（副作用，正式结算后写回经营挑战状态）。
        /// mult=true 时按乘法（可选 floor 表示至少净增 floor 层）。目标食物无关，全局共享一个计数器。</summary>
        public void AddHappyCakeLayers(float value, bool mult, int floor = 0)
        {
            SubmitCommand(new ChangeHappyCakeLayerCommand(value, mult, floor));
        }

        /// <summary>
        /// 目标食物「永久加法分」+value：本次结算即计入加法区，并登记持久增量（正式结算后写回实例，之后每次结算叠加进基础分）。
        /// </summary>
        public void AddPermanentFlatTo(DishInstance target, float value)
        {
            if (target == null || Math.Abs(value) < 0.0001f)
            {
                return;
            }

            _permanentFlatDeltas.TryGetValue(target.Id, out float cur);
            _permanentFlatDeltas[target.Id] = cur + value;
            SubmitCommand(new AddDishFlatCommand(target.Id, value));
        }

        /// <summary>
        /// 目标食物「永久倍率」×value：本次结算即计入倍率，并登记持久倍数（正式结算后写回实例，之后每次结算叠乘进倍率初值）。
        /// </summary>
        public void AddPermanentMultTo(DishInstance target, float value)
        {
            if (target == null || value <= 0f)
            {
                return;
            }

            _permanentMultDeltas.TryGetValue(target.Id, out float cur);
            _permanentMultDeltas[target.Id] = (cur <= 0f ? 1f : cur) * value;
            SubmitCommand(new MultiplyDishCommand(target.Id, value));
        }

        /// <summary>登记技能传递（副作用，正式结算后应用到实例的运行时技能集）。</summary>
        public void RecordSkillTransfer(DishInstance target, IReadOnlyList<SkillEffect> effects, string sourceName = null, int sourceInstanceId = 0)
        {
            if (target == null || effects == null || effects.Count == 0)
            {
                return;
            }

            _skillTransfers.Add(new SkillTransferSideEffect(target.Id, effects, sourceName, sourceInstanceId));
            EmitEvent(ScoreEventType.CommandExecuted, $"技能传递给 {target.Def.Name}（{effects.Count} 个）");
        }

        /// <summary>
        /// 在当前规则真正轮到结算时，为作用域内食物登记甜蜜传递 Buff。
        /// 注册只存在于本次 <see cref="ScoreContext"/>，因此天然遵守实际结算顺序。
        /// </summary>
        public void RegisterSweetTransferBuff(
            DishInstance owner,
            SkillRuleDef rule,
            IReadOnlyList<DishInstance> targets,
            int conditionCount)
        {
            if (owner == null || rule == null || targets == null || targets.Count == 0 || conditionCount <= 0)
            {
                return;
            }

            var ids = new List<int>();
            var cells = new List<GridPos>();
            var seenIds = new HashSet<int>();
            var seenCells = new HashSet<GridPos>();
            foreach (DishInstance target in targets)
            {
                if (target == null || !seenIds.Add(target.Id))
                {
                    continue;
                }

                ids.Add(target.Id);
                foreach (GridPos cell in target.OccupiedCells)
                {
                    if (seenCells.Add(cell))
                    {
                        cells.Add(cell);
                    }
                }
            }

            if (ids.Count == 0)
            {
                return;
            }

            SkillExecutionTrace trace = Trace?.WithVisualTargets(ids, cells);
            _sweetTransferBuffs.Add(new SweetTransferBuffRegistration(
                owner,
                rule,
                conditionCount,
                ids,
                Source,
                trace));
            AddLine(
                EnsureAccumulator(owner),
                ScoreLineKind.SweetTransferBuffApplied,
                ids.Count,
                0f,
                ids.Count,
                $"挂载甜蜜传递 Buff ×{ids.Count}",
                Source,
                trace);
        }

        public IReadOnlyList<SweetTransferBuffRegistration> SweetTransferBuffsFor(DishInstance source)
        {
            if (source == null || _sweetTransferBuffs.Count == 0)
            {
                return Array.Empty<SweetTransferBuffRegistration>();
            }

            var result = new List<SweetTransferBuffRegistration>();
            foreach (SweetTransferBuffRegistration registration in _sweetTransferBuffs)
            {
                if (registration.TargetDishInstanceIds.Contains(source.Id))
                {
                    result.Add(registration);
                }
            }

            return result;
        }

        public void RecordSweetTransferBuffTriggered(
            SweetTransferBuffRegistration registration,
            DishInstance transferSource,
            float value,
            IReadOnlyList<DishInstance> visualTargets)
        {
            if (registration?.Owner == null || transferSource == null)
            {
                return;
            }

            var ids = new List<int>();
            var cells = new List<GridPos>();
            var seenIds = new HashSet<int>();
            var seenCells = new HashSet<GridPos>();
            if (visualTargets != null)
            {
                foreach (DishInstance target in visualTargets)
                {
                    if (target == null || !seenIds.Add(target.Id))
                    {
                        continue;
                    }

                    ids.Add(target.Id);
                    foreach (GridPos cell in target.OccupiedCells)
                    {
                        if (seenCells.Add(cell))
                        {
                            cells.Add(cell);
                        }
                    }
                }
            }

            SkillExecutionTrace trace = registration.Trace?.WithRuntimeContext(
                registration.Owner,
                transferSource,
                ids,
                cells);
            AddLine(
                EnsureAccumulator(registration.Owner),
                ScoreLineKind.SweetTransferBuffTriggered,
                value,
                0f,
                value,
                $"响应 {transferSource.Def.Name} 的甜蜜传递",
                registration.Source,
                trace);
        }

        public void RecordSweetTransferFailed(DishInstance sourceDish)
        {
            if (sourceDish == null)
            {
                return;
            }

            SkillExecutionTrace trace = Trace?.WithVisualTargets(
                Array.Empty<int>(),
                Array.Empty<GridPos>());
            AddLine(
                EnsureAccumulator(sourceDish),
                ScoreLineKind.SweetTransferFailed,
                0f,
                0f,
                0f,
                "没有可传递目标",
                Source,
                trace);
        }

        /// <summary>记录一次「代触发甜蜜传递」技能命中来源食物，仅供施放者的结算演出。</summary>
        public void RecordTriggerSweetTransfer(DishInstance sourceDish, IReadOnlyList<DishInstance> sources)
        {
            int sourceCount = sources?.Count ?? 0;
            if (sourceDish == null || sourceCount <= 0)
            {
                return;
            }

            var ids = new List<int>();
            var cells = new List<GridPos>();
            var seenCells = new HashSet<GridPos>();
            foreach (DishInstance source in sources)
            {
                if (source == null)
                {
                    continue;
                }

                ids.Add(source.Id);
                foreach (GridPos cell in source.OccupiedCells)
                {
                    if (seenCells.Add(cell))
                    {
                        cells.Add(cell);
                    }
                }
            }

            SkillExecutionTrace trace = Trace?.WithVisualTargets(ids, cells);

            AddLine(
                EnsureAccumulator(sourceDish),
                ScoreLineKind.TriggerSweetTransfer,
                sourceCount,
                0f,
                sourceCount,
                $"代触发甜蜜传递 ×{sourceCount}",
                Source,
                trace);
        }

        /// <summary>记录「代触发」流程中某个来源食物开始执行，供逐个演出与状态收尾。</summary>
        public void RecordTriggeredSweetTransferSource(DishInstance sourceDish, int index, int total)
        {
            if (sourceDish == null || index <= 0 || total <= 0)
            {
                return;
            }

            ScoreSource source = Source;
            SkillExecutionTrace trace = Trace;
            foreach (string skillId in sourceDish.SkillIds)
            {
                SkillDef skill = Db?.GetSkill(skillId);
                SkillRuleDef transferRule = skill?.Rules?.FirstOrDefault(rule =>
                    rule.Trigger == SkillTrigger.OnSettle
                    && rule.ActionType == SkillActionType.TransferSkills);
                if (transferRule == null)
                {
                    continue;
                }

                string sourceLabel = sourceDish.GetSkillSource(skillId);
                source = string.IsNullOrEmpty(sourceLabel)
                    ? ScoreSource.DishSkill(skill, sourceDish)
                    : ScoreSource.TransferredDishSkill(skill, sourceDish, sourceLabel);
                trace = SkillExecutionTrace.Create(
                    Db,
                    DiningTable,
                    sourceDish,
                    sourceDish,
                    skill,
                    transferRule,
                    SkillRuleEffectSource.TraceKindForSourceLabel(sourceLabel),
                    sourceLabel,
                    SkillScopeVisualMode.CandidateScope);
                break;
            }

            AddLine(
                EnsureAccumulator(sourceDish),
                ScoreLineKind.TriggeredSweetTransferSource,
                index,
                0f,
                total,
                $"触发甜蜜传递 {index}/{total}",
                source,
                trace);
        }

        public void ResolveTransferredEffect(ScoreEffectEntry entry)
        {
            if (entry?.Dish == null)
            {
                return;
            }

            SubmitCommand(new ResolveScoreEffectCommand(entry));
        }

        /// <summary>登记技能复制请求，并立即触发本次选中的技能效果。</summary>
        public void RecordCopySkill(DishInstance target, IReadOnlyList<string> candidates, int count, string sourceName = null)
        {
            if (target == null || candidates == null || candidates.Count == 0 || count <= 0)
            {
                return;
            }

            int take = Math.Min(count, candidates.Count);
            IReadOnlyList<string> selected = SelectCopySkills(candidates, take);
            if (selected.Count == 0)
            {
                return;
            }

            _copySkillRequests.Add(new CopySkillRequest(target.Id, candidates, selected.Count, sourceName, selected));
            DishAccumulator accum = EnsureAccumulator(target);
            AddLine(accum, ScoreLineKind.CopySkill, selected.Count, target.SkillIds.Count, target.SkillIds.Count + selected.Count, $"复制技能 +{selected.Count}");
            EmitEvent(ScoreEventType.CommandExecuted, $"技能复制给 {target.Def.Name}（{selected.Count} 个）");
            ResolveCopiedSkillEffects(target, selected, sourceName);
        }

        private IReadOnlyList<string> SelectCopySkills(IReadOnlyList<string> candidates, int count)
        {
            IReadOnlyList<string> raw = Snapshot.CopySkillSelector != null
                ? Snapshot.CopySkillSelector(candidates, count)
                : candidates.Take(count).ToArray();
            if (raw == null || raw.Count == 0)
            {
                return Array.Empty<string>();
            }

            var selected = new List<string>();
            foreach (string skillId in raw)
            {
                if (!string.IsNullOrEmpty(skillId)
                    && candidates.Contains(skillId)
                    && !selected.Contains(skillId))
                {
                    selected.Add(skillId);
                }

                if (selected.Count >= count)
                {
                    break;
                }
            }

            return selected;
        }

        private void ResolveCopiedSkillEffects(DishInstance target, IReadOnlyList<string> selectedSkillIds, string sourceName)
        {
            string sourceLabel = string.IsNullOrEmpty(sourceName) ? null : $"{sourceName}<技能复制>";
            int boardOrder = target.Placement.Origin.Y * DiningTable.Width + target.Placement.Origin.X;
            foreach (string skillId in selectedSkillIds)
            {
                SkillDef skill = Db.GetSkill(skillId);
                if (skill == null || !skill.HasRules)
                {
                    continue;
                }

                ScoreSource source = ScoreSource.TransferredDishSkill(skill, target, sourceLabel);
                foreach (SkillRuleDef rule in skill.Rules)
                {
                    if (rule.Trigger != SkillTrigger.OnSettle
                        || rule.ActionType == SkillActionType.CopySkill)
                    {
                        continue;
                    }

                    var entry = new ScoreEffectEntry(
                        ScorePhase.DishSkills,
                        source,
                        new SkillRuleEffect(rule, target),
                        target,
                        null,
                        null,
                        rule.Order,
                        boardOrder,
                        SkillExecutionTrace.Create(
                            Db,
                            DiningTable,
                            target,
                            target,
                            skill,
                            rule,
                            SkillExecutionKind.CopiedSkill,
                            sourceLabel,
                            SkillScopeVisualMode.ResolvedTargets));
                    SubmitCommand(new ResolveScoreEffectCommand(entry));
                }
            }
        }

        public void SubmitCommand(IScoreCommand command)
        {
            if (command == null)
            {
                return;
            }

            _commands.Enqueue(new PendingScoreCommand(
                command,
                Phase,
                Source,
                CurrentCell,
                EffectDef,
                Trace,
                _currentExecutionGroupId));
            ResolveCommandQueue();
        }

        /// <summary>逐菜阶段结束（仅发事件；分数定稿延迟到 FinalizeDishes）。</summary>
        public void CompleteDish()
        {
            if (Dish == null)
            {
                return;
            }

            EmitEvent(ScoreEventType.DishCompleted, $"{Dish.Def.Name} 阶段结束");
        }

        /// <summary>所有逐菜阶段跑完后，统一把每个食物的累加器定稿为贡献并求和。</summary>
        public void FinalizeDishes()
        {
            if (_finalized)
            {
                return;
            }

            _finalized = true;
            RawSum = 0f;
            foreach (int id in _order)
            {
                DishAccumulator a = _accums[id];
                var score = new DishScore(
                    a.Dish.Id,
                    a.Dish.Def.Id,
                    a.Base,
                    a.Flat,
                    a.Mult,
                    GetEffectiveCountAs(a.Dish));
                _dishScores.Add(score);
                RawSum += score.Contribution;
            }
        }

        public void RecordInitialFinalModifiers()
        {
            if (_initialFinalModifiersRecorded)
            {
                return;
            }

            _initialFinalModifiersRecorded = true;
            var source = ScoreSource.FinalModifier("initial_final_modifier", "局级修正");
            if (Math.Abs(Snapshot.InitialFinalFlat) > 0.0001f)
            {
                _lines.Add(new ScoreLine(
                    ScorePhase.Final,
                    ScoreLineKind.FinalFlat,
                    source,
                    0,
                    string.Empty,
                    null,
                    Snapshot.InitialFinalFlat,
                    0f,
                    FinalFlat,
                    $"局级加法 +{Snapshot.InitialFinalFlat}",
                    executionGroupId: ++_nextExecutionGroupId));
            }

            if (Math.Abs(Snapshot.InitialFinalMultiplier - 1f) > 0.0001f)
            {
                _lines.Add(new ScoreLine(
                    ScorePhase.Final,
                    ScoreLineKind.FinalMultiplier,
                    source,
                    0,
                    string.Empty,
                    null,
                    Snapshot.InitialFinalMultiplier,
                    1f,
                    FinalMultiplier,
                    $"局级倍率 x{Snapshot.InitialFinalMultiplier}",
                    executionGroupId: ++_nextExecutionGroupId));
            }
        }

        public ScoreResult ToResult()
        {
            return new ScoreResult(_dishScores, RawSum, FinalFlat, FinalMultiplier, _lines, _events, GoldDelta, _happyCakeLayerDelta, _skillTransfers, _permanentFlatDeltas, _permanentMultDeltas, _silverItemRolls, _copySkillRequests);
        }

        // ------- 命令实际改分（internal，供命令调用） -------

        internal void ApplyDishFlatCommand(int dishId, float value)
        {
            if (!_accums.TryGetValue(dishId, out DishAccumulator a))
            {
                return;
            }

            float before = a.Flat;
            a.Flat += value;
            AddLine(a, ScoreLineKind.DishFlat, value, before, a.Flat, $"美味值 +{value}");
        }

        internal void ApplyDishMultiplierCommand(int dishId, float value)
        {
            if (!_accums.TryGetValue(dishId, out DishAccumulator a))
            {
                return;
            }

            float before = a.Mult;
            a.Mult *= value;
            AddLine(a, ScoreLineKind.DishMultiplier, value, before, a.Mult, $"倍率 x{value}");
        }

        internal void ApplyDishMultFlatCommand(int dishId, float value)
        {
            if (!_accums.TryGetValue(dishId, out DishAccumulator a))
            {
                return;
            }

            float before = a.Mult;
            a.Mult += value;
            AddLine(a, ScoreLineKind.DishMultiplierAdd, value, before, a.Mult, $"倍率 +{value}");
        }

        internal void ApplyGrantGoldCommand(float value)
        {
            float before = GoldDelta;
            GoldDelta += value;
            AddLine(_current, ScoreLineKind.Gold, value, before, GoldDelta, $"获得金币 +{value}");
        }

        internal void ApplyRequestSilverItemRollCommand()
        {
            int before = _silverItemRolls;
            _silverItemRolls++;
            AddLine(_current, ScoreLineKind.SilverItemRoll, 1, before, _silverItemRolls, "登记 1/2 获得消耗品");
        }

        internal void ApplyHappyCakeLayerCommand(float value, bool mult, int floor)
        {
            int before = CurrentHappyCakeLayers;
            int after;
            if (mult)
            {
                after = (int)Math.Round(before * value, MidpointRounding.AwayFromZero);
                if (floor > 0 && after < before + floor)
                {
                    after = before + floor;
                }
            }
            else
            {
                after = before + (int)value;
            }

            if (after < 0)
            {
                after = 0;
            }

            _happyCakeLayerDelta += after - before;
            AddLine(_current, ScoreLineKind.Layer, after - before, before, after, mult ? $"欢乐蛋糕层数 x{value}" : $"欢乐蛋糕层数 {(value >= 0 ? "+" : string.Empty)}{value}");
        }

        internal void ApplyFinalFlatCommand(float value)
        {
            float before = FinalFlat;
            FinalFlat += value;
            AddLine(null, ScoreLineKind.FinalFlat, value, before, FinalFlat, $"总分 +{value}");
        }

        internal void ApplyFinalMultiplierCommand(float value)
        {
            float before = FinalMultiplier;
            FinalMultiplier *= value;
            AddLine(null, ScoreLineKind.FinalMultiplier, value, before, FinalMultiplier, $"总分倍率 x{value}");
        }

        private DishAccumulator EnsureAccumulator(DishInstance dish)
        {
            if (!_accums.TryGetValue(dish.Id, out DishAccumulator a))
            {
                // 永久加分计入基础分、永久倍率计入倍率初值（本实例此前累积的永久量立即生效）。
                a = new DishAccumulator
                {
                    Dish = dish,
                    Base = dish.BaseScoreBeforeSettlement,
                    Mult = dish.BaseMultiplierBeforeSettlement,
                };
                _accums[dish.Id] = a;
                _order.Add(dish.Id);
            }

            return a;
        }

        private void ResolveCommandQueue()
        {
            if (_isResolvingCommands)
            {
                return;
            }

            _isResolvingCommands = true;
            try
            {
                while (_commands.Count > 0)
                {
                    if (_commandsExecuted++ >= MaxCommandsPerCalculation)
                    {
                        throw new InvalidOperationException("Score command limit exceeded. Check for recursive scoring effects.");
                    }

                    PendingScoreCommand pending = _commands.Dequeue();
                    ScorePhase previousPhase = Phase;
                    ScoreSource previousSource = Source;
                    GridPos? previousCell = CurrentCell;
                    IEffectDef previousEffectDef = EffectDef;
                    SkillExecutionTrace previousTrace = Trace;
                    int previousExecutionGroupId = _currentExecutionGroupId;
                    Phase = pending.Phase;
                    Source = pending.Source;
                    CurrentCell = pending.Cell;
                    EffectDef = pending.EffectDef;
                    Trace = pending.Trace;
                    _currentExecutionGroupId = pending.ExecutionGroupId;
                    try
                    {
                        EmitEvent(ScoreEventType.CommandExecuted, $"执行命令 {pending.Command.Name}");
                        pending.Command.Execute(this);
                    }
                    finally
                    {
                        Phase = previousPhase;
                        Source = previousSource;
                        CurrentCell = previousCell;
                        EffectDef = previousEffectDef;
                        Trace = previousTrace;
                        _currentExecutionGroupId = previousExecutionGroupId;
                    }
                }
            }
            finally
            {
                _isResolvingCommands = false;
            }
        }

        private void AddLine(
            DishAccumulator accum,
            ScoreLineKind kind,
            float value,
            float before,
            float after,
            string fallbackMessage,
            ScoreSource sourceOverride = null,
            SkillExecutionTrace traceOverride = null)
        {
            int dishInstanceId = accum != null ? accum.Dish.Id : (Dish != null ? Dish.Id : 0);
            string dishId = accum != null ? accum.Dish.Def.Id : (Dish != null ? Dish.Def.Id : string.Empty);
            ScoreSource lineSource = sourceOverride ?? Source;
            SkillExecutionTrace lineTrace = traceOverride ?? Trace;
            string sourceName = lineSource != null ? lineSource.Name : string.Empty;
            string message = string.IsNullOrEmpty(sourceName) ? fallbackMessage : $"{sourceName}: {fallbackMessage}";
            _lines.Add(new ScoreLine(
                Phase,
                kind,
                lineSource,
                dishInstanceId,
                dishId,
                CurrentCell,
                value,
                before,
                after,
                message,
                lineTrace,
                _currentExecutionGroupId));
        }

        private sealed class PendingScoreCommand
        {
            public PendingScoreCommand(
                IScoreCommand command,
                ScorePhase phase,
                ScoreSource source,
                GridPos? cell,
                IEffectDef effectDef,
                SkillExecutionTrace trace,
                int executionGroupId)
            {
                Command = command;
                Phase = phase;
                Source = source;
                Cell = cell;
                EffectDef = effectDef;
                Trace = trace;
                ExecutionGroupId = executionGroupId;
            }

            public IScoreCommand Command { get; }

            public ScorePhase Phase { get; }

            public ScoreSource Source { get; }

            public GridPos? Cell { get; }

            public IEffectDef EffectDef { get; }

            public SkillExecutionTrace Trace { get; }

            public int ExecutionGroupId { get; }
        }
    }

    /// <summary>结算期甜蜜传递 Buff。仅存于 ScoreContext，不写入食物实例或存档。</summary>
    public sealed class SweetTransferBuffRegistration
    {
        public SweetTransferBuffRegistration(
            DishInstance owner,
            SkillRuleDef rule,
            int conditionCount,
            IReadOnlyList<int> targetDishInstanceIds,
            ScoreSource source,
            SkillExecutionTrace trace)
        {
            Owner = owner;
            Rule = rule;
            ConditionCount = conditionCount;
            TargetDishInstanceIds = targetDishInstanceIds ?? Array.Empty<int>();
            Source = source;
            Trace = trace;
        }

        public DishInstance Owner { get; }

        public SkillRuleDef Rule { get; }

        public int ConditionCount { get; }

        public IReadOnlyList<int> TargetDishInstanceIds { get; }

        public ScoreSource Source { get; }

        public SkillExecutionTrace Trace { get; }
    }

    /// <summary>技能传递副作用：把外来子技能(Effects) 追加给某目标实例（可带来源名，用于「源名&lt;甜蜜传递&gt;」展示）。</summary>
    public sealed class SkillTransferSideEffect
    {
        public SkillTransferSideEffect(int targetInstanceId, IReadOnlyList<SkillEffect> effects, string sourceName = null, int sourceInstanceId = 0)
        {
            TargetInstanceId = targetInstanceId;
            Effects = effects ?? Array.Empty<SkillEffect>();
            SourceName = sourceName ?? string.Empty;
            SourceInstanceId = sourceInstanceId;
        }

        public int TargetInstanceId { get; }

        public IReadOnlyList<SkillEffect> Effects { get; }

        /// <summary>来来源食物名（非空时应用为「源名&lt;甜蜜传递&gt;」来源标签）。</summary>
        public string SourceName { get; }

        public int SourceInstanceId { get; }
    }

    /// <summary>
    /// 分数结算命令。效果通过提交命令改变结算状态，命令执行时也可以继续提交派生命令。
    /// </summary>
    public interface IScoreCommand
    {
        string Name { get; }

        void Execute(ScoreContext context);
    }

    /// <summary>指定食物加法区增加固定值。</summary>
    public sealed class AddDishFlatCommand : IScoreCommand
    {
        private readonly int _dishId;
        private readonly float _value;

        public AddDishFlatCommand(int dishId, float value)
        {
            _dishId = dishId;
            _value = value;
        }

        public string Name => "AddDishFlat";

        public void Execute(ScoreContext context) => context.ApplyDishFlatCommand(_dishId, _value);
    }

    /// <summary>指定食物倍率乘以固定值。</summary>
    public sealed class MultiplyDishCommand : IScoreCommand
    {
        private readonly int _dishId;
        private readonly float _value;

        public MultiplyDishCommand(int dishId, float value)
        {
            _dishId = dishId;
            _value = value;
        }

        public string Name => "MultiplyDish";

        public void Execute(ScoreContext context) => context.ApplyDishMultiplierCommand(_dishId, _value);
    }

    /// <summary>指定食物倍率区加法（倍率+X）。</summary>
    public sealed class AddDishMultFlatCommand : IScoreCommand
    {
        private readonly int _dishId;
        private readonly float _value;

        public AddDishMultFlatCommand(int dishId, float value)
        {
            _dishId = dishId;
            _value = value;
        }

        public string Name => "AddDishMultFlat";

        public void Execute(ScoreContext context) => context.ApplyDishMultFlatCommand(_dishId, _value);
    }

    /// <summary>获得金币（副作用）。</summary>
    public sealed class GrantGoldCommand : IScoreCommand
    {
        private readonly float _value;

        public GrantGoldCommand(float value)
        {
            _value = value;
        }

        public string Name => "GrantGold";

        public void Execute(ScoreContext context) => context.ApplyGrantGoldCommand(_value);
    }

    /// <summary>登记一次银材质 1/2 获得消耗品掷骰请求（副作用，不掷骰）。</summary>
    public sealed class RequestSilverItemRollCommand : IScoreCommand
    {
        public string Name => "RequestSilverItemRoll";

        public void Execute(ScoreContext context) => context.ApplyRequestSilverItemRollCommand();
    }

    /// <summary>全局欢乐蛋糕层数改动（副作用）。</summary>
    public sealed class ChangeHappyCakeLayerCommand : IScoreCommand
    {
        private readonly float _value;
        private readonly bool _mult;
        private readonly int _floor;

        public ChangeHappyCakeLayerCommand(float value, bool mult, int floor)
        {
            _value = value;
            _mult = mult;
            _floor = floor;
        }

        public string Name => "ChangeHappyCakeLayer";

        public void Execute(ScoreContext context) => context.ApplyHappyCakeLayerCommand(_value, _mult, _floor);
    }

    /// <summary>最终总分加法区增加固定值。</summary>
    public sealed class AddFinalFlatCommand : IScoreCommand
    {
        private readonly float _value;

        public AddFinalFlatCommand(float value)
        {
            _value = value;
        }

        public string Name => "AddFinalFlat";

        public void Execute(ScoreContext context) => context.ApplyFinalFlatCommand(_value);
    }

    /// <summary>最终总分倍率乘以固定值。</summary>
    public sealed class MultiplyFinalCommand : IScoreCommand
    {
        private readonly float _value;

        public MultiplyFinalCommand(float value)
        {
            _value = value;
        }

        public string Name => "MultiplyFinal";

        public void Execute(ScoreContext context) => context.ApplyFinalMultiplierCommand(_value);
    }

    /// <summary>立即解析另一个效果，用于“触发时再触发一个效果”的连锁结算。</summary>
    public sealed class ResolveScoreEffectCommand : IScoreCommand
    {
        private readonly ScoreEffectEntry _entry;

        public ResolveScoreEffectCommand(ScoreEffectEntry entry)
        {
            _entry = entry;
        }

        public string Name => "ResolveScoreEffect";

        public void Execute(ScoreContext context) => context.Apply(_entry);
    }
}
