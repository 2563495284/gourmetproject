using System;
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
        private const int MaxCommandsPerCalculation = 2048;

        private sealed class DishAccumulator
        {
            public DishInstance Dish;
            public BigDouble Base;
            public BigDouble Flat;
            public BigDouble Mult = BigDouble.One;
            public BigDouble ExtraSettlementContribution;
            public int ExtraSettlementCount;
        }

        private readonly Dictionary<int, DishAccumulator> _accums = new Dictionary<int, DishAccumulator>();
        private readonly List<int> _order = new List<int>();
        private readonly List<DishScore> _dishScores = new List<DishScore>();
        private readonly List<ScoreLine> _lines;
        private readonly List<ScoreEvent> _events;
        private readonly Queue<PendingScoreCommand> _commands = new Queue<PendingScoreCommand>();
        private int _happyCakeLayerDelta;
        private readonly List<SilverItemRollRequest> _silverItemRolls = new List<SilverItemRollRequest>();
        private readonly List<SkillTransferSideEffect> _skillTransfers = new List<SkillTransferSideEffect>();
        private readonly List<SweetTransferBuffRegistration> _sweetTransferBuffs = new List<SweetTransferBuffRegistration>();
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
        private int _commandsExecuted;
        private int _nextExecutionGroupId;
        private int _currentExecutionGroupId;
        private int _extraSettlementDishId;
        private int _extraSettlementLineInsertIndex = -1;
        private BigDouble _extraSettlementRestoreFlat;
        private BigDouble _extraSettlementRestoreMultiplier = BigDouble.One;

        public ScoreContext(ScoreSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            DiningTable = snapshot.DiningTable;
            Db = snapshot.Db;
            FinalFlat = snapshot.InitialFinalFlat;
            FinalMultiplier = snapshot.InitialFinalMultiplier;
            InitialHappyCakeLayers = snapshot.InitialHappyCakeLayers;
            CaptureDiagnostics = snapshot.CaptureDiagnostics;
            if (CaptureDiagnostics)
            {
                _lines = new List<ScoreLine>();
                _events = new List<ScoreEvent>();
            }

            // 预建全部菜的累加器，保证「A 改 B 的分」无论 B 是否已开始都有效。
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

            _liveCountAs[dish.Id] = Math.Max(1, GetEffectiveCountAs(dish) + delta);
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
                    basis = target.OccupiedCells.Count + ActionIntParam(rule, "offset", 0);
                }
                else if (HasActionParam(rule, "source:target-skill-count"))
                {
                    basis = SkillConditionEvaluator.CountSubSkills(target, Db);
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

                int before = GetEffectiveCountAs(target);
                AddLiveCountAs(target, delta);
                int after = GetEffectiveCountAs(target);
                if (CaptureDiagnostics)
                {
                    AddLine(
                        EnsureAccumulator(target),
                        ScoreLineKind.CountAs,
                        after - before,
                        before,
                        after,
                        $"份数 {(after - before >= 0 ? "+" : string.Empty)}{after - before}");
                }
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

        /// <summary>本场经营挑战开始时的全局欢乐蛋糕层数。</summary>
        public int InitialHappyCakeLayers { get; }

        /// <summary>本次结算产生的全局欢乐蛋糕层数增量（正式结算后由 Game 层写回经营挑战状态）。</summary>
        public int HappyCakeLayerDelta => _happyCakeLayerDelta;

        /// <summary>结算过程中「当前」的全局欢乐蛋糕层数（初始 + 已产生增量）。</summary>
        public int CurrentHappyCakeLayers => Math.Max(0, InitialHappyCakeLayers + _happyCakeLayerDelta);

        /// <summary>本次结算按银格登记的独立消耗品判定次数（正式结算后由 Game 层逐条掷骰发放）。</summary>
        public int SilverItemRollRequests => _silverItemRolls.Count;

        /// <summary>本次结算登记的逐条银材质掷骰请求；每条保留自己的概率与来源。</summary>
        public IReadOnlyList<SilverItemRollRequest> SilverItemRolls => _silverItemRolls;

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
        /// 把一轮完整逐菜阶段与本轮开始前的累加器差值折算为独立贡献并追加。
        /// 该贡献独立向上取整，不与主轮的加法/倍率发生复利。
        /// </summary>
        public void CompleteExtraSettlement(
            DishInstance dish,
            FlavorDef saltyFlavor)
        {
            if (dish == null || !_accums.TryGetValue(dish.Id, out DishAccumulator accumulator))
            {
                _extraSettlementDishId = 0;
                _extraSettlementLineInsertIndex = -1;
                return;
            }

            BigDouble contribution = DishScore.CeilContribution(
                dish.BaseScoreBeforeSettlement + accumulator.Flat,
                accumulator.Mult);

            accumulator.Flat = _extraSettlementRestoreFlat;
            accumulator.Mult = _extraSettlementRestoreMultiplier;
            BigDouble before = accumulator.ExtraSettlementContribution;
            accumulator.ExtraSettlementContribution += contribution;
            accumulator.ExtraSettlementCount++;
            _extraSettlementDishId = 0;
            if (CaptureDiagnostics)
            {
                var line = new ScoreLine(
                    ScorePhase.AfterDish,
                    ScoreLineKind.ExtraSettlement,
                    ScoreSource.DishFlavor(saltyFlavor, dish),
                    dish.Id,
                    dish.Def.Id,
                    null,
                    contribution,
                    before,
                    accumulator.ExtraSettlementContribution,
                    $"咸味额外结算第 {accumulator.ExtraSettlementCount} 次 +{contribution}",
                    executionGroupId: ++_nextExecutionGroupId);
                if (_extraSettlementLineInsertIndex >= 0
                    && _extraSettlementLineInsertIndex <= _lines.Count)
                {
                    _lines.Insert(_extraSettlementLineInsertIndex, line);
                }
                else
                {
                    _lines.Add(line);
                }
            }

            _extraSettlementLineInsertIndex = -1;
        }

        public BigDouble CurrentFlatOf(DishInstance dish)
            => dish != null && _accums.TryGetValue(dish.Id, out DishAccumulator a)
                ? a.Flat
                : BigDouble.Zero;

        public void BeginExtraSettlement(
            DishInstance dish,
            BigDouble flatBaseline,
            BigDouble multiplierBaseline)
        {
            _extraSettlementDishId = dish?.Id ?? 0;
            _extraSettlementLineInsertIndex = CaptureDiagnostics ? _lines.Count : -1;
            if (dish == null || !_accums.TryGetValue(dish.Id, out DishAccumulator accumulator))
            {
                return;
            }

            _extraSettlementRestoreFlat = accumulator.Flat;
            _extraSettlementRestoreMultiplier = accumulator.Mult;
            accumulator.Flat = flatBaseline;
            accumulator.Mult = multiplierBaseline;
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
            Trace = CaptureDiagnostics ? entry.Trace : null;
            _currentExecutionGroupId = ++_nextExecutionGroupId;
            if (CaptureDiagnostics)
            {
                EmitEvent(ScoreEventType.EffectStarted, $"开始效果 {Source.Name}");
            }
            try
            {
                entry.Effect.Apply(this);
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
            SubmitCommand(new AddDishFlatCommand(id, value));
        }

        public void MultiplyBy(BigDouble value)
        {
            int id = Dish != null ? Dish.Id : 0;
            SubmitCommand(new MultiplyDishCommand(id, value));
        }

        // ------- 跨菜 / 副作用 API -------

        public void AddFlatTo(DishInstance target, BigDouble value)
        {
            if (target == null)
            {
                return;
            }

            SubmitCommand(new AddDishFlatCommand(target.Id, value));
        }

        public void MultiplyTo(DishInstance target, BigDouble value)
        {
            if (target == null)
            {
                return;
            }

            SubmitCommand(new MultiplyDishCommand(target.Id, value));
        }

        /// <summary>目标食物「倍率区」加法（倍率+X），区别于乘法的 MultiplyTo。</summary>
        public void AddMultFlatTo(DishInstance target, BigDouble value)
        {
            if (target == null)
            {
                return;
            }

            SubmitCommand(new AddDishMultFlatCommand(target.Id, value));
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
            SubmitCommand(new GrantGoldCommand(value));
        }

        /// <summary>
        /// 登记一次带指定概率和当前来源的消耗品掷骰请求。结算层只累计请求、不掷骰，
        /// 保证 PreviewScore 纯净；正式 Settle 后由 Game 层用注入的随机流掷骰并发放装饰品和消耗品。
        /// 无参重载仅保留旧版 50% 默认值；当前银材质路径会显式传入 20%。
        /// </summary>
        public void RequestSilverItemRoll()
        {
            RequestSilverItemRoll(0.5f);
        }

        public void RequestSilverItemRoll(float probability)
        {
            SubmitCommand(new RequestSilverItemRollCommand(probability));
        }

        public void AddFinalFlat(BigDouble value)
        {
            SubmitCommand(new AddFinalFlatCommand(value));
        }

        public void MultiplyFinalBy(BigDouble value)
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
        public void AddPermanentFlatTo(DishInstance target, BigDouble value)
        {
            if (target == null || BigDouble.Abs(value) < 0.0001d)
            {
                return;
            }

            _permanentFlatDeltas.TryGetValue(target.Id, out BigDouble cur);
            _permanentFlatDeltas[target.Id] = cur + value;
            SubmitCommand(new AddDishPermanentFlatCommand(target.Id, value));
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
            if (CaptureDiagnostics)
            {
                EmitEvent(ScoreEventType.CommandExecuted, $"技能传递给 {target.Def.Name}（{effects.Count} 个）");
            }
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
            _sweetTransferBuffs.Add(new SweetTransferBuffRegistration(
                owner,
                rule,
                conditionCount,
                ids,
                Source,
                trace));
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

        public IReadOnlyList<SweetTransferBuffRegistration> SweetTransferReceiverBuffsFor(
            IReadOnlyList<DishInstance> transferTargets)
        {
            if (transferTargets == null || transferTargets.Count == 0 || _sweetTransferBuffs.Count == 0)
            {
                return Array.Empty<SweetTransferBuffRegistration>();
            }

            var targetIds = new HashSet<int>(transferTargets.Where(d => d != null).Select(d => d.Id));
            if (targetIds.Count == 0)
            {
                return Array.Empty<SweetTransferBuffRegistration>();
            }

            return _sweetTransferBuffs
                .Where(registration => registration?.Rule != null
                    && HasActionParam(registration.Rule, "when:receive-transfer")
                    && registration.TargetDishInstanceIds.Any(targetIds.Contains))
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
                            : null);
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
                    a.ExtraSettlementContribution,
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
                recipeRemovalRequests: _recipeRemovalRequests,
                silverItemRolls: _silverItemRolls);
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

        /// <summary>兼容旧命令的 50% 默认值；当前银材质路径不会调用此重载。</summary>
        internal void ApplyRequestSilverItemRollCommand()
        {
            ApplyRequestSilverItemRollCommand(0.5f);
        }

        internal void ApplyRequestSilverItemRollCommand(float probability)
        {
            int before = _silverItemRolls.Count;
            int dishInstanceId = Source != null && Source.DishInstanceId != 0
                ? Source.DishInstanceId
                : Dish != null ? Dish.Id : 0;
            string materialId = Source != null && Source.Type == ScoreSourceType.Material
                ? Source.Id
                : string.Empty;
            _silverItemRolls.Add(new SilverItemRollRequest(probability, dishInstanceId, materialId));
            if (CaptureDiagnostics)
            {
                AddLine(_current, ScoreLineKind.SilverItemRoll, 1, before, _silverItemRolls.Count, "登记消耗品判定");
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
                    Trace = CaptureDiagnostics ? pending.Trace : null;
                    _currentExecutionGroupId = pending.ExecutionGroupId;
                    try
                    {
                        if (CaptureDiagnostics)
                        {
                            EmitEvent(ScoreEventType.CommandExecuted, $"执行命令 {pending.Command.Name}");
                        }
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

            if (accum != null
                && accum.Dish.Id == _extraSettlementDishId
                && (kind == ScoreLineKind.DishFlat
                    || kind == ScoreLineKind.DishPermanentFlat
                    || kind == ScoreLineKind.DishMultiplier
                    || kind == ScoreLineKind.DishMultiplierAdd))
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

    /// <summary>登记一次带概率和来源的银材质消耗品判定请求（副作用，不掷骰）。</summary>
    public sealed class RequestSilverItemRollCommand : IScoreCommand
    {
        private readonly float _probability;

        public RequestSilverItemRollCommand()
            : this(0.5f)
        {
        }

        public RequestSilverItemRollCommand(float probability)
        {
            _probability = probability;
        }

        public string Name => "RequestSilverItemRoll";

        public void Execute(ScoreContext context) => context.ApplyRequestSilverItemRollCommand(_probability);
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
