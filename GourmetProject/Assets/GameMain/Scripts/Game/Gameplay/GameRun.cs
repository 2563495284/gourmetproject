using System.Collections.Generic;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Library;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using GpBoard = GourmetProject.Gameplay.Board.Board;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.Gameplay
{
    /// <summary>
    /// 一次肉鸽运行（局外状态）：角色、周进度、金币、道具，以及玩法静态数据库。
    /// 负责为「当前周」构建局内战斗会话。可被存档（见阶段 5 的 RunSaveData）。
    /// </summary>
    public sealed class GameRun : IPreconditionContext
    {
        public const int BoardWidth = 4;
        public const int BoardHeight = 4;
        public const int RecipeSlotCount = 2;

        private readonly cfg.Tables _tables;

        // 被动道具同一 id 唯一一条（带 Level）；主动道具同一 id 可有多条，每条为一份独立实例。
        private readonly List<RunItemState> _items = new List<RunItemState>();
        private readonly List<string> _bonusDishIds = new List<string>();
        private readonly List<string> _stomachFragmentIds = new List<string>();

        // —— 行动轴状态 ——
        private readonly List<string> _triggeredNodeIds = new List<string>();
        private readonly List<string> _usedEventIds = new List<string>();
        private readonly List<string> _usedActionIds = new List<string>();
        private readonly List<string> _completedBossIds = new List<string>();

        public GameRun(cfg.Tables tables, GameplayDatabase database, string characterId, string seedText, int weekIndex = 1)
        {
            _tables = tables;
            Database = database;
            Library = GameplayContentBuilder.BuildDishLibrary(database);
            CharacterId = characterId;
            SeedText = seedText;
            WeekIndex = weekIndex;

            cfg.Character character = _tables.TbCharacter.GetOrDefault(characterId);
            if (character != null)
            {
                foreach (string itemId in character.StartItems)
                {
                    AcquireItem(itemId, 0);
                }
            }
        }

        public GameplayDatabase Database { get; }

        public DishLibrary Library { get; }

        public string CharacterId { get; }

        public string SeedText { get; }

        public int WeekIndex { get; set; }

        public int Gold { get; set; }

        public IReadOnlyList<RunItemState> Items => _items;

        public IReadOnlyList<string> BonusDishIds => _bonusDishIds;

        public IReadOnlyList<string> StomachFragmentIds => _stomachFragmentIds;

        /// <summary>本周要求分的临时覆盖（&lt;0 表示无覆盖）。事件「歇业」等可降低本周目标。</summary>
        public int RequiredScoreOverride { get; set; } = -1;

        // —— 行动轴运行状态 ——
        /// <summary>本周行动轴 id。</summary>
        public string CurrentTimelineId { get; set; } = string.Empty;

        /// <summary>本周行动轴长度（天）。</summary>
        public int TimelineLengthDays { get; set; }

        /// <summary>当前天数游标（0..TimelineLengthDays）。</summary>
        public int CurrentDay { get; set; }

        public IReadOnlyList<string> TriggeredNodeIds => _triggeredNodeIds;

        public IReadOnlyList<string> UsedEventIds => _usedEventIds;

        public IReadOnlyList<string> UsedActionIds => _usedActionIds;

        public IReadOnlyList<string> CompletedBossIds => _completedBossIds;

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

        public void MarkBossCompleted(string bossId)
        {
            if (!string.IsNullOrEmpty(bossId) && !_completedBossIds.Contains(bossId))
            {
                _completedBossIds.Add(bossId);
            }
        }

        /// <summary>开始一条新的本周行动轴：重置天数游标、节点结算记录与本周行动使用记录。</summary>
        public void BeginTimeline(string timelineId, int lengthDays)
        {
            CurrentTimelineId = timelineId ?? string.Empty;
            TimelineLengthDays = lengthDays;
            CurrentDay = 0;
            _triggeredNodeIds.Clear();
            _usedActionIds.Clear();
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
                cfg.Week week = CurrentWeek ?? LastConfiguredWeek;
                if (week == null)
                {
                    return 0;
                }

                int extra = CurrentWeek != null ? 0 : System.Math.Max(1, WeekIndex - TotalWeeks);
                return week.RewardHiddenScore + extra * 8;
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
                Items = items,
                BonusDishIds = new List<string>(_bonusDishIds),
                StomachFragmentIds = new List<string>(_stomachFragmentIds),
                CurrentTimelineId = CurrentTimelineId,
                TimelineLengthDays = TimelineLengthDays,
                CurrentDay = CurrentDay,
                TriggeredNodeIds = new List<string>(_triggeredNodeIds),
                UsedEventIds = new List<string>(_usedEventIds),
                UsedActionIds = new List<string>(_usedActionIds),
                CompletedBossIds = new List<string>(_completedBossIds),
                ItemIds = legacyItemIds,
            };
        }

        /// <summary>从存档数据重建运行（不重复发放初始道具，整段持有列表以存档为准）。</summary>
        public static GameRun FromSaveData(cfg.Tables tables, GameplayDatabase database, RunSaveData data)
        {
            var run = new GameRun(tables, database, data.CharacterId, data.SeedText, data.WeekIndex);
            run.Gold = data.Gold;
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

            if (data.BonusDishIds != null)
            {
                run._bonusDishIds.AddRange(data.BonusDishIds);
            }

            if (data.StomachFragmentIds != null)
            {
                run._stomachFragmentIds.AddRange(data.StomachFragmentIds);
            }

            run.CurrentTimelineId = data.CurrentTimelineId ?? string.Empty;
            run.TimelineLengthDays = data.TimelineLengthDays;
            run.CurrentDay = data.CurrentDay;
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

            return run;
        }

        public int TotalWeeks => _tables.TbWeek.DataList.Count;

        public bool HasNextWeek => WeekIndex < TotalWeeks;

        /// <summary>
        /// 构建一局美食挑战战斗。<paramref name="requiredScore"/> 目标分、<paramref name="modifier"/> 特殊机制、
        /// <paramref name="key"/> 用于派生确定性随机流（同一周内不同天/不同战斗需用不同 key 才能各自独立复现）。
        /// </summary>
        public BattleSession BuildBattleSession(int requiredScore, string modifier, string key)
        {
            modifier ??= string.Empty;

            cfg.Character character = _tables.TbCharacter.GetOrDefault(CharacterId);
            string recipeId = character?.InitialRecipeId;
            RecipeDef recipe = Database.GetRecipe(recipeId);

            var slots = new List<RecipeSlot>(RecipeSlotCount);
            if (recipe != null)
            {
                var recipeStream = GameApp.Random.Stream($"recipe_{key}");
                for (int i = 0; i < RecipeSlotCount; i++)
                {
                    List<string> deck = RecipeRoller.Roll(recipe, Database, recipeStream);
                    foreach (string dishId in _bonusDishIds)
                    {
                        if (Database.GetDish(dishId) != null)
                        {
                            deck.Add(dishId);
                        }
                    }

                    slots.Add(new RecipeSlot($"菜谱{i + 1}", deck));
                }
            }
            else
            {
                Log.Warning($"Character '{CharacterId}' has no valid recipe '{recipeId}'.", "GameRun");
            }

            GpBoard board = BuildBoard(character, modifier);

            var battleStream = GameApp.Random.Stream($"battle_{key}");
            var session = new BattleSession(board, Database, battleStream, slots, requiredScore);

            if (modifier == "limit_serve")
            {
                session.MaxServes = 5;
            }

            ApplyPassiveItems(session);
            return session;
        }

        /// <summary>美食行动目标分：当前周目标分 × 倍率（倍率 &lt;= 0 视为 1）。</summary>
        public int ComputeFoodRequiredScore(float multiplier)
        {
            if (multiplier <= 0f)
            {
                multiplier = 1f;
            }

            int baseReq = RequiredScore;
            return System.Math.Max(1, (int)System.Math.Round(baseReq * multiplier, System.MidpointRounding.AwayFromZero));
        }

        /// <summary>Boss 目标分：用指定分数曲线（空则用当前周曲线），强制应用 Boss 倍率。</summary>
        public int ComputeBossRequiredScore(string scoreProfileId)
        {
            cfg.ScoreProfile profile = !string.IsNullOrEmpty(scoreProfileId)
                ? _tables.TbScoreProfile.GetOrDefault(scoreProfileId)
                : CurrentScoreProfile(CurrentWeek ?? LastConfiguredWeek);
            int endlessExtra = IsEndless ? System.Math.Max(1, WeekIndex - TotalWeeks) : 0;
            return ComputeRequiredScore(profile, true, endlessExtra);
        }

        /// <summary>
        /// 由角色配置构建本局胃部棋盘：初始胃形状取自碎片库，最大包围盒取角色 max 尺寸。
        /// Boss「small_board」修正收缩最大包围盒（初始碎片超出部分自动裁掉）。
        /// </summary>
        private GpBoard BuildBoard(cfg.Character character, string modifier)
        {
            int maxW = character != null && character.MaxStomachWidth > 0 ? character.MaxStomachWidth : BoardWidth;
            int maxH = character != null && character.MaxStomachHeight > 0 ? character.MaxStomachHeight : BoardHeight;

            if (modifier == "small_board")
            {
                maxW = System.Math.Min(maxW, 3);
                maxH = System.Math.Min(maxH, 3);
            }

            StomachFragmentDef fragment = Database.GetFragment(character?.InitialFragmentId);
            if (fragment == null)
            {
                Log.Warning($"Character '{CharacterId}' 无有效初始胃碎片 '{character?.InitialFragmentId}'，回退为满 {maxW}x{maxH} 棋盘。", "GameRun");
                return new GpBoard(maxW, maxH);
            }

            return StomachBuilder.BuildExpanded(fragment, GetAcquiredFragments(), maxW, maxH);
        }

        /// <summary>当前周是否为 Boss 周（用于表现层展示）。</summary>
        public bool IsBossWeek => CurrentWeek?.IsBoss ?? false;

        /// <summary>当前周的修正标识（small_board / limit_serve …），无则空串。</summary>
        public string WeekModifier => CurrentWeek?.Modifier ?? string.Empty;

        /// <summary>把被动道具效果汇总成局级修正注入战斗会话。</summary>
        private void ApplyPassiveItems(BattleSession session)
        {
            foreach (RunItemState state in _items)
            {
                cfg.Item item = _tables.TbItem.GetOrDefault(state.ItemId);
                if (item == null || item.Kind != cfg.ItemKind.Passive)
                {
                    continue;
                }

                PassiveItemEffectRegistry.ApplyToBattle(session, item, state);
            }
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
                int maxLevel = ItemPoolService.GetPassiveMaxLevel(item);
                RunItemState state = GetItemState(itemId);
                if (state == null)
                {
                    state = new RunItemState(itemId, 1);
                    _items.Add(state);
                    return new ItemAcquireResult(ItemAcquireOutcome.Added, itemId, item.Name, state.Level, 1, 0);
                }

                if (state.Level < maxLevel)
                {
                    state.IncreaseLevel(maxLevel);
                    return new ItemAcquireResult(ItemAcquireOutcome.Upgraded, itemId, item.Name, state.Level, 1, 0);
                }

                Gold += fallbackGold;
                return new ItemAcquireResult(ItemAcquireOutcome.ConvertedToGold, itemId, item.Name, state.Level, 1, fallbackGold);
            }

            // 主动道具：每获得一次新增一份独立实例；达到持有上限则折算金币。
            if (!ItemPoolService.CanEnterPool(this, item))
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

            _bonusDishIds.Add(dishId);
            return true;
        }

        /// <summary>从菜谱奖励池移除一道菜（商店删菜）。</summary>
        public bool RemoveBonusDish(string dishId)
        {
            return _bonusDishIds.Remove(dishId);
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

        public bool CanAttachStomachFragment(StomachFragmentDef fragment)
        {
            if (fragment == null)
            {
                return false;
            }

            cfg.Character character = _tables.TbCharacter.GetOrDefault(CharacterId);
            StomachFragmentDef initial = Database.GetFragment(character?.InitialFragmentId);
            int maxW = character != null && character.MaxStomachWidth > 0 ? character.MaxStomachWidth : BoardWidth;
            int maxH = character != null && character.MaxStomachHeight > 0 ? character.MaxStomachHeight : BoardHeight;
            return StomachBuilder.CanAttachFragment(initial, GetAcquiredFragments(), fragment, maxW, maxH);
        }

        private List<StomachFragmentDef> GetAcquiredFragments()
        {
            var fragments = new List<StomachFragmentDef>(_stomachFragmentIds.Count);
            foreach (string fragmentId in _stomachFragmentIds)
            {
                StomachFragmentDef fragment = Database.GetFragment(fragmentId);
                if (fragment != null)
                {
                    fragments.Add(fragment);
                }
            }

            return fragments;
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
