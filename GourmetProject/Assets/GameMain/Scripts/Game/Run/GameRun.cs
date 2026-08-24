using System.Collections.Generic;
using System.Globalization;
using BreakInfinity;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Library;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;
using Log = GourmetProject.Core.Diagnostics.Log;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Analytics;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Save;

namespace GourmetProject.Game.Run
{
    /// <summary>
    /// 一次肉鸽运行（局外状态）：经营方向、周进度、金币、装饰品和消耗品，以及玩法静态数据库。
    /// 负责为「当前周」构建局内经营挑战会话。可被存档（见阶段 5 的 RunSaveData）。
    /// </summary>
    public sealed class GameRun : IPreconditionContext
    {
        public const int MaxQueuedBusinessGoldEffects = 100;

        public event System.Action<RunContentAcquisition> ContentAcquired;
        private readonly cfg.Tables _tables;

        // 装饰品同一 id 唯一一条且不升级；消耗品同一 id 可有多条，每条为一份独立实例。
        private readonly List<RunItemState> _items = new List<RunItemState>();
        private readonly List<string> _bonusDishIds = new List<string>();
        private readonly List<RecipeBookSlot> _recipe = new List<RecipeBookSlot>();
        private readonly List<string> _stomachFragmentIds = new List<string>();

        // 玩家在餐桌编辑页手动拼贴的碎片放置（id + 旋转 + 原点）；作为可复现重建胃形的权威数据。
        private readonly List<TableFragmentPlacement> _fragmentPlacements = new List<TableFragmentPlacement>();

        // 已购买待拼贴的碎片包内容（rolled 出的候选碎片 id + 已随机方向）；拼贴或跳过后清空。
        private readonly List<string> _pendingFragmentPack = new List<string>();
        private readonly List<int> _pendingFragmentPackRotations = new List<int>();
        private int _fragmentPackPurchaseCount;
        private int _deleteDishCount;
        private int _currentShopDeleteDishCount;
        private int _currentShopFragmentPackPurchaseCount;

        // 整局累计已结算的食物 BaseId 次数（供技能「大局相同检测」，随存档保存）。
        private readonly Dictionary<string, int> _runSettledCounts = new Dictionary<string, int>();

        // 已完成日常营业/火热营业结算的 BattleKey；用于保证结算副作用至多执行一次。
        private readonly HashSet<string> _settledFoodBattleKeys =
            new HashSet<string>(System.StringComparer.Ordinal);

        // —— 时间轴状态 ——
        private readonly List<string> _triggeredNodeIds = new List<string>();

        // 当前周时间轴节点快照：周开始时从配置复制，之后可被装饰品和消耗品改写；随 BeginTimeline（换周）重建。
        private readonly List<RuntimeTimelineNode> _runtimeTimelineNodes = new List<RuntimeTimelineNode>();
        private int _runtimeTimelineNodeSerial;
        private readonly List<string> _usedEventIds = new List<string>();
        private readonly List<string> _completedBossIds = new List<string>();
        private readonly List<string> _rolledBossDebuffIds = new List<string>();
        private readonly Dictionary<string, string> _lockedBossDebuffIdsByNode =
            new Dictionary<string, string>(System.StringComparer.Ordinal);
        private int _bossDebuffRerollWeekIndex;
        private int _bossDebuffRerollIndex;
        private string _bossDebuffRerollNodeId = string.Empty;
        private string _bossDebuffRerollExcludedId = string.Empty;
        private int _forcedBossDebuffWeekIndex;
        private string _forcedBossDebuffId = string.Empty;
        private int _bossPassiveArchetypePityWeekIndex;
        private int _bossPassiveArchetypeRewardCount;
        private bool _bossPassiveArchetypePityArmed;

        // —— 日常行动随机状态：每周类别/奖励计数、候选卡序号与行动数洗牌袋 ——
        private int _actionRandomStateWeek;
        private int _actionRandomCandidateIndex;
        private int _actionRandomGroupSerial;
        private readonly List<int> _remainingActionChoiceCounts = new List<int>();
        private readonly Dictionary<int, int> _actionCategoryCounts = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _actionRewardCounts = new Dictionary<int, int>();

        private readonly List<RunActionChoiceSaveData> _pendingActionChoices = new List<RunActionChoiceSaveData>();
        private readonly List<ShopEntrySaveData> _pendingShopStock = new List<ShopEntrySaveData>();
        private readonly List<GenericRewardSaveData> _pendingGenericRewards = new List<GenericRewardSaveData>();
        private string _pendingActionChoiceKey = string.Empty;
        private int _pendingActionChoiceRevision;
        private string _pendingShopKey = string.Empty;
        private string _pendingRewardKey = string.Empty;
        private RewardOfferSaveData _pendingRewardOffer;
        private PendingRewardBattleViewSaveData _pendingRewardBattleView;
        private PendingHeartBreakSaveData _pendingHeartBreak;
        private PendingActionExecutionSaveData _pendingActionExecution;
        private bool _pendingGenericRewardsConfirmBattleAfterDone;
        private PendingGenericRewardContinuationKind _pendingGenericRewardContinuation;
        private int _activeUseIndex;
        private int _nextDailyActionHalfCostStacks;
        private int _nextBusinessRewardDoubleStacks;
        private string _lastDishChoiceArchetypeId = string.Empty;
        private int _dishChoiceArchetypeMissStreak;
        private readonly List<string> _pendingExtraTimelineNodeIds = new List<string>();
        private int _interestThreshold;
        private int _interestGoldPer;
        private int _interestCap;
        private int _actionRerollCount;
        private int _weekIndex = 1;

        // —— 装饰品计数状态（随存档保存）——
        private int _loanDebt;            // 高利贷待扣债务，下一周结算时扣除
        private int _mealBonusRemaining;  // 「食物分红」剩余生效局数（GoldMealBonus）
        private int _scoreToOneRemaining; // 「分数变1」剩余生效局数（RequiredScoreToOne，非星级评鉴）

        // —— 事件运行状态（随存档保存）——
        private float _eventTargetScoreHiddenOffset;
        private float _eventDishHiddenOffset;
        private float _eventItemLuckOffset;
        private float _eventFragmentHiddenOffset;
        private float _eventGoldHiddenOffset;
        private float _eventShopPricePct;
        private int _eventChoicePenaltyRemaining;
        private int _eventChoiceCountDelta;
        private float _nextFoodTargetScoreHiddenOffset;
        private int _nextMealRewardGold;
        private readonly List<float> _nextBusinessGoldMultipliers = new List<float>();
        private float _currentWeekBusinessGoldMultiplier = 1f;
        private int _currentWeekBusinessGoldWeek;
        private float _bossTargetScorePct;
        private float _bossBaseGoldPct;
        private int _actEventActionCount;
        private readonly Dictionary<string, int> _eventCounters = new Dictionary<string, int>();
        private readonly List<string> _forcedEventIds = new List<string>();

        public GameRun(
            cfg.Tables tables,
            GameplayDatabase database,
            string characterId,
            string seedText,
            int weekIndex = 1,
            bool isTutorialRun = false,
            IRunExecutionEnvironment execution = null)
            : this(
                tables,
                database,
                characterId,
                seedText,
                weekIndex,
                initializeCharacterLoadout: true,
                isTutorialRun: isTutorialRun,
                execution: execution ?? RunExecutionEnvironment.CreateIsolated(tables, seedText))
        {
        }

        private GameRun(
            cfg.Tables tables,
            GameplayDatabase database,
            string characterId,
            string seedText,
            int weekIndex,
            bool initializeCharacterLoadout,
            bool isTutorialRun,
            IRunExecutionEnvironment execution)
        {
            _tables = tables;
            Execution = execution ?? RunExecutionEnvironment.CreateIsolated(tables, seedText);
            Database = database;
            Library = GameplayContentBuilder.BuildDishLibrary(database);
            CharacterId = characterId;
            SeedText = seedText;
            RunId = System.Guid.NewGuid().ToString("N");
            IsTutorialRun = isTutorialRun;
            WeekIndex = weekIndex;

            cfg.TbGameBase gameBase = _tables.TbGameBase;
            Gold = System.Math.Max(0, gameBase.InitialGold);
            _heartCapacity = System.Math.Max(1, gameBase.InitialHeartCount);
            _heartsRemaining = _heartCapacity;
            _interestThreshold = System.Math.Max(0, gameBase.InterestThreshold);
            _interestGoldPer = gameBase.InterestGoldPer > 0 ? gameBase.InterestGoldPer : 1;
            _interestCap = System.Math.Max(0, gameBase.InitialInterestCap);
            _actionRerollCount = System.Math.Max(0, gameBase.InitialActionRerollCount);

            if (initializeCharacterLoadout)
            {
                cfg.Character character = _tables.TbCharacter.GetOrDefault(characterId);
                if (character != null)
                {
                    foreach (string itemId in character.StartItems)
                    {
                        AcquireItem(itemId, 0, fireOnAcquire: false);
                    }
                }

                InitializeRecipeBooksFromCharacter();
            }
        }

        public GameplayDatabase Database { get; }

        public DishLibrary Library { get; }

        public cfg.Tables Tables => _tables;

        /// <summary>Run-scoped random, persistence and meta-profile boundary.</summary>
        public IRunExecutionEnvironment Execution { get; }

        public RandomService Random => Execution.Random;

        public MetaProgressSaveData MetaProgress => Execution.MetaProgress;

        public void RequestSave()
        {
            Execution.Save(this);
        }

        public string CharacterId { get; }

        public string SeedText { get; }

        /// <summary>匿名统计与跨读档去重使用的单局稳定标识，不包含随机种子。</summary>
        public string RunId { get; private set; }

        public bool IsTutorialRun { get; }

        public int WeekIndex
        {
            get => _weekIndex;
            set => _weekIndex = System.Math.Max(1, value);
        }

        private int _gold;

        /// <summary>金币发生变化时通知表现层；参数依次为变化前、变化后。</summary>
        public event System.Action<int, int> GoldChanged;

        public int Gold
        {
            get => _gold;
            set
            {
                if (_gold == value)
                {
                    return;
                }

                int before = _gold;
                _gold = value;
                GoldChanged?.Invoke(before, _gold);
            }
        }

        private int _heartCapacity;
        private int _heartsRemaining;

        public int HeartCapacity
            => System.Math.Max(1, _heartCapacity + new ItemRuntime(this).HeartCapacityBonus());

        public int HeartsRemaining => System.Math.Min(_heartsRemaining, HeartCapacity);

        /// <summary>扣除指定数量爱心；最低截断至 0。</summary>
        public bool TryLoseHearts(int amount, out int before, out int after)
        {
            _heartsRemaining = HeartsRemaining;
            before = _heartsRemaining;
            if (_heartsRemaining <= 0 || amount <= 0)
            {
                after = _heartsRemaining;
                return false;
            }

            _heartsRemaining = System.Math.Max(0, _heartsRemaining - amount);
            after = _heartsRemaining;
            return true;
        }

        /// <summary>兼容旧调用：扣除一颗爱心。</summary>
        public bool TryLoseHeart(out int before, out int after) => TryLoseHearts(1, out before, out after);

        /// <summary>恢复当前爱心，不超过上限。</summary>
        public int RestoreHearts(int amount)
        {
            if (amount > 0)
            {
                _heartsRemaining = System.Math.Min(HeartCapacity, HeartsRemaining + amount);
            }

            return HeartsRemaining;
        }

        /// <summary>调整爱心上限；上限至少为 1，降低上限时同步压低当前值。</summary>
        public int AdjustHeartCapacity(int delta)
        {
            _heartCapacity = System.Math.Max(1, _heartCapacity + delta);
            _heartsRemaining = System.Math.Min(_heartsRemaining, HeartCapacity);
            return HeartCapacity;
        }

        public int FoodFlavorLimit
        {
            get
            {
                int baseLimit = _tables.TbGameBase.FoodFlavorLimit;
                return System.Math.Max(1, baseLimit + new ItemRuntime(this).FoodFlavorLimitBonus());
            }
        }

        public int InterestThreshold => _interestThreshold;

        public int InterestGoldPer => _interestGoldPer;

        /// <summary>当前运行的利息节点单次最高收益，本局基础值可被装饰品和消耗品提高。</summary>
        public int InterestCap
        {
            get
            {
                int baseCap = System.Math.Max(0, _interestCap);
                int itemCap = System.Math.Max(0, new ItemRuntime(this).InterestCapOverride());
                return System.Math.Max(baseCap, itemCap);
            }
        }

        private int _retainedHappyCakeLayers;

        public int ConsumeRetainedHappyCakeLayers()
        {
            int layers = System.Math.Max(0, _retainedHappyCakeLayers);
            _retainedHappyCakeLayers = 0;
            return layers;
        }

        public void SetRetainedHappyCakeLayers(int layers)
        {
            _retainedHappyCakeLayers = System.Math.Max(0, layers);
        }

        public void ClearRetainedHappyCakeLayers()
        {
            _retainedHappyCakeLayers = 0;
        }

        /// <summary>
        /// 尝试消耗一件「不死」装饰品和消耗品（名刀·加护）：持有时移除一件并返回 true，
        /// 同时写出恢复目标红心数。无则返回 false。
        /// </summary>
        public bool TryConsumeUndying(out int restoreToHearts)
        {
            restoreToHearts = 1;
            foreach (RunItemState state in _items)
            {
                if (state.Model != null && state.Model.IsUndying())
                {
                    restoreToHearts = System.Math.Max(1, state.Model.UndyingRestoreHearts());
                    state.Model.Flash();
                    RemoveItem(state.ItemId);
                    return true;
                }
            }

            return false;
        }

        /// <summary>为一份装饰品构建并绑定行为模型（挂到 state.Model 并返回）。</summary>
        private GourmetProject.Game.Meta.Passives.PassiveItemModel BindPassiveModel(RunItemState state, ItemDefinition item)
        {
            GourmetProject.Game.Meta.Passives.PassiveItemModel model =
                GourmetProject.Game.Meta.Passives.PassiveItemModelRegistry.Create(item.Id);
            model.Bind(this, item, state);
            state.Model = model;
            return model;
        }

        public IReadOnlyList<RunItemState> Items => _items;

        /// <summary>当前在场（持有）的装饰品模型集合，供 <see cref="GourmetProject.Game.Meta.ItemRuntime"/> 折叠钩子。</summary>
        public IEnumerable<GourmetProject.Game.Meta.Passives.PassiveItemModel> PassiveModels
        {
            get
            {
                foreach (RunItemState state in _items)
                {
                    if (state.Model != null)
                    {
                        yield return state.Model;
                    }
                }
            }
        }

        /// <summary>装饰品持有条目（同 id 唯一，不占消耗槽）。</summary>
        public IEnumerable<RunItemState> PassiveItemStates => ItemStatesOfKind(cfg.ItemKind.Passive);

        /// <summary>消耗品持有实例（每份占一个消耗槽）。</summary>
        public IEnumerable<RunItemState> ActiveItemStates => ItemStatesOfKind(cfg.ItemKind.Active);

        private IEnumerable<RunItemState> ItemStatesOfKind(cfg.ItemKind kind)
        {
            var result = new List<RunItemState>();
            foreach (RunItemState state in _items)
            {
                ItemDefinition item = ItemDefinition.Get(_tables, state.ItemId, kind);
                if (item != null)
                {
                    result.Add(state);
                }
            }

            return result;
        }

