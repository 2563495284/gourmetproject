using System.Collections.Generic;
using System.Globalization;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Library;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using GpBoard = GourmetProject.Gameplay.Board.Board;
using Log = GourmetProject.Core.Diagnostics.Log;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;

namespace GourmetProject.Game.Run
{
    /// <summary>
    /// 一次肉鸽运行（局外状态）：角色、周进度、金币、道具，以及玩法静态数据库。
    /// 负责为「当前周」构建局内战斗会话。可被存档（见阶段 5 的 RunSaveData）。
    /// </summary>
    public sealed class GameRun : IPreconditionContext
    {
        public const int BoardWidth = 4;
        public const int BoardHeight = 4;
        public const int DefaultRecipeBookCount = 2;
        public const int MaxRecipeBookCount = 4;
        public const int RecipeBookCapacity = 12;
        /// <summary>主动道具基础消耗槽数（可被 ExtraActiveSlot 被动道具增加）。</summary>
        public const int BaseActiveSlots = 2;

        private readonly cfg.Tables _tables;

        // 被动道具同一 id 唯一一条且不升级；主动道具同一 id 可有多条，每条为一份独立实例。
        private readonly List<RunItemState> _items = new List<RunItemState>();
        private readonly List<string> _bonusDishIds = new List<string>();
        private readonly List<List<string>> _recipeBooks = new List<List<string>>();
        private readonly List<string> _stomachFragmentIds = new List<string>();

        // 玩家在棋盘编辑页手动拼贴的碎片放置（id + 旋转 + 原点）；作为可复现重建胃形的权威数据。
        private readonly List<StomachFragmentPlacement> _fragmentPlacements = new List<StomachFragmentPlacement>();

        // 已购买待拼贴的碎片包内容（rolled 出的候选碎片 id）；拼贴或跳过后清空。
        private readonly List<string> _pendingFragmentPack = new List<string>();

        // 整局累计已结算的菜品 BaseId 次数（供技能「大局相同检测」，随存档保存）。
        private readonly Dictionary<string, int> _runSettledCounts = new Dictionary<string, int>();

        // —— 行动轴状态 ——
        private readonly List<string> _triggeredNodeIds = new List<string>();
        private readonly List<string> _usedEventIds = new List<string>();
        private readonly List<string> _usedActionIds = new List<string>();
        private readonly List<string> _completedBossIds = new List<string>();
        private readonly List<string> _rolledBossIds = new List<string>();
        private readonly List<string> _actionGroupSequence = new List<string>();
        private readonly List<RunActionChoiceSaveData> _pendingActionChoices = new List<RunActionChoiceSaveData>();
        private readonly List<ShopEntrySaveData> _pendingShopStock = new List<ShopEntrySaveData>();
        private string _pendingActionChoiceKey = string.Empty;
        private string _pendingShopKey = string.Empty;
        private string _pendingRewardKey = string.Empty;
        private RewardOfferSaveData _pendingRewardOffer;
        private int _activeUseIndex;
        private int _interestThreshold;
        private int _interestGoldPer;
        private int _interestCap;
        private int _foodAdjustBaseCount;

        public GameRun(cfg.Tables tables, GameplayDatabase database, string characterId, string seedText, int weekIndex = 1)
        {
            _tables = tables;
            Database = database;
            Library = GameplayContentBuilder.BuildDishLibrary(database);
            CharacterId = characterId;
            SeedText = seedText;
            WeekIndex = weekIndex;

            cfg.TbGameBase gameBase = _tables.TbGameBase;
            Gold = System.Math.Max(0, gameBase.InitialGold);
            _interestThreshold = System.Math.Max(0, gameBase.InterestThreshold);
            _interestGoldPer = gameBase.InterestGoldPer > 0 ? gameBase.InterestGoldPer : 1;
            _interestCap = System.Math.Max(0, gameBase.InitialInterestCap);
            _foodAdjustBaseCount = System.Math.Max(0, gameBase.InitialFoodAdjustCount);

            cfg.Character character = _tables.TbCharacter.GetOrDefault(characterId);
            if (character != null)
            {
                foreach (string itemId in character.StartItems)
                {
                    AcquireItem(itemId, 0);
                }
            }

            EnsureRecipeBookCount(DefaultRecipeBookCount);
        }

        public GameplayDatabase Database { get; }

        public DishLibrary Library { get; }

        public cfg.Tables Tables => _tables;

        public string CharacterId { get; }

        public string SeedText { get; }

        public int WeekIndex { get; set; }

        public int Gold { get; set; }

        public int InterestThreshold => _interestThreshold;

        public int InterestGoldPer => _interestGoldPer;

        /// <summary>当前运行的利息节点单次最高收益，本局基础值可被道具提高。</summary>
        public int InterestCap
        {
            get
            {
                int baseCap = System.Math.Max(0, _interestCap);
                int itemCap = System.Math.Max(0, new ItemRuntime(this).InterestCapOverride());
                return System.Math.Max(baseCap, itemCap);
            }
        }

