using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>结算效果来源类型。数值顺序用于同阶段内的稳定排序。</summary>
    public enum ScoreSourceType
    {
        Dish = 0,
        DishTag = 1,
        CellTag = 2,
        BoardTag = 3,
        Relic = 4,
        WeekModifier = 5,
        FinalModifier = 6,
    }

    /// <summary>记录一个结算效果来自哪里，供排序、调试和 UI 明细展示。</summary>
    public sealed class ScoreSource
    {
        public ScoreSource(
            ScoreSourceType type,
            string id,
            string name,
            int dishInstanceId = 0,
            string dishId = null,
            GridPos? cell = null)
        {
            Type = type;
            Id = id ?? string.Empty;
            Name = name ?? Id;
            DishInstanceId = dishInstanceId;
            DishId = dishId ?? string.Empty;
            Cell = cell;
        }

        public ScoreSourceType Type { get; }

        public string Id { get; }

        public string Name { get; }

        public int DishInstanceId { get; }

        public string DishId { get; }

        public GridPos? Cell { get; }

        public static ScoreSource Dish(DishInstance dish)
        {
            return new ScoreSource(
                ScoreSourceType.Dish,
                dish?.Def?.Id,
                dish?.Def?.Name,
                dish != null ? dish.Id : 0,
                dish?.Def?.Id);
        }

        public static ScoreSource DishTag(TagDef tag, DishInstance dish)
        {
            return new ScoreSource(
                ScoreSourceType.DishTag,
                tag?.Id,
                tag?.Name,
                dish != null ? dish.Id : 0,
                dish?.Def?.Id);
        }

        public static ScoreSource CellTag(TagDef tag, DishInstance dish, GridPos cell)
        {
            return new ScoreSource(
                ScoreSourceType.CellTag,
                tag?.Id,
                tag?.Name,
                dish != null ? dish.Id : 0,
                dish?.Def?.Id,
                cell);
        }

        public static ScoreSource BoardTag(string id, string name)
        {
            return new ScoreSource(ScoreSourceType.BoardTag, id, name);
        }

        public static ScoreSource Relic(string id, string name)
        {
            return new ScoreSource(ScoreSourceType.Relic, id, name);
        }

        public static ScoreSource WeekModifier(string id, string name)
        {
            return new ScoreSource(ScoreSourceType.WeekModifier, id, name);
        }

        public static ScoreSource FinalModifier(string id, string name)
        {
            return new ScoreSource(ScoreSourceType.FinalModifier, id, name);
        }
    }
}
