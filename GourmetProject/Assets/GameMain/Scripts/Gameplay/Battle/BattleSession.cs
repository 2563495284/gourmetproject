using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using GourmetProject.Gameplay.Tags;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Gameplay.Battle
{
    /// <summary>一次候选摆放的无副作用预览。</summary>
    public readonly struct PreparedPlacementPreview
    {
        private PreparedPlacementPreview(
            bool success,
            ServeOutcome outcome,
            Placement placement,
            BigDouble score,
            int scoreCalculationCount)
        {
            Success = success;
            Outcome = outcome;
            Placement = placement;
            Score = score;
            ScoreCalculationCount = scoreCalculationCount;
        }

        public bool Success { get; }

        public ServeOutcome Outcome { get; }

        public Placement Placement { get; }

        public BigDouble Score { get; }

        /// <summary>本次预览实际执行的完整 ScoreCalculator 次数；非法候选为 0。</summary>
        public int ScoreCalculationCount { get; }

        public static PreparedPlacementPreview Succeeded(
            Placement placement,
            BigDouble score,
            int scoreCalculationCount = 1)
            => new PreparedPlacementPreview(
                true,
                ServeOutcome.Placed,
                placement,
                score,
                Math.Max(0, scoreCalculationCount));

        public static PreparedPlacementPreview Fail(ServeOutcome outcome, Placement placement)
            => new PreparedPlacementPreview(false, outcome, placement, BigDouble.Zero, 0);
    }

    /// <summary>一次已经实际落到单个目标的甜蜜传递。</summary>
    public readonly struct SweetTransferOccurrence
    {
        public SweetTransferOccurrence(int sourceInstanceId, int targetInstanceId)
        {
            SourceInstanceId = sourceInstanceId;
            TargetInstanceId = targetInstanceId;
        }

        public int SourceInstanceId { get; }

        public int TargetInstanceId { get; }
    }

    /// <summary>一次成功丢弃及其在局外食谱中的来源。</summary>
    public readonly struct DishDiscardOccurrence
    {
        public DishDiscardOccurrence(int sourceBookIndex, int sourceDishIndex, string dishId)
        {
            SourceBookIndex = sourceBookIndex;
            SourceDishIndex = sourceDishIndex;
            DishId = dishId ?? string.Empty;
        }

        public int SourceBookIndex { get; }

        public int SourceDishIndex { get; }

        public string DishId { get; }
    }

    /// <summary>营业成败已确定后完成的一次食谱移除判定。</summary>
    public readonly struct RecipeRemovalOutcome
    {
        public RecipeRemovalOutcome(RecipeRemovalRequest request, bool removed)
        {
            Request = request;
            Removed = removed;
        }

        public RecipeRemovalRequest Request { get; }

        public bool Removed { get; }
    }

    /// <summary>
    /// 一局局内经营挑战的完整逻辑（纯 C#，可单测）：持有餐桌与食谱槽，处理「准备出菜 / 玩家摆放 / 吃」结算。
    /// 所有随机经由注入的确定性流，保证同种子可复现。表现层（BattleForm）只读取状态并转发操作。
    /// </summary>
    public sealed class BattleSession
    {
        private readonly GameplayDatabase _db;
        private readonly IRandomStream _rng;
        private readonly ScoreCalculator _calculator;
        private readonly SolverPreviewSampler _solverPreviewSampler = new SolverPreviewSampler();
        private readonly List<RecipeSlot> _slots;
        private readonly List<string> _recipeBaseIds = new List<string>();
        private readonly List<TrackedRecipeEntry> _trackedRecipeEntries = new List<TrackedRecipeEntry>();
        private readonly Dictionary<RecipeSlotEntry, TrackedRecipeEntry> _trackedRecipeEntriesBySource =
            new Dictionary<RecipeSlotEntry, TrackedRecipeEntry>();
        private readonly Dictionary<int, TrackedRecipeEntry> _trackedRecipeEntriesByDishId =
            new Dictionary<int, TrackedRecipeEntry>();
        private readonly Dictionary<string, int> _mealSettled = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _runSettled = new Dictionary<string, int>();
        private readonly List<RecipeScoreFlatDelta> _lastRecipeScoreFlatDeltas = new List<RecipeScoreFlatDelta>();
        private readonly List<RecipeScoreMultiplierDelta> _lastRecipeScoreMultiplierDeltas = new List<RecipeScoreMultiplierDelta>();
        private readonly List<RecipeRemovalOutcome> _lastRecipeRemovalOutcomes = new List<RecipeRemovalOutcome>();
        private readonly List<int> _pendingActiveItemGrantSources = new List<int>();
        private readonly List<DishInstance> _temporaryAreaDishes = new List<DishInstance>();
        private readonly Dictionary<int, PendingDishPlacement> _pendingDishPlacements =
            new Dictionary<int, PendingDishPlacement>();
        private bool _runRecipeGrowthApplied;
        private bool _runSettlementApplied;
        private int _nextInstanceId = 1;
        private int _appetizerRemoved;
        private float _settlementDishMultiplierFlat;
        private string _settlementDishMultiplierItemId = string.Empty;
        private string _settlementDishMultiplierItemName = string.Empty;
        private float _randomServeMultiplierMin;
        private float _randomServeMultiplierMax;
        private float _randomServeMultiplierStep;
        private float _alternateServeMultiplierLow = 1f;
        private float _alternateServeMultiplierHigh = 1f;
        private string _alternateServeMultiplierSourceId = string.Empty;
        private string _alternateServeMultiplierSourceName = string.Empty;
        private string _randomServeMultiplierSourceId = string.Empty;
        private string _randomServeMultiplierSourceName = string.Empty;
        private string _baseScoreMultiplierSourceId = string.Empty;
        private string _baseScoreMultiplierSourceName = string.Empty;
        private string _confirmedServeGoldCostSourceId = string.Empty;
        private string _confirmedServeGoldCostSourceName = string.Empty;
        private readonly List<LastServedDishMultiplierRule> _lastServedDishMultiplierRules =
            new List<LastServedDishMultiplierRule>();
        private string _insertDishId = string.Empty;
        private int _insertDishWindowSize;
        private int _insertDishCountPerWindow;
        private int _successfulBellPrepares;
        private readonly HashSet<int> _insertDishPositions = new HashSet<int>();
        private int _serveCookiePityCount;
        private int _consecutiveCookiePrepares;
        private readonly HashSet<string> _serveCookieDishIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public BattleSession(
            GpTable board,
            GameplayDatabase db,
            IRandomStream rng,
            IEnumerable<RecipeSlot> slots,
            int requiredScore,
            ScoreCalculator calculator = null,
            IReadOnlyDictionary<string, int> runSettledCounts = null)
        {
            DiningTable = board ?? throw new ArgumentNullException(nameof(board));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _slots = new List<RecipeSlot>(slots ?? Array.Empty<RecipeSlot>());
            RequiredScore = requiredScore;
            _calculator = calculator ?? new ScoreCalculator();

            if (runSettledCounts != null)
            {
                foreach (KeyValuePair<string, int> kv in runSettledCounts)
                {
                    _runSettled[kv.Key] = kv.Value;
                }
            }

            CaptureRecipeSnapshot();
        }

        public GpTable DiningTable { get; }

        /// <summary>仅供 Boss 演出层读取的装配差分；不参与玩法判定和随机。</summary>
        public BossDebuffPresentationPlan BossDebuffPresentation { get; private set; }

        public void AttachBossDebuffPresentation(BossDebuffPresentationPlan presentation)
        {
            BossDebuffPresentation = presentation;
        }

        public GameplayDatabase Database => _db;

        public IReadOnlyList<RecipeSlot> Slots => _slots;

        public int RequiredScore { get; }

        /// <summary>局级加法修正（由装饰品和消耗品/Buff 注入，影响最终结算）。</summary>
        public float FinalFlat { get; set; }

        /// <summary>局级倍率修正（由装饰品和消耗品/Buff 注入，影响最终结算）。</summary>
        public float FinalMultiplier { get; set; } = 1f;

        /// <summary>每个食物额外「视为食物数」（由装饰品注入，影响计数类前提）。</summary>
        public int ExtraCountAsPerDish { get; set; }

        /// <summary>本场结算开始时持有的被动装饰品数量。</summary>
        public int PassiveItemCount { get; set; }

        /// <summary>蛋糕层数 buff 阈值下调（由装饰品「蛋糕捷径」注入）。</summary>
        public int CakeLayerThresholdReduction { get; set; }

        /// <summary>蛋糕层数每次净增时的额外加成（由装饰品「蛋糕膨胀」注入）。</summary>
        public int CakeLayerAccelBonus { get; set; }

        /// <summary>设置本场经营挑战的初始蛋糕层数（装饰品和消耗品「蛋糕打底」/跨局保留）。下限 0。</summary>
        public void SeedHappyCakeLayers(int layers)
        {
            SetHappyCakeLayers(layers);
        }

        /// <summary>本场经营挑战经营挑战结束后清空蛋糕层数。</summary>
        public void ClearHappyCakeLayers()
        {
            SetHappyCakeLayers(0);
        }

        /// <summary>本局允许的最大上菜次数（-1 表示不限；星级评鉴机制「限量供应」会设上限）。</summary>
        public int MaxServes { get; set; } = -1;

        /// <summary>玩家真正确认“上菜”时扣除的金币。</summary>
        public int GoldCostPerConfirmedServe { get; set; }

        public bool RemoveFirstServedDishes { get; set; }

        public int FirstServedDishesToRemove { get; set; }

        public bool AlternateServeMultiplier { get; set; }

        public void ConfigureAlternateServeMultiplier(
            float low,
            float high,
            string sourceId = null,
            string sourceName = null)
        {
            _alternateServeMultiplierLow = low;
            _alternateServeMultiplierHigh = high;
            _alternateServeMultiplierSourceId = sourceId ?? string.Empty;
            _alternateServeMultiplierSourceName = sourceName ?? string.Empty;
            AlternateServeMultiplier = true;
        }

        public bool RandomServeMultiplier { get; set; }

        public void ConfigureRandomServeMultiplier(
            float min,
            float max,
            float step,
            string sourceId = null,
            string sourceName = null)
        {
            if (max < min)
            {
                (min, max) = (max, min);
            }

            _randomServeMultiplierMin = min;
            _randomServeMultiplierMax = max;
            _randomServeMultiplierStep = step > 0f ? step : 0f;
            _randomServeMultiplierSourceId = sourceId ?? string.Empty;
            _randomServeMultiplierSourceName = sourceName ?? string.Empty;
        }

        public void ConfigureConfirmedServeGoldCost(int cost, string sourceId, string sourceName)
        {
            GoldCostPerConfirmedServe = Math.Max(0, cost);
            _confirmedServeGoldCostSourceId = sourceId ?? string.Empty;
            _confirmedServeGoldCostSourceName = sourceName ?? string.Empty;
        }

        public void ConfigureBaseScoreMultiplier(float multiplier, string sourceId, string sourceName)
        {
            BaseScoreMultiplier = multiplier;
            _baseScoreMultiplierSourceId = sourceId ?? string.Empty;
            _baseScoreMultiplierSourceName = sourceName ?? string.Empty;
        }

        /// <summary>
        /// 配置连续饼干出菜保底。连续达到 count 次后，下一次普通出菜优先从当前可摆入的
        /// 非饼干候选中按占格数加权抽取；没有可摆入的非饼干时回退原候选。count=0 表示关闭。
        /// </summary>
        public void ConfigureCookieServePity(int count, IEnumerable<string> cookieDishIds)
        {
            _serveCookiePityCount = Math.Max(0, count);
            _consecutiveCookiePrepares = 0;
            _serveCookieDishIds.Clear();
            if (cookieDishIds == null)
            {
                return;
            }

            foreach (string dishId in cookieDishIds)
            {
                if (!string.IsNullOrWhiteSpace(dishId))
                {
                    _serveCookieDishIds.Add(dishId.Trim());
                }
            }
        }

        public bool ReverseSettlementOrder { get; set; }

        public int MinimumServesForScore { get; set; }

        public float BaseScoreMultiplier { get; set; } = 1f;

        /// <summary>本局已上菜次数。</summary>
        public int ServesUsed { get; private set; }

        /// <summary>当前连续成功出现在出菜口的饼干数量。</summary>
        public int ConsecutiveCookiePrepares => _consecutiveCookiePrepares;

        /// <summary>本局允许从出菜口丢弃食物的总次数。</summary>
        public int FoodDiscardLimit { get; private set; }

        /// <summary>本局已经从出菜口丢弃食物的次数。</summary>
        public int FoodDiscardsUsed { get; private set; }

        public int FoodDiscardsRemaining => Math.Max(0, FoodDiscardLimit - FoodDiscardsUsed);

        /// <summary>已随机出菜、尚未由玩家摆上餐桌的食物。</summary>
        public PreparedServeDish PreparedServe { get; private set; }

        /// <summary>
        /// 因麻风味旋转后暂存在临时桌、尚未放回主餐桌的食物。
        /// 它们不属于 <see cref="DiningTable"/>，因此不参与空间判定、预览分数或结算。
        /// </summary>
        public IReadOnlyList<DishInstance> TemporaryAreaDishes => _temporaryAreaDishes;

        public IReadOnlyCollection<PendingDishPlacement> PendingDishPlacements
            => _pendingDishPlacements.Values;

        public bool HasPendingTablePlacements
        {
            get
            {
                foreach (PendingDishPlacement pending in _pendingDishPlacements.Values)
                {
                    if (pending.IsOnDiningTable)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public PendingDishPlacement FindPendingDishPlacement(int dishId)
            => _pendingDishPlacements.TryGetValue(dishId, out PendingDishPlacement pending)
                ? pending
                : null;

        /// <summary>本场经营挑战共享的全局「欢乐蛋糕层数」，随上菜/结算累加，跨经营挑战重置。</summary>
        public int HappyCakeLayers { get; private set; }

        /// <summary>是否已结算（吃过）。</summary>
        public bool IsSettled { get; private set; }

        /// <summary>结算结果（未结算时为 null）。</summary>
        public ScoreResult LastResult { get; private set; }

        /// <summary>本局待入账的金币增量（上菜 OnServe + 结算经济运营累积；由 Game 层写回 GameRun.Gold）。</summary>
        public float PendingGold { get; private set; }

        /// <summary>本局待发放的消耗品数量（银格独立 1/5 判定命中数；由 Game 层在结算后发放）。</summary>
        public int PendingActiveItemGrants => _pendingActiveItemGrantSources.Count;

        /// <summary>每次银格命中对应的来源食物实例 Id，顺序与待发放消耗品一致。</summary>
        public IReadOnlyList<int> PendingActiveItemGrantSources => _pendingActiveItemGrantSources;

        /// <summary>本次结算各 BaseId 的结算增量（供 Game 层累加进 GameRun 大局历史）。</summary>
        public IReadOnlyDictionary<string, int> LastSettledIncrements { get; private set; } = new Dictionary<string, int>();

        public IReadOnlyList<RecipeScoreFlatDelta> LastRecipeScoreFlatDeltas => _lastRecipeScoreFlatDeltas;

        public IReadOnlyList<RecipeScoreMultiplierDelta> LastRecipeScoreMultiplierDeltas => _lastRecipeScoreMultiplierDeltas;

        public IReadOnlyList<RecipeRemovalOutcome> LastRecipeRemovalOutcomes => _lastRecipeRemovalOutcomes;

        /// <summary>Game 层是否已把本场食谱成长写回 GameRun。</summary>
        public bool IsRunRecipeGrowthApplied => _runRecipeGrowthApplied;

        /// <summary>Game 层是否已把本场最终结算写回 GameRun。</summary>
        public bool IsRunSettlementApplied => _runSettlementApplied;

        /// <summary>
        /// 原子标记食谱成长已写回。首次调用返回 true；后续返回 false，供 UI 与无界面流程防止重复应用。
        /// 本方法只记录生命周期，不执行任何写回。
        /// </summary>
        public bool TryMarkRunRecipeGrowthApplied()
        {
            if (_runRecipeGrowthApplied)
            {
                return false;
            }

            _runRecipeGrowthApplied = true;
            return true;
        }

        /// <summary>
        /// 原子标记最终结算已写回。首次调用返回 true；后续返回 false，供 UI 与无界面流程防止重复应用。
        /// 本方法只记录生命周期，不执行任何写回。
        /// </summary>
        public bool TryMarkRunSettlementApplied()
        {
            if (_runSettlementApplied)
            {
                return false;
            }

            _runSettlementApplied = true;
            return true;
        }

        /// <summary>本场经营挑战食谱内容（BaseId 列表，供食谱检测）。</summary>
        public IReadOnlyList<string> RecipeBaseIds => _recipeBaseIds;

        /// <summary>
        /// 返回本场开局经 星级评鉴修正后的完整食谱。已出菜、丢弃或移除的条目不会从该列表消失。
        /// “不能放置”由当前餐桌空间、结算状态与上菜次数上限实时计算。
        /// </summary>
        public IReadOnlyList<BattleRecipeEntrySnapshot> GetBattleRecipeEntries(int slotIndex)
        {
            var result = new List<BattleRecipeEntrySnapshot>();
            foreach (TrackedRecipeEntry tracked in _trackedRecipeEntries)
            {
                if (tracked.SlotIndex != slotIndex)
                {
                    continue;
                }

                RecipeSlotEntry entry = tracked.Entry;
                result.Add(new BattleRecipeEntrySnapshot(
                    tracked.EntryId,
                    tracked.SlotIndex,
                    entry.DishId,
                    entry.ExtraFlavorIds,
                    entry.ExtraSkillIds,
                    entry.ScoreMultiplier,
                    entry.ScoreFlatBonus,
                    entry.DisableSkills,
                    entry.ExcludeFromScore,
                    ResolveDisplayStatus(tracked)));
            }

            return result;
        }

        /// <summary>甜蜜传递实际落到每个目标后各触发一次。</summary>
        public event Action<SweetTransferOccurrence> SweetTransferTriggered;

        public event Action<DishInstance, int> Served;

        public event Action<DishDiscardOccurrence> DishDiscarded;

        /// <summary>欢乐蛋糕层数变化（旧值, 新值），供表现层驱动 HUD 与场景蛋糕演出。</summary>
        public event Action<int, int> HappyCakeLayersChanged;

        public event Action<DishInstance, float> ServeMultiplierFlatApplied;

        public event Action<ServeTriggerCue> ServeTriggerCueRaised;

        public void ConfigureFoodDiscardLimit(int count)
        {
            FoodDiscardLimit = Math.Max(0, count);
            FoodDiscardsUsed = Math.Min(FoodDiscardsUsed, FoodDiscardLimit);
        }

        /// <summary>
        /// 经营挑战进行中因获得 / 移除垃圾桶类装饰而调整本场上限。
        /// 已消耗次数不回滚：正增量会同时增加上限和当前剩余，
        /// 负增量最多把剩余压到 0，不会退还已用次数。
        /// </summary>
        public void AdjustFoodDiscardLimit(int delta)
        {
            if (delta == 0)
            {
                return;
            }

            FoodDiscardLimit = Math.Max(0, FoodDiscardLimit + delta);
        }

        /// <summary>
        /// 配置铃铛出菜序列中的独立插入食物。每个窗口会随机选择指定数量的位置，
        /// 命中时直接从食物表读取该菜，不消耗或替换食谱条目。
        /// </summary>
        public void ConfigureInsertedDishSequence(string dishId, int windowSize, int countPerWindow)
        {
            _insertDishId = dishId ?? string.Empty;
            _insertDishWindowSize = Math.Max(0, windowSize);
            _insertDishCountPerWindow = Math.Max(0, Math.Min(countPerWindow, _insertDishWindowSize));
            _successfulBellPrepares = 0;
            GenerateInsertDishPositions();
        }

        /// <summary>
        /// 丢弃当前出菜口食物。食物已从本局食谱副本取出，因此这里只清空暂存态；
        /// 不写回 GameRun 食谱，也不触发上菜次数、技能或费用。
        /// </summary>
        public bool TryDiscardPreparedServe()
        {
            if (IsSettled || PreparedServe == null || FoodDiscardsRemaining <= 0)
            {
                return false;
            }

            SetTrackedStatus(PreparedServe.Entry, BattleRecipeEntryStatus.Discarded);
            DishDiscarded?.Invoke(new DishDiscardOccurrence(
                PreparedServe.Entry.SourceBookIndex,
                PreparedServe.Entry.SourceDishIndex,
                PreparedServe.Entry.DishId));
            PreparedServe = null;
            FoodDiscardsUsed++;
            return true;
        }

        /// <summary>
        /// 丢弃一份已经落桌的食物并消耗一次丢弃次数。
        /// 已正式上菜的副作用不回滚；预摆菜则只清理预摆状态，不触发上菜。
        /// </summary>
        public bool TryDiscardPlacedDish(DishInstance dish)
        {
            if (IsSettled || dish == null || FoodDiscardsRemaining <= 0)
            {
                return false;
            }

            DishInstance placedDish = FindDishById(dish.Id);
            if (!ReferenceEquals(placedDish, dish))
            {
                return false;
            }

            DiningTable.RemoveDish(dish);
            MarkDishStatusBeforePendingRemoval(dish.Id, BattleRecipeEntryStatus.Discarded);
            DishDiscarded?.Invoke(new DishDiscardOccurrence(
                dish.SourceSlotIndex,
                dish.SourceDishIndex,
                dish.Def?.Id));
            _pendingDishPlacements.Remove(dish.Id);
            FoodDiscardsUsed++;
            ReevaluateLastServedDishMultipliers();
            return true;
        }

        /// <summary>直接丢弃仍在临时桌上的菜，包括从预摆餐桌被旋转回临时桌的菜。</summary>
        public bool TryDiscardTemporaryAreaDish(int dishId)
        {
            if (IsSettled || FoodDiscardsRemaining <= 0)
            {
                return false;
            }

            DishInstance dish = FindTemporaryAreaDishById(dishId);
            if (dish == null)
            {
                return false;
            }

            _temporaryAreaDishes.Remove(dish);
            MarkDishStatusBeforePendingRemoval(dish.Id, BattleRecipeEntryStatus.Discarded);
            DishDiscarded?.Invoke(new DishDiscardOccurrence(
                dish.SourceSlotIndex,
                dish.SourceDishIndex,
                dish.Def?.Id));
            _pendingDishPlacements.Remove(dish.Id);
            FoodDiscardsUsed++;
            ReevaluateLastServedDishMultipliers();
            return true;
        }

        /// <summary>每次传递给目标永久倍率累加的数值（如 0.1）。</summary>
        public float SweetTransferTargetMultiplier { get; set; }

        /// <summary>每成功传递一个目标，给来源永久倍率累加的数值（如 0.1）。</summary>
        public float SweetTransferSourceMultiplier { get; set; }

        /// <summary>装饰品为每次甜蜜传递追加的目标数量。</summary>
        public int SweetTransferExtraTargetCount { get; set; }

        public void AddPendingGold(float amount)
        {
            PendingGold += amount;
        }

        public void AddServeMultiplierFlat(DishInstance dish, float value)
        {
            if (dish == null || Math.Abs(value) < 0.0001f)
            {
                return;
            }

            dish.AddServeMultiplierFlat(value);
            ServeMultiplierFlatApplied?.Invoke(dish, value);
        }

        public void AddPassiveServeMultiplierFlat(
            DishInstance dish,
            float value,
            string itemId,
            string itemName)
        {
            if (dish == null || string.IsNullOrEmpty(itemId) || Math.Abs(value) < 0.0001f)
            {
                return;
            }

            float desired = dish.ServeMultiplierFlatForSource(itemId) + value;
            SetServeMultiplierFlatForSource(
                dish,
                itemId,
                desired,
                ServeCueSourceKind.PassiveItem,
                itemName,
                $"倍率 {FormatSigned(value)}",
                ServeCuePresentationKind.Gain,
                pulseSource: true);
        }

        public void ConfigureLastServedDishMultiplierFlat(
            string itemId,
            string itemName,
            float value,
            Func<bool> isActive = null)
        {
            if (string.IsNullOrEmpty(itemId) || Math.Abs(value) < 0.0001f)
            {
                return;
            }

            for (int i = 0; i < _lastServedDishMultiplierRules.Count; i++)
            {
                if (string.Equals(
                        _lastServedDishMultiplierRules[i].SourceId,
                        itemId,
                        StringComparison.Ordinal))
                {
                    return;
                }
            }

            _lastServedDishMultiplierRules.Add(new LastServedDishMultiplierRule(
                itemId,
                itemName,
                value,
                isActive));
            ReevaluateLastServedDishMultipliers();
        }

        /// <summary>
        /// 让指定来源在当前装饰品持有顺序的位置刷新“最后正式上菜”目标。
        /// 桌面移动、丢弃和销毁仍由会话统一刷新全部来源。
        /// </summary>
        public void RefreshLastServedDishMultiplierFlat(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return;
            }

            ReevaluateLastServedDishMultipliers(itemId);
        }

        public void ApplySettlementDishMultiplierFlat(float value, string itemId, string itemName)
        {
            if (value <= 0f)
            {
                return;
            }

            _settlementDishMultiplierFlat += value;
            _settlementDishMultiplierItemId = string.IsNullOrEmpty(_settlementDishMultiplierItemId)
                ? itemId ?? string.Empty
                : _settlementDishMultiplierItemId;
            _settlementDishMultiplierItemName = string.IsNullOrEmpty(_settlementDishMultiplierItemName)
                ? itemName ?? itemId ?? string.Empty
                : _settlementDishMultiplierItemName;
        }

        /// <summary>
        /// 当前餐桌状态下，指定食谱条目是否至少存在一个合法上菜位置。
        /// 与真正上菜共用同一套风味旋转/回退规则，供 HUD 实时展示可放置状态。
        /// </summary>
        public bool CanFitRecipeEntry(int slotIndex, int entryIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count)
            {
                return false;
            }

            RecipeSlot slot = _slots[slotIndex];
            if (entryIndex < 0 || entryIndex >= slot.Entries.Count)
            {
                return false;
            }

            RecipeSlotEntry entry = slot.Entries[entryIndex];
            DishDef dish = _db.GetDish(entry.DishId);
            return dish != null && FindServePlacements(dish, entry).Count > 0;
        }

        /// <summary>
        /// 由系统效果准备一道食物，不触发“点击铃铛出菜”类 Boss 效果。
        /// </summary>
        public ServePrepareResult PrepareServe(int slotIndex)
        {
            return PrepareServeCore(slotIndex, triggeredByAutomaticOutput: false);
        }

        /// <summary>
        /// 自动出菜：成功后出菜口出现食物，并推进依赖出菜序列的 Boss 效果。
        /// </summary>
        public ServePrepareResult PrepareServeAutomatically(int slotIndex)
        {
            if (IsSettled)
            {
                return ServePrepareResult.Fail(ServePrepareOutcome.LimitReached);
            }

            if (HasPendingTablePlacements)
            {
                return ServePrepareResult.Fail(ServePrepareOutcome.PendingPlacement);
            }

            return PrepareServeCore(slotIndex, triggeredByAutomaticOutput: true);
        }

        [Obsolete("Use PrepareServeAutomatically for the automatic battle output flow.")]
        public ServePrepareResult PrepareServeFromBell(int slotIndex)
        {
            return PrepareServeAutomatically(slotIndex);
        }

        private ServePrepareResult PrepareServeCore(int slotIndex, bool triggeredByAutomaticOutput)
        {
            if (PreparedServe != null)
            {
                return ServePrepareResult.Fail(ServePrepareOutcome.AlreadyPrepared);
            }

            if (slotIndex < 0 || slotIndex >= _slots.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            }

            if (MaxServes >= 0 && ServesUsed >= MaxServes)
            {
                return ServePrepareResult.Fail(ServePrepareOutcome.LimitReached);
            }

            RecipeSlot slot = _slots[slotIndex];
            bool insertConfiguredDish = triggeredByAutomaticOutput && ShouldInsertDishOnNextBellPrepare();
            RecipeSlotEntry entry;
            DishDef servedDish;
            IReadOnlyList<Placement> placements;

            if (insertConfiguredDish)
            {
                servedDish = _db.GetDish(_insertDishId);
                if (servedDish == null)
                {
                    return ServePrepareResult.Fail(ServePrepareOutcome.NoFittingDish);
                }

                entry = new RecipeSlotEntry(_insertDishId);
                placements = FindServePlacements(servedDish, entry);
                if (placements.Count == 0)
                {
                    return ServePrepareResult.Fail(ServePrepareOutcome.NoFittingDish);
                }
            }
            else
            {
                if (slot.IsEmpty)
                {
                    return ServePrepareResult.Fail(ServePrepareOutcome.SlotEmpty);
                }

                var candidates = new List<ServeCandidate>();
                for (int i = 0; i < slot.Entries.Count; i++)
                {
                    DishDef dish = _db.GetDish(slot.Entries[i].DishId);
                    if (dish == null)
                    {
                        continue;
                    }

                    // 抽菜权重只依赖食物占格数，不依赖合法位置数量。候选阶段只需判断
                    // 能否放下；正式 RNG 选中后再为唯一一道菜生成完整位置列表。
                    if (CanServeDish(dish, slot.Entries[i]))
                    {
                        candidates.Add(new ServeCandidate(i, dish, placements: null));
                    }
                }

                if (candidates.Count == 0)
                {
                    return ServePrepareResult.Fail(ServePrepareOutcome.NoFittingDish);
                }

                List<ServeCandidate> rollCandidates = PreferNonCookieCandidates(candidates);
                List<ServeCandidate> freshCandidates = rollCandidates
                    .Where(candidate => HasFlavorEffect(
                        candidate.Dish,
                        slot.Entries[candidate.SlotEntryIndex],
                        FlavorEffectType.ServePriority))
                    .ToList();
                if (freshCandidates.Count > 0)
                {
                    rollCandidates = freshCandidates;
                }
                var weights = new List<float>(rollCandidates.Count);
                foreach (ServeCandidate candidate in rollCandidates)
                {
                    weights.Add(Math.Max(1, candidate.Dish.Shape.CellCount));
                }

                ServeCandidate chosen = rollCandidates[_rng.WeightedPickIndex(weights)];
                entry = slot.RemoveEntryAt(chosen.SlotEntryIndex);
                servedDish = chosen.Dish;
                placements = FindServePlacements(servedDish, entry);
            }

            Placement initialPlacement = placements[0];
            List<string> skills = ComposeServeSkills(servedDish, entry);
            List<string> flavors = ComposeServeFlavors(servedDish, entry);
            var instance = new DishInstance(_nextInstanceId++, servedDish, initialPlacement, skills, flavors);
            // Boss recipe-entry state must be visible as soon as the dish reaches the outlet,
            // so outlet/preplacement tips describe the dish that will actually be served.
            ApplyPreparedEntryFlags(instance, entry);
            // Recipe-owned permanent score modifiers are part of the prepared dish itself.
            // Apply them before the outlet preview binds, then never apply them again on preplacement.
            ApplyPlacementEntryModifiers(instance, entry);
            instance.SetSourceRecipeIndex(slotIndex, entry.SourceDishIndex);
            PreparedServe = new PreparedServeDish(
                slotIndex,
                entry,
                instance,
                placements,
                isBossInsertedDish: insertConfiguredDish);
            SetTrackedStatus(entry, BattleRecipeEntryStatus.WaitingForPlacement);
            RecordPreparedDishForCookiePity(servedDish);
            if (triggeredByAutomaticOutput)
            {
                AdvanceBellPrepareSequence();
            }

            return new ServePrepareResult(ServePrepareOutcome.Prepared, PreparedServe);
        }

        private List<ServeCandidate> PreferNonCookieCandidates(List<ServeCandidate> candidates)
        {
            if (_serveCookiePityCount <= 0
                || _consecutiveCookiePrepares < _serveCookiePityCount
                || _serveCookieDishIds.Count == 0)
            {
                return candidates;
            }

            var nonCookieCandidates = new List<ServeCandidate>();
            foreach (ServeCandidate candidate in candidates)
            {
                if (!IsCookieDish(candidate.Dish))
                {
                    nonCookieCandidates.Add(candidate);
                }
            }

            return nonCookieCandidates.Count > 0 ? nonCookieCandidates : candidates;
        }

        private void RecordPreparedDishForCookiePity(DishDef dish)
        {
            if (_serveCookiePityCount <= 0 || _serveCookieDishIds.Count == 0)
            {
                _consecutiveCookiePrepares = 0;
                return;
            }

            if (IsCookieDish(dish))
            {
                _consecutiveCookiePrepares++;
            }
            else
            {
                _consecutiveCookiePrepares = 0;
            }
        }

        private bool IsCookieDish(DishDef dish)
        {
            return dish != null
                && (_serveCookieDishIds.Contains(dish.Id)
                    || _serveCookieDishIds.Contains(dish.BaseId));
        }

        private bool HasFlavorEffect(
            DishDef dish,
            RecipeSlotEntry entry,
            FlavorEffectType effectType)
        {
            foreach (string flavorId in ComposeServeFlavors(dish, entry))
            {
                FlavorDef flavor = _db.GetFlavor(flavorId);
                if (flavor != null && flavor.EffectType == effectType)
                {
                    return true;
                }
            }

            return false;
        }

        private bool ShouldInsertDishOnNextBellPrepare()
        {
            if (_insertDishWindowSize <= 0
                || _insertDishCountPerWindow <= 0
                || string.IsNullOrEmpty(_insertDishId))
            {
                return false;
            }

            int positionInWindow = _successfulBellPrepares % _insertDishWindowSize + 1;
            return _insertDishPositions.Contains(positionInWindow);
        }

        private void AdvanceBellPrepareSequence()
        {
            _successfulBellPrepares++;
            if (_insertDishWindowSize > 0 && _successfulBellPrepares % _insertDishWindowSize == 0)
            {
                GenerateInsertDishPositions();
            }
        }

        private void GenerateInsertDishPositions()
        {
            _insertDishPositions.Clear();
            if (_insertDishWindowSize <= 0
                || _insertDishCountPerWindow <= 0
                || string.IsNullOrEmpty(_insertDishId))
            {
                return;
            }

            var positions = new List<int>(_insertDishWindowSize);
            for (int position = 1; position <= _insertDishWindowSize; position++)
            {
                positions.Add(position);
            }

            _rng.Shuffle(positions);
            for (int i = 0; i < _insertDishCountPerWindow; i++)
            {
                _insertDishPositions.Add(positions[i]);
            }
        }

        /// <summary>
        /// 枚举出菜口食物以准备时确定的朝向放到餐桌的全部合法位置。
        /// <see cref="PreparedServeDish.Placements"/> 是准备那一刻的快照，出菜口等待期间餐桌仍可能变化
        /// （麻风味挪菜、临时桌放回、道具移除菜），因此玩家侧判定一律用本方法实时重算。
        /// </summary>
        public IReadOnlyList<Placement> FindPreparedServePlacements()
        {
            PreparedServeDish prepared = PreparedServe;
            if (prepared == null)
            {
                return Array.Empty<Placement>();
            }

            return DiningTable.FindValidPlacements(
                prepared.Dish.Placement.Orientation,
                prepared.Dish.Placement.RotationIndex);
        }

        /// <summary>
        /// 把出菜口食物预摆到玩家选择的位置。预摆会占用餐桌并参与预览，
        /// 但在 <see cref="ConfirmPendingDish"/> 前不增加上菜次数或触发 OnServe。
        /// </summary>
        public ServeResult PreplacePreparedServe(Placement placement)
        {
            PreparedServeDish prepared = PreparedServe;
            if (prepared == null)
            {
                return ServeResult.Fail(ServeOutcome.NoPreparedDish);
            }

            Placement preparedPlacement = prepared.Dish.Placement;
            if (placement.RotationIndex != preparedPlacement.RotationIndex
                || !DiningTable.CanPlace(preparedPlacement.Orientation, placement.Origin))
            {
                return ServeResult.Fail(ServeOutcome.InvalidPlacement);
            }

            PreparedServe = null;
            DishInstance instance = prepared.Dish;
            instance.Relocate(new Placement(
                preparedPlacement.Orientation,
                preparedPlacement.RotationIndex,
                placement.Origin));
            DiningTable.Place(instance);
            _pendingDishPlacements[instance.Id] = new PendingDishPlacement(
                instance,
                PendingDishActionKind.Serve,
                prepared,
                isOnDiningTable: true);

            return new ServeResult(ServeOutcome.Placed, instance);
        }

        /// <summary>
        /// 在不确认上菜、不触发 OnServe、也不消耗随机流的前提下，预览当前出菜口食物放到指定位置后的分数。
        /// 临时放置总会在 finally 中回滚，PreparedServe、食谱、上菜次数与餐桌内容保持不变。
        /// </summary>
        public PreparedPlacementPreview PreviewPreparedPlacement(Placement placement)
            => PreviewPreparedPlacementCore(placement, solverSample: false);

        /// <summary>
        /// 自动求解器专用的无副作用候选预览。与 UI 预览不同，这里用一组固定的共同随机样本
        /// 处理复制、甜蜜传递与概率结算，并且不套用最低上菜数的显示门槛。同一餐桌状态下
        /// 所有候选从相同样本状态开始，比较候选时没有随机噪声；样本流与局内随机流完全隔离。
        /// </summary>
        public PreparedPlacementPreview PreviewPreparedPlacementForSolver(Placement placement)
            => PreviewPreparedPlacementCore(placement, solverSample: true);

        /// <summary>
        /// 自动求解器专用的单个共同随机样本；只执行一次完整评分，不消耗局内随机流，
        /// 也不受最低上菜数显示门槛影响。
        /// </summary>
        public BigDouble PreviewScoreForSolver()
        {
            // 固定分层序列是 solver policy 的一部分，不读取 _rng.State，更不会推进 _rng。
            // 每个候选都从相同的 selector 计数开始，因此这是 common-random-number 比较而非
            // “窥视”正式结算随机。sampler 和 delegate 按 BattleSession 复用，候选热循环不分配 RNG。
            _solverPreviewSampler.Reset();
            return CalculatePreviewScore(
                _solverPreviewSampler.CopySkillSelector,
                _solverPreviewSampler.TransferTargetSelector,
                _solverPreviewSampler.RandomIntegerSelector,
                captureDiagnostics: false).Total;
        }

        private PreparedPlacementPreview PreviewPreparedPlacementCore(
            Placement placement,
            bool solverSample)
        {
            PreparedServeDish prepared = PreparedServe;
            if (prepared == null)
            {
                return PreparedPlacementPreview.Fail(ServeOutcome.NoPreparedDish, placement);
            }

            Placement preparedPlacement = prepared.Dish.Placement;
            if (placement.RotationIndex != preparedPlacement.RotationIndex
                || !DiningTable.CanPlace(preparedPlacement.Orientation, placement.Origin))
            {
                return PreparedPlacementPreview.Fail(ServeOutcome.InvalidPlacement, placement);
            }

            DishInstance instance = prepared.Dish;
            Placement original = instance.Placement;
            bool placed = false;
            try
            {
                instance.Relocate(new Placement(
                    preparedPlacement.Orientation,
                    preparedPlacement.RotationIndex,
                    placement.Origin));
                DiningTable.Place(instance);
                placed = true;
                BigDouble score = solverSample
                    ? PreviewScoreForSolver()
                    : PreviewScore().Total;
                return PreparedPlacementPreview.Succeeded(placement, score);
            }
            finally
            {
                if (placed)
                {
                    DiningTable.RemoveDish(instance);
                }

                instance.Relocate(original);
            }
        }

        private static IReadOnlyList<T> SelectRotatingSample<T>(
            IReadOnlyList<T> candidates,
            int count,
            int offset)
        {
            if (candidates == null || candidates.Count == 0 || count <= 0)
            {
                return Array.Empty<T>();
            }

            int take = Math.Min(count, candidates.Count);
            if (take >= candidates.Count)
            {
                return candidates;
            }

            int start = ((offset % candidates.Count) + candidates.Count) % candidates.Count;
            var selected = new List<T>(take);
            for (int i = 0; i < take; i++)
            {
                selected.Add(candidates[(start + i) % candidates.Count]);
            }

            return selected;
        }

        private static IReadOnlyList<T> SelectTransferSample<T>(
            IReadOnlyList<T> candidates,
            int count,
            int offset)
        {
            if (candidates == null || candidates.Count == 0 || count <= 0)
            {
                return Array.Empty<T>();
            }

            // 正式传递仅在候选数大于目标数时洗牌。候选全取时保持原顺序，避免
            // 求解器制造正式规则里不存在的“随机执行顺序”。
            if (count >= candidates.Count)
            {
                return candidates;
            }

            return SelectRotatingSample(candidates, count, offset);
        }

        private static int SelectStratifiedInteger(
            int minInclusive,
            int maxInclusive,
            int sample,
            int sampleCount)
        {
            if (maxInclusive < minInclusive)
            {
                (minInclusive, maxInclusive) = (maxInclusive, minInclusive);
            }

            long width = (long)maxInclusive - minInclusive + 1L;
            if (width <= 1L)
            {
                return minInclusive;
            }

            int stratum = ((sample % sampleCount) + sampleCount) % sampleCount;
            long offset = ((2L * stratum + 1L) * width) / (2L * sampleCount);
            return (int)Math.Min(maxInclusive, minInclusive + offset);
        }

        /// <summary>
        /// 单评分内复用的固定分层 selector。缓存 method-group delegate，避免每个候选创建
        /// RNG、闭包和全量 shuffle 池；只有规则确实要求从真子集中取值时才分配结果列表。
        /// </summary>
        private sealed class SolverPreviewSampler
        {
            private const int IntegerStratumCount = 4;
            private int _copyCall;
            private int _transferCall;
            private int _integerCall;

            public SolverPreviewSampler()
            {
                CopySkillSelector = SelectCopySkills;
                TransferTargetSelector = SelectTransferTargets;
                RandomIntegerSelector = SelectInteger;
            }

            public Func<IReadOnlyList<string>, int, IReadOnlyList<string>> CopySkillSelector { get; }

            public Func<IReadOnlyList<int>, int, IReadOnlyList<int>> TransferTargetSelector { get; }

            public Func<int, int, int> RandomIntegerSelector { get; }

            public void Reset()
            {
                _copyCall = 0;
                _transferCall = 0;
                _integerCall = 0;
            }

            private IReadOnlyList<string> SelectCopySkills(IReadOnlyList<string> candidates, int count)
                => SelectRotatingSample(candidates, count, _copyCall++);

            private IReadOnlyList<int> SelectTransferTargets(IReadOnlyList<int> candidates, int count)
                => SelectTransferSample(candidates, count, _transferCall++);

            private int SelectInteger(int minimum, int maximum)
                => SelectStratifiedInteger(
                    minimum,
                    maximum,
                    _integerCall++,
                    IntegerStratumCount);
        }

        /// <summary>确认一份餐桌预摆菜；出菜口来源执行上菜，临时桌来源只确认位置。</summary>
        public PendingDishConfirmResult ConfirmPendingDish(int dishId)
        {
            if (IsSettled
                || !_pendingDishPlacements.TryGetValue(dishId, out PendingDishPlacement pending)
                || !pending.IsOnDiningTable
                || !ReferenceEquals(FindDishById(dishId), pending.Dish))
            {
                return PendingDishConfirmResult.Fail();
            }

            _pendingDishPlacements.Remove(dishId);
            DishInstance instance = pending.Dish;
            if (pending.ActionKind == PendingDishActionKind.Confirm)
            {
                ReevaluateLastServedDishMultipliers();
                return new PendingDishConfirmResult(
                    true,
                    instance,
                    PendingDishActionKind.Confirm);
            }

            RecipeSlotEntry entry = pending.PreparedServe?.Entry;
            TrackServedDish(entry, instance);
            ServesUsed++;
            instance.SetServeOrder(ServesUsed);
            ApplyConfirmedServeModifiers(instance, ServesUsed);
            if (GoldCostPerConfirmedServe > 0)
            {
                PendingGold -= GoldCostPerConfirmedServe;
                RaiseServeTriggerCue(new ServeTriggerCue(
                    ServeCueSourceKind.BossDebuff,
                    _confirmedServeGoldCostSourceId,
                    _confirmedServeGoldCostSourceName,
                    instance.Id,
                    ServeCueEffectKind.GoldDelta,
                    -GoldCostPerConfirmedServe,
                    $"金币 -{GoldCostPerConfirmedServe}",
                    ServeCuePresentationKind.Penalty));
            }

            Served?.Invoke(instance, ServesUsed);
            ReevaluateLastServedDishMultipliers();

            // 上菜时（OnServe）规则：直接改运行时状态（技能）并积累金币/全局层数。
            if (!instance.SkillsDisabled)
            {
                ServeRuleResolver.ServeResolveResult serveResult =
                    ServeRuleResolver.ResolveOnServe(
                        DiningTable,
                        _db,
                        BuildHistory(),
                        instance,
                        HappyCakeLayers,
                        SweetTransferExtraTargetCount);
                PendingGold += serveResult.Gold;
                SetHappyCakeLayers(HappyCakeLayers + serveResult.HappyCakeLayerDelta + AccelFor(serveResult.HappyCakeLayerDelta));
                ApplyTransferRequests(serveResult.TransferRequests);
                ApplyCopySkillRequests(serveResult.CopySkillRequests);
            }

            bool removedAfterServe = false;
            if (RemoveFirstServedDishes && _appetizerRemoved < FirstServedDishesToRemove)
            {
                _appetizerRemoved++;
                DiningTable.RemoveDish(instance);
                SetTrackedDishStatus(instance.Id, BattleRecipeEntryStatus.Removed);
                removedAfterServe = true;
                ReevaluateLastServedDishMultipliers();
            }

            return new PendingDishConfirmResult(
                true,
                instance,
                PendingDishActionKind.Serve,
                removedAfterServe);
        }

        /// <summary>结算前确认所有仍在餐桌上的预摆菜；临时桌与出菜口内容保持不变。</summary>
        public IReadOnlyList<PendingDishConfirmResult> ConfirmAllPendingTableDishes()
        {
            var dishIds = new List<int>();
            foreach (PendingDishPlacement pending in _pendingDishPlacements.Values)
            {
                if (pending.IsOnDiningTable)
                {
                    dishIds.Add(pending.Dish.Id);
                }
            }

            var results = new List<PendingDishConfirmResult>(dishIds.Count);
            foreach (int dishId in dishIds)
            {
                PendingDishConfirmResult result = ConfirmPendingDish(dishId);
                if (result.Success)
                {
                    results.Add(result);
                }
            }

            return results;
        }

        /// <summary>保留给求解器与旧测试的原子操作：预摆后立即上菜。</summary>
        public ServeResult CommitPreparedServe(Placement placement)
        {
            ServeResult placed = PreplacePreparedServe(placement);
            if (!placed.Success)
            {
                return placed;
            }

            PendingDishConfirmResult confirmed = ConfirmPendingDish(placed.Dish.Id);
            return confirmed.Success
                ? new ServeResult(ServeOutcome.Placed, confirmed.Dish, confirmed.RemovedAfterServe)
                : ServeResult.Fail(ServeOutcome.NoPreparedDish);
        }

        private List<Placement> FindServePlacements(DishDef dish, RecipeSlotEntry entry)
        {
            // 麻：食谱里带「麻」风味的菜在上菜前即按逆时针 n×90° 旋转，用旋转后的形状随机放置；放不下则回退不旋转。
            int numbSteps = NumbStepsFor(ComposeServeFlavors(dish, entry));
            List<Placement> placements = numbSteps > 0
                ? DiningTable.FindValidPlacementsRotatedCcw(dish, numbSteps)
                : DiningTable.FindValidPlacements(dish);
            if (placements.Count == 0 && numbSteps > 0)
            {
                placements = DiningTable.FindValidPlacements(dish);
            }

            return placements;
        }

        private bool CanServeDish(DishDef dish, RecipeSlotEntry entry)
        {
            // 与 FindServePlacements 保持相同的「麻」旋转及放不下时回退基础朝向规则，
            // 但只寻找第一个合法位置，避免为未被抽中的菜构造完整 Placement 列表。
            int numbSteps = NumbStepsFor(ComposeServeFlavors(dish, entry));
            if (numbSteps > 0)
            {
                int rotationIndex = (4 - (numbSteps % 4)) % 4;
                if (DiningTable.CanFit(dish.Shape.RotatedBy(rotationIndex)))
                {
                    return true;
                }
            }

            return DiningTable.CanFit(dish.Shape);
        }

        /// <summary>枚举刚上桌且尚未锁定的食物在当前餐桌上的合法重定位位置。</summary>
        public IReadOnlyList<Placement> FindMovableDishPlacements(DishInstance dish)
        {
            if (dish == null)
            {
                return Array.Empty<Placement>();
            }

            int numbSteps = NumbStepsFor(dish.FlavorIds);
            List<Placement> placements = numbSteps > 0
                ? DiningTable.FindValidPlacementsRotatedCcw(dish.Def, numbSteps)
                : DiningTable.FindValidPlacements(dish.Def);
            if (placements.Count == 0 && numbSteps > 0)
            {
                placements = DiningTable.FindValidPlacements(dish.Def);
            }

            return placements;
        }

        /// <summary>计算当前餐桌的预览分数（不标记结算，不产生副作用），供 UI 实时展示。</summary>
        public ScoreResult PreviewScore()
        {
            if (MinimumServesForScore > 0 && ServesUsed < MinimumServesForScore)
            {
                return ZeroScoreResult();
            }

            return CalculatePreviewScore();
        }

        /// <summary>
        /// 返回当前餐桌状态下的实际「视为食物数」。未结算时复用完整但无副作用的预览结算；
        /// 结算后读取最终结果，避免把 live 规则在变化后的状态上重新计算。
        /// </summary>
        public int PreviewEffectiveCountAs(DishInstance dish)
        {
            if (dish == null)
            {
                return 1;
            }

            ScoreResult result = IsSettled && LastResult != null
                ? LastResult
                : CalculatePreviewScore();
            if (result?.DishScores != null)
            {
                foreach (DishScore score in result.DishScores)
                {
                    if (score != null && score.DishInstanceId == dish.Id)
                    {
                        return score.EffectiveCountAs;
                    }
                }
            }

            return Math.Max(1, dish.EffectiveCountAs);
        }

        private ScoreResult CalculatePreviewScore()
            => CalculatePreviewScore(
                copySkillSelector: null,
                transferTargetSelector: null,
                randomIntegerSelector: null);

        private ScoreResult CalculatePreviewScore(
            Func<IReadOnlyList<string>, int, IReadOnlyList<string>> copySkillSelector,
            Func<IReadOnlyList<int>, int, IReadOnlyList<int>> transferTargetSelector,
            Func<int, int, int> randomIntegerSelector,
            bool captureDiagnostics = true)
        {
            return _calculator.Calculate(DiningTable, _db, FinalFlat, FinalMultiplier, extraSources: BuildSettlementExtraSources(), history: BuildHistory(), initialHappyCakeLayers: HappyCakeLayers, extraCountAsPerDish: ExtraCountAsPerDish, cakeLayerThresholdReduction: CakeLayerThresholdReduction, reverseDishOrder: ReverseSettlementOrder, unservedRecipeDishes: BuildUnservedRecipeDishes(), copySkillSelector: copySkillSelector, transferTargetSelector: transferTargetSelector, randomIntegerSelector: randomIntegerSelector, passiveItemCount: PassiveItemCount, remainingFoodDiscards: FoodDiscardsRemaining, sweetTransferExtraTargetCount: SweetTransferExtraTargetCount, captureDiagnostics: captureDiagnostics);
        }

        /// <summary>「吃」：结算、应用副作用（金币/层数/技能传递/历史）并记录结果。</summary>
        public ScoreResult Settle()
        {
            ConfirmAllPendingTableDishes();
            ScoreResult result = MinimumServesForScore > 0 && ServesUsed < MinimumServesForScore
                ? ZeroScoreResult()
                : _calculator.Calculate(DiningTable, _db, FinalFlat, FinalMultiplier, extraSources: BuildSettlementExtraSources(), history: BuildHistory(), initialHappyCakeLayers: HappyCakeLayers, extraCountAsPerDish: ExtraCountAsPerDish, cakeLayerThresholdReduction: CakeLayerThresholdReduction, reverseDishOrder: ReverseSettlementOrder, unservedRecipeDishes: BuildUnservedRecipeDishes(), copySkillSelector: SelectCopySkills, transferTargetSelector: SelectTransferTargets, randomIntegerSelector: SelectRandomInteger, passiveItemCount: PassiveItemCount, remainingFoodDiscards: FoodDiscardsRemaining, sweetTransferExtraTargetCount: SweetTransferExtraTargetCount);
            ApplySideEffects(result);
            LastResult = result;
            IsSettled = true;
            ResolveRecipeRemovalRequests(result.RecipeRemovalRequests);
            return LastResult;
        }

        private static ScoreResult ZeroScoreResult()
        {
            return new ScoreResult(Array.Empty<DishScore>(), 0f, 0f, 1f);
        }

        private IReadOnlyList<int> SelectTransferTargets(IReadOnlyList<int> candidates, int count)
        {
            if (candidates == null || candidates.Count == 0 || count <= 0)
            {
                return Array.Empty<int>();
            }

            var targets = new List<int>(candidates);
            if (targets.Count > count)
            {
                _rng.Shuffle(targets);
                targets = targets.GetRange(0, count);
            }

            return targets;
        }

        private int SelectRandomInteger(int minInclusive, int maxInclusive)
        {
            if (maxInclusive < minInclusive)
            {
                (minInclusive, maxInclusive) = (maxInclusive, minInclusive);
            }

            return _rng.Range(minInclusive, maxInclusive + 1);
        }

        private void ResolveRecipeRemovalRequests(IReadOnlyList<RecipeRemovalRequest> requests)
        {
            _lastRecipeRemovalOutcomes.Clear();
            if (requests == null)
            {
                return;
            }

            foreach (RecipeRemovalRequest request in requests)
            {
                _lastRecipeRemovalOutcomes.Add(new RecipeRemovalOutcome(
                    request,
                    _rng.NextBool(request.Probability)));
            }
        }

        private List<string> ComposeServeSkills(DishDef dish, RecipeSlotEntry entry)
        {
            if (entry == null || entry.ExtraSkillIds.Count == 0)
            {
                return TagComposer.ComposeSkills(dish.SkillIds);
            }

            var ids = new List<string>(dish.SkillIds.Count + entry.ExtraSkillIds.Count);
            ids.AddRange(dish.SkillIds);
            ids.AddRange(entry.ExtraSkillIds);
            return TagComposer.ComposeSkills(ids);
        }

        /// <summary>统计一组风味里「麻」(Rotate) 的逆时针旋转步数（各麻风味 effectValue 之和）。</summary>
        /// <summary>
        /// 合成上菜风味：食谱变体自带风味 + 玩家用「调味小票」永久附加的额外风味。
        /// 有额外风味时解除单槽上限以支持叠加（如甜×n）；无额外风味时沿用单槽语义（后者覆盖）。
        /// </summary>
        private List<string> ComposeServeFlavors(DishDef dish, RecipeSlotEntry entry)
        {
            if (entry == null || entry.ExtraFlavorIds.Count == 0)
            {
                return TagComposer.ComposeFlavors(new[] { dish.FlavorId });
            }

            var ids = new List<string>(1 + entry.ExtraFlavorIds.Count) { dish.FlavorId };
            ids.AddRange(entry.ExtraFlavorIds);
            return TagComposer.ComposeFlavors(ids, removeFlavorCap: true);
        }

        private int NumbStepsFor(IReadOnlyList<string> flavorIds)
        {
            if (flavorIds == null)
            {
                return 0;
            }

            int steps = 0;
            foreach (string flavorId in flavorIds)
            {
                Gameplay.Model.FlavorDef flavor = _db.GetFlavor(flavorId);
                if (flavor != null && flavor.EffectType == Gameplay.Model.FlavorEffectType.Rotate)
                {
                    steps += (int)flavor.EffectValue;
                }
            }

            return steps;
        }

        private static void ApplyPreparedEntryFlags(DishInstance instance, RecipeSlotEntry entry)
        {
            if (instance == null || entry == null)
            {
                return;
            }

            if (entry.DisableSkills)
            {
                instance.DisableSkills();
            }

            if (entry.ExcludeFromScore)
            {
                instance.ExcludeFromScore();
            }
        }

        private static void ApplyPlacementEntryModifiers(DishInstance instance, RecipeSlotEntry entry)
        {
            if (instance == null || entry == null)
            {
                return;
            }

            if (entry.ScoreMultiplier > BigDouble.Zero
                && BigDouble.Abs(entry.ScoreMultiplier - BigDouble.One) > 0.0001d)
            {
                instance.MultiplyPermanentMult(entry.ScoreMultiplier);
            }

            if (BigDouble.Abs(entry.ScoreFlatBonus) > 0.0001d)
            {
                instance.AddPermanentFlat(entry.ScoreFlatBonus);
            }
        }

        private void ApplyConfirmedServeModifiers(DishInstance instance, int serveIndex)
        {
            if (instance == null)
            {
                return;
            }

            if (Math.Abs(BaseScoreMultiplier - 1f) > 0.0001f)
            {
                instance.MultiplyTemporaryBase(BaseScoreMultiplier);
                RaiseServeTriggerCue(new ServeTriggerCue(
                    ServeCueSourceKind.BossDebuff,
                    _baseScoreMultiplierSourceId,
                    _baseScoreMultiplierSourceName,
                    instance.Id,
                    ServeCueEffectKind.BaseScoreFactor,
                    BaseScoreMultiplier,
                    $"基础分 ×{FormatNumber(BaseScoreMultiplier)}",
                    ServeCuePresentationKind.Penalty));
            }

            if (AlternateServeMultiplier)
            {
                float multiplier = serveIndex % 2 == 1
                    ? _alternateServeMultiplierLow
                    : _alternateServeMultiplierHigh;
                instance.MultiplyServeMultiplier(multiplier);
                RaiseServeTriggerCue(new ServeTriggerCue(
                    ServeCueSourceKind.BossDebuff,
                    _alternateServeMultiplierSourceId,
                    _alternateServeMultiplierSourceName,
                    instance.Id,
                    ServeCueEffectKind.MultiplierFactor,
                    multiplier,
                    $"倍率 ×{FormatNumber(multiplier)}",
                    multiplier >= 1f
                        ? ServeCuePresentationKind.Gain
                        : ServeCuePresentationKind.Penalty));
            }
            else if (RandomServeMultiplier)
            {
                if (_randomServeMultiplierStep <= 0f)
                {
                    return;
                }

                float span = Math.Max(0f, _randomServeMultiplierMax - _randomServeMultiplierMin);
                int stepCount = Math.Max(0, (int)Math.Round(span / _randomServeMultiplierStep, MidpointRounding.AwayFromZero));
                int stepIndex = stepCount > 0 ? _rng.Range(0, stepCount + 1) : 0;
                float multiplier = Math.Min(_randomServeMultiplierMax, _randomServeMultiplierMin + stepIndex * _randomServeMultiplierStep);
                instance.MultiplyServeMultiplier(multiplier);
                RaiseServeTriggerCue(new ServeTriggerCue(
                    ServeCueSourceKind.BossDebuff,
                    _randomServeMultiplierSourceId,
                    _randomServeMultiplierSourceName,
                    instance.Id,
                    ServeCueEffectKind.MultiplierFactor,
                    multiplier,
                    $"倍率 ×{FormatNumber(multiplier)}",
                    multiplier >= 1f
                        ? ServeCuePresentationKind.Gain
                        : ServeCuePresentationKind.Penalty));
            }
        }

        private void ReevaluateLastServedDishMultipliers(string sourceId = null)
        {
            if (_lastServedDishMultiplierRules.Count == 0)
            {
                return;
            }

            DishInstance latest = null;
            foreach (DishInstance dish in DiningTable.Dishes)
            {
                if (dish.ServeOrder > 0
                    && (latest == null || dish.ServeOrder > latest.ServeOrder))
                {
                    latest = dish;
                }
            }

            foreach (LastServedDishMultiplierRule rule in _lastServedDishMultiplierRules)
            {
                if (!string.IsNullOrEmpty(sourceId)
                    && !string.Equals(rule.SourceId, sourceId, StringComparison.Ordinal))
                {
                    continue;
                }

                DishInstance next = rule.IsActive == null || rule.IsActive()
                    ? latest
                    : null;
                if (ReferenceEquals(rule.CurrentDish, next))
                {
                    continue;
                }

                DishInstance previous = rule.CurrentDish;
                if (previous != null)
                {
                    SetServeMultiplierFlatForSource(
                        previous,
                        rule.SourceId,
                        0f,
                        ServeCueSourceKind.PassiveItem,
                        rule.SourceName,
                        $"倍率 {FormatSigned(-rule.Value)}（效果转移）",
                        ServeCuePresentationKind.Cancel,
                        pulseSource: next == null);
                }

                rule.CurrentDish = next;
                if (next != null)
                {
                    SetServeMultiplierFlatForSource(
                        next,
                        rule.SourceId,
                        rule.Value,
                        ServeCueSourceKind.PassiveItem,
                        rule.SourceName,
                        $"倍率 {FormatSigned(rule.Value)}",
                        ServeCuePresentationKind.Gain,
                        pulseSource: true);
                }
            }
        }

        private void SetServeMultiplierFlatForSource(
            DishInstance dish,
            string sourceId,
            float desiredValue,
            ServeCueSourceKind sourceKind,
            string sourceName,
            string text,
            ServeCuePresentationKind presentationKind,
            bool pulseSource)
        {
            if (dish == null || string.IsNullOrEmpty(sourceId))
            {
                return;
            }

            float delta = dish.SetServeMultiplierFlatForSource(sourceId, desiredValue);
            if (Math.Abs(delta) < 0.0001f)
            {
                return;
            }

            ServeMultiplierFlatApplied?.Invoke(dish, delta);
            RaiseServeTriggerCue(new ServeTriggerCue(
                sourceKind,
                sourceId,
                sourceName,
                dish.Id,
                ServeCueEffectKind.MultiplierFlat,
                delta,
                text,
                presentationKind,
                pulseSource));
        }

        private void RaiseServeTriggerCue(ServeTriggerCue cue)
        {
            if (cue != null && !string.IsNullOrEmpty(cue.SourceId))
            {
                ServeTriggerCueRaised?.Invoke(cue);
            }
        }

        private static string FormatSigned(float value)
            => value >= 0f ? $"+{FormatNumber(value)}" : FormatNumber(value);

        private static string FormatNumber(float value)
            => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>把历史/食谱打包为只读快照注入结算（读取本次结算之前的状态）。</summary>
        private IScoreHistory BuildHistory()
        {
            return new ScoreHistory(
                new Dictionary<string, int>(_runSettled),
                new Dictionary<string, int>(_mealSettled),
                _recipeBaseIds);
        }

        /// <summary>收集当前仍未上菜的食谱条目及其全部风味，供酸/咸在结算开始时遍历。</summary>
        private List<UnservedRecipeDish> BuildUnservedRecipeDishes()
        {
            var result = new List<UnservedRecipeDish>();
            for (int slotIndex = 0; slotIndex < _slots.Count; slotIndex++)
            {
                foreach (RecipeSlotEntry entry in _slots[slotIndex].Entries)
                {
                    DishDef dish = _db.GetDish(entry.DishId);
                    result.Add(new UnservedRecipeDish(
                        slotIndex,
                        entry.DishId,
                        dish != null
                            ? ComposeServeFlavors(dish, entry)
                            : new List<string>(entry.ExtraFlavorIds)));
                }
            }

            return result;
        }

        private void CaptureRecipeSnapshot()
        {
            int entryId = 1;
            for (int slotIndex = 0; slotIndex < _slots.Count; slotIndex++)
            {
                RecipeSlot slot = _slots[slotIndex];
                foreach (RecipeSlotEntry entry in slot.Entries)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    var tracked = new TrackedRecipeEntry(entryId++, slotIndex, entry);
                    _trackedRecipeEntries.Add(tracked);
                    _trackedRecipeEntriesBySource[entry] = tracked;

                    DishDef def = _db.GetDish(entry.DishId);
                    if (def != null)
                    {
                        _recipeBaseIds.Add(def.BaseId);
                    }
                }
            }
        }

        private void ApplySideEffects(ScoreResult result)
        {
            _lastRecipeScoreFlatDeltas.Clear();
            _lastRecipeScoreMultiplierDeltas.Clear();

            // 金币入账（结算侧效果）。
            PendingGold += result.GoldDelta;

            // 银材质：每个银格登记一条带来源的独立判定请求；仅正式结算按请求概率掷骰。
            _pendingActiveItemGrantSources.Clear();
            foreach (SilverItemRollRequest request in result.SilverItemRolls)
            {
                if (_rng.NextBool(request.Probability))
                {
                    _pendingActiveItemGrantSources.Add(request.DishInstanceId);
                }
            }

            // 全局欢乐蛋糕层数：写回经营挑战级计数器（层数净增时叠加装饰品和消耗品加速）。
            SetHappyCakeLayers(HappyCakeLayers + result.HappyCakeLayerDelta + AccelFor(result.HappyCakeLayerDelta));

            // 技能传递。
            foreach (SkillTransferSideEffect transfer in result.SkillTransfers)
            {
                DishInstance inst = FindInstance(transfer.TargetInstanceId);
                if (inst == null)
                {
                    continue;
                }

                string label = string.IsNullOrEmpty(transfer.SourceName) ? null : $"{transfer.SourceName}<甜蜜传递>";
                foreach (SkillEffect effect in transfer.Effects)
                {
                    inst.AddTransferredSkill(effect, label, transfer.SourceInstanceId);
                }

                ApplySweetTransferTargetMultiplier(inst);
                ApplySweetTransferSourceMultiplier(FindInstance(transfer.SourceInstanceId));
                SweetTransferTriggered?.Invoke(new SweetTransferOccurrence(
                    transfer.SourceInstanceId,
                    transfer.TargetInstanceId));
            }

            // 技能复制：结算阶段只登记候选池，正式结算后由会话随机流落地，避免预览消耗 RNG。
            ApplyCopySkillRequests(result.CopySkillRequests);

            // 本场临时分类：计分上下文中已即时生效，正式结算后写回实例供后续结算继续读取。
            foreach (TemporaryCategorySideEffect category in result.TemporaryCategories)
            {
                FindInstance(category.DishInstanceId)?.AddTemporaryCategory(
                    category.Category,
                    category.SourceName,
                    category.EffectDescription);
            }

            // 永久分 / 永久倍率 / 视为食物数：写回实例（经营挑战内跨结算持久）。
            foreach (KeyValuePair<int, BigDouble> kv in result.PermanentFlatDeltas)
            {
                DishInstance inst = FindInstance(kv.Key);
                if (inst == null)
                {
                    continue;
                }

                inst.AddPermanentFlat(kv.Value);
                if (inst.SourceSlotIndex >= 0 && inst.SourceDishIndex >= 0)
                {
                    _lastRecipeScoreFlatDeltas.Add(new RecipeScoreFlatDelta(
                        inst.SourceSlotIndex,
                        inst.SourceDishIndex,
                        kv.Value));
                }
            }

            foreach (KeyValuePair<int, BigDouble> kv in result.PermanentMultDeltas)
            {
                DishInstance inst = FindInstance(kv.Key);
                if (inst == null)
                {
                    continue;
                }

                inst.MultiplyPermanentMult(kv.Value);
                if (inst.SourceSlotIndex >= 0 && inst.SourceDishIndex >= 0)
                {
                    _lastRecipeScoreMultiplierDeltas.Add(new RecipeScoreMultiplierDelta(
                        inst.SourceSlotIndex,
                        inst.SourceDishIndex,
                        kv.Value));
                }
            }

            // 历史累计：本次结算把盘面每个食物的 BaseId 计入大局/小局。
            var increments = new Dictionary<string, int>();
            foreach (DishInstance dish in DiningTable.Dishes)
            {
                if (dish.ExcludedFromScore)
                {
                    continue;
                }

                string baseId = dish.Def.BaseId;
                increments.TryGetValue(baseId, out int inc);
                increments[baseId] = inc + 1;
            }

            foreach (KeyValuePair<string, int> kv in increments)
            {
                _mealSettled.TryGetValue(kv.Key, out int meal);
                _mealSettled[kv.Key] = meal + kv.Value;
                _runSettled.TryGetValue(kv.Key, out int run);
                _runSettled[kv.Key] = run + kv.Value;
            }

            LastSettledIncrements = increments;
        }

        /// <summary>甜蜜传递落地：对每个请求，用随机流在候选目标中均权取 Count 个（0=全部），把技能追加给它们并标注来源。</summary>
        private void ApplyTransferRequests(IReadOnlyList<SkillTransferRequest> requests)
        {
            if (requests == null || requests.Count == 0)
            {
                return;
            }

            foreach (SkillTransferRequest request in requests)
            {
                if (request.CandidateTargetIds.Count == 0 || request.Effects.Count == 0)
                {
                    continue;
                }

                var targets = new List<int>(request.CandidateTargetIds);
                if (request.Count > 0 && targets.Count > request.Count)
                {
                    _rng.Shuffle(targets);
                    targets = targets.GetRange(0, request.Count);
                }

                string sourceLabel = $"{request.SourceName}<甜蜜传递>";
                foreach (int targetId in targets)
                {
                    DishInstance target = FindInstance(targetId);
                    if (target == null || target.Id == request.SourceInstanceId)
                    {
                        continue;
                    }

                    foreach (SkillEffect effect in request.Effects)
                    {
                        target.AddTransferredSkill(effect, sourceLabel, request.SourceInstanceId);
                    }

                    ApplySweetTransferTargetMultiplier(target);
                    ApplySweetTransferSourceMultiplier(FindInstance(request.SourceInstanceId));
                    SweetTransferTriggered?.Invoke(new SweetTransferOccurrence(
                        request.SourceInstanceId,
                        targetId));
                }
            }
        }

        private IReadOnlyList<IScoreEffectSource> BuildSettlementExtraSources()
        {
            if (_settlementDishMultiplierFlat <= 0f)
            {
                return Array.Empty<IScoreEffectSource>();
            }

            return new IScoreEffectSource[]
            {
                new AllDishMultiplierFlatSource(
                    _settlementDishMultiplierFlat,
                    _settlementDishMultiplierItemId,
                    _settlementDishMultiplierItemName),
            };
        }

        private void ApplySweetTransferTargetMultiplier(DishInstance target)
        {
            if (target != null && SweetTransferTargetMultiplier > 0f)
            {
                target.AddPermanentMultBonus(SweetTransferTargetMultiplier);
            }
        }

        private void ApplySweetTransferSourceMultiplier(DishInstance source)
        {
            if (source != null && SweetTransferSourceMultiplier > 0f)
            {
                source.AddPermanentMultBonus(SweetTransferSourceMultiplier);
            }
        }

        /// <summary>技能复制落地：对每个请求，用随机流从候选池挑选 Count 个不同技能加到目标实例。</summary>
        private void ApplyCopySkillRequests(IReadOnlyList<CopySkillRequest> requests)
        {
            if (requests == null || requests.Count == 0)
            {
                return;
            }

            foreach (CopySkillRequest request in requests)
            {
                DishInstance target = FindInstance(request.TargetInstanceId);
                if (target == null || request.Candidates.Count == 0)
                {
                    continue;
                }

                IReadOnlyList<string> selected = request.SelectedSkillIds.Count > 0
                    ? request.SelectedSkillIds
                    : SelectCopySkills(request.Candidates, request.Count);
                string label = string.IsNullOrEmpty(request.SourceName) ? null : $"{request.SourceName}<技能复制>";
                for (int i = 0; i < selected.Count; i++)
                {
                    target.AddSkill(selected[i], label);
                }
            }
        }

        private IReadOnlyList<string> SelectCopySkills(IReadOnlyList<string> candidates, int count)
        {
            if (candidates == null || candidates.Count == 0 || count <= 0)
            {
                return Array.Empty<string>();
            }

            var pool = new List<string>(candidates);
            _rng.Shuffle(pool);
            int take = Math.Min(count, pool.Count);
            var selected = new List<string>(take);
            for (int i = 0; i < take; i++)
            {
                selected.Add(pool[i]);
            }

            return selected;
        }

        /// <summary>清理本场经营挑战产生的临时克隆实例（经营挑战结束时调用）。</summary>
        public void ClearTemporaryDishes()
        {
            var temporaries = new List<DishInstance>();
            foreach (DishInstance dish in DiningTable.Dishes)
            {
                if (dish.IsTemporary)
                {
                    temporaries.Add(dish);
                }
            }

            foreach (DishInstance dish in temporaries)
            {
                DiningTable.RemoveDish(dish);
            }
        }

        /// <summary>层数净增（delta&gt;0）时返回额外加速层数，否则 0（装饰品和消耗品「蛋糕膨胀」）。</summary>
        private int AccelFor(int delta)
        {
            return delta > 0 ? CakeLayerAccelBonus : 0;
        }

        private void SetHappyCakeLayers(int layers)
        {
            int before = HappyCakeLayers;
            HappyCakeLayers = Math.Max(0, layers);
            if (before != HappyCakeLayers)
            {
                HappyCakeLayersChanged?.Invoke(before, HappyCakeLayers);
            }
        }

        private DishInstance FindInstance(int id)
        {
            foreach (DishInstance dish in DiningTable.Dishes)
            {
                if (dish.Id == id)
                {
                    return dish;
                }
            }

            return null;
        }

        /// <summary>读档恢复待领奖界面时，把会话标记为已结算的只读 UI 状态；不触发任何结算副作用。</summary>
        public void RestoreSettledForRewardView(BigDouble total)
        {
            RestoreSettledForRewardView(
                total,
                -1,
                Array.Empty<DishScore>(),
                BigDouble.Max(BigDouble.Zero, total),
                0f,
                1f,
                hasDetailedScore: false);
        }

        /// <summary>
        /// 从待领奖快照恢复只读结算状态。层数与详细分数均为表现恢复数据，不重复执行结算技能或资源副作用。
        /// </summary>
        public void RestoreSettledForRewardView(
            BigDouble total,
            int finalHappyCakeLayers,
            IReadOnlyList<DishScore> dishScores,
            BigDouble rawSum,
            BigDouble finalFlat,
            BigDouble finalMultiplier,
            bool hasDetailedScore)
        {
            if (finalHappyCakeLayers >= 0)
            {
                SetHappyCakeLayers(finalHappyCakeLayers);
            }

            LastResult = hasDetailedScore
                ? new ScoreResult(
                    dishScores ?? Array.Empty<DishScore>(),
                    rawSum,
                    finalFlat,
                    finalMultiplier)
                : new ScoreResult(Array.Empty<DishScore>(), BigDouble.Max(BigDouble.Zero, total), 0f, 1f);
            IsSettled = true;
        }

        public bool IsWin => IsSettled && LastResult != null && LastResult.Total >= RequiredScore;

        /// <summary>清空餐桌（消耗品「重摆铃」）。已结算后不允许。</summary>
        public void ClearBoard()
        {
            if (IsSettled)
            {
                return;
            }

            foreach (DishInstance dish in DiningTable.Dishes)
            {
                MarkDishStatusBeforePendingRemoval(dish.Id, BattleRecipeEntryStatus.Removed);
            }

            DiningTable.Clear();
            RemovePendingPlacementsNotOnTable();
            ReevaluateLastServedDishMultipliers();
        }

        /// <summary>按 Id 查找餐桌上的菜；不存在返回 null。</summary>
        public DishInstance FindDishById(int dishId)
        {
            foreach (DishInstance dish in DiningTable.Dishes)
            {
                if (dish.Id == dishId)
                {
                    return dish;
                }
            }

            return null;
        }

        /// <summary>按 Id 查找临时桌上的菜；不存在返回 null。</summary>
        public DishInstance FindTemporaryAreaDishById(int dishId)
        {
            foreach (DishInstance dish in _temporaryAreaDishes)
            {
                if (dish.Id == dishId)
                {
                    return dish;
                }
            }

            return null;
        }

        /// <summary>
        /// 按麻风味变化量旋转已上桌食物，并移动到独立临时桌。正数逆时针，负数顺时针。
        /// 绕当前占格质心旋转后再归一化原点，避免 1×3 等非正方形把占格平移到视觉中心之外。
        /// 不回滚该菜已发生的上菜次数、OnServe 或费用副作用。
        /// </summary>
        public bool MoveDishToTemporaryAreaAfterRotationDelta(int dishId, int ccwSteps)
        {
            if (IsSettled || ccwSteps == 0)
            {
                return false;
            }

            DishInstance dish = FindDishById(dishId);
            if (dish == null)
            {
                return false;
            }

            int rotationIndex = ((dish.Placement.RotationIndex - ccwSteps) % 4 + 4) % 4;
            DishShape orientation = dish.Def.Shape.RotatedBy(rotationIndex);
            var rotatedPlacement = new Placement(
                orientation,
                rotationIndex,
                DishShape.OriginAfterCenteredRotation(
                    dish.Placement.Orientation,
                    dish.Placement.Origin,
                    orientation));

            DiningTable.RemoveDish(dish);
            dish.Relocate(rotatedPlacement);
            _temporaryAreaDishes.Add(dish);
            if (_pendingDishPlacements.TryGetValue(dish.Id, out PendingDishPlacement pending))
            {
                pending.IsOnDiningTable = false;
            }
            ReevaluateLastServedDishMultipliers();
            return true;
        }

        /// <summary>枚举临时桌食物以当前固定朝向放回主餐桌的全部合法位置。</summary>
        public IReadOnlyList<Placement> FindTemporaryAreaDishPlacements(int dishId)
        {
            DishInstance dish = FindTemporaryAreaDishById(dishId);
            if (dish == null)
            {
                return Array.Empty<Placement>();
            }

            return DiningTable.FindValidPlacements(
                dish.Placement.Orientation,
                dish.Placement.RotationIndex);
        }

        /// <summary>
        /// 把临时桌食物预摆回主餐桌，等待玩家点击“确认”。
        /// 如果它原本就是尚未上菜的预摆菜，则保留“上菜”动作。
        /// </summary>
        public bool PreplaceTemporaryAreaDish(int dishId, Placement placement)
        {
            if (IsSettled)
            {
                return false;
            }

            DishInstance dish = FindTemporaryAreaDishById(dishId);
            if (dish == null
                || placement.RotationIndex != dish.Placement.RotationIndex
                || !DiningTable.CanPlace(dish.Placement.Orientation, placement.Origin))
            {
                return false;
            }

            var committedPlacement = new Placement(
                dish.Placement.Orientation,
                dish.Placement.RotationIndex,
                placement.Origin);
            dish.Relocate(committedPlacement);
            DiningTable.Place(dish);
            _temporaryAreaDishes.Remove(dish);
            if (_pendingDishPlacements.TryGetValue(dish.Id, out PendingDishPlacement pending))
            {
                pending.IsOnDiningTable = true;
            }
            else
            {
                _pendingDishPlacements[dish.Id] = new PendingDishPlacement(
                    dish,
                    PendingDishActionKind.Confirm,
                    preparedServe: null,
                    isOnDiningTable: true);
            }

            return true;
        }

        /// <summary>保留旧原子语义：放回临时桌菜并立即确认。</summary>
        public bool CommitTemporaryAreaDish(int dishId, Placement placement)
        {
            if (!PreplaceTemporaryAreaDish(dishId, placement))
            {
                return false;
            }

            return ConfirmPendingDish(dishId).Success;
        }

        /// <summary>消耗品：给指定餐桌菜永久加分（对标杀戮尖塔2 火焰药水打目标）。成功返回 true。</summary>
        public bool AddPermanentScoreToDish(int dishId, float amount)
        {
            if (IsSettled)
            {
                return false;
            }

            DishInstance dish = FindDishById(dishId);
            if (dish == null)
            {
                return false;
            }

            dish.AddPermanentFlat(amount);
            return true;
        }

        /// <summary>消耗品：给指定餐桌菜永久倍率加成。成功返回 true。</summary>
        public bool MultiplyScoreOnDish(int dishId, float multiplier)
        {
            if (IsSettled)
            {
                return false;
            }

            DishInstance dish = FindDishById(dishId);
            if (dish == null)
            {
                return false;
            }

            dish.MultiplyPermanentMult(multiplier);
            return true;
        }

        /// <summary>消耗品：给指定餐桌菜加「视为食物数」。成功返回 true。</summary>
        public bool AddCountAsToDish(int dishId, int amount)
        {
            if (IsSettled)
            {
                return false;
            }

            DishInstance dish = FindDishById(dishId);
            if (dish == null)
            {
                return false;
            }

            dish.AddCountAsBonus(amount);
            return true;
        }

        public bool AddFlavorToDishById(int dishId, string flavorId)
        {
            if (IsSettled || string.IsNullOrEmpty(flavorId))
            {
                return false;
            }

            DishInstance dish = FindDishById(dishId);
            if (dish == null)
            {
                return false;
            }

            dish.AddFlavor(flavorId);
            return true;
        }

        public bool RemoveFlavorFromDishById(int dishId, string flavorId)
        {
            if (IsSettled)
            {
                return false;
            }

            DishInstance dish = FindDishById(dishId);
            return dish != null && dish.RemoveFlavor(flavorId);
        }

        public bool ReplaceFlavorOnDishById(int dishId, string toFlavorId)
        {
            if (IsSettled || string.IsNullOrEmpty(toFlavorId))
            {
                return false;
            }

            DishInstance dish = FindDishById(dishId);
            return dish != null && dish.ReplaceFlavor(toFlavorId);
        }

        /// <summary>消耗品：移除指定餐桌菜（对标破坏族）。成功返回 true。</summary>
        public bool DestroyDishById(int dishId)
        {
            if (IsSettled)
            {
                return false;
            }

            DishInstance dish = FindDishById(dishId);
            if (dish == null)
            {
                return false;
            }

            DiningTable.RemoveDish(dish);
            MarkDishStatusBeforePendingRemoval(dish.Id, BattleRecipeEntryStatus.Removed);
            _pendingDishPlacements.Remove(dish.Id);
            ReevaluateLastServedDishMultipliers();
            return true;
        }

        /// <summary>
        /// 消耗品：复制指定餐桌菜到空位（对标增殖族）。摆放位置由经营挑战随机流选取，
        /// 复制产物为常驻实例。空位不足返回 false。
        /// </summary>
        public bool DuplicateDishById(int dishId)
        {
            if (IsSettled)
            {
                return false;
            }

            DishInstance source = FindDishById(dishId);
            if (source == null)
            {
                return false;
            }

            List<Placement> placements = DiningTable.FindValidPlacements(source.Def);
            if (placements.Count == 0)
            {
                return false;
            }

            Placement placement = placements[_rng.Range(0, placements.Count)];
            var clone = new DishInstance(_nextInstanceId++, source.Def, placement, source.SkillIds, source.FlavorIds);
            clone.SetSourceRecipeIndex(source.SourceSlotIndex, source.SourceDishIndex);
            clone.CopySkillSourcesFrom(source);
            clone.CopyServeMultiplierFlatSourcesFrom(source, sourceId => !IsLastServedMultiplierSource(sourceId));
            clone.CopyTransferredSkillsFrom(source);
            DiningTable.Place(clone);
            return true;
        }

        private bool IsLastServedMultiplierSource(string sourceId)
        {
            for (int i = 0; i < _lastServedDishMultiplierRules.Count; i++)
            {
                if (string.Equals(
                        _lastServedDishMultiplierRules[i].SourceId,
                        sourceId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public bool GenerateDishAt(string dishId, GridPos origin)
        {
            if (IsSettled || string.IsNullOrEmpty(dishId))
            {
                return false;
            }

            DishDef def = _db.GetDish(dishId);
            if (def == null)
            {
                return false;
            }

            const int rotationIndex = 0;
            DishShape shape = def.Shape;
            if (!DiningTable.CanPlace(shape, origin))
            {
                return false;
            }

            var placement = new Placement(shape, rotationIndex, origin);
            IReadOnlyList<string> flavors = string.IsNullOrEmpty(def.FlavorId)
                ? Array.Empty<string>()
                : new[] { def.FlavorId };
            var instance = new DishInstance(_nextInstanceId++, def, placement, def.SkillIds, flavors);
            DiningTable.Place(instance);
            return true;
        }

        /// <summary>当前所有食谱槽是否都无法再上菜（用于提示玩家结算）。</summary>
        public bool CanServeAny()
        {
            if (PreparedServe != null || HasPendingTablePlacements)
            {
                return true;
            }

            if (MaxServes >= 0 && ServesUsed >= MaxServes)
            {
                return false;
            }

            if (ShouldInsertDishOnNextBellPrepare())
            {
                DishDef insertedDish = _db.GetDish(_insertDishId);
                if (insertedDish != null && DiningTable.CanFit(insertedDish))
                {
                    return true;
                }
            }

            foreach (RecipeSlot slot in _slots)
            {
                if (slot.IsEmpty)
                {
                    continue;
                }

                foreach (string dishId in slot.Remaining)
                {
                    DishDef def = _db.GetDish(dishId);
                    if (def != null && DiningTable.CanFit(def))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private BattleRecipeEntryStatus ResolveDisplayStatus(TrackedRecipeEntry tracked)
        {
            if (tracked.Status != BattleRecipeEntryStatus.Normal)
            {
                return tracked.Status;
            }

            if (IsSettled || (MaxServes >= 0 && ServesUsed >= MaxServes))
            {
                return BattleRecipeEntryStatus.CannotPlace;
            }

            DishDef dish = _db.GetDish(tracked.Entry.DishId);
            return dish != null && FindServePlacements(dish, tracked.Entry).Count > 0
                ? BattleRecipeEntryStatus.Normal
                : BattleRecipeEntryStatus.CannotPlace;
        }

        private void SetTrackedStatus(RecipeSlotEntry entry, BattleRecipeEntryStatus status)
        {
            if (entry != null && _trackedRecipeEntriesBySource.TryGetValue(entry, out TrackedRecipeEntry tracked))
            {
                tracked.Status = status;
            }
        }

        private void TrackServedDish(RecipeSlotEntry entry, DishInstance dish)
        {
            if (entry == null
                || dish == null
                || !_trackedRecipeEntriesBySource.TryGetValue(entry, out TrackedRecipeEntry tracked))
            {
                return;
            }

            tracked.Status = BattleRecipeEntryStatus.Served;
            _trackedRecipeEntriesByDishId[dish.Id] = tracked;
        }

        private void SetTrackedDishStatus(int dishId, BattleRecipeEntryStatus status)
        {
            if (_trackedRecipeEntriesByDishId.TryGetValue(dishId, out TrackedRecipeEntry tracked))
            {
                tracked.Status = status;
            }
        }

        private void MarkDishStatusBeforePendingRemoval(
            int dishId,
            BattleRecipeEntryStatus status)
        {
            if (_pendingDishPlacements.TryGetValue(dishId, out PendingDishPlacement pending)
                && pending.ActionKind == PendingDishActionKind.Serve)
            {
                SetTrackedStatus(pending.PreparedServe?.Entry, status);
                return;
            }

            SetTrackedDishStatus(dishId, status);
        }

        private void RemovePendingPlacementsNotOnTable()
        {
            var removedIds = new List<int>();
            foreach (PendingDishPlacement pending in _pendingDishPlacements.Values)
            {
                if (pending.IsOnDiningTable && FindDishById(pending.Dish.Id) == null)
                {
                    removedIds.Add(pending.Dish.Id);
                }
            }

            foreach (int dishId in removedIds)
            {
                _pendingDishPlacements.Remove(dishId);
            }
        }

        private sealed class LastServedDishMultiplierRule
        {
            public LastServedDishMultiplierRule(
                string sourceId,
                string sourceName,
                float value,
                Func<bool> isActive)
            {
                SourceId = sourceId;
                SourceName = sourceName ?? sourceId;
                Value = value;
                IsActive = isActive;
            }

            public string SourceId { get; }

            public string SourceName { get; }

            public float Value { get; }

            public Func<bool> IsActive { get; }

            public DishInstance CurrentDish { get; set; }
        }

        private sealed class TrackedRecipeEntry
        {
            public TrackedRecipeEntry(int entryId, int slotIndex, RecipeSlotEntry entry)
            {
                EntryId = entryId;
                SlotIndex = slotIndex;
                Entry = entry;
            }

            public int EntryId { get; }

            public int SlotIndex { get; }

            public RecipeSlotEntry Entry { get; }

            public BattleRecipeEntryStatus Status { get; set; } = BattleRecipeEntryStatus.Normal;
        }

        private sealed class AllDishMultiplierFlatSource : IScoreEffectSource
        {
            private readonly float _value;
            private readonly string _itemId;
            private readonly string _itemName;

            public AllDishMultiplierFlatSource(float value, string itemId, string itemName)
            {
                _value = value;
                _itemId = itemId ?? string.Empty;
                _itemName = itemName ?? _itemId;
            }

            public void CollectEffects(ScoreSnapshot snapshot, ScoreEffectCollector collector)
            {
                if (_value <= 0f)
                {
                    return;
                }

                collector.Add(new ScoreEffectEntry(
                    ScorePhase.AfterAllDishes,
                    ScoreSource.Relic(_itemId, _itemName),
                    new AllDishMultiplierFlatEffect(_value)));
            }
        }

        private sealed class AllDishMultiplierFlatEffect : IScoreEffect
        {
            private readonly float _value;

            public AllDishMultiplierFlatEffect(float value)
            {
                _value = value;
            }

            public void Apply(ScoreContext context)
            {
                foreach (DishInstance dish in context.DiningTable.Dishes)
                {
                    context.AddMultFlatTo(dish, _value);
                }
            }
        }

        private readonly struct ServeCandidate
        {
            public ServeCandidate(int slotEntryIndex, DishDef dish, IReadOnlyList<Placement> placements)
            {
                SlotEntryIndex = slotEntryIndex;
                Dish = dish;
                Placements = placements;
            }

            public int SlotEntryIndex { get; }

            public DishDef Dish { get; }

            public IReadOnlyList<Placement> Placements { get; }
        }
    }
}
