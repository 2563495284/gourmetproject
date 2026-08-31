using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
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
        private const int MaxCommandChainDepth = 128;
        private const int MaxQueuedWorkItems = 65536;

        private sealed class DishAccumulator
        {
            public DishInstance Dish;
            public BigDouble Base;
            public BigDouble Flat;
            public BigDouble Mult = BigDouble.One;
            public int ExtraSettlementCount;
        }

        private readonly Dictionary<int, DishAccumulator> _accums = new Dictionary<int, DishAccumulator>();
        private readonly List<int> _order = new List<int>();
        private readonly List<DishScore> _dishScores = new List<DishScore>();
        private readonly List<ScoreLine> _lines;
        private readonly List<ScoreEvent> _events;
        private readonly Queue<PendingScoreWork> _commands = new Queue<PendingScoreWork>();
        private int _happyCakeLayerDelta;
        private readonly List<SkillTransferSideEffect> _skillTransfers = new List<SkillTransferSideEffect>();
        private readonly Dictionary<int, TransferredSkillExecutionView> _transferredSkillViews =
            new Dictionary<int, TransferredSkillExecutionView>();
        private readonly Dictionary<int, DishInstance> _dishesById = new Dictionary<int, DishInstance>();
        private readonly List<SweetTransferPermanentFlatRegistration> _sweetTransferPermanentFlats =
            new List<SweetTransferPermanentFlatRegistration>();
        private readonly List<SweetTransferBuffRegistration> _sweetTransferBuffs = new List<SweetTransferBuffRegistration>();
        private readonly Dictionary<int, List<SweetTransferBuffRegistration>> _sweetTransferBuffsByTarget =
            new Dictionary<int, List<SweetTransferBuffRegistration>>();
        private readonly Dictionary<SkillRuleDef, IReadOnlyList<SkillEffect>> _sweetTransferPayloads =
            new Dictionary<SkillRuleDef, IReadOnlyList<SkillEffect>>();
        private readonly Dictionary<int, IReadOnlyList<ScoreEffectEntry>> _sweetTransferSourceEffects =
            new Dictionary<int, IReadOnlyList<ScoreEffectEntry>>();
        private readonly Dictionary<TransferCandidateCacheKey, TransferCandidateSet> _transferCandidatesBySource =
            new Dictionary<TransferCandidateCacheKey, TransferCandidateSet>();
        private readonly Dictionary<TransferCandidateCacheKey, IReadOnlyList<DishInstance>> _stableRuleTargets =
            new Dictionary<TransferCandidateCacheKey, IReadOnlyList<DishInstance>>();
        private readonly Dictionary<TransferCandidateCacheKey, IReadOnlyList<DishInstance>> _sweetTransferSources =
            new Dictionary<TransferCandidateCacheKey, IReadOnlyList<DishInstance>>();
        private readonly List<CopySkillRequest> _copySkillRequests = new List<CopySkillRequest>();
        private readonly Dictionary<int, BigDouble> _permanentFlatDeltas = new Dictionary<int, BigDouble>();
        private readonly Dictionary<int, BigDouble> _permanentMultDeltas = new Dictionary<int, BigDouble>();
        private readonly Dictionary<int, int> _liveCountAs = new Dictionary<int, int>();
        private readonly Dictionary<int, HashSet<string>> _liveTemporaryCategories = new Dictionary<int, HashSet<string>>();
        private readonly List<TemporaryCategorySideEffect> _temporaryCategories = new List<TemporaryCategorySideEffect>();
        private readonly List<RecipeRemovalRequest> _recipeRemovalRequests = new List<RecipeRemovalRequest>();
        private int _emptyCountAsPerCell;
        private DishAccumulator _current;
        private bool _initialFinalModifiersRecorded;
        private bool _finalized;
        private bool _isResolvingCommands;
        private bool _hasActiveTransferredBatch;
        private TransferredReplayBatch _activeTransferredBatch;
        private int _currentCommandDepth;
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
            CaptureDiagnostics = snapshot.CaptureDiagnostics;
            CaptureCommandEvents = snapshot.CaptureCommandEvents;
            if (CaptureDiagnostics)
            {
                _lines = new List<ScoreLine>();
                _events = new List<ScoreEvent>();
            }

            // 预建全部菜的累加器，保证「A 改 B 的分」无论 B 是否已开始都有效。
            foreach (DishInstance dish in DiningTable.Dishes)
            {
                _dishesById[dish.Id] = dish;
            }

            foreach (DishInstance dish in snapshot.DishesInDefaultOrder)
            {
                EnsureAccumulator(dish);
            }

            // 初始化本次结算的「视为食物数」live 值；技能 AddCountAs 会在实际执行时继续修改。
            InitializeLiveCountAs(snapshot);
        }

        /// <summary>
        /// 本次结算每个食物的「视为食物数」实际值（下限 1）：静态定义 + 已持久化运行时加成 +
        /// 已执行的 AddCountAs 规则。AddCountAs 只影响其后的规则，且不跨结算持久。
        /// </summary>
        public int GetEffectiveCountAs(DishInstance dish)
        {
            if (dish == null)
            {
                return 1;
            }

            return _liveCountAs.TryGetValue(dish.Id, out int v) ? v : Math.Max(1, dish.EffectiveCountAs);
        }

        private void InitializeLiveCountAs(ScoreSnapshot snapshot)
        {
            int itemBonus = Math.Max(0, snapshot.ExtraCountAsPerDish);
            foreach (DishInstance d in snapshot.DishesInDefaultOrder)
            {
                _liveCountAs[d.Id] = Math.Max(1, d.Def.CountAs + d.RuntimeCountAsBonus + itemBonus);
            }
        }

        /// <summary>装饰品在结算开场为指定食物增加本次结算的有效份数。</summary>
        public void AddLiveCountAs(DishInstance dish, int delta)
        {
            if (dish == null || delta == 0)
            {
                return;
            }

            int before = GetEffectiveCountAs(dish);
            int after = Math.Max(1, before + delta);
            _liveCountAs[dish.Id] = after;
            int appliedDelta = after - before;
            if (CaptureDiagnostics && appliedDelta != 0)
            {
                AddLine(
                    EnsureAccumulator(dish),
                    ScoreLineKind.CountAs,
                    appliedDelta,
                    before,
                    after,
                    $"份数 {(appliedDelta >= 0 ? "+" : string.Empty)}{appliedDelta}");
            }
        }

        /// <summary>AddCountAs 在规则实际执行到时修改 live 值，只影响后续规则。</summary>
        public void ApplyLiveCountAs(
            SkillRuleDef rule,
            DishInstance self,
            int count,
            float value,
            IReadOnlyList<DishInstance> targets)
        {
            if (rule == null || self == null || count <= 0 || value == 0f || targets == null)
            {
                return;
            }

            foreach (DishInstance target in targets)
            {
                float basis;
                if (HasActionParam(rule, "target:occupiedcells"))
                {
                    int occupiedCellBasis = Math.Max(
                        0,
                        target.OccupiedCells.Count + ActionIntParam(rule, "offset", 0));
                    int divisor = Math.Max(1, ActionIntParam(rule, "div", 1));
                    basis = occupiedCellBasis / divisor;
                }
                else if (HasActionParam(rule, "source:target-skill-count"))
                {
                    basis = SkillConditionEvaluator.CountSubSkills(target, Db, this);
                }
                else
                {
                    basis = 1f;
                }

                int delta = (int)Math.Round(value * count * basis, MidpointRounding.AwayFromZero);
                if (delta == 0)
                {
                    continue;
                }

                AddLiveCountAs(target, delta);
            }
        }

        private static int ActionIntParam(SkillRuleDef rule, string key, int defaultValue)
        {
            if (rule?.ActionParams == null)
            {
                return defaultValue;
            }

            string prefix = key + ":";
            foreach (string param in rule.ActionParams)
            {
                if (string.IsNullOrEmpty(param)) continue;
                foreach (string raw in param.Split(';'))
                {
                    string segment = raw.Trim();
                    if (segment.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(segment.Substring(prefix.Length), out int value))
                    {
                        return value;
                    }
                }
            }

            return defaultValue;
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

        /// <summary>用规则运行时实际命中的食物刷新当前演出范围。</summary>
        public void UpdateTraceVisualTargets(IReadOnlyList<DishInstance> targets)
        {
            if (!CaptureDiagnostics || Trace == null || targets == null)
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

            Trace = Trace.WithVisualTargets(ids, cells);
        }

        public BigDouble FlatBonus => _current?.Flat ?? BigDouble.Zero;

        public BigDouble Multiplier => _current?.Mult ?? BigDouble.One;

        public BigDouble RawSum { get; private set; }

        public BigDouble FinalFlat { get; private set; }

        public BigDouble FinalMultiplier { get; private set; } = BigDouble.One;

        /// <summary>本次结算产生的金币增量（副作用，由 Game 层在正式结算后入账）。</summary>
        public float GoldDelta { get; private set; }

        public IReadOnlyList<DishScore> DishScores => _dishScores;

        public IReadOnlyList<ScoreLine> Lines => _lines != null
            ? (IReadOnlyList<ScoreLine>)_lines
            : Array.Empty<ScoreLine>();

        public IReadOnlyList<ScoreEvent> Events => _events != null
            ? (IReadOnlyList<ScoreEvent>)_events
            : Array.Empty<ScoreEvent>();

        /// <summary>是否捕获仅供解释和演出的结算诊断数据。</summary>
        public bool CaptureDiagnostics { get; }

        /// <summary>是否捕获每条底层命令的执行事件。</summary>
        public bool CaptureCommandEvents { get; }

        /// <summary>本场经营挑战开始时的全局欢乐蛋糕层数。</summary>
        public int InitialHappyCakeLayers { get; }

        /// <summary>本次结算产生的全局欢乐蛋糕层数增量（正式结算后由 Game 层写回经营挑战状态）。</summary>
        public int HappyCakeLayerDelta => _happyCakeLayerDelta;

        /// <summary>结算过程中「当前」的全局欢乐蛋糕层数（初始 + 已产生增量）。</summary>
        public int CurrentHappyCakeLayers => Math.Max(0, InitialHappyCakeLayers + _happyCakeLayerDelta);

        public IReadOnlyList<SkillTransferSideEffect> SkillTransfers => _skillTransfers;

        public IReadOnlyList<CopySkillRequest> CopySkillRequests => _copySkillRequests;

        public IReadOnlyList<TemporaryCategorySideEffect> TemporaryCategories => _temporaryCategories;

        public IReadOnlyList<RecipeRemovalRequest> RecipeRemovalRequests => _recipeRemovalRequests;

        /// <summary>当前结算中每个空格额外提供的有效份数；同类效果相加，结算结束即丢弃。</summary>
        public int EmptyCountAsPerCell => _emptyCountAsPerCell;

        public void AddEmptyCountAsPerCell(int value)
        {
            if (value > 0)
            {
                int before = _emptyCountAsPerCell;
                _emptyCountAsPerCell += value;
                if (CaptureDiagnostics)
                {
                    AddLine(
                        _current,
                        ScoreLineKind.EmptyCountAs,
                        value,
                        before,
                        _emptyCountAsPerCell,
                        $"每个空格视为 {_emptyCountAsPerCell} 份食物");
                }
            }
        }

        public int EmptyCountAsInScope(DishInstance self, SkillScope scope)
        {
            if (_emptyCountAsPerCell <= 0 || self == null)
            {
                return 0;
            }

            int emptyCells;
            if (scope == SkillScope.All || scope == SkillScope.Empty)
            {
                emptyCells = DiningTable.EmptyCellCount;
            }
            else
            {
                emptyCells = SkillConditionEvaluator.ScopeCells(DiningTable, self, scope)
                    .Count(DiningTable.IsEmpty);
            }

            return emptyCells * _emptyCountAsPerCell;
        }

        public bool IsCategory(DishInstance dish, string category)
        {
            if (dish == null || string.IsNullOrEmpty(category))
            {
                return false;
            }

            if (dish.IsCategory(category))
            {
                return true;
            }

            return _liveTemporaryCategories.TryGetValue(dish.Id, out HashSet<string> categories)
                && categories.Contains(category);
        }

        public void AddTemporaryCategory(
            DishInstance dish,
            string category,
            string sourceName = null,
            string effectDescription = null)
        {
            if (dish == null || string.IsNullOrEmpty(category))
            {
                return;
            }

            string categoryName = string.Equals(category, "cake", StringComparison.OrdinalIgnoreCase)
                ? "蛋糕"
                : category;
            string resolvedEffect = string.IsNullOrEmpty(effectDescription)
                ? $"视为{categoryName}"
                : effectDescription;
            bool alreadyCategory = IsCategory(dish, category);
            HashSet<string> categories = null;
            if (!alreadyCategory
                && !_liveTemporaryCategories.TryGetValue(dish.Id, out categories))
            {
                categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _liveTemporaryCategories[dish.Id] = categories;
            }

            if (!alreadyCategory && categories.Add(category))
            {
                string resolvedSource = string.IsNullOrEmpty(sourceName)
                    ? Source?.Name ?? string.Empty
                    : sourceName;
                _temporaryCategories.Add(new TemporaryCategorySideEffect(
                    dish.Id,
                    category,
                    resolvedSource,
                    resolvedEffect));
            }

            if (CaptureDiagnostics)
            {
                AddLine(
                    EnsureAccumulator(dish),
                    ScoreLineKind.TemporaryCategory,
                    1,
                    alreadyCategory ? 1 : 0,
                    1,
                    resolvedEffect);
                EmitEvent(ScoreEventType.CommandExecuted, $"{dish.Def.Name} 临时视为 {categoryName}");
            }
        }

        /// <summary>
        /// 仅在当前计分上下文中追加分类，不产出会写回 DishInstance 的副作用。
        /// 适用于“持有装饰品时，本次结算视为某分类”这类非持久效果。
        /// </summary>
        public void AddLiveCategory(DishInstance dish, string category)
        {
            if (dish == null || string.IsNullOrEmpty(category) || IsCategory(dish, category))
            {
                return;
            }

            if (!_liveTemporaryCategories.TryGetValue(dish.Id, out HashSet<string> categories))
            {
                categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _liveTemporaryCategories[dish.Id] = categories;
            }

            categories.Add(category);
        }

        public void RequestRecipeRemoval(DishInstance dish, float probability)
        {
            if (dish == null || dish.SourceDishIndex < 0 || probability <= 0f)
            {
                return;
            }

            _recipeRemovalRequests.Add(new RecipeRemovalRequest(
                dish.Id,
                dish.SourceDishIndex,
                dish.Def.Id,
                dish.Def.Name,
                probability));
            if (CaptureDiagnostics)
            {
                EmitEvent(ScoreEventType.CommandExecuted, $"登记 {dish.Def.Name} 营业后移除判定");
            }
        }

        /// <summary>本次结算登记的永久加法分增量（实例 Id → 累加值）。正式结算后写回实例。</summary>
        public IReadOnlyDictionary<int, BigDouble> PermanentFlatDeltas => _permanentFlatDeltas;

        /// <summary>本次结算登记的永久倍率增量（实例 Id → 累乘倍数）。正式结算后写回实例。</summary>
        public IReadOnlyDictionary<int, BigDouble> PermanentMultDeltas => _permanentMultDeltas;

        /// <summary>当前正在执行的根/嵌套效果批次，仅用于诊断与演出关联。</summary>
        internal int CurrentExecutionGroupId => _currentExecutionGroupId;

        public void EmitEvent(ScoreEventType type, string message)
        {
            if (!CaptureDiagnostics)
            {
                return;
            }

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
            if (CaptureDiagnostics)
            {
                EmitEvent(ScoreEventType.DishStarted, $"开始结算 {dish.Def.Name}");
            }
        }

        public void RecordDishBase()
        {
            if (Dish == null)
            {
                return;
            }

            Phase = ScorePhase.DishBase;
            Source = ScoreSource.Dish(Dish);
            BigDouble baseScore = Dish.BaseScoreBeforeSettlement;
            if (CaptureDiagnostics)
            {
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
        }

        /// <summary>
        /// 记录咸味命中。该行只作为“再次触发原生技能”的演出标记，不直接修改任何分数或倍率。
        /// </summary>
        public void RecordExtraSettlementTrigger(
            DishInstance dish,
            FlavorDef saltyFlavor)
        {
            if (dish == null || !_accums.TryGetValue(dish.Id, out DishAccumulator accumulator))
            {
                return;
            }

            int before = accumulator.ExtraSettlementCount;
            accumulator.ExtraSettlementCount++;
            if (CaptureDiagnostics)
            {
                _lines.Add(new ScoreLine(
                    ScorePhase.AfterDish,
                    ScoreLineKind.ExtraSettlement,
                    ScoreSource.DishFlavor(saltyFlavor, dish),
                    dish.Id,
                    dish.Def.Id,
                    null,
                    1f,
                    before,
                    accumulator.ExtraSettlementCount,
                    $"咸味额外结算第 {accumulator.ExtraSettlementCount} 次",
                    executionGroupId: ++_nextExecutionGroupId));
            }
        }

        public void Apply(ScoreEffectEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            ApplyEffect(
                entry.Phase,
                entry.Source,
                entry.Effect,
                entry.Dish,
                entry.EffectDef,
                entry.Cell,
                entry.Trace);
        }

        private void ApplyEffect(
            ScorePhase phase,
            ScoreSource source,
            IScoreEffect effect,
            DishInstance dish,
            IEffectDef effectDef,
            GridPos? cell,
            SkillExecutionTrace trace)
        {
            if (source == null || effect == null)
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
            if (dish != null)
            {
                Dish = dish;
                _current = EnsureAccumulator(dish);
            }

            Phase = phase;
            Source = source;
            CurrentCell = cell;
            EffectDef = effectDef;
            Trace = CaptureDiagnostics ? trace : null;
            _currentExecutionGroupId = ++_nextExecutionGroupId;
            if (CaptureDiagnostics)
            {
                EmitEvent(ScoreEventType.EffectStarted, $"开始效果 {Source.Name}");
            }
            try
            {
                effect.Apply(this);
                ResolveCommandQueue();
                if (CaptureDiagnostics)
                {
                    EmitEvent(ScoreEventType.EffectFinished, $"结束效果 {Source.Name}");
                }
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

        public void AddFlat(BigDouble value)
        {
            int id = Dish != null ? Dish.Id : 0;
            SubmitBuiltInCommand(ScoreCommandKind.AddDishFlat, id, value);
        }

        public void MultiplyBy(BigDouble value)
        {
            int id = Dish != null ? Dish.Id : 0;
            SubmitBuiltInCommand(ScoreCommandKind.MultiplyDish, id, value);
        }

        // ------- 跨菜 / 副作用 API -------

        public void AddFlatTo(DishInstance target, BigDouble value)
        {
            if (target == null)
            {
                return;
            }

            SubmitBuiltInCommand(ScoreCommandKind.AddDishFlat, target.Id, value);
        }

        public void MultiplyTo(DishInstance target, BigDouble value)
        {
            if (target == null)
            {
                return;
            }

            SubmitBuiltInCommand(ScoreCommandKind.MultiplyDish, target.Id, value);
        }

        /// <summary>目标食物「倍率区」加法（倍率+X），区别于乘法的 MultiplyTo。</summary>
        public void AddMultFlatTo(DishInstance target, BigDouble value)
        {
            if (target == null)
            {
                return;
            }

            SubmitBuiltInCommand(ScoreCommandKind.AddDishMultFlat, target.Id, value);
        }

        /// <summary>读取目标食物结算到当前时刻的倍率（含固化倍率与此前已执行的倍率效果）。</summary>
        public BigDouble GetCurrentMultiplier(DishInstance target)
        {
            if (target == null || !_accums.TryGetValue(target.Id, out DishAccumulator accumulator))
            {
                return BigDouble.Zero;
            }

            return accumulator.Mult;
        }

        /// <summary>读取目标食物结算到当前时刻的分数（基础分 + 此前已执行的固定加分，不含倍率）。</summary>
        public BigDouble GetCurrentScore(DishInstance target)
        {
            if (target == null || !_accums.TryGetValue(target.Id, out DishAccumulator accumulator))
            {
                return BigDouble.Zero;
            }

            return accumulator.Base + accumulator.Flat;
        }

        public void GrantGold(float value)
        {
            SubmitBuiltInCommand(ScoreCommandKind.GrantGold, floatValue: value);
        }

        public void AddFinalFlat(BigDouble value)
        {
            SubmitBuiltInCommand(ScoreCommandKind.AddFinalFlat, bigValue: value);
        }

        public void MultiplyFinalBy(BigDouble value)
        {
            SubmitBuiltInCommand(ScoreCommandKind.MultiplyFinal, bigValue: value);
        }

        /// <summary>全局「欢乐蛋糕层数」改动（副作用，正式结算后写回经营挑战状态）。
        /// mult=true 时按乘法（可选 floor 表示至少净增 floor 层）。目标食物无关，全局共享一个计数器。</summary>
        public void AddHappyCakeLayers(float value, bool mult, int floor = 0)
        {
            SubmitBuiltInCommand(
                ScoreCommandKind.ChangeHappyCakeLayer,
                floatValue: value,
                boolValue: mult,
                intValue: floor);
        }

        /// <summary>
        /// 目标食物「永久加法分」+value：本次结算即计入加法区，并登记持久增量（正式结算后写回实例，之后每次结算叠加进基础分）。
        /// </summary>
        public void AddPermanentFlatTo(DishInstance target, BigDouble value)
            => AddPermanentFlatTo(target, value, null);

        private void AddPermanentFlatTo(
            DishInstance target,
            BigDouble value,
            ScoreSource sourceOverride)
        {
            if (target == null || BigDouble.Abs(value) < 0.0001d)
            {
                return;
            }

            _permanentFlatDeltas.TryGetValue(target.Id, out BigDouble cur);
            _permanentFlatDeltas[target.Id] = cur + value;
            SubmitBuiltInCommand(
                ScoreCommandKind.AddDishPermanentFlat,
                target.Id,
                value,
                sourceOverride: sourceOverride);
        }

        /// <summary>
        /// 登记甜蜜传递成功后才响应的永久分装饰品。登记本身不改分，来源保留为当前装饰品，
        /// 供真正触发时生成可解释明细和驱动装饰品演出。
        /// </summary>
        internal void RegisterSweetTransferPermanentFlat(ItemScoreEffectType type, BigDouble value)
        {
            if ((type != ItemScoreEffectType.SweetTransferTargetPermanentFlat
                 && type != ItemScoreEffectType.SweetTransferSourcePermanentFlat)
                || BigDouble.Abs(value) < 0.0001d)
            {
                return;
            }

            _sweetTransferPermanentFlats.Add(new SweetTransferPermanentFlatRegistration(
                type,
                value,
                Source));
        }

        /// <summary>
        /// 一轮甜蜜传递的全部交接与外来技能执行完后应用永久分：接收方逐个增加，
        /// 传递方按本轮成功目标数累计。此时提交标准永久分命令，因此本轮计分立即包含并登记持久增量。
        /// </summary>
        internal void ApplySweetTransferPermanentFlats(
            DishInstance transferSource,
            IReadOnlyList<DishInstance> transferTargets)
        {
            if (transferSource == null
                || transferTargets == null
                || transferTargets.Count == 0
                || _sweetTransferPermanentFlats.Count == 0)
            {
                return;
            }

            foreach (SweetTransferPermanentFlatRegistration registration in _sweetTransferPermanentFlats)
            {
                if (registration.Type == ItemScoreEffectType.SweetTransferTargetPermanentFlat)
                {
                    foreach (DishInstance target in transferTargets)
                    {
                        AddPermanentFlatTo(target, registration.Value, registration.Source);
                    }
                }
                else if (registration.Type == ItemScoreEffectType.SweetTransferSourcePermanentFlat)
                {
                    AddPermanentFlatTo(
                        transferSource,
                        registration.Value * transferTargets.Count,
                        registration.Source);
                }
            }
        }

        /// <summary>
        /// 目标食物「永久倍率」×value：本次结算即计入倍率，并登记持久倍数（正式结算后写回实例，之后每次结算叠乘进倍率初值）。
        /// </summary>
        public void AddPermanentMultTo(DishInstance target, BigDouble value)
        {
            if (target == null || value <= BigDouble.Zero)
            {
                return;
            }

            _permanentMultDeltas.TryGetValue(target.Id, out BigDouble cur);
            _permanentMultDeltas[target.Id] = (cur <= BigDouble.Zero ? BigDouble.One : cur) * value;
            SubmitBuiltInCommand(ScoreCommandKind.MultiplyDish, target.Id, value);
        }

        /// <summary>登记技能传递（副作用，正式结算后应用到实例的运行时技能集）。</summary>
        public void RecordSkillTransfer(
            DishInstance target,
            IReadOnlyList<SkillEffect> effects,
            string sourceName = null,
            int sourceInstanceId = 0,
            int handoffExecutionGroupId = 0)
        {
            if (target == null || effects == null || effects.Count == 0)
            {
                return;
            }

            var transfer = new SkillTransferSideEffect(
                target.Id,
                effects,
                sourceName,
                sourceInstanceId,
                handoffExecutionGroupId);
            _skillTransfers.Add(transfer);

            TransferredSkillExecutionView view = GetOrCreateTransferredSkillView(target);
            string sourceLabel = string.IsNullOrEmpty(sourceName)
                ? string.Empty
                : $"{sourceName}<甜蜜传递>";
            foreach (SkillEffect effect in effects)
            {
                if (effect?.Rule != null)
                {
                    view.Add(CreateTransferredSkillTemplate(
                        target,
                        new TransferredSkill(effect, sourceLabel, sourceInstanceId)));
                }
            }
            if (CaptureDiagnostics)
            {
                EmitEvent(ScoreEventType.CommandExecuted, $"技能传递给 {target.Def.Name}（{effects.Count} 个）");
            }
        }

        /// <summary>
        /// 返回目标在当前计算中已经获得的全部甜蜜传递子技能。
        /// 顺序固定为实例上已持久化的技能，再按本轮传递发生顺序追加待提交技能；
        /// 只构造结算视图，不修改实例，保证预览仍为纯计算。
        /// </summary>
        internal IReadOnlyList<TransferredSkill> TransferredSkillsForCurrentCalculation(DishInstance target)
        {
            if (target == null)
            {
                return Array.Empty<TransferredSkill>();
            }

            return GetOrCreateTransferredSkillView(target);
        }

        internal IReadOnlyList<SkillEffect> SweetTransferPayloadFor(SkillRuleDef transferRule)
        {
            if (transferRule == null)
            {
                return Array.Empty<SkillEffect>();
            }

            if (!_sweetTransferPayloads.TryGetValue(
                    transferRule,
                    out IReadOnlyList<SkillEffect> payload))
            {
                payload = SkillRuleEffect.EffectsToTransfer(Db, transferRule);
                _sweetTransferPayloads[transferRule] = payload;
            }

            return payload;
        }

        internal IReadOnlyList<ScoreEffectEntry> SweetTransferEffectsForSource(DishInstance sourceDish)
        {
            if (sourceDish == null || sourceDish.SkillsDisabled)
            {
                return Array.Empty<ScoreEffectEntry>();
            }

            if (_sweetTransferSourceEffects.TryGetValue(
                    sourceDish.Id,
                    out IReadOnlyList<ScoreEffectEntry> cached))
            {
                return cached;
            }

            var entries = new List<ScoreEffectEntry>();
            int boardOrder = sourceDish.Placement.Origin.Y * DiningTable.Width
                + sourceDish.Placement.Origin.X;
            foreach (string skillId in sourceDish.SkillIds)
            {
                SkillDef skill = Db.GetSkill(skillId);
                if (skill == null || !skill.HasRules)
                {
                    continue;
                }

                foreach (SkillRuleDef transferRule in skill.Rules)
                {
                    if (transferRule.Trigger != SkillTrigger.OnSettle
                        || transferRule.ActionType != SkillActionType.TransferSkills)
                    {
                        continue;
                    }

                    string sourceLabel = sourceDish.GetSkillSource(skillId);
                    SkillExecutionKind skillKind = SkillRuleEffectSource.KindForDishSkill(
                        sourceDish,
                        skillId,
                        sourceLabel);
                    if (skillKind == SkillExecutionKind.NativeSkill)
                    {
                        sourceLabel = null;
                    }

                    ScoreSource scoreSource = string.IsNullOrEmpty(sourceLabel)
                        ? ScoreSource.DishSkill(skill, sourceDish)
                        : ScoreSource.TransferredDishSkill(skill, sourceDish, sourceLabel);
                    entries.Add(new ScoreEffectEntry(
                        ScorePhase.DishSkills,
                        scoreSource,
                        new SkillRuleEffect(transferRule, sourceDish),
                        sourceDish,
                        null,
                        null,
                        transferRule.Order,
                        boardOrder,
                        CaptureDiagnostics
                            ? SkillExecutionTrace.Create(
                                Db,
                                DiningTable,
                                sourceDish,
                                sourceDish,
                                skill,
                                transferRule,
                                skillKind,
                                sourceLabel,
                                SkillScopeVisualMode.CandidateScope)
                            : null,
                        skillKind));
                }
            }

            cached = entries;
            _sweetTransferSourceEffects[sourceDish.Id] = cached;
            return cached;
        }

        internal bool TryGetTransferCandidates(
            DishInstance source,
            SkillRuleDef rule,
            out TransferCandidateSet candidates)
        {
            candidates = null;
            return source != null
                && rule != null
                && _transferCandidatesBySource.TryGetValue(
                    new TransferCandidateCacheKey(source.Id, rule),
                    out candidates);
        }

        internal void CacheTransferCandidates(
            DishInstance source,
            SkillRuleDef rule,
            TransferCandidateSet candidates)
        {
            if (source == null || rule == null || candidates == null)
            {
                return;
            }

            _transferCandidatesBySource[new TransferCandidateCacheKey(source.Id, rule)] = candidates;
        }

        internal bool TryGetStableRuleTargets(
            DishInstance self,
            SkillRuleDef rule,
            out IReadOnlyList<DishInstance> targets)
        {
            targets = null;
            return self != null
                && rule != null
                && _stableRuleTargets.TryGetValue(
                    new TransferCandidateCacheKey(self.Id, rule),
                    out targets);
        }

        internal void CacheStableRuleTargets(
            DishInstance self,
            SkillRuleDef rule,
            IReadOnlyList<DishInstance> targets)
        {
            if (self != null && rule != null && targets != null)
            {
                _stableRuleTargets[new TransferCandidateCacheKey(self.Id, rule)] = targets;
            }
        }

        internal bool TryGetSweetTransferSources(
            DishInstance self,
            SkillRuleDef rule,
            out IReadOnlyList<DishInstance> sources)
        {
            sources = null;
            return self != null
                && rule != null
                && _sweetTransferSources.TryGetValue(
                    new TransferCandidateCacheKey(self.Id, rule),
                    out sources);
        }

        internal void CacheSweetTransferSources(
            DishInstance self,
            SkillRuleDef rule,
            IReadOnlyList<DishInstance> sources)
        {
            if (self != null && rule != null && sources != null)
            {
                _sweetTransferSources[new TransferCandidateCacheKey(self.Id, rule)] = sources;
            }
        }

        /// <summary>
        /// 将一次“收到新技能后重算当前全部外来技能”压缩为单个物理工作项。
        /// Count 在入队时冻结，后续传递追加到同一视图也不会被更早批次看到。
        /// </summary>
        internal void ResolveTransferredEffectsBatch(
            DishInstance target,
            int handoffSourceDishInstanceId,
            int handoffExecutionGroupId,
            string handoffSkillId,
            int handoffPayloadCount)
        {
            if (target == null)
            {
                return;
            }

            TransferredSkillExecutionView view = GetOrCreateTransferredSkillView(target);
            int countAtEnqueue = view.Count;
            if (!view.HasExecutableRule(countAtEnqueue))
            {
                return;
            }

            int depth = NextCommandDepth();
            var batch = new TransferredReplayBatch(
                view,
                countAtEnqueue,
                handoffSourceDishInstanceId,
                handoffExecutionGroupId,
                handoffSkillId,
                handoffPayloadCount,
                Phase,
                Source,
                CurrentCell,
                EffectDef,
                Trace,
                _currentExecutionGroupId,
                depth,
                expandBeforeQueuedWork: _isResolvingCommands);
            EnqueueWork(PendingScoreWork.ForTransferredBatch(batch), depth, "TransferredSkillReplayBatch");
            ResolveCommandQueue();
        }

        private TransferredSkillExecutionView GetOrCreateTransferredSkillView(DishInstance target)
        {
            if (_transferredSkillViews.TryGetValue(
                    target.Id,
                    out TransferredSkillExecutionView view))
            {
                return view;
            }

            view = new TransferredSkillExecutionView();
            foreach (TransferredSkill transferred in target.TransferredSkills)
            {
                view.Add(CreateTransferredSkillTemplate(target, transferred));
            }

            _transferredSkillViews[target.Id] = view;
            return view;
        }

        private TransferredSkillExecutionTemplate CreateTransferredSkillTemplate(
            DishInstance target,
            TransferredSkill transferred)
        {
            SkillRuleDef rule = transferred?.Rule;
            if (rule == null || rule.Trigger != SkillTrigger.OnSettle)
            {
                return new TransferredSkillExecutionTemplate(transferred, null, null, null, target);
            }

            SkillDef parent = Db.GetSkill(rule.SkillId);
            string sourceLabel = transferred.SourceLabel;
            SkillExecutionTrace trace = null;
            if (CaptureDiagnostics)
            {
                _dishesById.TryGetValue(transferred.SourceInstanceId, out DishInstance owner);
                trace = owner != null
                    ? SkillExecutionTrace.Create(
                        Db,
                        DiningTable,
                        owner,
                        target,
                        parent,
                        rule,
                        SkillExecutionKind.SweetTransfer,
                        sourceLabel,
                        SkillScopeVisualMode.ResolvedTargets)
                    : SkillExecutionTrace.CreateWithOwnerFallback(
                        Db,
                        DiningTable,
                        transferred.SourceInstanceId,
                        SourceNameWithoutTag(sourceLabel),
                        target,
                        parent,
                        rule,
                        SkillExecutionKind.SweetTransfer,
                        sourceLabel,
                        SkillScopeVisualMode.ResolvedTargets);
            }

            return new TransferredSkillExecutionTemplate(
                transferred,
                ScoreSource.TransferredDishSkill(parent, target, sourceLabel),
                new SkillRuleEffect(rule, target),
                trace,
                target);
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

            SkillExecutionTrace trace = CaptureDiagnostics
                ? Trace?.WithVisualTargets(ids, cells)
                : null;
            var registration = new SweetTransferBuffRegistration(
                owner,
                rule,
                conditionCount,
                ids,
                Source,
                trace,
                _sweetTransferBuffs.Count);
            _sweetTransferBuffs.Add(registration);
            foreach (int targetId in ids)
            {
                if (!_sweetTransferBuffsByTarget.TryGetValue(
                        targetId,
                        out List<SweetTransferBuffRegistration> registrations))
                {
                    registrations = new List<SweetTransferBuffRegistration>();
                    _sweetTransferBuffsByTarget[targetId] = registrations;
                }

                registrations.Add(registration);
            }
            if (CaptureDiagnostics)
            {
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
        }

        public IReadOnlyList<SweetTransferBuffRegistration> SweetTransferBuffsFor(DishInstance source)
        {
            if (source == null
                || !_sweetTransferBuffsByTarget.TryGetValue(
                    source.Id,
                    out List<SweetTransferBuffRegistration> registrations))
            {
                return Array.Empty<SweetTransferBuffRegistration>();
            }

            return registrations;
        }

        public IReadOnlyList<SweetTransferBuffRegistration> SweetTransferReceiverBuffsFor(
            IReadOnlyList<DishInstance> transferTargets)
        {
            if (transferTargets == null || transferTargets.Count == 0 || _sweetTransferBuffs.Count == 0)
            {
                return Array.Empty<SweetTransferBuffRegistration>();
            }

            var matches = new HashSet<SweetTransferBuffRegistration>();
            foreach (DishInstance target in transferTargets)
            {
                if (target == null
                    || !_sweetTransferBuffsByTarget.TryGetValue(
                        target.Id,
                        out List<SweetTransferBuffRegistration> registrations))
                {
                    continue;
                }

                foreach (SweetTransferBuffRegistration registration in registrations)
                {
                    if (registration?.Rule != null
                        && HasActionParam(registration.Rule, "when:receive-transfer"))
                    {
                        matches.Add(registration);
                    }
                }
            }

            return matches
                .OrderBy(registration => registration.RegistrationOrder)
                .ToArray();
        }

        public void RecordSweetTransferBuffTriggered(
            SweetTransferBuffRegistration registration,
            DishInstance transferSource,
            float value,
            IReadOnlyList<DishInstance> visualTargets)
        {
            if (!CaptureDiagnostics || registration?.Owner == null || transferSource == null)
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

            // ResolveSweetTransferBuffTrigger 会在进入嵌套响应效果前，把本次真实交接的
            // handoff group 写入当前 Trace。优先继承它；回退到 registration.Trace 仅用于
            // 兼容没有经过标准响应效果入口的旧调用。否则这里重建 trace 会丢掉波次关联。
            SkillExecutionTrace trace = (Trace ?? registration.Trace)?.WithRuntimeContext(
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
            if (!CaptureDiagnostics || sourceDish == null)
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
            if (!CaptureDiagnostics || sourceDish == null || sourceCount <= 0)
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
            if (!CaptureDiagnostics || sourceDish == null || index <= 0 || total <= 0)
            {
                return;
            }

            ScoreSource source = Source;
            SkillExecutionTrace trace = Trace;
            foreach (ScoreEffectEntry transferEntry in SweetTransferEffectsForSource(sourceDish))
            {
                source = transferEntry.Source;
                trace = transferEntry.Trace;
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

            SubmitScoreEffect(entry);
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
            if (CaptureDiagnostics)
            {
                DishAccumulator accum = EnsureAccumulator(target);
                AddLine(accum, ScoreLineKind.CopySkill, selected.Count, target.SkillIds.Count, target.SkillIds.Count + selected.Count, $"复制技能 +{selected.Count}");
                EmitEvent(ScoreEventType.CommandExecuted, $"技能复制给 {target.Def.Name}（{selected.Count} 个）");
            }
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
                        CaptureDiagnostics
                            ? SkillExecutionTrace.Create(
                                Db,
                                DiningTable,
                                target,
                                target,
                                skill,
                                rule,
                                SkillExecutionKind.CopiedSkill,
                                sourceLabel,
                                SkillScopeVisualMode.ResolvedTargets)
                            : null,
                        SkillExecutionKind.CopiedSkill);
                    SubmitScoreEffect(entry);
                }
            }
        }

        public void SubmitCommand(IScoreCommand command)
            => SubmitCommand(command, null);

        private void SubmitCommand(IScoreCommand command, ScoreSource sourceOverride)
        {
            if (command == null)
            {
                return;
            }

            int depth = NextCommandDepth();
            PendingScoreCommand pending = PendingScoreCommand.ForExternal(
                command,
                Phase,
                sourceOverride ?? Source,
                CurrentCell,
                EffectDef,
                Trace,
                _currentExecutionGroupId,
                depth);
            EnqueueWork(PendingScoreWork.ForCommand(pending), depth, command.Name);
            ResolveCommandQueue();
        }

        private void SubmitBuiltInCommand(
            ScoreCommandKind kind,
            int dishId = 0,
            BigDouble bigValue = default,
            float floatValue = 0f,
            bool boolValue = false,
            int intValue = 0,
            ScoreSource sourceOverride = null)
        {
            int depth = NextCommandDepth();
            PendingScoreCommand pending = PendingScoreCommand.ForBuiltIn(
                kind,
                dishId,
                bigValue,
                floatValue,
                boolValue,
                intValue,
                Phase,
                sourceOverride ?? Source,
                CurrentCell,
                EffectDef,
                Trace,
                _currentExecutionGroupId,
                depth);
            EnqueueWork(PendingScoreWork.ForCommand(pending), depth, pending.Name);
            ResolveCommandQueue();
        }

        private void SubmitScoreEffect(ScoreEffectEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            int depth = NextCommandDepth();
            PendingScoreCommand pending = PendingScoreCommand.ForScoreEffect(
                entry,
                Phase,
                Source,
                CurrentCell,
                EffectDef,
                Trace,
                _currentExecutionGroupId,
                depth);
            EnqueueWork(PendingScoreWork.ForCommand(pending), depth, pending.Name);
            ResolveCommandQueue();
        }

        private int NextCommandDepth() => _isResolvingCommands ? _currentCommandDepth + 1 : 1;

        private int PhysicalPendingWorkCount => _commands.Count + (_hasActiveTransferredBatch ? 1 : 0);

        private void EnqueueWork(PendingScoreWork work, int depth, string commandType)
        {
            int queueSize = PhysicalPendingWorkCount;
            if (queueSize >= MaxQueuedWorkItems)
            {
                throw CreateCommandSafetyException(
                    "physical pending work limit exceeded",
                    depth,
                    queueSize,
                    commandType);
            }

            _commands.Enqueue(work);
        }

        private static InvalidOperationException CreateCommandSafetyException(
            string reason,
            int depth,
            int queueSize,
            string commandType)
        {
            return new InvalidOperationException(
                $"Score command safety limit: {reason}; depth={depth}; "
                + $"physicalQueue={queueSize}; commandType={commandType ?? "<unknown>"}.");
        }

        /// <summary>逐菜阶段结束（仅发事件；分数定稿延迟到 FinalizeDishes）。</summary>
        public void CompleteDish()
        {
            if (Dish == null)
            {
                return;
            }

            if (CaptureDiagnostics)
            {
                EmitEvent(ScoreEventType.DishCompleted, $"{Dish.Def.Name} 阶段结束");
            }
        }

        /// <summary>所有逐菜阶段跑完后，统一把每个食物的累加器定稿为贡献并求和。</summary>
        public void FinalizeDishes()
        {
            if (_finalized)
            {
                return;
            }

            _finalized = true;
            RawSum = BigDouble.Zero;
            foreach (int id in _order)
            {
                DishAccumulator a = _accums[id];
                var score = new DishScore(
                    a.Dish.Id,
                    a.Dish.Def.Id,
                    a.Base,
                    a.Flat,
                    a.Mult,
                    GetEffectiveCountAs(a.Dish),
                    BigDouble.Zero,
                    a.ExtraSettlementCount);
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
            if (!CaptureDiagnostics)
            {
                return;
            }

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
            return new ScoreResult(
                _dishScores,
                RawSum,
                FinalFlat,
                FinalMultiplier,
                scoreLines: Lines,
                scoreEvents: Events,
                goldDelta: GoldDelta,
                happyCakeLayerDelta: _happyCakeLayerDelta,
                skillTransfers: _skillTransfers,
                permanentFlatDeltas: _permanentFlatDeltas,
                permanentMultDeltas: _permanentMultDeltas,
                copySkillRequests: _copySkillRequests,
                temporaryCategories: _temporaryCategories,
                recipeRemovalRequests: _recipeRemovalRequests);
        }

        // ------- 命令实际改分（internal，供命令调用） -------

        internal void ApplyDishFlatCommand(int dishId, BigDouble value)
        {
            if (!_accums.TryGetValue(dishId, out DishAccumulator a))
            {
                return;
            }

            BigDouble before = a.Flat;
            a.Flat += value;
            if (CaptureDiagnostics)
            {
                AddLine(a, ScoreLineKind.DishFlat, value, before, a.Flat, $"美味值 +{value}");
            }
        }

        internal void ApplyDishPermanentFlatCommand(int dishId, BigDouble value)
        {
            if (!_accums.TryGetValue(dishId, out DishAccumulator a))
            {
                return;
            }

            BigDouble before = a.Flat;
            a.Flat += value;
            if (CaptureDiagnostics)
            {
                AddLine(a, ScoreLineKind.DishPermanentFlat, value, before, a.Flat, $"永久美味值 +{value}");
            }
        }

        internal void ApplyDishMultiplierCommand(int dishId, BigDouble value)
        {
            if (!_accums.TryGetValue(dishId, out DishAccumulator a))
            {
                return;
            }

            BigDouble before = a.Mult;
            a.Mult *= value;
            if (CaptureDiagnostics)
            {
                AddLine(a, ScoreLineKind.DishMultiplier, value, before, a.Mult, $"倍率 x{value}");
            }
        }

        internal void ApplyDishMultFlatCommand(int dishId, BigDouble value)
        {
            if (!_accums.TryGetValue(dishId, out DishAccumulator a))
            {
                return;
            }

            BigDouble before = a.Mult;
            a.Mult += value;
            if (CaptureDiagnostics)
            {
                AddLine(a, ScoreLineKind.DishMultiplierAdd, value, before, a.Mult, $"倍率 +{value}");
            }
        }

        internal void ApplyGrantGoldCommand(float value)
        {
            float before = GoldDelta;
            GoldDelta += value;
            if (CaptureDiagnostics)
            {
                AddLine(_current, ScoreLineKind.Gold, value, before, GoldDelta, $"获得金币 +{value}");
            }
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
            if (CaptureDiagnostics)
            {
                AddLine(_current, ScoreLineKind.Layer, after - before, before, after, mult ? $"欢乐蛋糕层数 x{value}" : $"欢乐蛋糕层数 {(value >= 0 ? "+" : string.Empty)}{value}");
            }
        }

        internal void ApplyFinalFlatCommand(BigDouble value)
        {
            BigDouble before = FinalFlat;
            FinalFlat += value;
            if (CaptureDiagnostics)
            {
                AddLine(null, ScoreLineKind.FinalFlat, value, before, FinalFlat, $"总分 +{value}");
            }
        }

        internal void ApplyFinalMultiplierCommand(BigDouble value)
        {
            BigDouble before = FinalMultiplier;
            FinalMultiplier *= value;
            if (CaptureDiagnostics)
            {
                AddLine(null, ScoreLineKind.FinalMultiplier, value, before, FinalMultiplier, $"总分倍率 x{value}");
            }
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
                while (TryDequeuePendingCommand(out PendingScoreCommand pending))
                {
                    if (pending.Depth > MaxCommandChainDepth)
                    {
                        throw CreateCommandSafetyException(
                            "command chain depth exceeded",
                            pending.Depth,
                            PhysicalPendingWorkCount,
                            pending.Name);
                    }

                    ScorePhase previousPhase = Phase;
                    ScoreSource previousSource = Source;
                    GridPos? previousCell = CurrentCell;
                    IEffectDef previousEffectDef = EffectDef;
                    SkillExecutionTrace previousTrace = Trace;
                    int previousExecutionGroupId = _currentExecutionGroupId;
                    int previousCommandDepth = _currentCommandDepth;
                    Phase = pending.Phase;
                    Source = pending.Source;
                    CurrentCell = pending.Cell;
                    EffectDef = pending.EffectDef;
                    Trace = CaptureDiagnostics ? pending.Trace : null;
                    _currentExecutionGroupId = pending.ExecutionGroupId;
                    _currentCommandDepth = pending.Depth;
                    try
                    {
                        if (CaptureCommandEvents)
                        {
                            EmitEvent(ScoreEventType.CommandExecuted, $"执行命令 {pending.Name}");
                        }
                        ExecutePendingCommand(pending);
                    }
                    finally
                    {
                        Phase = previousPhase;
                        Source = previousSource;
                        CurrentCell = previousCell;
                        EffectDef = previousEffectDef;
                        Trace = previousTrace;
                        _currentExecutionGroupId = previousExecutionGroupId;
                        _currentCommandDepth = previousCommandDepth;
                    }
                }
            }
            finally
            {
                _isResolvingCommands = false;
            }
        }

        private bool TryDequeuePendingCommand(out PendingScoreCommand pending)
        {
            while (true)
            {
                bool shouldExpandActiveBatch = _hasActiveTransferredBatch
                    && (_activeTransferredBatch.ExpandBeforeQueuedWork || _commands.Count == 0);
                if (shouldExpandActiveBatch)
                {
                    if (_activeTransferredBatch.TryTakeNext(CaptureDiagnostics, out pending))
                    {
                        return true;
                    }

                    _hasActiveTransferredBatch = false;
                    continue;
                }

                if (_commands.Count == 0)
                {
                    pending = default(PendingScoreCommand);
                    return false;
                }

                PendingScoreWork work = _commands.Dequeue();
                if (work.Kind == PendingScoreWorkKind.Command)
                {
                    pending = work.Command;
                    return true;
                }

                _activeTransferredBatch = work.TransferredBatch;
                _hasActiveTransferredBatch = true;
            }
        }

        private void ExecutePendingCommand(PendingScoreCommand pending)
        {
            switch (pending.Kind)
            {
                case ScoreCommandKind.External:
                    pending.ExternalCommand.Execute(this);
                    break;
                case ScoreCommandKind.AddDishFlat:
                    ApplyDishFlatCommand(pending.DishId, pending.BigValue);
                    break;
                case ScoreCommandKind.AddDishPermanentFlat:
                    ApplyDishPermanentFlatCommand(pending.DishId, pending.BigValue);
                    break;
                case ScoreCommandKind.MultiplyDish:
                    ApplyDishMultiplierCommand(pending.DishId, pending.BigValue);
                    break;
                case ScoreCommandKind.AddDishMultFlat:
                    ApplyDishMultFlatCommand(pending.DishId, pending.BigValue);
                    break;
                case ScoreCommandKind.GrantGold:
                    ApplyGrantGoldCommand(pending.FloatValue);
                    break;
                case ScoreCommandKind.ChangeHappyCakeLayer:
                    ApplyHappyCakeLayerCommand(
                        pending.FloatValue,
                        pending.BoolValue,
                        pending.IntValue);
                    break;
                case ScoreCommandKind.AddFinalFlat:
                    ApplyFinalFlatCommand(pending.BigValue);
                    break;
                case ScoreCommandKind.MultiplyFinal:
                    ApplyFinalMultiplierCommand(pending.BigValue);
                    break;
                case ScoreCommandKind.ResolveScoreEffect:
                    Apply(pending.ScoreEffectEntry);
                    break;
                case ScoreCommandKind.ResolveTransferredTemplate:
                    TransferredSkillExecutionTemplate template = pending.TransferredTemplate;
                    ApplyEffect(
                        ScorePhase.DishSkills,
                        template.Source,
                        template.Effect,
                        template.Target,
                        null,
                        null,
                        pending.TransferredTrace);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown score command kind: {pending.Kind}.");
            }
        }

        private void AddLine(
            DishAccumulator accum,
            ScoreLineKind kind,
            BigDouble value,
            BigDouble before,
            BigDouble after,
            string fallbackMessage,
            ScoreSource sourceOverride = null,
            SkillExecutionTrace traceOverride = null)
        {
            if (!CaptureDiagnostics)
            {
                return;
            }

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

        private enum ScoreCommandKind
        {
            External,
            AddDishFlat,
            AddDishPermanentFlat,
            MultiplyDish,
            AddDishMultFlat,
            GrantGold,
            ChangeHappyCakeLayer,
            AddFinalFlat,
            MultiplyFinal,
            ResolveScoreEffect,
            ResolveTransferredTemplate,
        }

        private enum PendingScoreWorkKind
        {
            Command,
            TransferredBatch,
        }

        private readonly struct PendingScoreWork
        {
            private PendingScoreWork(
                PendingScoreWorkKind kind,
                PendingScoreCommand command,
                TransferredReplayBatch transferredBatch)
            {
                Kind = kind;
                Command = command;
                TransferredBatch = transferredBatch;
            }

            public PendingScoreWorkKind Kind { get; }

            public PendingScoreCommand Command { get; }

            public TransferredReplayBatch TransferredBatch { get; }

            public static PendingScoreWork ForCommand(PendingScoreCommand command)
                => new PendingScoreWork(
                    PendingScoreWorkKind.Command,
                    command,
                    default(TransferredReplayBatch));

            public static PendingScoreWork ForTransferredBatch(TransferredReplayBatch batch)
                => new PendingScoreWork(
                    PendingScoreWorkKind.TransferredBatch,
                    default(PendingScoreCommand),
                    batch);
        }

        private readonly struct PendingScoreCommand
        {
            private PendingScoreCommand(
                ScoreCommandKind kind,
                IScoreCommand externalCommand,
                ScoreEffectEntry scoreEffectEntry,
                TransferredSkillExecutionTemplate transferredTemplate,
                SkillExecutionTrace transferredTrace,
                int dishId,
                BigDouble bigValue,
                float floatValue,
                bool boolValue,
                int intValue,
                ScorePhase phase,
                ScoreSource source,
                GridPos? cell,
                IEffectDef effectDef,
                SkillExecutionTrace trace,
                int executionGroupId,
                int depth)
            {
                Kind = kind;
                ExternalCommand = externalCommand;
                ScoreEffectEntry = scoreEffectEntry;
                TransferredTemplate = transferredTemplate;
                TransferredTrace = transferredTrace;
                DishId = dishId;
                BigValue = bigValue;
                FloatValue = floatValue;
                BoolValue = boolValue;
                IntValue = intValue;
                Phase = phase;
                Source = source;
                Cell = cell;
                EffectDef = effectDef;
                Trace = trace;
                ExecutionGroupId = executionGroupId;
                Depth = depth;
            }

            public ScoreCommandKind Kind { get; }

            public IScoreCommand ExternalCommand { get; }

            public ScoreEffectEntry ScoreEffectEntry { get; }

            public TransferredSkillExecutionTemplate TransferredTemplate { get; }

            public SkillExecutionTrace TransferredTrace { get; }

            public int DishId { get; }

            public BigDouble BigValue { get; }

            public float FloatValue { get; }

            public bool BoolValue { get; }

            public int IntValue { get; }

            public ScorePhase Phase { get; }

            public ScoreSource Source { get; }

            public GridPos? Cell { get; }

            public IEffectDef EffectDef { get; }

            public SkillExecutionTrace Trace { get; }

            public int ExecutionGroupId { get; }

            public int Depth { get; }

            public string Name
            {
                get
                {
                    switch (Kind)
                    {
                        case ScoreCommandKind.External:
                            return ExternalCommand?.Name ?? "External";
                        case ScoreCommandKind.AddDishFlat:
                            return "AddDishFlat";
                        case ScoreCommandKind.AddDishPermanentFlat:
                            return "AddDishPermanentFlat";
                        case ScoreCommandKind.MultiplyDish:
                            return "MultiplyDish";
                        case ScoreCommandKind.AddDishMultFlat:
                            return "AddDishMultFlat";
                        case ScoreCommandKind.GrantGold:
                            return "GrantGold";
                        case ScoreCommandKind.ChangeHappyCakeLayer:
                            return "ChangeHappyCakeLayer";
                        case ScoreCommandKind.AddFinalFlat:
                            return "AddFinalFlat";
                        case ScoreCommandKind.MultiplyFinal:
                            return "MultiplyFinal";
                        case ScoreCommandKind.ResolveScoreEffect:
                        case ScoreCommandKind.ResolveTransferredTemplate:
                            return "ResolveScoreEffect";
                        default:
                            return Kind.ToString();
                    }
                }
            }

            public static PendingScoreCommand ForExternal(
                IScoreCommand command,
                ScorePhase phase,
                ScoreSource source,
                GridPos? cell,
                IEffectDef effectDef,
                SkillExecutionTrace trace,
                int executionGroupId,
                int depth)
            {
                return new PendingScoreCommand(
                    ScoreCommandKind.External,
                    command,
                    null,
                    null,
                    null,
                    0,
                    default(BigDouble),
                    0f,
                    false,
                    0,
                    phase,
                    source,
                    cell,
                    effectDef,
                    trace,
                    executionGroupId,
                    depth);
            }

            public static PendingScoreCommand ForBuiltIn(
                ScoreCommandKind kind,
                int dishId,
                BigDouble bigValue,
                float floatValue,
                bool boolValue,
                int intValue,
                ScorePhase phase,
                ScoreSource source,
                GridPos? cell,
                IEffectDef effectDef,
                SkillExecutionTrace trace,
                int executionGroupId,
                int depth)
            {
                return new PendingScoreCommand(
                    kind,
                    null,
                    null,
                    null,
                    null,
                    dishId,
                    bigValue,
                    floatValue,
                    boolValue,
                    intValue,
                    phase,
                    source,
                    cell,
                    effectDef,
                    trace,
                    executionGroupId,
                    depth);
            }

            public static PendingScoreCommand ForScoreEffect(
                ScoreEffectEntry entry,
                ScorePhase phase,
                ScoreSource source,
                GridPos? cell,
                IEffectDef effectDef,
                SkillExecutionTrace trace,
                int executionGroupId,
                int depth)
            {
                return new PendingScoreCommand(
                    ScoreCommandKind.ResolveScoreEffect,
                    null,
                    entry,
                    null,
                    null,
                    0,
                    default(BigDouble),
                    0f,
                    false,
                    0,
                    phase,
                    source,
                    cell,
                    effectDef,
                    trace,
                    executionGroupId,
                    depth);
            }

            public static PendingScoreCommand ForTransferredTemplate(
                TransferredSkillExecutionTemplate template,
                SkillExecutionTrace transferredTrace,
                ScorePhase phase,
                ScoreSource source,
                GridPos? cell,
                IEffectDef effectDef,
                SkillExecutionTrace trace,
                int executionGroupId,
                int depth)
            {
                return new PendingScoreCommand(
                    ScoreCommandKind.ResolveTransferredTemplate,
                    null,
                    null,
                    template,
                    transferredTrace,
                    0,
                    default(BigDouble),
                    0f,
                    false,
                    0,
                    phase,
                    source,
                    cell,
                    effectDef,
                    trace,
                    executionGroupId,
                    depth);
            }
        }

        private struct TransferredReplayBatch
        {
            private readonly TransferredSkillExecutionView _view;
            private readonly int _countAtEnqueue;
            private readonly int _handoffSourceDishInstanceId;
            private readonly int _handoffExecutionGroupId;
            private readonly string _handoffSkillId;
            private readonly int _handoffPayloadCount;
            private readonly ScorePhase _phase;
            private readonly ScoreSource _source;
            private readonly GridPos? _cell;
            private readonly IEffectDef _effectDef;
            private readonly SkillExecutionTrace _trace;
            private readonly int _executionGroupId;
            private readonly int _depth;
            private int _index;

            public TransferredReplayBatch(
                TransferredSkillExecutionView view,
                int countAtEnqueue,
                int handoffSourceDishInstanceId,
                int handoffExecutionGroupId,
                string handoffSkillId,
                int handoffPayloadCount,
                ScorePhase phase,
                ScoreSource source,
                GridPos? cell,
                IEffectDef effectDef,
                SkillExecutionTrace trace,
                int executionGroupId,
                int depth,
                bool expandBeforeQueuedWork)
            {
                _view = view;
                _countAtEnqueue = countAtEnqueue;
                _handoffSourceDishInstanceId = handoffSourceDishInstanceId;
                _handoffExecutionGroupId = handoffExecutionGroupId;
                _handoffSkillId = handoffSkillId;
                _handoffPayloadCount = handoffPayloadCount;
                _phase = phase;
                _source = source;
                _cell = cell;
                _effectDef = effectDef;
                _trace = trace;
                _executionGroupId = executionGroupId;
                _depth = depth;
                _index = 0;
                ExpandBeforeQueuedWork = expandBeforeQueuedWork;
            }

            public bool ExpandBeforeQueuedWork { get; }

            public bool TryTakeNext(bool captureDiagnostics, out PendingScoreCommand pending)
            {
                while (_index < _countAtEnqueue)
                {
                    TransferredSkillExecutionTemplate template = _view.TemplateAt(_index++);
                    if (template?.Effect == null)
                    {
                        continue;
                    }

                    SkillExecutionTrace replayTrace = captureDiagnostics
                        ? template.BaseTrace?.WithSweetTransferHandoff(
                            _handoffSourceDishInstanceId,
                            _handoffExecutionGroupId,
                            _handoffSkillId,
                            _handoffPayloadCount)
                        : null;
                    pending = PendingScoreCommand.ForTransferredTemplate(
                        template,
                        replayTrace,
                        _phase,
                        _source,
                        _cell,
                        _effectDef,
                        _trace,
                        _executionGroupId,
                        _depth);
                    return true;
                }

                pending = default(PendingScoreCommand);
                return false;
            }
        }

        private sealed class TransferredSkillExecutionView : IReadOnlyList<TransferredSkill>
        {
            private readonly List<TransferredSkillExecutionTemplate> _templates =
                new List<TransferredSkillExecutionTemplate>();

            public int Count => _templates.Count;

            public TransferredSkill this[int index] => _templates[index].Transferred;

            public void Add(TransferredSkillExecutionTemplate template)
            {
                _templates.Add(template);
            }

            public TransferredSkillExecutionTemplate TemplateAt(int index) => _templates[index];

            public bool HasExecutableRule(int count)
            {
                int limit = Math.Min(count, _templates.Count);
                for (int i = 0; i < limit; i++)
                {
                    if (_templates[i]?.Effect != null)
                    {
                        return true;
                    }
                }

                return false;
            }

            public IEnumerator<TransferredSkill> GetEnumerator()
            {
                foreach (TransferredSkillExecutionTemplate template in _templates)
                {
                    yield return template.Transferred;
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class TransferredSkillExecutionTemplate
        {
            public TransferredSkillExecutionTemplate(
                TransferredSkill transferred,
                ScoreSource source,
                IScoreEffect effect,
                SkillExecutionTrace baseTrace,
                DishInstance target)
            {
                Transferred = transferred;
                Source = source;
                Effect = effect;
                BaseTrace = baseTrace;
                Target = target;
            }

            public TransferredSkill Transferred { get; }

            public ScoreSource Source { get; }

            public IScoreEffect Effect { get; }

            public SkillExecutionTrace BaseTrace { get; }

            public DishInstance Target { get; }
        }
    }

    internal readonly struct TransferCandidateCacheKey : IEquatable<TransferCandidateCacheKey>
    {
        public TransferCandidateCacheKey(int sourceDishInstanceId, SkillRuleDef rule)
        {
            SourceDishInstanceId = sourceDishInstanceId;
            Rule = rule;
        }

        public int SourceDishInstanceId { get; }

        public SkillRuleDef Rule { get; }

        public bool Equals(TransferCandidateCacheKey other)
            => SourceDishInstanceId == other.SourceDishInstanceId
               && ReferenceEquals(Rule, other.Rule);

        public override bool Equals(object obj)
            => obj is TransferCandidateCacheKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (SourceDishInstanceId * 397) ^ (Rule != null ? Rule.GetHashCode() : 0);
            }
        }
    }

    internal sealed class TransferCandidateSet : IReadOnlyList<DishInstance>
    {
        private readonly IReadOnlyList<DishInstance> _dishes;

        public TransferCandidateSet(IReadOnlyList<DishInstance> dishes)
        {
            _dishes = dishes ?? Array.Empty<DishInstance>();
            Ids = _dishes.Select(dish => dish.Id).ToArray();
            IdSet = new HashSet<int>(Ids);
            ById = _dishes.ToDictionary(dish => dish.Id);
        }

        public int Count => _dishes.Count;

        public DishInstance this[int index] => _dishes[index];

        public IReadOnlyList<int> Ids { get; }

        public HashSet<int> IdSet { get; }

        public Dictionary<int, DishInstance> ById { get; }

        public IEnumerator<DishInstance> GetEnumerator() => _dishes.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal readonly struct SweetTransferPermanentFlatRegistration
    {
        public SweetTransferPermanentFlatRegistration(
            ItemScoreEffectType type,
            BigDouble value,
            ScoreSource source)
        {
            Type = type;
            Value = value;
            Source = source;
        }

        public ItemScoreEffectType Type { get; }

        public BigDouble Value { get; }

        public ScoreSource Source { get; }
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
            SkillExecutionTrace trace,
            int registrationOrder = 0)
        {
            Owner = owner;
            Rule = rule;
            ConditionCount = conditionCount;
            TargetDishInstanceIds = targetDishInstanceIds ?? Array.Empty<int>();
            Source = source;
            Trace = trace;
            RegistrationOrder = registrationOrder;
        }

        public DishInstance Owner { get; }

        public SkillRuleDef Rule { get; }

        public int ConditionCount { get; }

        public IReadOnlyList<int> TargetDishInstanceIds { get; }

        public ScoreSource Source { get; }

        public SkillExecutionTrace Trace { get; }

        internal int RegistrationOrder { get; }
    }

    /// <summary>技能传递副作用：把外来子技能(Effects) 追加给某目标实例（可带来源名，用于「源名&lt;甜蜜传递&gt;」展示）。</summary>
    public sealed class SkillTransferSideEffect
    {
        public SkillTransferSideEffect(
            int targetInstanceId,
            IReadOnlyList<SkillEffect> effects,
            string sourceName = null,
            int sourceInstanceId = 0,
            int handoffExecutionGroupId = 0)
        {
            TargetInstanceId = targetInstanceId;
            Effects = effects ?? Array.Empty<SkillEffect>();
            SourceName = sourceName ?? string.Empty;
            SourceInstanceId = sourceInstanceId;
            HandoffExecutionGroupId = handoffExecutionGroupId;
        }

        public int TargetInstanceId { get; }

        public IReadOnlyList<SkillEffect> Effects { get; }

        /// <summary>来来源食物名（非空时应用为「源名&lt;甜蜜传递&gt;」来源标签）。</summary>
        public string SourceName { get; }

        public int SourceInstanceId { get; }

        /// <summary>发起本次真实传递的 TransferSkills 根效果执行批次；0 表示旧入口未提供。</summary>
        public int HandoffExecutionGroupId { get; }
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
        private readonly BigDouble _value;

        public AddDishFlatCommand(int dishId, BigDouble value)
        {
            _dishId = dishId;
            _value = value;
        }

        public string Name => "AddDishFlat";

        public void Execute(ScoreContext context) => context.ApplyDishFlatCommand(_dishId, _value);
    }

    /// <summary>指定食物增加永久分数；当次进入加法区，结算后写回实例。</summary>
    public sealed class AddDishPermanentFlatCommand : IScoreCommand
    {
        private readonly int _dishId;
        private readonly BigDouble _value;

        public AddDishPermanentFlatCommand(int dishId, BigDouble value)
        {
            _dishId = dishId;
            _value = value;
        }

        public string Name => "AddDishPermanentFlat";

        public void Execute(ScoreContext context) => context.ApplyDishPermanentFlatCommand(_dishId, _value);
    }

    /// <summary>指定食物倍率乘以固定值。</summary>
    public sealed class MultiplyDishCommand : IScoreCommand
    {
        private readonly int _dishId;
        private readonly BigDouble _value;

        public MultiplyDishCommand(int dishId, BigDouble value)
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
        private readonly BigDouble _value;

        public AddDishMultFlatCommand(int dishId, BigDouble value)
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
        private readonly BigDouble _value;

        public AddFinalFlatCommand(BigDouble value)
        {
            _value = value;
        }

        public string Name => "AddFinalFlat";

        public void Execute(ScoreContext context) => context.ApplyFinalFlatCommand(_value);
    }

    /// <summary>最终总分倍率乘以固定值。</summary>
    public sealed class MultiplyFinalCommand : IScoreCommand
    {
        private readonly BigDouble _value;

        public MultiplyFinalCommand(BigDouble value)
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