        /// <summary>当前占用的消耗品槽数（= 主动实例份数）。</summary>
        public int ActiveItemCount
        {
            get
            {
                int n = 0;
                foreach (RunItemState state in _items)
                {
                    ItemDefinition item = ItemDefinition.Get(_tables, state.ItemId, cfg.ItemKind.Active);
                    if (item != null)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        private const int MaxActiveSlotCapacity = 9;

        /// <summary>消耗品消耗槽总容量 = 基础槽 + ExtraActiveSlot 被动加成（范围 0..9）。</summary>
        public int ActiveSlotCapacity
        {
            get
            {
                int baseSlots = _tables.TbGameBase.BaseActiveSlots;
                int capacity = baseSlots + new ItemRuntime(this).ExtraActiveSlots();
                return System.Math.Min(MaxActiveSlotCapacity, System.Math.Max(0, capacity));
            }
        }

        /// <summary>消耗品是否还有空槽。</summary>
        public bool HasFreeActiveSlot => ActiveItemCount < ActiveSlotCapacity;

        /// <summary>消耗品累计使用序号；随机类主动效果按它派生随机流。</summary>
        public int ActiveUseIndex => _activeUseIndex;

        /// <summary>取下一个消耗品随机流 key 并推进使用序号（保证生成/复制类效果同种子可复现）。</summary>
        public string NextActiveUseKey()
        {
            return $"active_{_activeUseIndex++}";
        }

        public int ActionRerollCount => System.Math.Max(0, _actionRerollCount);

        public void AddActionRerollCount(int amount)
        {
            if (amount > 0)
            {
                _actionRerollCount += amount;
            }
        }

        public bool TrySpendActionReroll()
        {
            if (_actionRerollCount <= 0)
            {
                return false;
            }

            _actionRerollCount--;
            return true;
        }

        // —— 装饰品计数状态 API ——

        /// <summary>高利贷待扣债务（下一周结算时扣除）。</summary>
        public int LoanDebt => _loanDebt;

        /// <summary>登记一笔高利贷债务（获得「高利贷」时调用）。</summary>
        public void RegisterLoanDebt(int amount)
        {
            if (amount > 0)
            {
                _loanDebt += amount;
            }
        }

        /// <summary>取出并清空当前高利贷债务（周末结算时调用）。</summary>
        public int ConsumeLoanDebt()
        {
            int debt = _loanDebt;
            _loanDebt = 0;
            return debt;
        }

        /// <summary>「食物分红」剩余生效局数（GoldMealBonus）。</summary>
        public int MealBonusRemaining => _mealBonusRemaining;

        /// <summary>增加「食物分红」生效局数（获得装饰品和消耗品时初始化）。</summary>
        public void AddMealBonusMeals(int meals)
        {
            if (meals > 0)
            {
                _mealBonusRemaining += meals;
            }
        }

        /// <summary>消耗一局「食物分红」额度。</summary>
        public void ConsumeMealBonusMeal()
        {
            if (_mealBonusRemaining > 0)
            {
                _mealBonusRemaining--;
            }
        }

        public void ClearMealBonusMeals()
        {
            _mealBonusRemaining = 0;
        }

        /// <summary>「分数变1」剩余生效局数（非星级评鉴，RequiredScoreToOne）。</summary>
        public int ScoreToOneRemaining => _scoreToOneRemaining;

        /// <summary>增加「分数变1」生效局数（获得装饰品和消耗品时初始化）。</summary>
        public void AddScoreToOneMeals(int meals)
        {
            if (meals > 0)
            {
                _scoreToOneRemaining += meals;
            }
        }

        /// <summary>消耗一局「分数变1」额度。</summary>
        public void ConsumeScoreToOneMeal()
        {
            if (_scoreToOneRemaining > 0)
            {
                _scoreToOneRemaining--;
            }
        }

        public void ClearScoreToOneMeals()
        {
            _scoreToOneRemaining = 0;
        }

        public float EventHiddenScoreOffset(HiddenScorePurpose purpose)
        {
            return purpose switch
            {
                HiddenScorePurpose.TargetScore => _eventTargetScoreHiddenOffset,
                HiddenScorePurpose.Dish => _eventDishHiddenOffset,
                HiddenScorePurpose.ItemLuck => _eventItemLuckOffset,
                HiddenScorePurpose.Fragment => _eventFragmentHiddenOffset,
                HiddenScorePurpose.Gold => _eventGoldHiddenOffset,
                _ => 0f,
            };
        }

        public void AddEventHiddenScoreOffset(HiddenScorePurpose purpose, float amount)
        {
            switch (purpose)
            {
                case HiddenScorePurpose.TargetScore:
                    _eventTargetScoreHiddenOffset += amount;
                    break;
                case HiddenScorePurpose.Dish:
                    _eventDishHiddenOffset += amount;
                    break;
                case HiddenScorePurpose.ItemLuck:
                    _eventItemLuckOffset += amount;
                    break;
                case HiddenScorePurpose.Fragment:
                    _eventFragmentHiddenOffset += amount;
                    break;
                case HiddenScorePurpose.Gold:
                    _eventGoldHiddenOffset += amount;
                    break;
            }
        }

        public void AddEventShopPricePct(float pct)
        {
            _eventShopPricePct += pct;
        }

        public int ModifyEventShopPrice(int price)
        {
            int result = (int)System.Math.Round(price * (1f + _eventShopPricePct), System.MidpointRounding.AwayFromZero);
            return System.Math.Max(1, result);
        }

        public void AddEventChoiceCountPenalty(int times, int delta)
        {
            if (times <= 0 || delta == 0)
            {
                return;
            }

            _eventChoicePenaltyRemaining += times;
            _eventChoiceCountDelta += delta;
        }

        public int ConsumeEventChoiceCountDelta()
        {
            if (_eventChoicePenaltyRemaining <= 0 || _eventChoiceCountDelta == 0)
            {
                return 0;
            }

            _eventChoicePenaltyRemaining--;
            int delta = _eventChoiceCountDelta;
            if (_eventChoicePenaltyRemaining <= 0)
            {
                _eventChoiceCountDelta = 0;
            }

            return delta;
        }

        public void AddNextFoodTargetScoreHiddenOffset(float amount)
        {
            _nextFoodTargetScoreHiddenOffset += amount;
        }

        public float ConsumeNextFoodTargetScoreHiddenOffset()
        {
            float amount = _nextFoodTargetScoreHiddenOffset;
            _nextFoodTargetScoreHiddenOffset = 0f;
            return amount;
        }

        public void AddNextMealRewardGold(int amount)
        {
            if (amount != 0)
            {
                _nextMealRewardGold += amount;
            }
        }

        public int ConsumeNextMealRewardGold()
        {
            int amount = _nextMealRewardGold;
            _nextMealRewardGold = 0;
            return amount;
        }

        public float CurrentWeekBusinessGoldMultiplier =>
            _currentWeekBusinessGoldWeek == WeekIndex
                ? System.Math.Max(0f, _currentWeekBusinessGoldMultiplier)
                : 1f;

        public void AddBusinessGoldPct(float pct, bool nextBusiness)
        {
            if (nextBusiness)
            {
                AddBusinessGoldPct(pct, 1);
                return;
            }

            float multiplier = System.Math.Max(0f, 1f + pct);
            if (_currentWeekBusinessGoldWeek != WeekIndex)
            {
                _currentWeekBusinessGoldWeek = WeekIndex;
                _currentWeekBusinessGoldMultiplier = 1f;
            }

            _currentWeekBusinessGoldMultiplier *= multiplier;
        }

        /// <summary>
        /// 为接下来指定次数的营业分别叠加基础金币倍率。不同持续次数的效果按每一场独立叠加。
        /// </summary>
        public void AddBusinessGoldPct(float pct, int nextBusinessCount)
        {
            if (nextBusinessCount <= 0 || nextBusinessCount > MaxQueuedBusinessGoldEffects)
            {
                return;
            }

            float multiplier = System.Math.Max(0f, 1f + pct);
            while (_nextBusinessGoldMultipliers.Count < nextBusinessCount)
            {
                _nextBusinessGoldMultipliers.Add(1f);
            }

            for (int index = 0; index < nextBusinessCount; index++)
            {
                _nextBusinessGoldMultipliers[index] *= multiplier;
            }
        }

        public float ConsumeNextBusinessGoldMultiplier()
        {
            if (_nextBusinessGoldMultipliers.Count == 0)
            {
                return 1f;
            }

            float multiplier = System.Math.Max(0f, _nextBusinessGoldMultipliers[0]);
            _nextBusinessGoldMultipliers.RemoveAt(0);
            return multiplier;
        }

        public void AddBossTargetScorePct(float pct)
        {
            _bossTargetScorePct += pct;
        }

        public void AddBossBaseGoldPct(float pct)
        {
            _bossBaseGoldPct += pct;
        }

        public int ModifyBossTargetScore(int targetScore)
        {
            int result = (int)System.Math.Round(
                targetScore * System.Math.Max(0f, 1f + _bossTargetScorePct),
                System.MidpointRounding.AwayFromZero);
            return System.Math.Max(1, result);
        }

        public float BossBaseGoldMultiplier => System.Math.Max(0f, 1f + _bossBaseGoldPct);

        public int ActEventActionCount => _actEventActionCount;

        public int MarkActEventActionEntered()
        {
            _actEventActionCount++;
            return _actEventActionCount;
        }

        public int GetEventCounter(string counterId)
        {
            if (string.IsNullOrEmpty(counterId))
            {
                return 0;
            }

            return _eventCounters.TryGetValue(counterId, out int current) ? current : 0;
        }

        public int ScheduleEventCounterAfterActEvents(string counterId, int delayActEventCount)
        {
            if (string.IsNullOrEmpty(counterId))
            {
                return 0;
            }

            if (_eventCounters.TryGetValue(counterId, out int existingTarget) && existingTarget > 0)
            {
                return existingTarget;
            }

            int delay = System.Math.Max(1, delayActEventCount);
            int targetActEventCount = _actEventActionCount + delay + 1;
            _eventCounters[counterId] = targetActEventCount;
            return targetActEventCount;
        }

        public int IncrementEventCounter(string counterId, int threshold, string forcedEventId)
        {
            if (string.IsNullOrEmpty(counterId))
            {
                return 0;
            }

            _eventCounters.TryGetValue(counterId, out int current);
            current++;
            if (threshold > 0 && current >= threshold)
            {
                if (string.IsNullOrEmpty(forcedEventId))
                {
                    current = threshold;
                }
                else
                {
                    current = 0;
                    QueueForcedEvent(forcedEventId);
                }
            }

            _eventCounters[counterId] = current;
            return current;
        }

        public void QueueForcedEvent(string eventId)
        {
            if (!string.IsNullOrEmpty(eventId))
            {
                _forcedEventIds.Add(eventId);
            }
        }

        public bool TryConsumeForcedEventId(out string eventId)
        {
            while (_forcedEventIds.Count > 0)
            {
                eventId = _forcedEventIds[0];
                _forcedEventIds.RemoveAt(0);
                if (!string.IsNullOrEmpty(eventId))
                {
                    return true;
                }
            }

            eventId = string.Empty;
            return false;
        }

        public IReadOnlyList<string> BonusDishIds => _bonusDishIds;

        public IReadOnlyList<string> RecipeDishes => ProjectDishIds(_recipe);

        public IReadOnlyList<RecipeBookSlot> RecipeEntries => _recipe;

        public IReadOnlyList<string> TableFragmentIds => _stomachFragmentIds;

        /// <summary>玩家手动拼贴的碎片放置列表（餐桌编辑页产出，随存档保存）。</summary>
        public IReadOnlyList<TableFragmentPlacement> FragmentPlacements => _fragmentPlacements;

        /// <summary>已购买待拼贴的碎片包候选碎片 id（三选一）；为空表示没有待处理的碎片包。</summary>
        public IReadOnlyList<string> PendingFragmentPack => _pendingFragmentPack;

        /// <summary>与 <see cref="PendingFragmentPack"/> 同下标的顺时针旋转次数（0..3）。</summary>
        public IReadOnlyList<int> PendingFragmentPackRotations => _pendingFragmentPackRotations;

        public int FragmentPackPurchaseCount => _fragmentPackPurchaseCount;

        public int DeleteDishCount => _deleteDishCount;

        public int CurrentShopDeleteDishCount => _currentShopDeleteDishCount;

        public int CurrentShopFragmentPackPurchaseCount => _currentShopFragmentPackPurchaseCount;

        /// <summary>进入一次新的商店时重置本次商店状态；从存档恢复当前商店时不调用。</summary>
        public void BeginShopVisit()
        {
            _currentShopDeleteDishCount = 0;
            _currentShopFragmentPackPurchaseCount = 0;
            ClearPendingShopStock();
        }

        public void RecordFragmentPackPurchased()
        {
            _fragmentPackPurchaseCount++;
            _currentShopFragmentPackPurchaseCount++;
        }

        public void RecordDishDeleted()
        {
            _deleteDishCount++;
            _currentShopDeleteDishCount++;
        }

        public TableFragmentDef GetTableFragmentDef(string fragmentId)
        {
            return Database.GetFragment(fragmentId);
        }

        /// <summary>餐桌格总数（奖励自动附着 + 手动拼贴），供统计/预览展示。</summary>
        public int StomachFragmentCount => _stomachFragmentIds.Count + _fragmentPlacements.Count;

        /// <summary>整局累计已结算的食物 BaseId 次数（大局历史）。</summary>
        public IReadOnlyDictionary<string, int> RunSettledCounts => _runSettledCounts;

        /// <summary>把一次结算的各 BaseId 增量累加进大局历史。</summary>
        public void AddSettledCounts(IReadOnlyDictionary<string, int> increments)
        {
            if (increments == null)
            {
                return;
            }

            foreach (KeyValuePair<string, int> kv in increments)
            {
                _runSettledCounts.TryGetValue(kv.Key, out int cur);
                _runSettledCounts[kv.Key] = cur + kv.Value;
            }
        }

        /// <summary>首次记录营业经营挑战结算返回 true；同一 key 再次进入返回 false。</summary>
        public bool TryMarkFoodBattleSettled(string battleKey)
        {
            return !string.IsNullOrEmpty(battleKey) && _settledFoodBattleKeys.Add(battleKey);
        }

        /// <summary>本周要求分的临时覆盖（&lt;0 表示无覆盖）。事件「歇业」等可降低本周目标。</summary>
        public int RequiredScoreOverride { get; set; } = -1;

        // —— 时间轴运行状态 ——
        /// <summary>本周时间轴 id。</summary>
        public string CurrentTimelineId { get; set; } = string.Empty;

        /// <summary>当前时间轴所属周。</summary>
        public int CurrentTimelineWeekIndex { get; set; }

        /// <summary>本周时间轴长度（天，0.1 粒度）。</summary>
        public float TimelineLengthDays { get; set; }

        /// <summary>当前天数游标（0..TimelineLengthDays，0.1 粒度）。</summary>
        public float CurrentDay { get; set; }

        public IReadOnlyList<string> TriggeredNodeIds => _triggeredNodeIds;

        /// <summary>当前周时间轴节点快照（配置节点 + 装饰品和消耗品插入/改写后的节点）。</summary>
        public IReadOnlyList<RuntimeTimelineNode> RuntimeTimelineNodes => _runtimeTimelineNodes;

        public int NextDailyActionHalfCostStacks => _nextDailyActionHalfCostStacks;

        public int NextBusinessRewardDoubleStacks => _nextBusinessRewardDoubleStacks;

        internal string LastDishChoiceArchetypeId => _lastDishChoiceArchetypeId;

        internal int DishChoiceArchetypeMissStreak => _dishChoiceArchetypeMissStreak;

        public IReadOnlyList<string> PendingExtraTimelineNodeIds => _pendingExtraTimelineNodeIds;

        public void AddNextDailyActionHalfCostStack()
        {
            _nextDailyActionHalfCostStacks++;
        }

        public bool TryConsumeNextDailyActionHalfCostStack()
        {
            if (_nextDailyActionHalfCostStacks <= 0)
            {
                return false;
            }

            _nextDailyActionHalfCostStacks--;
            return true;
        }

        public void AddNextBusinessRewardDoubleStack()
        {
            _nextBusinessRewardDoubleStacks++;
        }

        public bool TryConsumeNextBusinessRewardDoubleStack()
        {
            if (_nextBusinessRewardDoubleStacks <= 0)
            {
                return false;
            }

            _nextBusinessRewardDoubleStacks--;
            return true;
        }

        internal bool BeginDishChoiceArchetypePity(string archetypeId, int missThreshold)
        {
            if (missThreshold <= 0 || !IsDishChoiceArchetypeId(archetypeId))
            {
                ClearDishChoiceArchetypePity();
                return false;
            }

            if (!string.Equals(
                    _lastDishChoiceArchetypeId,
                    archetypeId,
                    System.StringComparison.Ordinal))
            {
                _lastDishChoiceArchetypeId = archetypeId;
                _dishChoiceArchetypeMissStreak = 0;
                return false;
            }

            return _dishChoiceArchetypeMissStreak >= missThreshold;
        }

        internal void CompleteDishChoiceArchetypePity(
            string archetypeId,
            bool targetOffered,
            int missThreshold)
        {
            if (missThreshold <= 0 || !IsDishChoiceArchetypeId(archetypeId))
            {
                ClearDishChoiceArchetypePity();
                return;
            }

            if (!string.Equals(
                    _lastDishChoiceArchetypeId,
                    archetypeId,
                    System.StringComparison.Ordinal))
            {
                _lastDishChoiceArchetypeId = archetypeId;
                _dishChoiceArchetypeMissStreak = 0;
            }

            if (targetOffered)
            {
                _dishChoiceArchetypeMissStreak = 0;
                return;
            }

            int current = System.Math.Max(0, _dishChoiceArchetypeMissStreak);
            _dishChoiceArchetypeMissStreak = current >= missThreshold
                ? missThreshold
                : current + 1;
        }

        private static bool IsDishChoiceArchetypeId(string archetypeId)
        {
            return string.Equals(archetypeId, "0", System.StringComparison.Ordinal)
                || string.Equals(archetypeId, "1", System.StringComparison.Ordinal)
                || string.Equals(archetypeId, "2", System.StringComparison.Ordinal);
        }

        private void ClearDishChoiceArchetypePity()
        {
            _lastDishChoiceArchetypeId = string.Empty;
            _dishChoiceArchetypeMissStreak = 0;
        }

        public float PreviewDailyActionCost(float baseCostDays)
        {
            float cost = System.Math.Max(0f, baseCostDays);
            if (_nextDailyActionHalfCostStacks > 0)
            {
                cost *= 0.5f;
            }

            return GourmetProject.Game.Meta.TimelineMath.Quantize(cost);
        }

        /// <summary>生成普通行动选项时快照持有被动的耗时倍率；之后获得/失去装饰品和消耗品不追溯。</summary>
        public float SnapshotDailyActionCost(float baseCostDays)
        {
            float multiplier = new ItemRuntime(this).DailyActionCostMultiplier();
            return GourmetProject.Game.Meta.TimelineMath.Quantize(
                System.Math.Max(0f, baseCostDays) * System.Math.Max(0f, multiplier));
        }

        public float SnapshotTimelineStopChance() => new ItemRuntime(this).TimelineStopChance();

        public bool EnqueueExtraTimelineNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return false;
            }

            _pendingExtraTimelineNodeIds.Add(nodeId);
            return true;
        }

        public bool TryDequeueExtraTimelineNode(out string nodeId)
        {
            if (_pendingExtraTimelineNodeIds.Count == 0)
            {
                nodeId = string.Empty;
                return false;
            }

            nodeId = _pendingExtraTimelineNodeIds[0];
            _pendingExtraTimelineNodeIds.RemoveAt(0);
            return true;
        }

        public void SetWeekIndex(int weekIndex)
        {
            int normalized = System.Math.Max(1, weekIndex);
            if (normalized != WeekIndex)
            {
                _currentWeekBusinessGoldMultiplier = 1f;
                _currentWeekBusinessGoldWeek = 0;
            }

            WeekIndex = normalized;
        }

        public void IncrementWeek()
        {
            SetWeekIndex(WeekIndex + 1);
        }

        public bool DecreaseWeek(int amount)
        {
            int old = WeekIndex;
            SetWeekIndex(WeekIndex - System.Math.Max(0, amount));
            return WeekIndex != old;
        }

        /// <summary>
        /// 兼容装饰品的随机追加路径：仍优先寻找当前或未来的空整数日，实际创建统一走定点接口。
        /// </summary>
        public string AddRuntimeTimelineNode(string actionId, IRandomStream rng = null)
        {
            if (string.IsNullOrEmpty(CurrentTimelineId) || string.IsNullOrEmpty(actionId))
            {
                return string.Empty;
            }

            var days = new List<int>();
            int start = GourmetProject.Game.Meta.TimelineMath.CurrentOrNextIntegerDay(CurrentDay);
            int end = (int)System.Math.Floor(
                TimelineLengthDays + GourmetProject.Game.Meta.TimelineMath.Epsilon);
            for (int day = start; day <= end; day++)
            {
                if (!HasTimelineNodeAtDay(day))
                {
                    days.Add(day);
                }
            }

            if (days.Count == 0)
            {
                return string.Empty;
            }

            int index = rng != null ? rng.Range(0, days.Count) : 0;
            if (index < 0)
            {
                index = 0;
            }
            else if (index >= days.Count)
            {
                index = days.Count - 1;
            }

            return AddRuntimeTimelineNodeAtDay(actionId, days[index]);
        }

        /// <summary>
        /// 在玩家指定的当前或未来整数日追加一个时间轴节点。同一天允许叠放多个节点。
        /// </summary>
        public string AddRuntimeTimelineNodeAtDay(string actionId, int day, string sourceItemId = "")
            => AddRuntimeTimelineNodeAtDayInternal(actionId, day, sourceItemId, false);

        /// <summary>系统级周末追加；允许节点落在当前整数日，并记录来源与锚定属性。</summary>
        public string AddWeekEndAnchoredTimelineNode(string actionId, string sourceItemId)
        {
            int day = System.Math.Max(1, (int)System.Math.Floor(
                TimelineLengthDays + GourmetProject.Game.Meta.TimelineMath.Epsilon));
            return AddRuntimeTimelineNodeAtDayInternal(actionId, day, sourceItemId, true);
        }

        /// <summary>把所选节点当前行动复制到当前或下一个整数日；原节点及完成状态不变。</summary>
        public string CloneRuntimeTimelineNodeToCurrentOrNextIntegerDay(
            string sourceNodeId,
            string sourceItemId = "")
        {
            int index = _runtimeTimelineNodes.FindIndex(node => node.Id == sourceNodeId);
            if (index < 0)
            {
                return string.Empty;
            }

            int day = GourmetProject.Game.Meta.TimelineMath.CurrentOrNextIntegerDay(CurrentDay);
            if (day > (int)System.Math.Floor(
                    TimelineLengthDays + GourmetProject.Game.Meta.TimelineMath.Epsilon))
            {
                return string.Empty;
            }

            return AddRuntimeTimelineNodeAtDayInternal(
                _runtimeTimelineNodes[index].ActionId,
                day,
                sourceItemId,
                false);
        }

        private string AddRuntimeTimelineNodeAtDayInternal(
            string actionId,
            int day,
            string sourceItemId,
            bool weekEndAnchored)
        {
            if (string.IsNullOrEmpty(CurrentTimelineId)
                || string.IsNullOrEmpty(actionId)
                || Tables.TbAction.GetOrDefault(actionId) == null
                || day + GourmetProject.Game.Meta.TimelineMath.Epsilon < CurrentDay
                || day < 1
                || day > (int)System.Math.Floor(TimelineLengthDays + GourmetProject.Game.Meta.TimelineMath.Epsilon))
            {
                return string.Empty;
            }

            string id;
            do
            {
                _runtimeTimelineNodeSerial++;
                id = $"dyn_w{WeekIndex}_{_runtimeTimelineNodeSerial}";
            }
            while (ContainsRuntimeTimelineNode(id));

            _runtimeTimelineNodes.Add(new RuntimeTimelineNode(
                id,
                CurrentTimelineId,
                day,
                actionId,
                sourceItemId,
                weekEndAnchored));
            SortRuntimeTimelineNodes();
            return id;
        }

        public bool EnsureTimelineLengthAtLeast(int days)
        {
            float target = GourmetProject.Game.Meta.TimelineMath.Quantize(System.Math.Max(1, days));
            if (TimelineLengthDays + GourmetProject.Game.Meta.TimelineMath.Epsilon >= target)
            {
                return false;
            }

            TimelineLengthDays = target;
            int endDay = (int)System.Math.Floor(target + GourmetProject.Game.Meta.TimelineMath.Epsilon);
            bool moved = false;
            for (int i = 0; i < _runtimeTimelineNodes.Count; i++)
            {
                RuntimeTimelineNode node = _runtimeTimelineNodes[i];
                if (!node.WeekEndAnchored || IsNodeTriggered(node.Id) || IsTimelineNodeExecutionInProgress(node.Id))
                {
                    continue;
                }

                _runtimeTimelineNodes[i] = node.WithDay(endDay);
                moved = true;
            }

            if (moved)
            {
                SortRuntimeTimelineNodes();
            }

            return true;
        }

        /// <summary>
        /// 删除尚未结算、尚未开始执行的运行态节点，并同步清理额外执行队列。
        /// </summary>
        public bool RemoveRuntimeTimelineNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)
                || IsNodeTriggered(nodeId)
                || IsTimelineNodeExecutionInProgress(nodeId))
            {
                return false;
            }

