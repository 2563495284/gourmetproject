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
    public sealed class GameRun
    {
        public const int BoardWidth = 4;
        public const int BoardHeight = 4;
        public const int RecipeSlotCount = 2;

        private readonly cfg.Tables _tables;
        private readonly List<RunItemState> _items = new List<RunItemState>();
        private readonly List<string> _bonusDishIds = new List<string>();
        private readonly List<string> _stomachFragmentIds = new List<string>();

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
            cfg.ScoreProfile profile = CurrentScoreProfile(week);
            if (profile == null)
            {
                return 100;
            }

            double value = profile.BaseScore;
            value *= profile.DifficultyMul > 0f ? profile.DifficultyMul : 1f;
            if (week.IsBoss)
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
                if (!state.IsEmpty)
                {
                    legacyItemIds.Add(state.ItemId);
                }
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
                    if (!string.IsNullOrEmpty(item.ItemId))
                    {
                        run._items.Add(RunItemState.FromSaveData(item));
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

            return run;
        }

        public int TotalWeeks => _tables.TbWeek.DataList.Count;

        public bool HasNextWeek => WeekIndex < TotalWeeks;

        /// <summary>为当前周构建一局战斗。菜谱、上菜都走以周编号命名的确定性随机流。</summary>
        public BattleSession BuildBattleSession()
        {
            ResetBattleItemUseCounts();

            cfg.Character character = _tables.TbCharacter.GetOrDefault(CharacterId);
            string recipeId = character?.InitialRecipeId;
            RecipeDef recipe = Database.GetRecipe(recipeId);

            var slots = new List<RecipeSlot>(RecipeSlotCount);
            if (recipe != null)
            {
                var recipeStream = GameApp.Random.Stream($"recipe_w{WeekIndex}");
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

            string modifier = CurrentWeek?.Modifier ?? string.Empty;
            GpBoard board = BuildBoard(character, modifier);

            var battleStream = GameApp.Random.Stream($"battle_w{WeekIndex}");
            var session = new BattleSession(board, Database, battleStream, slots, RequiredScore);

            if (modifier == "limit_serve")
            {
                session.MaxServes = 5;
            }

            ApplyPassiveItems(session);
            return session;
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
            RunItemState state = GetItemState(itemId);
            return state != null && !state.IsEmpty;
        }

        public ItemAcquireResult AcquireItem(string itemId, int fallbackGold)
        {
            cfg.Item item = _tables.TbItem.GetOrDefault(itemId);
            if (item == null)
            {
                return default;
            }

            RunItemState state = GetItemState(itemId);
            if (item.Kind == cfg.ItemKind.Passive)
            {
                int maxLevel = ItemPoolService.GetPassiveMaxLevel(item);
                if (state == null)
                {
                    state = new RunItemState(itemId, 1, 1, 1);
                    _items.Add(state);
                    return new ItemAcquireResult(ItemAcquireOutcome.Added, itemId, item.Name, state.Level, state.Count, 0);
                }

                if (state.Level < maxLevel)
                {
                    state.IncreaseLevel(maxLevel);
                    return new ItemAcquireResult(ItemAcquireOutcome.Upgraded, itemId, item.Name, state.Level, state.Count, 0);
                }

                Gold += fallbackGold;
                return new ItemAcquireResult(ItemAcquireOutcome.ConvertedToGold, itemId, item.Name, state.Level, state.Count, fallbackGold);
            }

            if (state == null)
            {
                state = new RunItemState(itemId, 1, 1, 1);
                _items.Add(state);
                return new ItemAcquireResult(ItemAcquireOutcome.Stacked, itemId, item.Name, state.Level, state.Count, 0);
            }

            if (!ItemPoolService.CanEnterPool(this, item))
            {
                Gold += fallbackGold;
                return new ItemAcquireResult(ItemAcquireOutcome.ConvertedToGold, itemId, item.Name, state.Level, state.Count, fallbackGold);
            }

            state.AddCount(1);
            return new ItemAcquireResult(ItemAcquireOutcome.Stacked, itemId, item.Name, state.Level, state.Count, 0);
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

        public bool RemoveItem(string itemId)
        {
            RunItemState state = GetItemState(itemId);
            if (state == null)
            {
                return false;
            }

            _items.Remove(state);
            return true;
        }

        public bool UseActiveItem(string itemId)
        {
            cfg.Item item = _tables.TbItem.GetOrDefault(itemId);
            if (item == null || item.Kind != cfg.ItemKind.Active)
            {
                return false;
            }

            RunItemState state = GetItemState(itemId);
            if (state == null)
            {
                return false;
            }

            if (item.ConsumeOnUse)
            {
                return state.ConsumeOne();
            }

            state.RecordUse();
            return true;
        }

        private void ResetBattleItemUseCounts()
        {
            foreach (RunItemState state in _items)
            {
                state.ResetBattleUseCount();
            }
        }
    }
}
