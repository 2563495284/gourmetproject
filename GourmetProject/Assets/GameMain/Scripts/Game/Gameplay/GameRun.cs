using System.Collections.Generic;
using GourmetProject.Gameplay.Battle;
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

        public GameRun(cfg.Tables tables, GameplayDatabase database, string characterId, string seedText, int weekIndex = 1)
        {
            _tables = tables;
            Database = database;
            Library = GameplayContentBuilder.BuildDishLibrary(database);
            CharacterId = characterId;
            SeedText = seedText;
            WeekIndex = weekIndex;
            ItemIds = new List<string>();

            cfg.Character character = _tables.TbCharacter.GetOrDefault(characterId);
            if (character != null)
            {
                ItemIds.AddRange(character.StartItems);
            }
        }

        public GameplayDatabase Database { get; }

        public DishLibrary Library { get; }

        public string CharacterId { get; }

        public string SeedText { get; }

        public int WeekIndex { get; set; }

        public int Gold { get; set; }

        public List<string> ItemIds { get; }

        /// <summary>本周要求分的临时覆盖（&lt;0 表示无覆盖）。事件「歇业」等可降低本周目标。</summary>
        public int RequiredScoreOverride { get; set; } = -1;

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

                if (CurrentWeek != null)
                {
                    return CurrentWeek.RequiredScore;
                }

                return EndlessRequiredScore();
            }
        }

        /// <summary>无尽模式要求分：以最后一周为基准按 1.5 倍逐周递增。</summary>
        private int EndlessRequiredScore()
        {
            int total = TotalWeeks;
            cfg.Week last = total > 0 ? _tables.TbWeek.GetOrDefault(total) : null;
            int baseScore = last?.RequiredScore ?? 100;
            int extra = System.Math.Max(1, WeekIndex - total);
            double scaled = baseScore * System.Math.Pow(1.5, extra);
            return (int)System.Math.Round(scaled, System.MidpointRounding.AwayFromZero);
        }

        /// <summary>导出为存档数据。</summary>
        public RunSaveData ToSaveData()
        {
            return new RunSaveData
            {
                CharacterId = CharacterId,
                SeedText = SeedText,
                WeekIndex = WeekIndex,
                Gold = Gold,
                ItemIds = new List<string>(ItemIds),
            };
        }

        /// <summary>从存档数据重建运行（不重复发放初始道具，整段持有列表以存档为准）。</summary>
        public static GameRun FromSaveData(cfg.Tables tables, GameplayDatabase database, RunSaveData data)
        {
            var run = new GameRun(tables, database, data.CharacterId, data.SeedText, data.WeekIndex);
            run.Gold = data.Gold;
            run.ItemIds.Clear();
            if (data.ItemIds != null)
            {
                run.ItemIds.AddRange(data.ItemIds);
            }

            return run;
        }

        public int TotalWeeks => _tables.TbWeek.DataList.Count;

        public bool HasNextWeek => WeekIndex < TotalWeeks;

        /// <summary>为当前周构建一局战斗。菜谱、上菜都走以周编号命名的确定性随机流。</summary>
        public BattleSession BuildBattleSession()
        {
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
                    slots.Add(new RecipeSlot($"菜谱{i + 1}", deck));
                }
            }
            else
            {
                Log.Warning($"Character '{CharacterId}' has no valid recipe '{recipeId}'.", "GameRun");
            }

            string modifier = CurrentWeek?.Modifier ?? string.Empty;
            int boardW = BoardWidth;
            int boardH = BoardHeight;
            if (modifier == "small_board")
            {
                boardW = 3;
                boardH = 3;
            }

            var battleStream = GameApp.Random.Stream($"battle_w{WeekIndex}");
            var session = new BattleSession(new GpBoard(boardW, boardH), Database, battleStream, slots, RequiredScore);

            if (modifier == "limit_serve")
            {
                session.MaxServes = 5;
            }

            ApplyPassiveItems(session);
            return session;
        }

        /// <summary>当前周是否为 Boss 周（用于表现层展示）。</summary>
        public bool IsBossWeek => CurrentWeek?.IsBoss ?? false;

        /// <summary>当前周的修正标识（small_board / limit_serve …），无则空串。</summary>
        public string WeekModifier => CurrentWeek?.Modifier ?? string.Empty;

        /// <summary>把被动道具效果汇总成局级修正注入战斗会话。</summary>
        private void ApplyPassiveItems(BattleSession session)
        {
            foreach (string itemId in ItemIds)
            {
                cfg.Item item = _tables.TbItem.GetOrDefault(itemId);
                if (item == null || item.Kind != cfg.ItemKind.Passive)
                {
                    continue;
                }

                switch (item.EffectType)
                {
                    case "FinalAddFlat":
                        session.FinalFlat += item.EffectValue;
                        break;
                    case "FinalAddMult":
                        session.FinalMultiplier *= item.EffectValue;
                        break;
                }
            }
        }
    }
}