        private bool _foodAdjustActionActive;
        private int _foodAdjustActionBonus;
        private int _foodAdjustSpent;

        /// <summary>「食物调整」本次美食行动额度：本局基础值 + 被动道具加成。</summary>
        public int FoodAdjustBaseCount
        {
            get
            {
                return System.Math.Max(0, _foodAdjustBaseCount + new ItemRuntime(this).AdjustCountBonus());
            }
        }

        /// <summary>「食物调整」剩余次数。只在当前美食行动内消耗，行动结束后恢复为基础额度。</summary>
        public int FoodAdjustCount => System.Math.Max(0, FoodAdjustLimit - _foodAdjustSpent);

        private int FoodAdjustLimit =>
            System.Math.Max(0, FoodAdjustBaseCount + (_foodAdjustActionActive ? _foodAdjustActionBonus : 0));

        public void BeginFoodActionAdjustments()
        {
            _foodAdjustActionActive = true;
            _foodAdjustActionBonus = 0;
            _foodAdjustSpent = 0;
        }

        public void EndFoodActionAdjustments()
        {
            _foodAdjustActionActive = false;
            _foodAdjustActionBonus = 0;
            _foodAdjustSpent = 0;
        }

        /// <summary>增加当前美食行动的临时调整次数，供主动道具等一次性效果使用。</summary>
        public bool AddFoodAdjustCount(int amount)
        {
            if (!_foodAdjustActionActive || amount <= 0)
            {
                return false;
            }

            _foodAdjustActionBonus += amount;
            return true;
        }

        /// <summary>尝试消耗一次食物调整：仅在 &gt;0 时 -1 并返回 true。</summary>
        public bool TrySpendFoodAdjust()
        {
            if (FoodAdjustCount <= 0)
            {
                return false;
            }

            _foodAdjustSpent++;
            return true;
        }

        /// <summary>
        /// 尝试消耗一件「不死」道具（名刀·加护）：持有时移除一件并返回 true，
        /// 供结算失败判定改为「不失败」。无则返回 false。
        /// </summary>
        public bool TryConsumeUndying()
        {
            foreach (RunItemState state in _items)
            {
                cfg.Item item = _tables.TbItem.GetOrDefault(state.ItemId);
                if (item != null && item.Kind == cfg.ItemKind.Passive && item.EffectType == ItemEffectTypes.Undying)
                {
                    RemoveItem(item.Id);
                    return true;
                }
            }

            return false;
        }

        public IReadOnlyList<RunItemState> Items => _items;

        /// <summary>被动道具持有条目（同 id 唯一，不占消耗槽）。</summary>
        public IEnumerable<RunItemState> PassiveItemStates => ItemStatesOfKind(cfg.ItemKind.Passive);

        /// <summary>主动道具持有实例（每份占一个消耗槽）。</summary>
        public IEnumerable<RunItemState> ActiveItemStates => ItemStatesOfKind(cfg.ItemKind.Active);

        private IEnumerable<RunItemState> ItemStatesOfKind(cfg.ItemKind kind)
        {
            var result = new List<RunItemState>();
            foreach (RunItemState state in _items)
            {
                cfg.Item item = _tables.TbItem.GetOrDefault(state.ItemId);
                if (item != null && item.Kind == kind)
                {
                    result.Add(state);
                }
            }

            return result;
        }