            int index = _runtimeTimelineNodes.FindIndex(node => node.Id == nodeId);
            if (index < 0)
            {
                return false;
            }

            _runtimeTimelineNodes.RemoveAt(index);
            _pendingExtraTimelineNodeIds.RemoveAll(id => id == nodeId);
            _lockedBossDebuffIdsByNode.Remove(nodeId);
            if (_bossDebuffRerollNodeId == nodeId)
            {
                _bossDebuffRerollNodeId = string.Empty;
                _bossDebuffRerollIndex = 0;
                _bossDebuffRerollExcludedId = string.Empty;
            }

            return true;
        }

        public bool IsTimelineNodeExecutionInProgress(string nodeId)
        {
            return !string.IsNullOrEmpty(nodeId)
                && _pendingActionExecution != null
                && _pendingActionExecution.SourceKey == nodeId;
        }

        private bool ContainsRuntimeTimelineNode(string nodeId)
        {
            for (int i = 0; i < _runtimeTimelineNodes.Count; i++)
            {
                if (_runtimeTimelineNodes[i].Id == nodeId)
                {
                    return true;
                }
            }

            return false;
        }

        public bool RandomizeFutureTimelineActions(IRandomStream rng)
        {
            if (rng == null || _runtimeTimelineNodes.Count <= 1)
            {
                return false;
            }

            var indexes = new List<int>();
            var actions = new List<string>();
            for (int i = 0; i < _runtimeTimelineNodes.Count; i++)
            {
                RuntimeTimelineNode node = _runtimeTimelineNodes[i];
                if (node.Day <= CurrentDay + GourmetProject.Game.Meta.TimelineMath.Epsilon
                    || IsNodeTriggered(node.Id)
                    || IsBossAction(node.ActionId))
                {
                    continue;
                }

                indexes.Add(i);
                actions.Add(node.ActionId);
            }

            if (indexes.Count <= 1)
            {
                return false;
            }

            rng.Shuffle(actions);
            bool changed = false;
            for (int i = 0; i < indexes.Count; i++)
            {
                int nodeIndex = indexes[i];
                if (_runtimeTimelineNodes[nodeIndex].ActionId != actions[i])
                {
                    changed = true;
                }

                _runtimeTimelineNodes[nodeIndex] = _runtimeTimelineNodes[nodeIndex].WithActionId(actions[i]);
            }

            return changed;
        }

        public bool DelayFutureBossNodes(int days)
        {
            days = System.Math.Max(0, days);
            if (days == 0)
            {
                return false;
            }

            bool changed = false;
            for (int i = 0; i < _runtimeTimelineNodes.Count; i++)
            {
                RuntimeTimelineNode node = _runtimeTimelineNodes[i];
                if (node.Day <= CurrentDay + GourmetProject.Game.Meta.TimelineMath.Epsilon
                    || IsNodeTriggered(node.Id)
                    || !IsBossAction(node.ActionId))
                {
                    continue;
                }

                int newDay = node.Day + days;
                _runtimeTimelineNodes[i] = node.WithDay(newDay);
                TimelineLengthDays = System.Math.Max(TimelineLengthDays, newDay);
                changed = true;
            }

            if (changed)
            {
                SortRuntimeTimelineNodes();
            }

            return changed;
        }

        private bool HasTimelineNodeAtDay(int day)
        {
            foreach (RuntimeTimelineNode node in _runtimeTimelineNodes)
            {
                if (node.Day == day)
                {
                    return true;
                }
            }

            return false;
        }

        private void SortRuntimeTimelineNodes()
        {
            _runtimeTimelineNodes.Sort((a, b) =>
            {
                int cmp = a.Day.CompareTo(b.Day);
                return cmp != 0 ? cmp : CompareTimelineNodeIds(a.Id, b.Id);
            });
        }

        private static int CompareTimelineNodeIds(string left, string right)
        {
            bool leftDynamic = TryGetDynamicNodeSerial(left, out int leftSerial);
            bool rightDynamic = TryGetDynamicNodeSerial(right, out int rightSerial);
            if (leftDynamic != rightDynamic)
            {
                return leftDynamic ? 1 : -1;
            }

            if (leftDynamic && leftSerial != rightSerial)
            {
                return leftSerial.CompareTo(rightSerial);
            }

            return string.CompareOrdinal(left, right);
        }

        private static bool TryGetDynamicNodeSerial(string id, out int serial)
        {
            serial = 0;
            if (string.IsNullOrEmpty(id) || !id.StartsWith("dyn_", System.StringComparison.Ordinal))
            {
                return false;
            }

            int separator = id.LastIndexOf('_');
            return separator >= 0
                && separator + 1 < id.Length
                && int.TryParse(id.Substring(separator + 1), out serial);
        }

        private bool IsBossAction(string actionId)
        {
            cfg.GameAction action = _tables?.TbAction.GetOrDefault(actionId);
            return GourmetProject.Game.Meta.FoodService.IsBossAction(_tables, action);
        }

        public IReadOnlyList<string> UsedEventIds => _usedEventIds;

        public IReadOnlyList<string> CompletedBossIds => _completedBossIds;

        public IReadOnlyList<string> RolledBossDebuffIds => _rolledBossDebuffIds;

        public int BossDebuffRerollIndex =>
            _bossDebuffRerollWeekIndex == WeekIndex ? _bossDebuffRerollIndex : 0;

        public string BossDebuffRerollNodeId =>
            _bossDebuffRerollWeekIndex == WeekIndex ? _bossDebuffRerollNodeId : string.Empty;

        public string BossDebuffRerollExcludedId =>
            _bossDebuffRerollWeekIndex == WeekIndex ? _bossDebuffRerollExcludedId : string.Empty;

        public string ForcedBossDebuffId =>
            _forcedBossDebuffWeekIndex == WeekIndex ? _forcedBossDebuffId : string.Empty;

        public int BossPassiveArchetypeRewardCount
        {
            get
            {
                EnsureBossPassiveArchetypePityForCurrentWeek();
                return _bossPassiveArchetypeRewardCount;
            }
        }

        public bool BossPassiveArchetypePityArmed
        {
            get
            {
                EnsureBossPassiveArchetypePityForCurrentWeek();
                return _bossPassiveArchetypePityArmed;
            }
        }

        /// <summary>生成本周星级评鉴装饰品候选前，判断本次是否需要流派保底。</summary>
        public bool BeginBossPassiveArchetypePity()
        {
            EnsureBossPassiveArchetypePityForCurrentWeek();
            return _bossPassiveArchetypeRewardCount == 1 && _bossPassiveArchetypePityArmed;
        }

        /// <summary>候选生成完成后推进周内状态；第二次无论是否能执行保底都会消费保底。</summary>
        public void CompleteBossPassiveArchetypePity(bool archetypeOffered)
        {
            EnsureBossPassiveArchetypePityForCurrentWeek();
            if (_bossPassiveArchetypeRewardCount == 0)
            {
                _bossPassiveArchetypePityArmed = !archetypeOffered;
            }
            else
            {
                _bossPassiveArchetypePityArmed = false;
            }

            _bossPassiveArchetypeRewardCount++;
        }

        private void EnsureBossPassiveArchetypePityForCurrentWeek()
        {
            if (_bossPassiveArchetypePityWeekIndex == WeekIndex)
            {
                return;
            }

            _bossPassiveArchetypePityWeekIndex = WeekIndex;
            _bossPassiveArchetypeRewardCount = 0;
            _bossPassiveArchetypePityArmed = false;
        }

        /// <summary>本周已执行的行动次数，用于 UI、随机流和隐藏分进度。</summary>
        public int ActionStepIndex { get; private set; }

        /// <summary>整局累计已执行行动次数，用于随机流和隐藏分分段。</summary>
        public int RunActionStepIndex { get; private set; }

        internal int ActionRandomCandidateIndex
        {
            get
            {
                EnsureActionRandomStateForCurrentWeek();
                return _actionRandomCandidateIndex;
            }
        }

        internal void EnsureActionRandomStateForCurrentWeek()
        {
            if (_actionRandomStateWeek == WeekIndex)
            {
                return;
            }

            _actionRandomStateWeek = WeekIndex;
            _actionRandomCandidateIndex = 0;
            _actionRandomGroupSerial = 0;
            _remainingActionChoiceCounts.Clear();
            _actionCategoryCounts.Clear();
            _actionRewardCounts.Clear();
        }

        internal bool TryTakeActionChoiceCount(out int count)
        {
            EnsureActionRandomStateForCurrentWeek();
            if (_remainingActionChoiceCounts.Count == 0)
            {
                count = 0;
                return false;
            }

            int last = _remainingActionChoiceCounts.Count - 1;
            count = _remainingActionChoiceCounts[last];
            _remainingActionChoiceCounts.RemoveAt(last);
            return true;
        }

        internal void ReplaceActionChoiceCountBag(IEnumerable<int> counts)
        {
            EnsureActionRandomStateForCurrentWeek();
            _remainingActionChoiceCounts.Clear();
            if (counts != null)
            {
                _remainingActionChoiceCounts.AddRange(counts);
            }
        }

        internal int BeginActionRandomGroup()
        {
            EnsureActionRandomStateForCurrentWeek();
            _actionRandomGroupSerial++;
            return _actionRandomGroupSerial;
        }

        internal int GetActionCategoryCount(cfg.ActionRandomCategory category)
        {
            EnsureActionRandomStateForCurrentWeek();
            return _actionCategoryCounts.TryGetValue((int)category, out int count) ? count : 0;
        }

        internal int GetActionRewardCount(cfg.RewardKind rewardKind)
        {
            EnsureActionRandomStateForCurrentWeek();
            return _actionRewardCounts.TryGetValue((int)rewardKind, out int count) ? count : 0;
        }

        internal void RecordRandomAction(cfg.ActionRandomCategory category, cfg.RewardKind? rewardKind)
        {
            EnsureActionRandomStateForCurrentWeek();
            int categoryKey = (int)category;
            _actionCategoryCounts[categoryKey] = GetActionCategoryCount(category) + 1;
            if (rewardKind.HasValue)
            {
                int rewardKey = (int)rewardKind.Value;
                _actionRewardCounts[rewardKey] = GetActionRewardCount(rewardKind.Value) + 1;
            }

            _actionRandomCandidateIndex++;
        }

        public ActionExecutionContext LastActionContext { get; private set; }

        /// <summary>把天数游标格式化为跨语言环境稳定的 key 片段（一位小数，如 d1.5）。</summary>
        private static string DayKey(float currentDay)
        {
            return currentDay.ToString("0.0", CultureInfo.InvariantCulture);
        }

        public static string BuildActionChoiceKey(int runStepIndex, int weekIndex, float currentDay, int actionStepIndex)
        {
            return $"r{runStepIndex}_w{weekIndex}_d{DayKey(currentDay)}_s{actionStepIndex}";
        }

        public static string BuildShopKey(int weekIndex, float currentDay)
        {
            return $"w{weekIndex}_d{DayKey(currentDay)}";
        }

        public static string BuildRewardKey(int weekIndex, float currentDay, ActionExecutionContext context)
        {
            if (context != null && context.IsValid)
            {
                return $"r{context.RunStepIndex}_w{weekIndex}_d{DayKey(currentDay)}_s{context.StepIndex}_{context.ActionGroupId}_{context.Action.Id}_repeat{System.Math.Max(1, context.NodeRepeatIndex)}";
            }

            return $"w{weekIndex}_d{DayKey(currentDay)}";
        }

        public bool IsNodeTriggered(string nodeId) => !string.IsNullOrEmpty(nodeId) && _triggeredNodeIds.Contains(nodeId);

        public void MarkNodeTriggered(string nodeId)
        {
            if (!string.IsNullOrEmpty(nodeId) && !_triggeredNodeIds.Contains(nodeId))
            {
                _triggeredNodeIds.Add(nodeId);
            }
        }

        public void UnmarkNodeTriggered(string nodeId)
        {
            if (!string.IsNullOrEmpty(nodeId))
            {
                _triggeredNodeIds.Remove(nodeId);
            }
        }

        public void MarkEventUsed(string eventId)
        {
            if (!string.IsNullOrEmpty(eventId) && !_usedEventIds.Contains(eventId))
            {
                _usedEventIds.Add(eventId);
            }
        }

        public bool HasUsedEvent(string eventId) => !string.IsNullOrEmpty(eventId) && _usedEventIds.Contains(eventId);

        public bool IsBossCompleted(string bossId) => !string.IsNullOrEmpty(bossId) && _completedBossIds.Contains(bossId);

        public bool IsBossDebuffRolled(string debuffId) => !string.IsNullOrEmpty(debuffId) && _rolledBossDebuffIds.Contains(debuffId);

        internal bool TryGetLockedBossDebuffId(string nodeId, out string debuffId)
        {
            if (!string.IsNullOrEmpty(nodeId)
                && _lockedBossDebuffIdsByNode.TryGetValue(nodeId, out debuffId)
                && !string.IsNullOrEmpty(debuffId))
            {
                return true;
            }

            debuffId = string.Empty;
            return false;
        }

        internal void LockBossDebuffForNode(string nodeId, string debuffId)
        {
            if (!string.IsNullOrEmpty(nodeId) && !string.IsNullOrEmpty(debuffId))
            {
                _lockedBossDebuffIdsByNode[nodeId] = debuffId;
            }
        }

        internal void UnlockBossDebuffForNode(string nodeId)
        {
            if (!string.IsNullOrEmpty(nodeId))
            {
                _lockedBossDebuffIdsByNode.Remove(nodeId);
            }
        }

        internal bool IsBossDebuffLockedByOtherPendingNode(string debuffId, string sourceNodeId)
        {
            if (string.IsNullOrEmpty(debuffId))
            {
                return false;
            }

            foreach (KeyValuePair<string, string> pair in _lockedBossDebuffIdsByNode)
            {
                if (pair.Key != sourceNodeId
                    && pair.Value == debuffId
                    && ContainsRuntimeTimelineNode(pair.Key)
                    && !IsNodeTriggered(pair.Key))
                {
                    return true;
                }
            }

            return false;
        }

        private void ClearLockedBossDebuffs()
        {
            _lockedBossDebuffIdsByNode.Clear();
        }

        public void ForceBossDebuffForCurrentWeek(string debuffId)
        {
            _forcedBossDebuffWeekIndex = WeekIndex;
            _forcedBossDebuffId = debuffId ?? string.Empty;
            ClearLockedBossDebuffs();
        }

        public void ClearForcedBossDebuff()
        {
            _forcedBossDebuffWeekIndex = 0;
            _forcedBossDebuffId = string.Empty;
            ClearLockedBossDebuffs();
        }

        public void MarkBossCompleted(string bossId)
        {
            if (!string.IsNullOrEmpty(bossId) && !_completedBossIds.Contains(bossId))
            {
                _completedBossIds.Add(bossId);
            }
        }

        public void MarkBossDebuffRolled(string debuffId)
        {
            if (!string.IsNullOrEmpty(debuffId) && !_rolledBossDebuffIds.Contains(debuffId))
            {
                _rolledBossDebuffIds.Add(debuffId);
            }
        }

        public void ResetBossDebuffRollHistory()
        {
            _rolledBossDebuffIds.Clear();
        }

        public bool RerollBossDebuffForNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return false;
            }

            cfg.TimelineNode node = TimelineService.GetNode(this, nodeId);
            cfg.BossDebuff current = node != null ? BossService.PreviewBossDebuff(this, node) : null;

            if (_bossDebuffRerollWeekIndex != WeekIndex)
            {
                _bossDebuffRerollWeekIndex = WeekIndex;
                _bossDebuffRerollIndex = 0;
                _bossDebuffRerollExcludedId = string.Empty;
            }

            _bossDebuffRerollNodeId = nodeId;
            _bossDebuffRerollExcludedId = current?.Id ?? string.Empty;
            _bossDebuffRerollIndex++;
            UnlockBossDebuffForNode(nodeId);
            return true;
        }

        /// <summary>开始一条新的本周时间轴：重置天数游标、节点结算记录与本周行动使用记录。</summary>
        public void BeginTimeline(string timelineId, float lengthDays)
        {
            BeginTimeline(timelineId, lengthDays, null);
        }

        public void BeginTimeline(string timelineId, float lengthDays, IEnumerable<RuntimeTimelineNode> nodes)
        {
            CurrentTimelineId = timelineId ?? string.Empty;
            CurrentTimelineWeekIndex = WeekIndex;
            TimelineLengthDays = lengthDays;
            CurrentDay = 0f;
            ActionStepIndex = 0;
            _triggeredNodeIds.Clear();
            _runtimeTimelineNodes.Clear();
            _runtimeTimelineNodeSerial = 0;
            _pendingExtraTimelineNodeIds.Clear();
            ClearLockedBossDebuffs();
            _bossDebuffRerollNodeId = string.Empty;
            _bossDebuffRerollExcludedId = string.Empty;
            if (nodes != null)
            {
                foreach (RuntimeTimelineNode node in nodes)
                {
                    if (!string.IsNullOrEmpty(node.Id) && !string.IsNullOrEmpty(node.ActionId))
                    {
                        _runtimeTimelineNodes.Add(node);
                    }
                }

                SortRuntimeTimelineNodes();
            }

            LastActionContext = null;
            ClearPendingActionExecution();
            ClearPendingActionChoices();
            ClearPendingShopStock();
            ClearPendingBattleReward();
        }

        public void AdvanceActionStep()
        {
            ClearPendingActionExecution();
            ClearPendingActionChoices();
            ActionStepIndex++;
            RunActionStepIndex++;
        }

        public void RestoreActionStepIndex(int actionStepIndex)
        {
            ActionStepIndex = System.Math.Max(0, actionStepIndex);
        }

        public void RestoreRunActionStepIndex(int runActionStepIndex)
        {
            RunActionStepIndex = System.Math.Max(0, runActionStepIndex);
        }

        public void SetLastActionContext(ActionExecutionContext context)
        {
            LastActionContext = context;
        }

        public bool HasPendingActionExecution =>
            _pendingActionExecution != null && !string.IsNullOrEmpty(_pendingActionExecution.ActionId);

        public PendingActionExecutionSaveData GetPendingActionExecution()
        {
            return ClonePendingActionExecution(_pendingActionExecution);
        }

        public void SetPendingActionExecution(
            ActionExecutionContext context,
            ActionOutcome outcome,
            string resolvedEventId = null)
        {
            if (context == null || !context.IsValid)
            {
                _pendingActionExecution = null;
                return;
            }

            SetLastActionContext(context);

            _pendingActionExecution = new PendingActionExecutionSaveData
            {
                ActionId = context.Action.Id,
                StepIndex = context.StepIndex,
                RunStepIndex = context.RunStepIndex,
                ActionGroupId = context.ActionGroupId ?? string.Empty,
                CostDays = context.CostDays,
                SourceKey = context.SourceKey ?? string.Empty,
                HasTargetScoreDayOverride = context.TargetScoreDayOverride.HasValue,
                TargetScoreDayOverride = context.TargetScoreDayOverride ?? 0f,
                HalfDayBuffApplied = context.HalfDayBuffApplied,
                IsExtraTimelineExecution = context.IsExtraTimelineExecution,
                TimelineStopChance = context.TimelineStopChance,
                NodeRepeatIndex = System.Math.Max(1, context.NodeRepeatIndex),
                NodeRepeatTotal = System.Math.Max(1, context.NodeRepeatTotal),
                OutcomeKind = outcome?.Kind ?? ActionOutcomeKind.Immediate,
                Feedback = outcome?.Feedback ?? string.Empty,
                RequiredScore = outcome?.RequiredScore ?? 0,
                Modifier = outcome?.Modifier ?? string.Empty,
                BattleKey = outcome?.BattleKey ?? string.Empty,
                IsBoss = outcome?.IsBoss ?? false,
                BossId = outcome?.BossId ?? string.Empty,
                BossDebuffId = outcome?.BossDebuffId ?? string.Empty,
                EventId = resolvedEventId ?? outcome?.EventId ?? string.Empty,
                EventEntryGoldGranted = false,
                SlotEventId = outcome?.SlotEventId ?? string.Empty,
                SlotSpinsUsed = 0,
                SlotStage = SlotExecutionStage.Ready,
                SlotRewardKey = string.Empty,
            };
        }

        /// <summary>
        /// 原子标记当前根事件的「进入事件」金币已发放。返回 false 表示没有待处理事件或已经发过，
        /// 用于阻止事件页恢复、重复回调和读档续接重复加钱。
        /// </summary>
        public bool TryMarkPendingEventEntryGoldGranted()
        {
            if (_pendingActionExecution == null
                || string.IsNullOrEmpty(_pendingActionExecution.EventId)
                || _pendingActionExecution.EventEntryGoldGranted)
            {
                return false;
            }

            _pendingActionExecution.EventEntryGoldGranted = true;
            return true;
        }

        public bool SetPendingSlotExecutionState(
            string slotEventId,
            int spinsUsed,
            SlotExecutionStage stage,
            string rewardKey = null)
        {
            if (_pendingActionExecution == null
                || _pendingActionExecution.OutcomeKind != ActionOutcomeKind.Slot)
            {
                return false;
            }

            _pendingActionExecution.SlotEventId = slotEventId ?? string.Empty;
            _pendingActionExecution.SlotSpinsUsed = System.Math.Max(0, spinsUsed);
            _pendingActionExecution.SlotStage = stage;
            _pendingActionExecution.SlotRewardKey = rewardKey ?? string.Empty;
            return true;
        }

        public void ClearPendingActionExecution()
        {
            _pendingActionExecution = null;
        }

        public List<ActionChoice> GetPendingActionChoices(string key)
        {
            var result = new List<ActionChoice>();
            if (!HasPendingActionChoices(key))
            {
                return result;
            }

            foreach (RunActionChoiceSaveData data in _pendingActionChoices)
            {
                cfg.GameAction action = _tables.TbAction.GetOrDefault(data.ActionId);
                if (action == null)
                {
                    continue;
                }

                result.Add(new ActionChoice(
                    action,
                    data.ActionGroupId,
                    data.WeekStepIndex,
                    data.RunStepIndex,
                    data.CostDays,
                    timelineStopChance: data.TimelineStopChance));
            }

            return result;
        }

        public bool HasPendingActionChoices(string key)
        {
            return !string.IsNullOrEmpty(key) && key == _pendingActionChoiceKey;
        }

        public void SetPendingActionChoices(string key, IReadOnlyList<ActionChoice> choices)
        {
            string normalizedKey = key ?? string.Empty;
            bool isReroll = !string.IsNullOrEmpty(normalizedKey)
                && normalizedKey == _pendingActionChoiceKey
                && _pendingActionChoices.Count > 0;
            _pendingActionChoiceRevision = isReroll ? _pendingActionChoiceRevision + 1 : 0;
            _pendingActionChoiceKey = normalizedKey;
            _pendingActionChoices.Clear();
            if (choices == null)
            {
                return;
            }

            foreach (ActionChoice choice in choices)
            {
                if (choice == null || !choice.IsValid)
                {
                    continue;
                }

                _pendingActionChoices.Add(new RunActionChoiceSaveData
                {
                    ActionId = choice.Action.Id,
                    ActionGroupId = choice.ActionGroupId,
                    WeekStepIndex = choice.WeekStepIndex,
                    RunStepIndex = choice.RunStepIndex,
                    CostDays = choice.CostDays,
                    TimelineStopChance = choice.TimelineStopChance,
                });
            }
        }

        public void ClearPendingActionChoices()
        {
            _pendingActionChoiceKey = string.Empty;
            _pendingActionChoiceRevision = 0;
            _pendingActionChoices.Clear();
        }

        public string PendingActionChoiceKey => _pendingActionChoiceKey;

        public int PendingActionChoiceRevision => _pendingActionChoiceRevision;

        public List<ShopEntry> GetPendingShopStock(string key)
        {
            var result = new List<ShopEntry>();
            if (!HasPendingShopStock(key))
            {
                return result;
            }

            var usedSlots = new Dictionary<ShopEntryKind, HashSet<int>>();
            foreach (ShopEntrySaveData entry in _pendingShopStock)
            {
                if (entry == null)
                {
                    continue;
                }

                if (!usedSlots.TryGetValue(entry.Kind, out HashSet<int> used))
                {
                    used = new HashSet<int>();
                    usedSlots.Add(entry.Kind, used);
                }

                int slotIndex = entry.SlotIndex;
                if (slotIndex < 0 || !used.Add(slotIndex))
                {
                    slotIndex = NextAvailableShopSlot(used);
                    used.Add(slotIndex);
                }

                result.Add(string.IsNullOrEmpty(entry.Id)
                    ? ShopEntry.CreateEmpty(entry.Kind, slotIndex)
                    : new ShopEntry(
                        entry.Kind,
                        entry.Id,
                        entry.Name,
                        entry.Desc,
                        entry.BasePrice,
                        entry.Price,
                        slotIndex));
            }

            return result;
        }

        public bool HasPendingShopStock(string key)
        {
            return !string.IsNullOrEmpty(key) && key == _pendingShopKey;
        }

        public void SetPendingShopStock(string key, IReadOnlyList<ShopEntry> stock)
        {
            _pendingShopKey = key ?? string.Empty;
            _pendingShopStock.Clear();
            if (stock == null)
            {
                return;
            }

            foreach (ShopEntry entry in stock)
            {
                if (entry == null)
                {
                    continue;
                }

                _pendingShopStock.Add(new ShopEntrySaveData
                {
                    Kind = entry.Kind,
                    SlotIndex = entry.SlotIndex,
                    Id = entry.Id,
                    Name = entry.Name,
                    Desc = entry.Desc,
                    BasePrice = entry.BasePrice,
                    Price = entry.Price,
                });
            }
        }

        private static int NextAvailableShopSlot(HashSet<int> used)
        {
            int slotIndex = 0;
            while (used != null && used.Contains(slotIndex))
            {
                slotIndex++;
            }

            return slotIndex;
        }

        public void ClearPendingShopStock()
        {
            _pendingShopKey = string.Empty;
            _pendingShopStock.Clear();
        }

        public RewardOffer GetPendingRewardOffer(string key)
        {
            if (string.IsNullOrEmpty(key) || key != _pendingRewardKey || _pendingRewardOffer == null)
            {
                return null;
            }

            return FromSaveData(_pendingRewardOffer);
        }

        public bool HasPendingRewardOffer => !string.IsNullOrEmpty(_pendingRewardKey) && _pendingRewardOffer != null;

        public string PendingRewardKey => _pendingRewardKey;

        public RewardOffer GetPendingRewardOffer()
        {
            return HasPendingRewardOffer ? FromSaveData(_pendingRewardOffer) : null;
        }

        public void SetPendingRewardOffer(string key, RewardOffer offer)
        {
            _pendingRewardKey = key ?? string.Empty;
            _pendingRewardOffer = ToSaveData(offer);
        }

        public void ClearPendingRewardOffer()
        {
            _pendingRewardKey = string.Empty;
            _pendingRewardOffer = null;
        }

        public bool HasPendingRewardBattleView => _pendingRewardBattleView != null;

        public PendingRewardBattleViewSaveData GetPendingRewardBattleView()
        {
            return ClonePendingRewardBattleView(_pendingRewardBattleView);
        }

        public void SetPendingRewardBattleView(PendingRewardBattleViewSaveData data)
        {
            _pendingRewardBattleView = ClonePendingRewardBattleView(data);
        }

        public void ClearPendingRewardBattleView()
        {
            _pendingRewardBattleView = null;
        }

        public bool HasPendingHeartBreak => _pendingHeartBreak != null;

        public PendingHeartBreakSaveData GetPendingHeartBreak()
        {
            return ClonePendingHeartBreak(_pendingHeartBreak);
        }

        public void SetPendingHeartBreak(PendingHeartBreakSaveData data)
        {
            _pendingHeartBreak = ClonePendingHeartBreak(data);
        }

        public void ClearPendingHeartBreak()
        {
            _pendingHeartBreak = null;
        }

        /// <summary>待领奖 Food 生命周期真正结束时，同时清除奖励内容和结算画面快照。</summary>
        public void ClearPendingBattleReward()
        {
            ClearPendingRewardOffer();
            ClearPendingRewardBattleView();
        }

        public bool HasPendingGenericRewards => _pendingGenericRewards.Count > 0;

        public bool PendingGenericRewardsConfirmBattleAfterDone
        {
            get => PendingGenericRewardContinuation == PendingGenericRewardContinuationKind.Battle;
            set
            {
                _pendingGenericRewardsConfirmBattleAfterDone = value;
                if (value)
                {
                    _pendingGenericRewardContinuation = PendingGenericRewardContinuationKind.Battle;
                }
                else if (_pendingGenericRewardContinuation == PendingGenericRewardContinuationKind.Battle)
                {
                    _pendingGenericRewardContinuation = PendingGenericRewardContinuationKind.None;
                }
            }
        }

        public PendingGenericRewardContinuationKind PendingGenericRewardContinuation
        {
            get
            {
                if (_pendingGenericRewardContinuation != PendingGenericRewardContinuationKind.None)
                {
                    return _pendingGenericRewardContinuation;
                }

                return _pendingGenericRewardsConfirmBattleAfterDone
                    ? PendingGenericRewardContinuationKind.Battle
                    : PendingGenericRewardContinuationKind.None;
            }
            set
            {
                _pendingGenericRewardContinuation = value;
                _pendingGenericRewardsConfirmBattleAfterDone =
                    value == PendingGenericRewardContinuationKind.Battle;
            }
        }

        public void EnqueueGenericRewardOffer(string key, string title, RewardOffer offer)
        {
            if (offer == null)
            {
                return;
            }

            string safeKey = string.IsNullOrEmpty(key) ? $"generic_{_pendingGenericRewards.Count}" : key;
            for (int i = 0; i < _pendingGenericRewards.Count; i++)
            {
                if (string.Equals(_pendingGenericRewards[i]?.Key, safeKey, System.StringComparison.Ordinal))
                {
                    _pendingGenericRewards[i] = new GenericRewardSaveData
                    {
                        Key = safeKey,
                        Title = title ?? string.Empty,
                        Offer = ToSaveData(offer),
                    };
                    return;
                }
            }

            _pendingGenericRewards.Add(new GenericRewardSaveData
            {
                Key = safeKey,
                Title = title ?? string.Empty,
                Offer = ToSaveData(offer),
            });
        }

        public bool TryPeekPendingGenericReward(out string key, out string title, out RewardOffer offer)
        {
            while (_pendingGenericRewards.Count > 0 && _pendingGenericRewards[0]?.Offer == null)
            {
                _pendingGenericRewards.RemoveAt(0);
            }

            if (_pendingGenericRewards.Count == 0)
            {
                key = string.Empty;
                title = string.Empty;
                offer = null;
                return false;
            }

            GenericRewardSaveData pending = _pendingGenericRewards[0];
            key = pending.Key ?? string.Empty;
            title = pending.Title ?? string.Empty;
            offer = FromSaveData(pending.Offer);
            return offer != null;
        }

        public void SetPendingGenericRewardOffer(string key, RewardOffer offer)
        {
            if (offer == null)
            {
                return;
            }

            for (int i = 0; i < _pendingGenericRewards.Count; i++)
            {
                GenericRewardSaveData pending = _pendingGenericRewards[i];
                if (pending != null && string.Equals(pending.Key, key, System.StringComparison.Ordinal))
                {
                    pending.Offer = ToSaveData(offer);
                    return;
                }
            }
        }

        public void ClearPendingGenericRewardOffer(string key)
        {
            for (int i = 0; i < _pendingGenericRewards.Count; i++)
            {
                GenericRewardSaveData pending = _pendingGenericRewards[i];
                if (pending != null && string.Equals(pending.Key, key, System.StringComparison.Ordinal))
                {
                    _pendingGenericRewards.RemoveAt(i);
                    return;
                }
            }
        }

        public cfg.Week CurrentWeek => _tables.TbWeek.GetOrDefault(WeekIndex);

        /// <summary>是否已进入无尽模式（周序号超过配置表最后一周）。</summary>
        public bool IsEndless => WeekIndex > TotalWeeks;

        public int RequiredScore
        {
            get
            {
                if (RequiredScoreOverride >= 0)
                {
                    return RequiredScoreOverride;
                }

                return HiddenScoreService.TargetScore(this);
            }
        }

        private cfg.Week LastConfiguredWeek => TotalWeeks > 0 ? _tables.TbWeek.GetOrDefault(TotalWeeks) : null;

        private ActionRandomStateSaveData CaptureActionRandomState()
        {
            EnsureActionRandomStateForCurrentWeek();
            var categoryCounts = new Dictionary<int, int>(_actionCategoryCounts);
            categoryCounts.TryAdd((int)cfg.ActionRandomCategory.Daily, 0);
            categoryCounts.TryAdd((int)cfg.ActionRandomCategory.Hot, 0);
            categoryCounts.TryAdd((int)cfg.ActionRandomCategory.Event, 0);

            var rewardCounts = new Dictionary<int, int>(_actionRewardCounts);
            rewardCounts.TryAdd((int)cfg.RewardKind.PassiveItemChoice, 0);
            rewardCounts.TryAdd((int)cfg.RewardKind.FragmentChoice, 0);
            rewardCounts.TryAdd((int)cfg.RewardKind.ActiveItemStrengthen, 0);
            rewardCounts.TryAdd((int)cfg.RewardKind.ActiveItemAdjust, 0);
            rewardCounts.TryAdd((int)cfg.RewardKind.Gold, 0);
            return new ActionRandomStateSaveData
            {
                WeekIndex = _actionRandomStateWeek,
                CandidateIndex = _actionRandomCandidateIndex,
                GroupSerial = _actionRandomGroupSerial,
                RemainingChoiceCounts = new List<int>(_remainingActionChoiceCounts),
                CategoryCounts = categoryCounts,
                RewardCounts = rewardCounts,
            };
        }

        private void RestoreActionRandomState(ActionRandomStateSaveData state)
        {
            _remainingActionChoiceCounts.Clear();
            _actionCategoryCounts.Clear();
            _actionRewardCounts.Clear();

            if (state == null || state.WeekIndex != WeekIndex)
            {
                _actionRandomStateWeek = 0;
                EnsureActionRandomStateForCurrentWeek();
                return;
            }

            _actionRandomStateWeek = state.WeekIndex;
            _actionRandomCandidateIndex = System.Math.Max(0, state.CandidateIndex);
            _actionRandomGroupSerial = System.Math.Max(0, state.GroupSerial);
            if (state.RemainingChoiceCounts != null)
            {
                foreach (int count in state.RemainingChoiceCounts)
                {
                    if (count == 2 || count == 3)
                    {
                        _remainingActionChoiceCounts.Add(count);
                    }
                }
            }

            if (state.CategoryCounts != null)
            {
                foreach (KeyValuePair<int, int> pair in state.CategoryCounts)
                {
                    if (pair.Value > 0)
                    {
                        _actionCategoryCounts[pair.Key] = pair.Value;
                    }
                }
            }

            if (state.RewardCounts != null)
            {
                foreach (KeyValuePair<int, int> pair in state.RewardCounts)
                {
                    if (pair.Value > 0)
                    {
                        _actionRewardCounts[pair.Key] = pair.Value;
                    }
                }
            }
        }

        /// <summary>导出为存档数据。</summary>
        public RunSaveData ToSaveData()
        {
            var items = new List<RunItemSaveData>(_items.Count);
            var legacyItemIds = new List<string>(_items.Count);
            foreach (RunItemState state in _items)
            {
                items.Add(state.ToSaveData());
                legacyItemIds.Add(state.ItemId);
            }

            return new RunSaveData
            {
                ActionRandomRuleVersion = RunPersistence.CurrentActionRandomRuleVersion,
                RunId = RunId,
                CharacterId = CharacterId,
                SeedText = SeedText,
                IsTutorialRun = IsTutorialRun,
                WeekIndex = WeekIndex,
                Gold = Gold,
                HeartCapacity = _heartCapacity,
                HeartsRemaining = HeartsRemaining,
                PendingHeartBreak = ClonePendingHeartBreak(_pendingHeartBreak),
                InterestThreshold = _interestThreshold,
                InterestGoldPer = _interestGoldPer,
                InterestCap = _interestCap,
                RetainedHappyCakeLayers = _retainedHappyCakeLayers,
                ActiveUseIndex = _activeUseIndex,
                NextDailyActionHalfCostStacks = _nextDailyActionHalfCostStacks,
                NextBusinessRewardDoubleStacks = _nextBusinessRewardDoubleStacks,
                LastDishChoiceArchetypeId = _lastDishChoiceArchetypeId,
                DishChoiceArchetypeMissStreak = _dishChoiceArchetypeMissStreak,
                BossPassiveArchetypePityWeekIndex = _bossPassiveArchetypePityWeekIndex,
                BossPassiveArchetypeRewardCount = _bossPassiveArchetypeRewardCount,
                BossPassiveArchetypePityArmed = _bossPassiveArchetypePityArmed,
                ActionRerollCount = _actionRerollCount,
                LoanDebt = _loanDebt,
                MealBonusRemaining = _mealBonusRemaining,
                ScoreToOneRemaining = _scoreToOneRemaining,
                EventTargetScoreHiddenOffset = _eventTargetScoreHiddenOffset,
                EventDishHiddenOffset = _eventDishHiddenOffset,
                EventItemLuckOffset = _eventItemLuckOffset,
                EventFragmentHiddenOffset = _eventFragmentHiddenOffset,
                EventGoldHiddenOffset = _eventGoldHiddenOffset,
                EventShopPricePct = _eventShopPricePct,
                EventChoicePenaltyRemaining = _eventChoicePenaltyRemaining,
                EventChoiceCountDelta = _eventChoiceCountDelta,
                NextFoodTargetScoreHiddenOffset = _nextFoodTargetScoreHiddenOffset,
                NextMealRewardGold = _nextMealRewardGold,
                NextBusinessGoldMultiplier = _nextBusinessGoldMultipliers.Count > 0
                    ? _nextBusinessGoldMultipliers[0]
                    : 1f,
                NextBusinessGoldMultipliers = new List<float>(_nextBusinessGoldMultipliers),
                CurrentWeekBusinessGoldMultiplier = _currentWeekBusinessGoldMultiplier,
                CurrentWeekBusinessGoldWeek = _currentWeekBusinessGoldWeek,
                BossTargetScorePct = _bossTargetScorePct,
                BossBaseGoldPct = _bossBaseGoldPct,
                ActEventActionCount = _actEventActionCount,
                EventCounters = new Dictionary<string, int>(_eventCounters),
                ForcedEventIds = new List<string>(_forcedEventIds),
                Items = items,
                BonusDishIds = new List<string>(_bonusDishIds),
                Recipe = ToRecipeSaveData(),
                TableFragmentIds = new List<string>(_stomachFragmentIds),
                FragmentPlacements = ToFragmentPlacementSaveData(),
                PendingFragmentPackIds = new List<string>(_pendingFragmentPack),
                PendingFragmentPackRotations = new List<int>(_pendingFragmentPackRotations),
                FragmentPackPurchaseCount = _fragmentPackPurchaseCount,
                DeleteDishCount = _deleteDishCount,
                CurrentShopDeleteDishCount = _currentShopDeleteDishCount,
                CurrentShopFragmentPackPurchaseCount = _currentShopFragmentPackPurchaseCount,
                RunSettledCounts = new Dictionary<string, int>(_runSettledCounts),
                SettledFoodBattleKeys = new List<string>(_settledFoodBattleKeys),
                CurrentTimelineId = CurrentTimelineId,
                CurrentTimelineWeekIndex = CurrentTimelineWeekIndex,
                TimelineLengthDays = TimelineLengthDays,
                CurrentDay = CurrentDay,
                ActionStepIndex = ActionStepIndex,
                RunActionStepIndex = RunActionStepIndex,
                RequiredScoreOverride = RequiredScoreOverride,
                LastActionId = LastActionContext?.Action?.Id ?? string.Empty,
                LastActionStepIndex = LastActionContext?.StepIndex ?? 0,
                LastRunActionStepIndex = LastActionContext?.RunStepIndex ?? 0,
                LastActionGroupId = LastActionContext?.ActionGroupId ?? string.Empty,
                LastActionCostDays = LastActionContext?.CostDays ?? 0,
                LastActionSourceKey = LastActionContext?.SourceKey ?? string.Empty,
                LastActionHasTargetScoreDayOverride = LastActionContext?.TargetScoreDayOverride.HasValue ?? false,
                LastActionTargetScoreDayOverride = LastActionContext?.TargetScoreDayOverride ?? 0f,
                LastActionHalfDayBuffApplied = LastActionContext?.HalfDayBuffApplied ?? false,
                LastActionIsExtraTimelineExecution = LastActionContext?.IsExtraTimelineExecution ?? false,
                LastActionTimelineStopChance = LastActionContext?.TimelineStopChance ?? 0f,
                LastActionNodeRepeatIndex = LastActionContext != null
                    ? System.Math.Max(1, LastActionContext.NodeRepeatIndex)
                    : 1,
                LastActionNodeRepeatTotal = LastActionContext != null
                    ? System.Math.Max(1, LastActionContext.NodeRepeatTotal)
                    : 1,
                PendingActionExecution = ClonePendingActionExecution(_pendingActionExecution),
                ActionRandomState = CaptureActionRandomState(),
                TriggeredNodeIds = new List<string>(_triggeredNodeIds),
                RuntimeTimelineNodes = ToRuntimeTimelineNodeSaveData(),
                RuntimeTimelineNodeSerial = _runtimeTimelineNodeSerial,
                UsedEventIds = new List<string>(_usedEventIds),
                CompletedBossIds = new List<string>(_completedBossIds),
                RolledBossDebuffIds = new List<string>(_rolledBossDebuffIds),
                LockedBossDebuffIdsByNode = new Dictionary<string, string>(_lockedBossDebuffIdsByNode),
                BossDebuffRerollWeekIndex = _bossDebuffRerollWeekIndex,
                BossDebuffRerollIndex = _bossDebuffRerollIndex,
                BossDebuffRerollNodeId = _bossDebuffRerollNodeId,
                BossDebuffRerollExcludedId = _bossDebuffRerollExcludedId,
                PendingExtraTimelineNodeIds = new List<string>(_pendingExtraTimelineNodeIds),
                ForcedBossDebuffWeekIndex = _forcedBossDebuffWeekIndex,
                ForcedBossDebuffId = _forcedBossDebuffId,
                PendingActionChoiceKey = _pendingActionChoiceKey,
                PendingActionChoiceRevision = _pendingActionChoiceRevision,
                PendingActionChoices = new List<RunActionChoiceSaveData>(_pendingActionChoices),
                PendingShopKey = _pendingShopKey,
                PendingShopStock = new List<ShopEntrySaveData>(_pendingShopStock),
                PendingRewardKey = _pendingRewardKey,
                PendingRewardOffer = _pendingRewardOffer,
                PendingRewardBattleView = ClonePendingRewardBattleView(_pendingRewardBattleView),
                PendingGenericRewards = CloneGenericRewardSaveData(_pendingGenericRewards),
                PendingGenericRewardsConfirmBattleAfterDone = _pendingGenericRewardsConfirmBattleAfterDone,
                PendingGenericRewardContinuation = PendingGenericRewardContinuation,
            };
        }

        /// <summary>从存档数据重建运行（不重复发放初始装饰品和消耗品，整段持有列表以存档为准）。</summary>
        public static GameRun FromSaveData(
            cfg.Tables tables,
            GameplayDatabase database,
            RunSaveData data,
            IRunExecutionEnvironment execution = null)
        {
            execution ??= RunExecutionEnvironment.CreateIsolated(
                tables,
                data.SeedText,
                data.RandomSnapshot);
            var run = new GameRun(
                tables,
                database,
                data.CharacterId,
                data.SeedText,
                data.WeekIndex,
                initializeCharacterLoadout: false,
                isTutorialRun: data.IsTutorialRun,
                execution: execution);
            run.RunId = string.IsNullOrWhiteSpace(data.RunId)
                ? System.Guid.NewGuid().ToString("N")
                : data.RunId;
            run.Gold = data.Gold;
            run._heartCapacity = System.Math.Max(1, data.HeartCapacity);
            // 装饰品模型尚未恢复，不能先按基础上限截断；待持有列表重建后再按有效上限收口。
            run._heartsRemaining = System.Math.Max(0, data.HeartsRemaining);
            run._pendingHeartBreak = ClonePendingHeartBreak(data.PendingHeartBreak);
            run._interestThreshold = data.InterestThreshold >= 0
                ? data.InterestThreshold
                : System.Math.Max(0, tables.TbGameBase.InterestThreshold);
            run._interestGoldPer = data.InterestGoldPer > 0
                ? data.InterestGoldPer
                : (tables.TbGameBase.InterestGoldPer > 0 ? tables.TbGameBase.InterestGoldPer : 1);
            run._interestCap = data.InterestCap >= 0
                ? data.InterestCap
                : System.Math.Max(0, tables.TbGameBase.InitialInterestCap);
            run._retainedHappyCakeLayers = System.Math.Max(0, data.RetainedHappyCakeLayers);
            run._activeUseIndex = data.ActiveUseIndex;
            run._nextDailyActionHalfCostStacks = System.Math.Max(0, data.NextDailyActionHalfCostStacks);
            run._nextBusinessRewardDoubleStacks = System.Math.Max(
                System.Math.Max(0, data.NextBusinessRewardDoubleStacks),
                System.Math.Max(0, data.NextBusinessSpecificRewardDoubleStacks));
            if (IsDishChoiceArchetypeId(data.LastDishChoiceArchetypeId))
            {
                run._lastDishChoiceArchetypeId = data.LastDishChoiceArchetypeId;
                run._dishChoiceArchetypeMissStreak = System.Math.Max(
                    0,
                    data.DishChoiceArchetypeMissStreak);
            }
            else
            {
                run.ClearDishChoiceArchetypePity();
            }
            run._actionRerollCount = data.ActionRerollCount >= 0
                ? data.ActionRerollCount
                : System.Math.Max(0, tables.TbGameBase.InitialActionRerollCount);
            run._loanDebt = System.Math.Max(0, data.LoanDebt);
            run._mealBonusRemaining = System.Math.Max(0, data.MealBonusRemaining);
            run._scoreToOneRemaining = System.Math.Max(0, data.ScoreToOneRemaining);
            run._eventTargetScoreHiddenOffset = data.EventTargetScoreHiddenOffset;
            run._eventDishHiddenOffset = data.EventDishHiddenOffset;
            run._eventItemLuckOffset = data.EventItemLuckOffset;
            run._eventFragmentHiddenOffset = data.EventFragmentHiddenOffset;
            run._eventGoldHiddenOffset = data.EventGoldHiddenOffset;
            run._eventShopPricePct = data.EventShopPricePct;
            run._eventChoicePenaltyRemaining = System.Math.Max(0, data.EventChoicePenaltyRemaining);
            run._eventChoiceCountDelta = data.EventChoiceCountDelta;
            run._nextFoodTargetScoreHiddenOffset = data.NextFoodTargetScoreHiddenOffset;
            run._nextMealRewardGold = data.NextMealRewardGold;
            if (data.NextBusinessGoldMultipliers != null && data.NextBusinessGoldMultipliers.Count > 0)
            {
                int multiplierCount = System.Math.Min(
                    data.NextBusinessGoldMultipliers.Count,
                    MaxQueuedBusinessGoldEffects);
                for (int index = 0; index < multiplierCount; index++)
                {
                    run._nextBusinessGoldMultipliers.Add(
                        System.Math.Max(0f, data.NextBusinessGoldMultipliers[index]));
                }
            }
            else if (System.Math.Abs(data.NextBusinessGoldMultiplier - 1f) > 0.0001f)
            {
                // 兼容只保存单次倍率的旧存档。
                run._nextBusinessGoldMultipliers.Add(System.Math.Max(0f, data.NextBusinessGoldMultiplier));
            }
            run._currentWeekBusinessGoldMultiplier = System.Math.Max(0f, data.CurrentWeekBusinessGoldMultiplier);
            run._currentWeekBusinessGoldWeek = data.CurrentWeekBusinessGoldWeek;
            run._bossTargetScorePct = data.BossTargetScorePct;
            run._bossBaseGoldPct = data.BossBaseGoldPct;
            run._actEventActionCount = System.Math.Max(0, data.ActEventActionCount);
            if (data.EventCounters != null)
            {
                foreach (KeyValuePair<string, int> kv in data.EventCounters)
                {
                    if (!string.IsNullOrEmpty(kv.Key))
                    {
                        run._eventCounters[kv.Key] = System.Math.Max(0, kv.Value);
                    }
                }
            }

            if (data.ForcedEventIds != null)
            {
                foreach (string eventId in data.ForcedEventIds)
                {
                    if (!string.IsNullOrEmpty(eventId))
                    {
                        run._forcedEventIds.Add(eventId);
                    }
                }
            }

            run._items.Clear();

            if (data.Items != null && data.Items.Count > 0)
            {
                foreach (RunItemSaveData item in data.Items)
                {
                    if (string.IsNullOrEmpty(item.ItemId))
                    {
                        continue;
                    }

                    ItemDefinition def = ItemDefinition.Get(tables, item.ItemId);
                    if (def == null)
                    {
                        // 配置删改后，旧档可能仍残留已经不存在的装饰品和消耗品。未知条目不能继续进入
                        // 持有列表，否则会形成无模型、不可见、也无法正常移除的“幽灵装饰品和消耗品”。
                        Log.Warning($"存档包含未知装饰品和消耗品，已安全跳过：{item.ItemId}。", "RunSave");
                        continue;
                    }

                    // 旧档迁移：消耗品曾用单条 + Count 表示堆叠，这里展开为多份实例；
                    // Count<=0 的旧「僵尸条目」直接丢弃（用完即不存在）。装饰品恒为一条。
                    int instances = 1;
                    if (def.Kind == cfg.ItemKind.Active)
                    {
                        instances = System.Math.Max(0, item.Count);
                    }

                    for (int k = 0; k < instances; k++)
                    {
                        if (def.Kind == cfg.ItemKind.Passive && run.GetItemState(item.ItemId) != null)
                        {
                            continue;
                        }

                        var state = new RunItemState(item.ItemId, item.Level);
                        run._items.Add(state);

                        // 装饰品读档：重建行为模型并恢复 per-instance 状态（不重复触发 OnAcquired）。
                        if (def.Kind == cfg.ItemKind.Passive)
                        {
                            GourmetProject.Game.Meta.Passives.PassiveItemModel model = run.BindPassiveModel(state, def);
                            model.RestoreState(item.StateJson ?? string.Empty);
                        }
                    }
                }
            }

            run._heartsRemaining = System.Math.Min(run._heartsRemaining, run.HeartCapacity);

            // 这两个旧全局计数只有对应被动仍在场时才有语义。配置移除/旧档缺项时清零，
            // 避免被跳过的未知装饰品和消耗品继续从全局字段暗中生效；不发金币或其它补偿。
            if (!run.HasItem("item_gold_meal_bonus"))
            {
                run._mealBonusRemaining = 0;
            }

            if (!run.HasItem("item_score_to_one"))
            {
                run._scoreToOneRemaining = 0;
            }

            if (!run.HasItem("item_cake_retain"))
            {
                run._retainedHappyCakeLayers = 0;
            }


            run.RestoreRecipeBooks(data);

            if (data.TableFragmentIds != null)
            {
                run._stomachFragmentIds.AddRange(data.TableFragmentIds);
            }

            if (data.FragmentPlacements != null)
            {
                foreach (TableFragmentPlacementSaveData p in data.FragmentPlacements)
                {
                    if (p == null || string.IsNullOrEmpty(p.FragmentId))
                    {
                        continue;
                    }

                    run._fragmentPlacements.Add(new TableFragmentPlacement(
                        p.FragmentId, p.Rotation, new GridPos(p.OriginX, p.OriginY)));
                }
            }

            if (data.PendingFragmentPackIds != null)
            {
                for (int i = 0; i < data.PendingFragmentPackIds.Count; i++)
                {
                    string fragmentId = data.PendingFragmentPackIds[i];
                    if (string.IsNullOrEmpty(fragmentId))
                    {
                        continue;
                    }

                    run._pendingFragmentPack.Add(fragmentId);
                    int rotation = data.PendingFragmentPackRotations != null
                        && i < data.PendingFragmentPackRotations.Count
                            ? data.PendingFragmentPackRotations[i]
                            : 0;
                    run._pendingFragmentPackRotations.Add(((rotation % 4) + 4) % 4);
                }
            }

            run._fragmentPackPurchaseCount = System.Math.Max(0, data.FragmentPackPurchaseCount);
            run._deleteDishCount = System.Math.Max(0, data.DeleteDishCount);
            run._currentShopDeleteDishCount = System.Math.Max(0, data.CurrentShopDeleteDishCount);
            run._currentShopFragmentPackPurchaseCount =
                System.Math.Max(0, data.CurrentShopFragmentPackPurchaseCount);

            if (data.RunSettledCounts != null)
            {
                foreach (KeyValuePair<string, int> kv in data.RunSettledCounts)
                {
                    run._runSettledCounts[kv.Key] = kv.Value;
                }
            }

            if (data.SettledFoodBattleKeys != null)
            {
                foreach (string battleKey in data.SettledFoodBattleKeys)
                {
                    if (!string.IsNullOrEmpty(battleKey))
                    {
                        run._settledFoodBattleKeys.Add(battleKey);
                    }
                }
            }

            run.CurrentTimelineId = data.CurrentTimelineId ?? string.Empty;
            run.CurrentTimelineWeekIndex = data.CurrentTimelineWeekIndex;
            run.TimelineLengthDays = data.TimelineLengthDays;
            run.CurrentDay = data.CurrentDay;
            run.RestoreActionStepIndex(data.ActionStepIndex);
            run.RestoreRunActionStepIndex(data.RunActionStepIndex);
            run.RequiredScoreOverride = data.RequiredScoreOverride;
            run.RestoreActionRandomState(data.ActionRandomState);
            if (!string.IsNullOrEmpty(data.LastActionId))
            {
                cfg.GameAction lastAction = tables.TbAction.GetOrDefault(data.LastActionId);
                if (lastAction != null)
                {
                    float costDays = data.LastActionCostDays > 0f ? data.LastActionCostDays : lastAction.MinCostDays;
                    run.SetLastActionContext(new ActionExecutionContext(
                        lastAction,
                        data.LastActionStepIndex,
                        data.LastRunActionStepIndex,
                        data.LastActionGroupId,
                        costDays)
                    {
                        SourceKey = data.LastActionSourceKey ?? string.Empty,
                        TargetScoreDayOverride = data.LastActionHasTargetScoreDayOverride
                            ? (float?)data.LastActionTargetScoreDayOverride
                            : null,
                        HalfDayBuffApplied = data.LastActionHalfDayBuffApplied,
                        IsExtraTimelineExecution = data.LastActionIsExtraTimelineExecution,
                        TimelineStopChance = data.LastActionTimelineStopChance,
                        NodeRepeatIndex = System.Math.Max(1, data.LastActionNodeRepeatIndex),
                        NodeRepeatTotal = System.Math.Max(1, data.LastActionNodeRepeatTotal),
                    });
                }
            }

            run._pendingActionExecution = ClonePendingActionExecution(data.PendingActionExecution);

            if (data.TriggeredNodeIds != null)
            {
                run._triggeredNodeIds.AddRange(data.TriggeredNodeIds);
            }

            if (data.RuntimeTimelineNodes != null)
            {
                foreach (RuntimeTimelineNodeSaveData n in data.RuntimeTimelineNodes)
                {
                    if (n == null || string.IsNullOrEmpty(n.Id) || string.IsNullOrEmpty(n.ActionId))
                    {
                        continue;
                    }

                    run._runtimeTimelineNodes.Add(new RuntimeTimelineNode(
                        n.Id,
                        n.TimelineId,
                        n.Day,
                        n.ActionId,
                        n.SourceItemId,
                        n.WeekEndAnchored));
                }

                run.SortRuntimeTimelineNodes();
            }

            run._runtimeTimelineNodeSerial = System.Math.Max(
                System.Math.Max(0, data.RuntimeTimelineNodeSerial),
                HighestDynamicTimelineNodeSerial(run._runtimeTimelineNodes, run.WeekIndex));

            float minimumTimelineLength = 7f;
            cfg.Timeline savedTimeline = tables.TbTimeline.GetOrDefault(run.CurrentTimelineId);
            if (savedTimeline != null && savedTimeline.BaseLengthDays > 0)
            {
                minimumTimelineLength = savedTimeline.BaseLengthDays;
            }

            foreach (RuntimeTimelineNode node in run._runtimeTimelineNodes)
            {
                minimumTimelineLength = System.Math.Max(minimumTimelineLength, node.Day);
            }

            run.TimelineLengthDays = GourmetProject.Game.Meta.TimelineMath.Quantize(
                System.Math.Max(run.TimelineLengthDays, minimumTimelineLength));

            if (data.UsedEventIds != null)
            {
                run._usedEventIds.AddRange(data.UsedEventIds);
            }

            if (data.CompletedBossIds != null)
            {
                run._completedBossIds.AddRange(data.CompletedBossIds);
            }

            if (data.RolledBossDebuffIds != null)
            {
                run._rolledBossDebuffIds.AddRange(data.RolledBossDebuffIds);
            }

            if (data.LockedBossDebuffIdsByNode != null)
            {
                foreach (KeyValuePair<string, string> pair in data.LockedBossDebuffIdsByNode)
                {
                    if (!string.IsNullOrEmpty(pair.Key)
                        && !string.IsNullOrEmpty(pair.Value)
                        && run.ContainsRuntimeTimelineNode(pair.Key))
                    {
                        run._lockedBossDebuffIdsByNode[pair.Key] = pair.Value;
                    }
                }
            }

            run._bossDebuffRerollWeekIndex = data.BossDebuffRerollWeekIndex;
            run._bossDebuffRerollIndex = data.BossDebuffRerollIndex;
            run._bossDebuffRerollNodeId = data.BossDebuffRerollNodeId ?? string.Empty;
            run._bossPassiveArchetypePityWeekIndex = data.BossPassiveArchetypePityWeekIndex;
            run._bossPassiveArchetypeRewardCount = System.Math.Max(0, data.BossPassiveArchetypeRewardCount);
            run._bossPassiveArchetypePityArmed = data.BossPassiveArchetypePityArmed;
            run._bossDebuffRerollExcludedId = data.BossDebuffRerollExcludedId ?? string.Empty;
            if (data.PendingExtraTimelineNodeIds != null)
            {
                foreach (string nodeId in data.PendingExtraTimelineNodeIds)
                {
                    if (!string.IsNullOrEmpty(nodeId))
                    {
                        run._pendingExtraTimelineNodeIds.Add(nodeId);
                    }
                }
            }
            run._forcedBossDebuffWeekIndex = data.ForcedBossDebuffWeekIndex;
            run._forcedBossDebuffId = data.ForcedBossDebuffId ?? string.Empty;

            run._pendingActionChoiceKey = data.PendingActionChoiceKey ?? string.Empty;
            run._pendingActionChoiceRevision = System.Math.Max(0, data.PendingActionChoiceRevision);
            if (data.PendingActionChoices != null)
            {
                run._pendingActionChoices.AddRange(data.PendingActionChoices);
            }

            run._pendingShopKey = data.PendingShopKey ?? string.Empty;
            if (data.PendingShopStock != null)
            {
                run._pendingShopStock.AddRange(data.PendingShopStock);
            }

            run._pendingRewardKey = data.PendingRewardKey ?? string.Empty;
            run._pendingRewardOffer = data.PendingRewardOffer;
            run._pendingRewardBattleView = ClonePendingRewardBattleView(data.PendingRewardBattleView);
            run._pendingGenericRewardsConfirmBattleAfterDone = data.PendingGenericRewardsConfirmBattleAfterDone;
            run._pendingGenericRewardContinuation =
                data.PendingGenericRewardContinuation != PendingGenericRewardContinuationKind.None
                    ? data.PendingGenericRewardContinuation
                    : (data.PendingGenericRewardsConfirmBattleAfterDone
                        ? PendingGenericRewardContinuationKind.Battle
                        : PendingGenericRewardContinuationKind.None);
            if (data.PendingGenericRewards != null)
            {
                run._pendingGenericRewards.AddRange(CloneGenericRewardSaveData(data.PendingGenericRewards));
            }

            return run;
        }

        private static RewardOfferSaveData ToSaveData(RewardOffer offer)
        {
            if (offer == null)
            {
                return null;
            }

            return new RewardOfferSaveData
            {
                BaseGold = offer.BaseGold,
                RawBaseGold = offer.RawBaseGold,
                RawBonusGold = offer.RawBonusGold,
                BonusGold = offer.BonusGold,
                DoubleRewardTarget = offer.DoubleRewardTarget,
                GoldAmountsResolved = offer.GoldAmountsResolved,
                BaseGoldClaimed = offer.BaseGoldClaimed,
                BonusGoldClaimed = offer.BonusGoldClaimed,
                MainChoiceIndex = offer.MainChoiceIndex,
                ExtraChoiceIndex = offer.ExtraChoiceIndex,
                BonusChoiceIndex = offer.BonusChoiceIndex,
                MainChoiceSkipped = offer.MainChoiceSkipped,
                ExtraChoiceSkipped = offer.ExtraChoiceSkipped,
                BonusChoiceSkipped = offer.BonusChoiceSkipped,
                MainRequiredChoiceCount = offer.MainRequiredChoiceCount,
                ExtraRequiredChoiceCount = offer.ExtraRequiredChoiceCount,
                BonusRequiredChoiceCount = offer.BonusRequiredChoiceCount,
                MainChoiceIndices = new List<int>(offer.MainChoiceIndices),
                ExtraChoiceIndices = new List<int>(offer.ExtraChoiceIndices),
                BonusChoiceIndices = new List<int>(offer.BonusChoiceIndices),
                MainChoices = ToSaveData(offer.MainChoices),
                ExtraChoices = ToSaveData(offer.ExtraChoices),
                BonusChoices = ToSaveData(offer.BonusChoices),
                FixedGroups = ToSaveData(offer.FixedGroups),
                SpecificGroup = ToSaveData(offer.SpecificGroup),
            };
        }

        private static List<RewardChoiceGroupSaveData> ToSaveData(IReadOnlyList<RewardChoiceGroup> groups)
        {
            var result = new List<RewardChoiceGroupSaveData>();
            if (groups == null)
            {
                return result;
            }

            for (int i = 0; i < groups.Count; i++)
            {
                RewardChoiceGroup group = groups[i];
                if (group != null)
                {
                    result.Add(ToSaveData(group));
                }
            }

            return result;
        }

        private static RewardChoiceGroupSaveData ToSaveData(RewardChoiceGroup group)
        {
            return group == null
                ? null
                : new RewardChoiceGroupSaveData
                {
                    Title = group.Title,
                    Description = group.Description,
                    RuleText = group.RuleText,
                    SourceSlotId = group.SourceSlotId,
                    RequiredChoiceCount = group.RequiredChoiceCount,
                    Skipped = group.Skipped,
                    ClaimedIndices = new List<int>(group.ClaimedIndices),
                    Choices = ToSaveData(group.Choices),
                };
        }

        private static List<RewardChoiceSaveData> ToSaveData(IReadOnlyList<RewardChoice> choices)
        {
            var result = new List<RewardChoiceSaveData>();
            if (choices == null)
            {
                return result;
            }

            foreach (RewardChoice choice in choices)
            {
                if (choice == null)
                {
                    continue;
                }

                result.Add(new RewardChoiceSaveData
                {
                    Kind = choice.Kind,
                    Id = choice.Id,
                    Name = choice.Name,
                    Description = choice.Description,
                    GoldAmount = choice.GoldAmount,
                    IsFallbackGold = choice.IsFallbackGold,
                    FragmentRotation = choice.FragmentRotation,
                });
            }

            return result;
        }

        private static RewardOffer FromSaveData(RewardOfferSaveData data)
        {
            if (data == null)
            {
                return null;
            }

            if (data.FixedGroups != null && data.FixedGroups.Count > 0)
            {
                return new RewardOffer(
                    data.BaseGold,
                    FromGroupSaveData(data.FixedGroups),
                    FromGroupSaveData(data.SpecificGroup),
                    data.BaseGoldClaimed,
                    data.DoubleRewardTarget,
                    ResolveRawBaseGold(data),
                    data.RawBonusGold,
                    data.BonusGold,
                    data.BonusGoldClaimed,
                    data.GoldAmountsResolved);
            }

            return new RewardOffer(
                    data.BaseGold,
                    FromSaveData(data.MainChoices),
                    FromSaveData(data.ExtraChoices),
                    FromSaveData(data.BonusChoices),
                    data.BaseGoldClaimed,
                    data.MainChoiceIndex,
                    data.ExtraChoiceIndex,
                    data.MainChoiceSkipped,
                    data.ExtraChoiceSkipped,
                    data.MainRequiredChoiceCount,
                    data.ExtraRequiredChoiceCount,
                    data.BonusChoiceIndex,
                    data.BonusChoiceSkipped,
                    data.BonusRequiredChoiceCount,
                    data.MainChoiceIndices,
                    data.ExtraChoiceIndices,
                    data.BonusChoiceIndices);
        }

        private static int ResolveRawBaseGold(RewardOfferSaveData data)
        {
            if (data == null)
            {
                return 0;
            }

            // 旧存档只保存 BaseGold；新存档在生成奖励时同时保存倍率前原值。
            return data.GoldAmountsResolved || data.RawBaseGold > 0 || data.BaseGold == 0
                ? data.RawBaseGold
                : data.BaseGold;
        }

        private static List<RewardChoiceGroup> FromGroupSaveData(List<RewardChoiceGroupSaveData> groups)
        {
            var result = new List<RewardChoiceGroup>();
            if (groups == null)
            {
                return result;
            }

            for (int i = 0; i < groups.Count; i++)
            {
                RewardChoiceGroup group = FromGroupSaveData(groups[i]);
                if (group != null)
                {
                    result.Add(group);
                }
            }

            return result;
        }

        private static RewardChoiceGroup FromGroupSaveData(RewardChoiceGroupSaveData group)
        {
            return group == null
                ? new RewardChoiceGroup("特定奖励", null, 0)
                : new RewardChoiceGroup(
                    group.Title,
                    FromSaveData(group.Choices),
                    group.RequiredChoiceCount,
                    group.ClaimedIndices,
                    group.Skipped,
                    group.Description,
                    group.RuleText,
                    group.SourceSlotId);
        }

        private static List<RewardChoice> FromSaveData(List<RewardChoiceSaveData> choices)
        {
            var result = new List<RewardChoice>();
            if (choices == null)
            {
                return result;
            }

            foreach (RewardChoiceSaveData choice in choices)
            {
                if (choice == null)
                {
                    continue;
                }

                result.Add(new RewardChoice(
                    choice.Kind,
                    choice.Id,
                    choice.Name,
                    choice.Description,
                    choice.GoldAmount,
                    choice.IsFallbackGold,
                    fragmentRotation: choice.FragmentRotation));
            }

            return result;
        }

        private static List<GenericRewardSaveData> CloneGenericRewardSaveData(IReadOnlyList<GenericRewardSaveData> rewards)
        {
            var result = new List<GenericRewardSaveData>();
            if (rewards == null)
            {
                return result;
            }

            foreach (GenericRewardSaveData reward in rewards)
            {
                if (reward?.Offer == null)
                {
                    continue;
                }

                result.Add(new GenericRewardSaveData
                {
                    Key = reward.Key ?? string.Empty,
                    Title = reward.Title ?? string.Empty,
                    Offer = ToSaveData(FromSaveData(reward.Offer)),
                });
            }

            return result;
        }

        private static PendingRewardBattleViewSaveData ClonePendingRewardBattleView(PendingRewardBattleViewSaveData data)
        {
            if (data == null)
            {
                return null;
            }

            var result = new PendingRewardBattleViewSaveData
            {
                RequiredScore = data.RequiredScore,
                RawRequiredScore = data.RawRequiredScore,
                Modifier = data.Modifier ?? string.Empty,
                BossDebuffId = data.BossDebuffId ?? string.Empty,
                BattleKey = data.BattleKey ?? string.Empty,
                IsBoss = data.IsBoss,
                LastTotal = data.LastTotal,
                LastTotalBig = data.LastTotalBig?.Clone(),
                FinalHappyCakeLayers = data.FinalHappyCakeLayers,
                HasDetailedScore = data.HasDetailedScore,
                RawSum = data.RawSum,
                RawSumBig = data.RawSumBig?.Clone(),
                FinalFlat = data.FinalFlat,
                FinalFlatBig = data.FinalFlatBig?.Clone(),
                FinalMultiplier = data.FinalMultiplier,
                FinalMultiplierBig = data.FinalMultiplierBig?.Clone(),
                Dishes = new List<PendingRewardBattleDishSaveData>(),
                Cakes = new List<PendingRewardCakeVisualSaveData>(),
            };

            if (data.Cakes != null)
            {
                foreach (PendingRewardCakeVisualSaveData cake in data.Cakes)
                {
                    if (cake == null)
                    {
                        continue;
                    }

                    result.Cakes.Add(new PendingRewardCakeVisualSaveData
                    {
                        ViewportX = cake.ViewportX,
                        ViewportY = cake.ViewportY,
                        RotationZ = cake.RotationZ,
                        ScaleX = cake.ScaleX,
                        ScaleY = cake.ScaleY,
                        ScaleZ = cake.ScaleZ,
                    });
                }
            }

            if (data.Dishes == null)
            {
                return result;
            }

            foreach (PendingRewardBattleDishSaveData dish in data.Dishes)
            {
                if (dish == null || string.IsNullOrEmpty(dish.DishId))
                {
                    continue;
                }

                result.Dishes.Add(new PendingRewardBattleDishSaveData
                {
                    Id = dish.Id,
                    DishId = ContentIdAliases.NormalizeDishId(dish.DishId),
                    OriginX = dish.OriginX,
                    OriginY = dish.OriginY,
                    Rotation = dish.Rotation,
                    SourceSlotIndex = dish.SourceSlotIndex,
                    SourceDishIndex = dish.SourceDishIndex,
                    SkillIds = dish.SkillIds != null ? new List<string>(dish.SkillIds) : new List<string>(),
                    FlavorIds = ContentIdAliases.NormalizeFlavorIds(dish.FlavorIds),
                    RuntimeCountAsBonus = dish.RuntimeCountAsBonus,
                    PermanentFlatBonus = dish.PermanentFlatBonus,
                    PermanentFlatBonusBig = dish.PermanentFlatBonusBig?.Clone(),
                    PermanentMultBonus = dish.PermanentMultBonus,
                    PermanentMultBonusBig = dish.PermanentMultBonusBig?.Clone(),
                    TemporaryBaseMultiplier = dish.TemporaryBaseMultiplier,
                    TemporaryBaseMultiplierBig = dish.TemporaryBaseMultiplierBig?.Clone(),
                    ServeMultiplier = dish.ServeMultiplier,
                    ServeMultiplierBig = dish.ServeMultiplierBig?.Clone(),
                    ServeMultiplierFlatBonus = dish.ServeMultiplierFlatBonus,
                    ServeMultiplierFlatBonusBig = dish.ServeMultiplierFlatBonusBig?.Clone(),
                    SkillsDisabled = dish.SkillsDisabled,
                    ExcludedFromScore = dish.ExcludedFromScore,
                    IsTemporary = dish.IsTemporary,
                    HasDishScore = dish.HasDishScore,
                    ScoreBaseValue = dish.ScoreBaseValue,
                    ScoreBaseValueBig = dish.ScoreBaseValueBig?.Clone(),
                    ScoreFlatBonus = dish.ScoreFlatBonus,
                    ScoreFlatBonusBig = dish.ScoreFlatBonusBig?.Clone(),
                    ScoreMultiplier = dish.ScoreMultiplier,
                    ScoreMultiplierBig = dish.ScoreMultiplierBig?.Clone(),
                    ScoreEffectiveCountAs = dish.ScoreEffectiveCountAs,
                    ScoreExtraSettlementContribution = dish.ScoreExtraSettlementContribution,
                    ScoreExtraSettlementContributionBig = dish.ScoreExtraSettlementContributionBig?.Clone(),
                    ScoreExtraSettlementCount = dish.ScoreExtraSettlementCount,
                });
            }

            return result;
        }

        private static PendingHeartBreakSaveData ClonePendingHeartBreak(PendingHeartBreakSaveData data)
        {
            if (data == null)
            {
                return null;
            }

            return new PendingHeartBreakSaveData
            {
                BeforeHeartCount = data.BeforeHeartCount,
                AfterHeartCount = data.AfterHeartCount,
                BattleTotal = data.BattleTotal,
                BattleTotalBig = data.BattleTotalBig?.Clone(),
                IsTerminal = data.IsTerminal,
            };
        }

        private static PendingActionExecutionSaveData ClonePendingActionExecution(PendingActionExecutionSaveData data)
        {
            if (data == null || string.IsNullOrEmpty(data.ActionId))
            {
                return null;
            }

            return new PendingActionExecutionSaveData
            {
                ActionId = data.ActionId ?? string.Empty,
                StepIndex = data.StepIndex,
                RunStepIndex = data.RunStepIndex,
                ActionGroupId = data.ActionGroupId ?? string.Empty,
                CostDays = data.CostDays,
                SourceKey = data.SourceKey ?? string.Empty,
                HasTargetScoreDayOverride = data.HasTargetScoreDayOverride,
                TargetScoreDayOverride = data.TargetScoreDayOverride,
                HalfDayBuffApplied = data.HalfDayBuffApplied,
                IsExtraTimelineExecution = data.IsExtraTimelineExecution,
                TimelineStopChance = data.TimelineStopChance,
                NodeRepeatIndex = System.Math.Max(1, data.NodeRepeatIndex),
                NodeRepeatTotal = System.Math.Max(1, data.NodeRepeatTotal),
                OutcomeKind = data.OutcomeKind,
                Feedback = data.Feedback ?? string.Empty,
                RequiredScore = data.RequiredScore,
                Modifier = data.Modifier ?? string.Empty,
                BattleKey = data.BattleKey ?? string.Empty,
                IsBoss = data.IsBoss,
                BossId = data.BossId ?? string.Empty,
                BossDebuffId = data.BossDebuffId ?? string.Empty,
                EventId = data.EventId ?? string.Empty,
                EventEntryGoldGranted = data.EventEntryGoldGranted,
                SlotEventId = data.SlotEventId ?? string.Empty,
                SlotSpinsUsed = System.Math.Max(0, data.SlotSpinsUsed),
                SlotStage = data.SlotStage,
                SlotRewardKey = data.SlotRewardKey ?? string.Empty,
            };
        }

        /// <summary>本局需要完成的总周数，由 game_base 统一配置。</summary>
        public int TotalWeeks => _tables.TbGameBase.TotalWeeks;

        public bool HasNextWeek => WeekIndex < TotalWeeks;

        /// <summary>
        /// 构建一局经营挑战经营挑战。<paramref name="requiredScore"/> 为目标美味值，
        /// <paramref name="bossDebuffId"/> 选择 Boss Debuff 模型，
        /// <paramref name="key"/> 用于派生确定性随机流（同一周内不同天/不同经营挑战需用不同 key 才能各自独立复现）。
        /// </summary>
        public BattleSession BuildBattleSession(
            int requiredScore,
            string modifier,
            string key,
            string bossDebuffId = "")
        {
            return BattleSessionFactory.Build(this, requiredScore, modifier, key, bossDebuffId);
        }

        public GpTable BuildTablePreviewFromFragments(string modifier = "", string bossDebuffId = "")
        {
            return BattleSessionFactory.BuildTablePreview(this, modifier, bossDebuffId);
        }

        /// <summary>
        /// 给食谱中的1 个食物永久添加风味。自带风味与后续风味共用总上限；
        /// 达到上限时淘汰最早获得的风味。
        /// </summary>
        public bool AddRecipeFlavor(int dishIndex, string flavorId)
        {
            if (string.IsNullOrEmpty(flavorId) || dishIndex < 0 || dishIndex >= _recipe.Count)
            {
                return false;
            }

            RecipeBookSlot slot = _recipe[dishIndex];
            int limit = FoodFlavorLimit;
            while (RecipeFlavorCount(slot) >= limit)
            {
                DishDef dish = Database.GetDish(slot.DishId);
                if (dish != null && dish.HasFlavor)
                {
                    slot.ReplaceDishId(dish.BaseId);
                }
                else if (!slot.RemoveOldestFlavor())
                {
                    break;
                }
            }

            slot.AddFlavor(flavorId);
            return true;
        }

        public bool RemoveRecipeFlavor(int dishIndex, string flavorId)
        {
            if (dishIndex < 0 || dishIndex >= _recipe.Count)
            {
                return false;
            }

            RecipeBookSlot slot = _recipe[dishIndex];
            DishDef dish = Database.GetDish(slot.DishId);
            if (!string.IsNullOrEmpty(flavorId)
                && dish != null
                && string.Equals(dish.FlavorId, flavorId, System.StringComparison.Ordinal))
            {
                slot.ReplaceDishId(dish.BaseId);
                return true;
            }

            if (slot.RemoveFlavor(flavorId))
            {
                return true;
            }

            if (string.IsNullOrEmpty(flavorId) && dish != null && dish.HasFlavor)
            {
                slot.ReplaceDishId(dish.BaseId);
                return true;
            }

            return false;
        }

        public bool ReplaceRecipeFlavor(int dishIndex, string toFlavorId)
        {
            if (string.IsNullOrEmpty(toFlavorId))
            {
                return false;
            }

            if (dishIndex < 0 || dishIndex >= _recipe.Count)
            {
                return false;
            }

            RecipeBookSlot slot = _recipe[dishIndex];
            if (slot.HasExtraFlavors)
            {
                return slot.ReplaceFlavor(toFlavorId);
            }

            DishDef dish = Database.GetDish(slot.DishId);
            if (dish != null && dish.HasFlavor)
            {
                slot.ReplaceDishId(dish.BaseId);
            }

            slot.AddFlavor(toFlavorId);
            return true;
        }

        public IReadOnlyList<string> GetRecipeFlavorIds(int dishIndex)
        {
            if (dishIndex < 0 || dishIndex >= _recipe.Count)
            {
                return System.Array.Empty<string>();
            }

            RecipeBookSlot slot = _recipe[dishIndex];
            DishDef dish = Database.GetDish(slot.DishId);
            var result = new List<string>(slot.ExtraFlavorIds.Count + 1);
            if (dish != null && dish.HasFlavor)
            {
                result.Add(dish.FlavorId);
            }

            result.AddRange(slot.ExtraFlavorIds);
            return result;
        }

        private int RecipeFlavorCount(RecipeBookSlot slot)
        {
            if (slot == null)
            {
                return 0;
            }

            DishDef dish = Database.GetDish(slot.DishId);
            return slot.ExtraFlavorIds.Count + (dish != null && dish.HasFlavor ? 1 : 0);
        }

        public bool AddRecipeExtraSkill(int dishIndex, string skillId)
        {
            RecipeBookSlot slot = GetRecipeSlot(dishIndex);
            if (slot == null || string.IsNullOrEmpty(skillId))
            {
                return false;
            }

            slot.AddExtraSkill(skillId);
            return true;
        }

        public bool MultiplyRecipeScore(int dishIndex, BigDouble multiplier)
        {
            RecipeBookSlot slot = GetRecipeSlot(dishIndex);
            if (slot == null || multiplier <= BigDouble.Zero)
            {
                return false;
            }

            slot.MultiplyScore(multiplier);
            return true;
        }

        public bool AddRecipeScoreFlat(int dishIndex, BigDouble amount)
        {
            RecipeBookSlot slot = GetRecipeSlot(dishIndex);
            if (slot == null || BigDouble.Abs(amount) < 0.0001d)
            {
                return false;
            }

            slot.AddScoreFlat(amount);
            return true;
        }

        private RecipeBookSlot GetRecipeSlot(int dishIndex)
        {
            return dishIndex >= 0 && dishIndex < _recipe.Count ? _recipe[dishIndex] : null;
        }

        private static IReadOnlyList<string> ProjectDishIds(List<RecipeBookSlot> book)
        {
            var ids = new List<string>(book.Count);
            foreach (RecipeBookSlot slot in book)
            {
                ids.Add(slot.DishId);
            }

            return ids;
        }

        public bool MoveBonusDish(int dishIndex, int toDishIndex)
        {
            if (dishIndex < 0 || dishIndex >= _recipe.Count)
            {
                return false;
            }

            RecipeBookSlot slot = _recipe[dishIndex];
            _recipe.RemoveAt(dishIndex);
            toDishIndex = ClampIndex(toDishIndex, _recipe.Count);
            _recipe.Insert(toDishIndex, slot);

            RebuildBonusDishCache();
            return true;
        }

        private static int ClampIndex(int index, int maxInclusive)
        {
            if (index < 0)
            {
                return 0;
            }

            return index > maxInclusive ? maxInclusive : index;
        }

        public bool RemoveBonusDishAt(int dishIndex)
        {
            if (dishIndex < 0 || dishIndex >= _recipe.Count)
            {
                return false;
            }

            _recipe.RemoveAt(dishIndex);
            RebuildBonusDishCache();
            return true;
        }

        /// <summary>经营挑战行动目标美味值：隐藏分曲线结果 × 倍率（倍率 &lt;= 0 视为 1）。</summary>
        public int ComputeFoodRequiredScore(float multiplier)
        {
            // 「分数变1」（RequiredScoreToOne）：非星级评鉴食物剩余生效局数内，要求分固定为 1（计数消耗在每局奖励结算时）。
            if (_scoreToOneRemaining > 0)
            {
                return 1;
            }

            if (multiplier <= 0f)
            {
                multiplier = 1f;
            }

            int baseReq = RequiredScore;
            int scaled = System.Math.Max(1, (int)System.Math.Round(baseReq * multiplier, System.MidpointRounding.AwayFromZero));
            // 火热营业倍率 > 1 视为 Super 档，否则普通档；装饰品和消耗品目标美味值修正随档位施加。
            cfg.FoodActionKind tier = multiplier > 1f ? cfg.FoodActionKind.Super : cfg.FoodActionKind.Normal;
            return new ItemRuntime(this).ModifyRequiredScore(scaled, tier);
        }

        public RunItemState GetItemState(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return null;
            }

            foreach (RunItemState state in _items)
            {
                if (state.ItemId == itemId)
                {
                    return state;
                }
            }

            return null;
        }

        public bool HasItem(string itemId)
        {
            return GetItemCount(itemId) > 0;
        }

        public bool HasRecipeDish(bool requireFlavor)
        {
            return GetRecipeDishCount(requireFlavor) > 0;
        }

        public int GetRecipeDishCount(bool requireFlavor)
        {
            int count = 0;
            for (int dishIndex = 0; dishIndex < _recipe.Count; dishIndex++)
            {
                if (!requireFlavor || RecipeSlotHasFlavor(_recipe[dishIndex]))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>当前持有该装饰品和消耗品的份数：装饰品为 0/1，消耗品为实例条目数。</summary>
        public int GetItemCount(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return 0;
            }

            int count = 0;
            foreach (RunItemState state in _items)
            {
                if (state.ItemId == itemId)
                {
                    count++;
                }
            }

            return count;
        }

        private bool RecipeSlotHasFlavor(RecipeBookSlot slot)
        {
            if (slot == null)
            {
                return false;
            }

            DishDef dish = Database?.GetDish(slot.DishId);
            return (dish != null && dish.HasFlavor) || slot.ExtraFlavorIds.Count > 0;
        }

        /// <summary>
        /// 获得一件装饰品和消耗品。<paramref name="fireOnAcquire"/> 为 true 时，装饰品首次加入成功后会立即结算
        /// 其「获得时(OnAcquire)」一次性效果；存档恢复 / 经营方向初始装饰品和消耗品应传 false，避免重复触发。
        /// </summary>
        public ItemAcquireResult AcquireItem(string itemId, int fallbackGold, bool fireOnAcquire = true)
        {
            ItemDefinition item = ItemDefinition.Get(_tables, itemId);
            if (item == null)
            {
                return default;
            }

            if (item.Kind == cfg.ItemKind.Passive)
            {
                RunItemState state = GetItemState(itemId);
                if (state == null)
                {
                    state = new RunItemState(itemId, 1);
                    _items.Add(state);
                    GourmetProject.Game.Meta.Passives.PassiveItemModel model = BindPassiveModel(state, item);
                    if (fireOnAcquire)
                    {
                        model.OnAcquired();
                        NotifyItemAcquired(item);
                    }

                    return new ItemAcquireResult(ItemAcquireOutcome.Added, itemId, item.Name, 1, 1, 0);
                }

                Gold += fallbackGold;
                return new ItemAcquireResult(ItemAcquireOutcome.ConvertedToGold, itemId, item.Name, 1, 1, fallbackGold);
            }

            // 消耗品：每份占一个全局消耗槽；槽满则折算金币（不再有 per-item 囤积上限）。
            if (!HasFreeActiveSlot || !ItemPoolService.CanEnterPool(this, item))
            {
                Gold += fallbackGold;
                return new ItemAcquireResult(ItemAcquireOutcome.ConvertedToGold, itemId, item.Name, 1, GetItemCount(itemId), fallbackGold);
            }

            _items.Add(new RunItemState(itemId, 1));
            int held = GetItemCount(itemId);
            if (fireOnAcquire)
            {
                NotifyItemAcquired(item);
            }
            return new ItemAcquireResult(ItemAcquireOutcome.Stacked, itemId, item.Name, 1, held, 0);
        }

        public bool AddBonusDish(string dishId)
        {
            if (Database.GetDish(dishId) == null)
            {
                return false;
            }

            _recipe.Add(new RecipeBookSlot(dishId));
            if (Database.GetDish(dishId).HasFlavor)
                NotifyFlavorAcquired(dishId, Database.GetDish(dishId).FlavorId);
            RebuildBonusDishCache();
            return true;
        }

        /// <summary>
        /// 完整复制食谱中的一格食物并追加到食谱末尾。
        /// 后续风味、额外技能及永久分数修正都会随格子一并复制。
        /// </summary>
        public int CloneRecipeEntry(int dishIndex)
        {
            if (dishIndex < 0 || dishIndex >= _recipe.Count)
            {
                return -1;
            }

            _recipe.Add(_recipe[dishIndex].Clone());
            RebuildBonusDishCache();
            return _recipe.Count - 1;
        }

        /// <summary>替换食谱格的食物定义，同时保留该格后天风味、额外技能与永久分数修正。</summary>
        public bool ReplaceRecipeDishAt(int dishIndex, string dishId)
        {
            if (dishIndex < 0 || dishIndex >= _recipe.Count || Database.GetDish(dishId) == null)
            {
                return false;
            }

            _recipe[dishIndex].ReplaceDishId(dishId);
            RebuildBonusDishCache();
            return true;
        }

        public bool AddBonusDish(string dishId, string flavorId)
        {
            if (Database.GetDish(dishId) == null)
            {
                return false;
            }

            var slot = new RecipeBookSlot(dishId);
            _recipe.Add(slot);
            if (!string.IsNullOrEmpty(flavorId))
            {
                AddRecipeFlavor(_recipe.Count - 1, flavorId);
                NotifyFlavorAcquired(dishId, flavorId);
            }

            RebuildBonusDishCache();
            return true;
        }

        public bool AddBonusDishWithFlavor(string dishId, string flavorId)
        {
            if (Database.GetDish(dishId) == null || string.IsNullOrEmpty(flavorId))
            {
                return false;
            }

            var slot = new RecipeBookSlot(dishId);
            _recipe.Add(slot);
            AddRecipeFlavor(_recipe.Count - 1, flavorId);
            NotifyFlavorAcquired(dishId, flavorId);
            RebuildBonusDishCache();
            return true;
        }

        /// <summary>从食谱奖励池移除1 个食物（商店删菜）。</summary>
        public bool RemoveBonusDish(string dishId)
        {
            int idx = _recipe.FindIndex(s => s.DishId == dishId);
            if (idx >= 0)
            {
                _recipe.RemoveAt(idx);
                RebuildBonusDishCache();
                return true;
            }

            return false;
        }

        public bool AddTableFragment(string fragmentId)
        {
            TableFragmentDef fragment = Database.GetFragment(fragmentId);
            if (fragment == null || _stomachFragmentIds.Contains(fragmentId))
            {
                return false;
            }

            _stomachFragmentIds.Add(fragmentId);
            return true;
        }

        /// <summary>记录一次玩家手动拼贴的碎片放置（餐桌编辑页调用；合法性由调用方在放置前校验）。</summary>
        public bool AddFragmentPlacement(string fragmentId, int rotation, GridPos origin)
        {
            if (Database.GetFragment(fragmentId) == null)
            {
                return false;
            }

            _fragmentPlacements.Add(new TableFragmentPlacement(fragmentId, rotation, origin));
            return true;
        }

        private void NotifyItemAcquired(ItemDefinition item)
        {
            if (item == null) return;
            NotifyContentAcquired(new RunContentAcquisition
            {
                Kind = RunContentAcquisitionKind.Item,
                ItemId = item.Id,
                ItemKind = item.Kind,
                ActiveItemCategory = item.ActiveItemCategory,
                ItemEffectType = item.EffectType,
            });
        }

        private void NotifyFlavorAcquired(string dishId, string flavorId)
        {
            NotifyContentAcquired(new RunContentAcquisition
            {
                Kind = RunContentAcquisitionKind.DishFlavor,
                DishId = dishId ?? string.Empty,
                FlavorId = flavorId ?? string.Empty,
            });
        }

        private void NotifyContentAcquired(RunContentAcquisition acquisition) => ContentAcquired?.Invoke(acquisition);

        /// <summary>
        /// 置入一份待拼贴碎片包。未提供朝向时，从该碎片当前可拼上的顺时针朝向中均匀抽取。
        /// </summary>
        public void SetPendingFragmentPack(
            IEnumerable<string> fragmentIds,
            IRandomStream rotationRng = null)
        {
            SetPendingFragmentPackInternal(fragmentIds, null, rotationRng);
        }

        /// <summary>置入方向已在候选抽取阶段确定的待拼贴碎片包。</summary>
        public void SetPendingFragmentPack(
            IEnumerable<string> fragmentIds,
            IReadOnlyList<int> rotations)
        {
            SetPendingFragmentPackInternal(fragmentIds, rotations, null);
        }

        private void SetPendingFragmentPackInternal(
            IEnumerable<string> fragmentIds,
            IReadOnlyList<int> rotations,
            IRandomStream rotationRng)
        {
            _pendingFragmentPack.Clear();
            _pendingFragmentPackRotations.Clear();
            if (fragmentIds != null)
            {
                IRandomStream directionRng = rotationRng ?? FragmentRotationRollStream();
                int index = 0;
                foreach (string id in fragmentIds)
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        _pendingFragmentPack.Add(id);
                        if (rotations != null && index < rotations.Count)
                        {
                            int rotation = rotations[index];
                            _pendingFragmentPackRotations.Add(((rotation % 4) + 4) % 4);
                        }
                        else
                        {
                            TableFragmentDef fragment = Database.GetFragment(id);
                            _pendingFragmentPackRotations.Add(
                                RollAttachableFragmentRotation(fragment, directionRng));
                        }
                    }

                    index++;
                }
            }
        }

        /// <summary>清空待拼贴的碎片包（拼贴完成或跳过后调用）。</summary>
        public void ClearPendingFragmentPack()
        {
            _pendingFragmentPack.Clear();
            _pendingFragmentPackRotations.Clear();
        }

        private IRandomStream FragmentRotationRollStream()
        {
            return Random != null && Random.IsInitialized
                ? Random.DomainStream(SeedDomains.Reward, "fragment_rotation_rolls")
                : null;
        }

        /// <summary>该碎片是否存在至少一个可拼入当前餐桌的朝向（按旋转后形状判定）。</summary>
        public bool CanAttachTableFragment(TableFragmentDef fragment)
        {
            return GetAttachableFragmentRotations(fragment).Count > 0;
        }

        /// <summary>当前餐桌上该碎片可拼入的顺时针朝向（0..3）。</summary>
        public List<int> GetAttachableFragmentRotations(TableFragmentDef fragment)
        {
            if (fragment == null)
            {
                return new List<int>();
            }

            GpTable board = BattleSessionFactory.BuildTablePreview(this);
            cfg.Character character = Tables.TbCharacter.GetOrDefault(CharacterId);
            if (character == null)
            {
                return new List<int>();
            }

            return TableFragmentBuilder.CollectAttachableRotations(
                board,
                fragment,
                character.MaxDiningTableWidth,
                character.MaxDiningTableHeight);
        }

        /// <summary>从当前可拼上的朝向中均匀抽取一个顺时针旋转次数；无可拼朝向时返回 0。</summary>
        public int RollAttachableFragmentRotation(TableFragmentDef fragment, IRandomStream rng)
        {
            List<int> attachable = GetAttachableFragmentRotations(fragment);
            if (attachable.Count == 0)
            {
                return 0;
            }

            if (rng == null)
            {
                return attachable[0];
            }

            return attachable[rng.Range(0, attachable.Count)];
        }

        /// <summary>移除一份装饰品和消耗品（被动整条移除；主动移除其中一份实例）。供商店出售、事件移除等使用。</summary>
        public bool RemoveItem(string itemId)
        {
            return RemoveOneInstance(itemId);
        }

        public List<ItemAcquireResult> ReplaceItems(IEnumerable<string> itemIds)
        {
            foreach (RunItemState state in _items)
            {
                state.Model?.OnRemoved();
            }

            _items.Clear();

            var results = new List<ItemAcquireResult>();
            if (itemIds == null)
            {
                _heartsRemaining = System.Math.Min(_heartsRemaining, HeartCapacity);
                return results;
            }

            foreach (string itemId in itemIds)
            {
                if (!string.IsNullOrEmpty(itemId))
                {
                    results.Add(AcquireItem(itemId, fallbackGold: 0, fireOnAcquire: false));
                }
            }

            _heartsRemaining = System.Math.Min(_heartsRemaining, HeartCapacity);
            return results;
        }

        private bool RemoveOneInstance(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return false;
            }

            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].ItemId == itemId)
                {
                    _items[i].Model?.OnRemoved();
                    _items.RemoveAt(i);
                    _heartsRemaining = System.Math.Min(_heartsRemaining, HeartCapacity);
                    return true;
                }
            }

            return false;
        }

        private void InitializeRecipeBooksFromCharacter()
        {
            _recipe.Clear();

            cfg.Character character = _tables.TbCharacter.GetOrDefault(CharacterId);
            if (character?.InitialRecipeId == null || character.InitialRecipeId.Count == 0)
            {
                RebuildBonusDishCache();
                return;
            }

            IRandomStream recipeStream = InitialRecipeStream();
            string recipeId = character.InitialRecipeId[0];
            RecipeDef recipe = Database.GetRecipe(recipeId);
            if (recipe != null)
            {
                List<string> deck = RecipeRoller.Roll(recipe, Database, recipeStream);
                foreach (string dishId in deck)
                {
                    if (Database.GetDish(dishId) != null)
                    {
                        _recipe.Add(new RecipeBookSlot(dishId));
                    }
                }
            }
            else
            {
                Log.Warning($"Character '{CharacterId}' has no valid recipe '{recipeId}'.", "GameRun");
            }

            RebuildBonusDishCache();
        }

        private IRandomStream InitialRecipeStream()
        {
            const string keyPrefix = "initial_";
            if (Random != null && Random.IsInitialized)
            {
                return Random.DomainStream(SeedDomains.Recipe, keyPrefix + CharacterId);
            }

            var random = new RandomService();
            random.Init(SeedText);
            return random.DomainStream(SeedDomains.Recipe, keyPrefix + CharacterId);
        }

        private void RebuildBonusDishCache()
        {
            _bonusDishIds.Clear();
            foreach (RecipeBookSlot slot in _recipe)
            {
                _bonusDishIds.Add(slot.DishId);
            }
        }

        private RunRecipeBookSaveData ToRecipeSaveData()
        {
            var save = new RunRecipeBookSaveData
            {
                DishIds = new List<string>(_recipe.Count),
                DishExtraFlavors = new List<RunRecipeDishFlavorSaveData>(_recipe.Count),
            };
            foreach (RecipeBookSlot slot in _recipe)
            {
                save.DishIds.Add(slot.DishId);
                save.DishExtraFlavors.Add(new RunRecipeDishFlavorSaveData
                {
                    FlavorIds = new List<string>(slot.ExtraFlavorIds),
                    ExtraSkillIds = new List<string>(slot.ExtraSkillIds),
                    ScoreFlatBonus = BigNumberSaveData.ToLegacyFloat(slot.ScoreFlatBonus),
                    ScoreFlatBonusBig = BigNumberSaveData.From(slot.ScoreFlatBonus),
                    ScoreMultiplier = BigNumberSaveData.ToLegacyFloat(slot.ScoreMultiplier),
                    ScoreMultiplierBig = BigNumberSaveData.From(slot.ScoreMultiplier),
                });
            }

            return save;
        }

        private List<TableFragmentPlacementSaveData> ToFragmentPlacementSaveData()
        {
            var list = new List<TableFragmentPlacementSaveData>(_fragmentPlacements.Count);
            foreach (TableFragmentPlacement p in _fragmentPlacements)
            {
                list.Add(new TableFragmentPlacementSaveData
                {
                    FragmentId = p.FragmentId,
                    Rotation = p.Rotation,
                    OriginX = p.Origin.X,
                    OriginY = p.Origin.Y,
                });
            }

            return list;
        }

        private List<RuntimeTimelineNodeSaveData> ToRuntimeTimelineNodeSaveData()
        {
            var list = new List<RuntimeTimelineNodeSaveData>(_runtimeTimelineNodes.Count);
            foreach (RuntimeTimelineNode n in _runtimeTimelineNodes)
            {
                list.Add(new RuntimeTimelineNodeSaveData
                {
                    Id = n.Id,
                    TimelineId = n.TimelineId,
                    Day = n.Day,
                    ActionId = n.ActionId,
                    SourceItemId = n.SourceItemId,
                    WeekEndAnchored = n.WeekEndAnchored,
                });
            }

            return list;
        }

        private static int HighestDynamicTimelineNodeSerial(IReadOnlyList<RuntimeTimelineNode> nodes, int weekIndex)
        {
            int highest = 0;
            string prefix = $"dyn_w{weekIndex}_";
            if (nodes == null)
            {
                return highest;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                string id = nodes[i].Id;
                if (string.IsNullOrEmpty(id)
                    || !id.StartsWith(prefix, System.StringComparison.Ordinal)
                    || !int.TryParse(id.Substring(prefix.Length), out int serial))
                {
                    continue;
                }

                highest = System.Math.Max(highest, serial);
            }

            return highest;
        }

        private void RestoreRecipeBooks(RunSaveData data)
        {
            _recipe.Clear();
            RunRecipeBookSaveData recipeData = data.Recipe;
            List<string> dishIds = recipeData?.DishIds;
            List<RunRecipeDishFlavorSaveData> extraFlavors = recipeData?.DishExtraFlavors;
            if (dishIds != null)
            {
                for (int k = 0; k < dishIds.Count; k++)
                {
                    if (Database.GetDish(dishIds[k]) == null)
                    {
                        continue;
                    }

                    var slot = new RecipeBookSlot(dishIds[k]);
                    if (extraFlavors != null && k < extraFlavors.Count && extraFlavors[k]?.FlavorIds != null)
                    {
                        foreach (string flavorId in extraFlavors[k].FlavorIds)
                        {
                            slot.AddFlavor(flavorId);
                        }
                    }

                    if (extraFlavors != null && k < extraFlavors.Count)
                    {
                        RunRecipeDishFlavorSaveData extra = extraFlavors[k];
                        if (extra?.ExtraSkillIds != null)
                        {
                            foreach (string skillId in extra.ExtraSkillIds)
                            {
                                slot.AddExtraSkill(skillId);
                            }
                        }

                        slot.RestoreScoreMultiplier(
                            extra?.ScoreMultiplierBig?.GetValue(extra.ScoreMultiplier) ?? extra?.ScoreMultiplier ?? 1f);
                        slot.RestoreScoreFlatBonus(
                            extra?.ScoreFlatBonusBig?.GetValue(extra.ScoreFlatBonus) ?? extra?.ScoreFlatBonus ?? 0f);
                    }

                    _recipe.Add(slot);
                }
            }

            RebuildBonusDishCache();
        }

        /// <summary>使用一份消耗品：使用后该实例直接移除（不存在数量消耗的中间态）。</summary>
        public bool UseActiveItem(string itemId, string useContext = "unknown")
        {
            ItemDefinition item = ItemDefinition.Get(_tables, itemId, cfg.ItemKind.Active);
            if (item == null)
            {
                return false;
            }

            if (!RemoveOneInstance(itemId))
            {
                return false;
            }

            var itemRuntime = new ItemRuntime(this);
            int gold = itemRuntime.ActiveUseGold();
            if (gold > 0)
            {
                Gold += gold;
                itemRuntime.FlashTriggered(m => m.ActiveUseGold() > 0);
            }

            Execution.Telemetry.Track(
                () => GameAnalyticsService.TrackActiveItemUsed(this, itemId, useContext));

            return true;
        }
    }
}