        /// <summary>当前占用的主动道具槽数（= 主动实例份数）。</summary>
        public int ActiveItemCount
        {
            get
            {
                int n = 0;
                foreach (RunItemState state in _items)
                {
                    cfg.Item item = _tables.TbItem.GetOrDefault(state.ItemId);
                    if (item != null && item.Kind == cfg.ItemKind.Active)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        /// <summary>主动道具消耗槽总容量 = 基础槽 + ExtraActiveSlot 被动加成（下限 0）。</summary>
        public int ActiveSlotCapacity =>
            System.Math.Max(0, BaseActiveSlots + new ItemRuntime(this).ExtraActiveSlots());

        /// <summary>主动道具是否还有空槽。</summary>
        public bool HasFreeActiveSlot => ActiveItemCount < ActiveSlotCapacity;

        /// <summary>主动道具累计使用序号；随机类主动效果按它派生随机流。</summary>
        public int ActiveUseIndex => _activeUseIndex;

        /// <summary>取下一个主动道具随机流 key 并推进使用序号（保证生成/复制类效果同种子可复现）。</summary>
        public string NextActiveUseKey()
        {
            return $"active_{_activeUseIndex++}";
        }

        public IReadOnlyList<string> BonusDishIds => _bonusDishIds;

        public IReadOnlyList<IReadOnlyList<string>> RecipeBooks => _recipeBooks;

        public int RecipeBookCount => _recipeBooks.Count;

        public IReadOnlyList<string> StomachFragmentIds => _stomachFragmentIds;

        /// <summary>玩家手动拼贴的碎片放置列表（棋盘编辑页产出，随存档保存）。</summary>
        public IReadOnlyList<StomachFragmentPlacement> FragmentPlacements => _fragmentPlacements;

        /// <summary>已购买待拼贴的碎片包候选碎片 id（三选一）；为空表示没有待处理的碎片包。</summary>
        public IReadOnlyList<string> PendingFragmentPack => _pendingFragmentPack;

        public bool HasPendingFragmentPack => _pendingFragmentPack.Count > 0;

        /// <summary>胃部碎片总数（奖励自动附着 + 手动拼贴），供统计/预览展示。</summary>
        public int StomachFragmentCount => _stomachFragmentIds.Count + _fragmentPlacements.Count;

        /// <summary>整局累计已结算的菜品 BaseId 次数（大局历史）。</summary>
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

        /// <summary>本周要求分的临时覆盖（&lt;0 表示无覆盖）。事件「歇业」等可降低本周目标。</summary>
        public int RequiredScoreOverride { get; set; } = -1;

        // —— 行动轴运行状态 ——
        /// <summary>本周行动轴 id。</summary>
        public string CurrentTimelineId { get; set; } = string.Empty;

        /// <summary>本周行动轴长度（天，0.1 粒度）。</summary>
        public float TimelineLengthDays { get; set; }

        /// <summary>当前天数游标（0..TimelineLengthDays，0.1 粒度）。</summary>
        public float CurrentDay { get; set; }

        public IReadOnlyList<string> TriggeredNodeIds => _triggeredNodeIds;

        public IReadOnlyList<string> UsedEventIds => _usedEventIds;

        public IReadOnlyList<string> UsedActionIds => _usedActionIds;

        public IReadOnlyList<string> CompletedBossIds => _completedBossIds;

        public IReadOnlyList<string> RolledBossIds => _rolledBossIds;

        /// <summary>本周已执行的行动次数，用于 UI、随机流和隐藏分进度。</summary>
        public int ActionStepIndex { get; private set; }

        /// <summary>整局累计已执行行动次数，用于行动组序列和隐藏分分段。</summary>
        public int RunActionStepIndex { get; private set; }

        public IReadOnlyList<string> ActionGroupSequence => _actionGroupSequence;

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
                return $"r{context.RunStepIndex}_w{weekIndex}_d{DayKey(currentDay)}_s{context.StepIndex}_{context.ActionGroupId}_{context.Action.Id}";
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

        public bool IsEventUsed(string eventId) => !string.IsNullOrEmpty(eventId) && _usedEventIds.Contains(eventId);

        public void MarkEventUsed(string eventId)
        {
            if (!string.IsNullOrEmpty(eventId) && !_usedEventIds.Contains(eventId))
            {
                _usedEventIds.Add(eventId);
            }
        }

        public bool IsActionUsed(string actionId) => !string.IsNullOrEmpty(actionId) && _usedActionIds.Contains(actionId);

        public void MarkActionUsed(string actionId)
        {
            if (!string.IsNullOrEmpty(actionId) && !_usedActionIds.Contains(actionId))
            {
                _usedActionIds.Add(actionId);
            }
        }

        public bool IsBossCompleted(string bossId) => !string.IsNullOrEmpty(bossId) && _completedBossIds.Contains(bossId);

        public bool IsBossRolled(string bossId) => !string.IsNullOrEmpty(bossId) && _rolledBossIds.Contains(bossId);

        public void MarkBossCompleted(string bossId)
        {
            if (!string.IsNullOrEmpty(bossId) && !_completedBossIds.Contains(bossId))
            {
                _completedBossIds.Add(bossId);
            }

            MarkBossRolled(bossId);
        }

        public void MarkBossRolled(string bossId)
        {
            if (!string.IsNullOrEmpty(bossId) && !_rolledBossIds.Contains(bossId))
            {
                _rolledBossIds.Add(bossId);
            }
        }

        public void ResetBossRollHistory()
        {
            _rolledBossIds.Clear();
        }

        /// <summary>开始一条新的本周行动轴：重置天数游标、节点结算记录与本周行动使用记录。</summary>
        public void BeginTimeline(string timelineId, float lengthDays)
        {
            CurrentTimelineId = timelineId ?? string.Empty;
            TimelineLengthDays = lengthDays;
            CurrentDay = 0f;
            ActionStepIndex = 0;
            _triggeredNodeIds.Clear();
            _usedActionIds.Clear();
            LastActionContext = null;
            ClearPendingActionChoices();
            ClearPendingShopStock();
            ClearPendingRewardOffer();
        }

        public void AdvanceActionStep()
        {
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

        public void AppendActionGroup(string groupId)
        {
            _actionGroupSequence.Add(groupId ?? string.Empty);
        }

        public void RestoreActionGroupSequence(IEnumerable<string> groupIds)
        {
            _actionGroupSequence.Clear();
            if (groupIds == null)
            {
                return;
            }

            foreach (string groupId in groupIds)
            {
                _actionGroupSequence.Add(groupId ?? string.Empty);
            }
        }

        public void SetLastActionContext(ActionExecutionContext context)
        {
            LastActionContext = context;
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

                result.Add(new ActionChoice(action, data.ActionGroupId, data.WeekStepIndex, data.RunStepIndex, data.CostDays));
            }

            return result;
        }

        public bool HasPendingActionChoices(string key)
        {
            return !string.IsNullOrEmpty(key) && key == _pendingActionChoiceKey;
        }

        public void SetPendingActionChoices(string key, IReadOnlyList<ActionChoice> choices)
        {
            _pendingActionChoiceKey = key ?? string.Empty;
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
                });
            }
        }

        public void ClearPendingActionChoices()
        {
            _pendingActionChoiceKey = string.Empty;
            _pendingActionChoices.Clear();
        }

        public List<ShopEntry> GetPendingShopStock(string key)
        {
            var result = new List<ShopEntry>();
            if (!HasPendingShopStock(key))
            {
                return result;
            }

            foreach (ShopEntrySaveData entry in _pendingShopStock)
            {
                result.Add(new ShopEntry(entry.Kind, entry.Id, entry.Name, entry.Desc, entry.Price));
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
                    Id = entry.Id,
                    Name = entry.Name,
                    Desc = entry.Desc,
                    Price = entry.Price,
                });
            }
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

        public cfg.Week CurrentWeek => _tables.TbWeek.GetOrDefault(WeekIndex);

        public cfg.RewardPackage CurrentRewardPackage
        {
            get
            {
                cfg.Week week = CurrentWeek ?? LastConfiguredWeek;
                return week == null ? null : _tables.TbRewardPackage.GetOrDefault(week.RewardPackageId);
            }
        }

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

                if (CurrentWeek != null)
                {
                    return ComputeRequiredScore(CurrentWeek, 0);
                }

                return EndlessRequiredScore();
            }
        }

        public int RewardHiddenScore
        {
            get
            {
                int derived = HiddenScoreService.DishHiddenScore(this, LastActionContext);
                if (derived > 0)
                {
                    return derived;
                }

                cfg.Week week = CurrentWeek ?? LastConfiguredWeek;
                return week?.RewardHiddenScore ?? 0;
            }
        }

        private cfg.Week LastConfiguredWeek => TotalWeeks > 0 ? _tables.TbWeek.GetOrDefault(TotalWeeks) : null;

        private cfg.ScoreProfile CurrentScoreProfile(cfg.Week week)
        {
            return week == null ? null : _tables.TbScoreProfile.GetOrDefault(week.ScoreProfileId);
        }

        private int ComputeRequiredScore(cfg.Week week, int endlessExtra)
        {
            return ComputeRequiredScore(CurrentScoreProfile(week), week != null && week.IsBoss, endlessExtra);
        }

        private int ComputeRequiredScore(cfg.ScoreProfile profile, bool boss, int endlessExtra)
        {
            if (profile == null)
            {
                return 100;
            }

            double value = profile.BaseScore;
            value *= profile.DifficultyMul > 0f ? profile.DifficultyMul : 1f;
            if (boss)
            {
                value *= profile.BossMul > 0f ? profile.BossMul : 1f;
            }

            if (endlessExtra > 0)
            {
                double growth = profile.EndlessGrowthMul > 0f ? profile.EndlessGrowthMul : 1.5f;
                value *= System.Math.Pow(growth, endlessExtra);
            }

            int rounded = (int)System.Math.Round(value, System.MidpointRounding.AwayFromZero);
            int roundTo = profile.RoundTo > 0 ? profile.RoundTo : 1;
            return ((rounded + roundTo - 1) / roundTo) * roundTo;
        }

        /// <summary>无尽模式要求分：以最后一周的目标分曲线为基准递增。</summary>
        private int EndlessRequiredScore()
        {
            cfg.Week last = LastConfiguredWeek;
            int extra = System.Math.Max(1, WeekIndex - TotalWeeks);
            return ComputeRequiredScore(last, extra);
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
                CharacterId = CharacterId,
                SeedText = SeedText,
                WeekIndex = WeekIndex,
                Gold = Gold,
                InterestThreshold = _interestThreshold,
                InterestGoldPer = _interestGoldPer,
                InterestCap = _interestCap,
                FoodAdjustCount = _foodAdjustBaseCount,
                ActiveUseIndex = _activeUseIndex,
                Items = items,
                BonusDishIds = new List<string>(_bonusDishIds),
                RecipeBooks = ToRecipeBookSaveData(),
                StomachFragmentIds = new List<string>(_stomachFragmentIds),
                FragmentPlacements = ToFragmentPlacementSaveData(),
                PendingFragmentPackIds = new List<string>(_pendingFragmentPack),
                RunSettledCounts = new Dictionary<string, int>(_runSettledCounts),
                CurrentTimelineId = CurrentTimelineId,
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
                ActionGroupSequence = new List<string>(_actionGroupSequence),
                TriggeredNodeIds = new List<string>(_triggeredNodeIds),
                UsedEventIds = new List<string>(_usedEventIds),
                UsedActionIds = new List<string>(_usedActionIds),
                CompletedBossIds = new List<string>(_completedBossIds),
                RolledBossIds = new List<string>(_rolledBossIds),
                PendingActionChoiceKey = _pendingActionChoiceKey,
                PendingActionChoices = new List<RunActionChoiceSaveData>(_pendingActionChoices),
                PendingShopKey = _pendingShopKey,
                PendingShopStock = new List<ShopEntrySaveData>(_pendingShopStock),
                PendingRewardKey = _pendingRewardKey,
                PendingRewardOffer = _pendingRewardOffer,
                ItemIds = legacyItemIds,
            };
        }

        /// <summary>从存档数据重建运行（不重复发放初始道具，整段持有列表以存档为准）。</summary>
        public static GameRun FromSaveData(cfg.Tables tables, GameplayDatabase database, RunSaveData data)
        {
            var run = new GameRun(tables, database, data.CharacterId, data.SeedText, data.WeekIndex);
            run.Gold = data.Gold;
            run._interestThreshold = data.InterestThreshold >= 0
                ? data.InterestThreshold
                : System.Math.Max(0, tables.TbGameBase.InterestThreshold);
            run._interestGoldPer = data.InterestGoldPer > 0
                ? data.InterestGoldPer
                : (tables.TbGameBase.InterestGoldPer > 0 ? tables.TbGameBase.InterestGoldPer : 1);
            run._interestCap = data.InterestCap >= 0
                ? data.InterestCap
                : System.Math.Max(0, tables.TbGameBase.InitialInterestCap);
            run._foodAdjustBaseCount = data.FoodAdjustCount >= 0
                ? data.FoodAdjustCount
                : System.Math.Max(0, tables.TbGameBase.InitialFoodAdjustCount);
            run._activeUseIndex = data.ActiveUseIndex;
            run._items.Clear();

            if (data.Items != null && data.Items.Count > 0)
            {
                foreach (RunItemSaveData item in data.Items)
                {
                    if (string.IsNullOrEmpty(item.ItemId))
                    {
                        continue;
                    }

                    cfg.Item def = tables.TbItem.GetOrDefault(item.ItemId);

                    // 旧档迁移：主动道具曾用单条 + Count 表示堆叠，这里展开为多份实例；
                    // Count<=0 的旧「僵尸条目」直接丢弃（用完即不存在）。被动道具恒为一条。
                    int instances = 1;
                    if (def != null && def.Kind == cfg.ItemKind.Active)
                    {
                        instances = System.Math.Max(0, item.Count);
                    }

                    for (int k = 0; k < instances; k++)
                    {
                        if (def != null && def.Kind == cfg.ItemKind.Passive && run.GetItemState(item.ItemId) != null)
                        {
                            continue;
                        }

                        run._items.Add(new RunItemState(item.ItemId, item.Level));
                    }
                }
            }
            else if (data.ItemIds != null)
            {
                foreach (string itemId in data.ItemIds)
                {
                    run.AcquireItem(itemId, 0);
                }
            }

            run.RestoreRecipeBooks(data);

            if (data.StomachFragmentIds != null)
            {
                run._stomachFragmentIds.AddRange(data.StomachFragmentIds);
            }

            if (data.FragmentPlacements != null)
            {
                foreach (StomachFragmentPlacementSaveData p in data.FragmentPlacements)
                {
                    if (p == null || string.IsNullOrEmpty(p.FragmentId))
                    {
                        continue;
                    }

                    run._fragmentPlacements.Add(new StomachFragmentPlacement(
                        p.FragmentId, p.Rotation, new GridPos(p.OriginX, p.OriginY)));
                }
            }

            if (data.PendingFragmentPackIds != null)
            {
                run._pendingFragmentPack.AddRange(data.PendingFragmentPackIds);
            }

            if (data.RunSettledCounts != null)
            {
                foreach (KeyValuePair<string, int> kv in data.RunSettledCounts)
                {
                    run._runSettledCounts[kv.Key] = kv.Value;
                }
            }

            run.CurrentTimelineId = data.CurrentTimelineId ?? string.Empty;
            run.TimelineLengthDays = data.TimelineLengthDays;
            run.CurrentDay = data.CurrentDay;
            run.RestoreActionStepIndex(data.ActionStepIndex);
            run.RestoreRunActionStepIndex(data.RunActionStepIndex);
            run.RequiredScoreOverride = data.RequiredScoreOverride;
            run.RestoreActionGroupSequence(data.ActionGroupSequence);
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
                        costDays));
                }
            }

            if (data.TriggeredNodeIds != null)
            {
                run._triggeredNodeIds.AddRange(data.TriggeredNodeIds);
            }

            if (data.UsedEventIds != null)
            {
                run._usedEventIds.AddRange(data.UsedEventIds);
            }

            if (data.UsedActionIds != null)
            {
                run._usedActionIds.AddRange(data.UsedActionIds);
            }

            if (data.CompletedBossIds != null)
            {
                run._completedBossIds.AddRange(data.CompletedBossIds);
            }

            if (data.RolledBossIds != null)
            {
                run._rolledBossIds.AddRange(data.RolledBossIds);
            }

            run._pendingActionChoiceKey = data.PendingActionChoiceKey ?? string.Empty;
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
                BaseGoldClaimed = offer.BaseGoldClaimed,
                MainChoiceIndex = offer.MainChoiceIndex,
                ExtraChoiceIndex = offer.ExtraChoiceIndex,
                MainChoiceSkipped = offer.MainChoiceSkipped,
                ExtraChoiceSkipped = offer.ExtraChoiceSkipped,
                MainChoices = ToSaveData(offer.MainChoices),
                ExtraChoices = ToSaveData(offer.ExtraChoices),
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
                });
            }

            return result;
        }

        private static RewardOffer FromSaveData(RewardOfferSaveData data)
        {
            return data == null
                ? null
                : new RewardOffer(
                    data.BaseGold,
                    FromSaveData(data.MainChoices),
                    FromSaveData(data.ExtraChoices),
                    data.BaseGoldClaimed,
                    data.MainChoiceIndex,
                    data.ExtraChoiceIndex,
                    data.MainChoiceSkipped,
                    data.ExtraChoiceSkipped);
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
                    choice.IsFallbackGold));
            }

            return result;
        }

        public int TotalWeeks => _tables.TbWeek.DataList.Count;

        public bool HasNextWeek => WeekIndex < TotalWeeks;

        /// <summary>
        /// 构建一局美食挑战战斗。<paramref name="requiredScore"/> 目标分、<paramref name="modifier"/> 特殊机制、
        /// <paramref name="key"/> 用于派生确定性随机流（同一周内不同天/不同战斗需用不同 key 才能各自独立复现）。
        /// </summary>
        public BattleSession BuildBattleSession(int requiredScore, string modifier, string key)
        {
            return BattleSessionFactory.Build(this, requiredScore, modifier, key);
        }

        public GpBoard BuildStomachPreviewBoard(string modifier = "")
        {
            return BattleSessionFactory.BuildBoardPreview(this, modifier);
        }

        public IReadOnlyList<string> GetRecipeBookDishes(int bookIndex)
        {
            return IsRecipeBookIndexValid(bookIndex) ? _recipeBooks[bookIndex] : System.Array.Empty<string>();
        }

        public bool CanAddRecipeBook => _recipeBooks.Count < MaxRecipeBookCount;

        public bool AddRecipeBook()
        {
            if (!CanAddRecipeBook)
            {
                return false;
            }

            _recipeBooks.Add(new List<string>());
            RebuildBonusDishCache();
            return true;
        }

        public bool MoveBonusDish(int fromBookIndex, int dishIndex, int toBookIndex)
        {
            if (!IsRecipeBookIndexValid(fromBookIndex) || !IsRecipeBookIndexValid(toBookIndex))
            {
                return false;
            }

            List<string> from = _recipeBooks[fromBookIndex];
            List<string> to = _recipeBooks[toBookIndex];
            if (dishIndex < 0 || dishIndex >= from.Count || to.Count >= RecipeBookCapacity)
            {
                return false;
            }

            if (fromBookIndex == toBookIndex)
            {
                return true;
            }

            string dishId = from[dishIndex];
            from.RemoveAt(dishIndex);
            to.Add(dishId);
            RebuildBonusDishCache();
            return true;
        }

        public bool RemoveBonusDishAt(int bookIndex, int dishIndex)
        {
            if (!IsRecipeBookIndexValid(bookIndex))
            {
                return false;
            }

            List<string> book = _recipeBooks[bookIndex];
            if (dishIndex < 0 || dishIndex >= book.Count)
            {
                return false;
            }

            book.RemoveAt(dishIndex);
            RebuildBonusDishCache();
            return true;
        }

        /// <summary>美食行动目标分：当前周目标分 × 倍率（倍率 &lt;= 0 视为 1）。</summary>
        public int ComputeFoodRequiredScore(float multiplier)
        {
            if (multiplier <= 0f)
            {
                multiplier = 1f;
            }

            int baseReq = RequiredScore;
            int scaled = System.Math.Max(1, (int)System.Math.Round(baseReq * multiplier, System.MidpointRounding.AwayFromZero));
            // 超级美食倍率 > 1 视为 Super 档，否则普通档；道具目标分修正随档位施加。
            var tier = multiplier > 1f ? MealTier.Super : MealTier.Normal;
            return new ItemRuntime(this).ModifyRequiredScore(scaled, tier);
        }

        /// <summary>Boss 目标分：用指定分数曲线（空则用当前周曲线），强制应用 Boss 倍率。</summary>
        public int ComputeBossRequiredScore(string scoreProfileId)
        {
            cfg.ScoreProfile profile = !string.IsNullOrEmpty(scoreProfileId)
                ? _tables.TbScoreProfile.GetOrDefault(scoreProfileId)
                : CurrentScoreProfile(CurrentWeek ?? LastConfiguredWeek);
            int endlessExtra = IsEndless ? System.Math.Max(1, WeekIndex - TotalWeeks) : 0;
            int bossReq = ComputeRequiredScore(profile, true, endlessExtra);
            return new ItemRuntime(this).ModifyRequiredScore(bossReq, MealTier.Feast);
        }

        /// <summary>当前周是否为 Boss 周（用于表现层展示）。</summary>
        public bool IsBossWeek => CurrentWeek?.IsBoss ?? false;

        /// <summary>当前周的修正标识（small_board / limit_serve …），无则空串。</summary>
        public string WeekModifier => CurrentWeek?.Modifier ?? string.Empty;

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

        /// <summary>当前持有该道具的份数：被动道具为 0/1，主动道具为实例条目数。</summary>
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

        public ItemAcquireResult AcquireItem(string itemId, int fallbackGold)
        {
            cfg.Item item = _tables.TbItem.GetOrDefault(itemId);
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
                    return new ItemAcquireResult(ItemAcquireOutcome.Added, itemId, item.Name, 1, 1, 0);
                }

                Gold += fallbackGold;
                return new ItemAcquireResult(ItemAcquireOutcome.ConvertedToGold, itemId, item.Name, 1, 1, fallbackGold);
            }

            // 主动道具：每份占一个全局消耗槽；槽满则折算金币（不再有 per-item 囤积上限）。
            if (!HasFreeActiveSlot || !ItemPoolService.CanEnterPool(this, item))
            {
                Gold += fallbackGold;
                return new ItemAcquireResult(ItemAcquireOutcome.ConvertedToGold, itemId, item.Name, 1, GetItemCount(itemId), fallbackGold);
            }

            _items.Add(new RunItemState(itemId, 1));
            int held = GetItemCount(itemId);
            return new ItemAcquireResult(ItemAcquireOutcome.Stacked, itemId, item.Name, 1, held, 0);
        }

        public bool AddBonusDish(string dishId)
        {
            if (Database.GetDish(dishId) == null)
            {
                return false;
            }

            EnsureRecipeBookCount(DefaultRecipeBookCount);
            List<string> target = FirstRecipeBookWithSpace();
            if (target == null)
            {
                return false;
            }

            target.Add(dishId);
            RebuildBonusDishCache();
            return true;
        }

        public bool AddBonusDishToBook(string dishId, int bookIndex)
        {
            if (Database.GetDish(dishId) == null || !IsRecipeBookIndexValid(bookIndex))
            {
                return false;
            }

            List<string> book = _recipeBooks[bookIndex];
            if (book.Count >= RecipeBookCapacity)
            {
                return false;
            }

            book.Add(dishId);
            RebuildBonusDishCache();
            return true;
        }

        /// <summary>从菜谱奖励池移除一道菜（商店删菜）。</summary>
        public bool RemoveBonusDish(string dishId)
        {
            foreach (List<string> book in _recipeBooks)
            {
                if (book.Remove(dishId))
                {
                    RebuildBonusDishCache();
                    return true;
                }
            }

            return false;
        }

        public bool AddStomachFragment(string fragmentId)
        {
            StomachFragmentDef fragment = Database.GetFragment(fragmentId);
            if (fragment == null || _stomachFragmentIds.Contains(fragmentId))
            {
                return false;
            }

            _stomachFragmentIds.Add(fragmentId);
            return true;
        }

        /// <summary>记录一次玩家手动拼贴的碎片放置（棋盘编辑页调用；合法性由调用方在放置前校验）。</summary>
        public bool AddFragmentPlacement(string fragmentId, int rotation, GridPos origin)
        {
            if (Database.GetFragment(fragmentId) == null)
            {
                return false;
            }

            _fragmentPlacements.Add(new StomachFragmentPlacement(fragmentId, rotation, origin));
            return true;
        }

        /// <summary>置入一份已购买待拼贴的碎片包（三选一候选 id）。</summary>
        public void SetPendingFragmentPack(IEnumerable<string> fragmentIds)
        {
            _pendingFragmentPack.Clear();
            if (fragmentIds != null)
            {
                foreach (string id in fragmentIds)
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        _pendingFragmentPack.Add(id);
                    }
                }
            }
        }

        /// <summary>清空待拼贴的碎片包（拼贴完成或跳过后调用）。</summary>
        public void ClearPendingFragmentPack()
        {
            _pendingFragmentPack.Clear();
        }

        public bool CanAttachStomachFragment(StomachFragmentDef fragment)
        {
            if (fragment == null)
            {
                return false;
            }

            // 基于当前实际胃形判断；棋盘碎片奖励固定朝向，不允许旋转。
            GpBoard board = BattleSessionFactory.BuildBoardPreview(this);
            cfg.Character character = Tables.TbCharacter.GetOrDefault(CharacterId);
            int maxW = character != null && character.MaxStomachWidth > 0 ? character.MaxStomachWidth : BoardWidth;
            int maxH = character != null && character.MaxStomachHeight > 0 ? character.MaxStomachHeight : BoardHeight;
            return StomachBuilder.CanAttachAnywhereLocalBounds(board, fragment, maxW, maxH);
        }

        /// <summary>移除一份道具（被动整条移除；主动移除其中一份实例）。供商店出售、事件移除等使用。</summary>
        public bool RemoveItem(string itemId)
        {
            return RemoveOneInstance(itemId);
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
                    _items.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        private bool IsRecipeBookIndexValid(int index)
        {
            return index >= 0 && index < _recipeBooks.Count;
        }

        private void EnsureRecipeBookCount(int count)
        {
            while (_recipeBooks.Count < count)
            {
                _recipeBooks.Add(new List<string>());
            }
        }

        private List<string> FirstRecipeBookWithSpace()
        {
            foreach (List<string> book in _recipeBooks)
            {
                if (book.Count < RecipeBookCapacity)
                {
                    return book;
                }
            }

            return null;
        }

        private void RebuildBonusDishCache()
        {
            _bonusDishIds.Clear();
            foreach (List<string> book in _recipeBooks)
            {
                foreach (string dishId in book)
                {
                    _bonusDishIds.Add(dishId);
                }
            }
        }

        private List<RunRecipeBookSaveData> ToRecipeBookSaveData()
        {
            var books = new List<RunRecipeBookSaveData>(_recipeBooks.Count);
            foreach (List<string> book in _recipeBooks)
            {
                books.Add(new RunRecipeBookSaveData
                {
                    DishIds = new List<string>(book),
                });
            }

            return books;
        }

        private List<StomachFragmentPlacementSaveData> ToFragmentPlacementSaveData()
        {
            var list = new List<StomachFragmentPlacementSaveData>(_fragmentPlacements.Count);
            foreach (StomachFragmentPlacement p in _fragmentPlacements)
            {
                list.Add(new StomachFragmentPlacementSaveData
                {
                    FragmentId = p.FragmentId,
                    Rotation = p.Rotation,
                    OriginX = p.Origin.X,
                    OriginY = p.Origin.Y,
                });
            }

            return list;
        }

        private void RestoreRecipeBooks(RunSaveData data)
        {
            _recipeBooks.Clear();
            if (data.RecipeBooks != null && data.RecipeBooks.Count > 0)
            {
                int count = System.Math.Min(data.RecipeBooks.Count, MaxRecipeBookCount);
                for (int i = 0; i < count; i++)
                {
                    var book = new List<string>();
                    List<string> dishIds = data.RecipeBooks[i]?.DishIds;
                    if (dishIds != null)
                    {
                        for (int k = 0; k < dishIds.Count && book.Count < RecipeBookCapacity; k++)
                        {
                            if (Database.GetDish(dishIds[k]) != null)
                            {
                                book.Add(dishIds[k]);
                            }
                        }
                    }

                    _recipeBooks.Add(book);
                }
            }
            else
            {
                EnsureRecipeBookCount(DefaultRecipeBookCount);
                if (data.BonusDishIds != null)
                {
                    foreach (string dishId in data.BonusDishIds)
                    {
                        AddBonusDish(dishId);
                    }
                }
            }

            EnsureRecipeBookCount(DefaultRecipeBookCount);
            RebuildBonusDishCache();
        }

        /// <summary>使用一份主动道具：使用后该实例直接移除（不存在数量消耗的中间态）。</summary>
        public bool UseActiveItem(string itemId)
        {
            cfg.Item item = _tables.TbItem.GetOrDefault(itemId);
            if (item == null || item.Kind != cfg.ItemKind.Active)
            {
                return false;
            }

            return RemoveOneInstance(itemId);
        }
    }
}
