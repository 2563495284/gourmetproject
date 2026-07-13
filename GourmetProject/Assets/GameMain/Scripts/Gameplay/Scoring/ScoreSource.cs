using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>结算效果来源类型。数值顺序用于同阶段内的稳定排序。</summary>
    public enum ScoreSourceType
    {
        Dish = 0,
        DishSkill = 1,
        DishFlavor = 2,
        Material = 3,
        TableTag = 4,
        Relic = 5,
        WeekModifier = 6,
        FinalModifier = 7,
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

        public static ScoreSource DishSkill(SkillDef skill, DishInstance dish)
        {
            return new ScoreSource(
                ScoreSourceType.DishSkill,
                skill?.Id,
                skill?.Name,
                dish != null ? dish.Id : 0,
                dish?.Def?.Id);
        }

        /// <summary>
        /// 由甜蜜传递/技能复制获得的技能来源：Id 仍为技能 id，但 Name 用来源标签（如「马卡龙&lt;甜蜜传递&gt;」），
        /// 使结算明细显示该技能来自别的菜。类型仍归为 DishSkill 保持排序一致。
        /// </summary>
        public static ScoreSource TransferredDishSkill(SkillDef skill, DishInstance dish, string sourceLabel)
        {
            return new ScoreSource(
                ScoreSourceType.DishSkill,
                skill?.Id,
                string.IsNullOrEmpty(sourceLabel) ? skill?.Name : sourceLabel,
                dish != null ? dish.Id : 0,
                dish?.Def?.Id);
        }

        public static ScoreSource DishFlavor(IEffectDef tag, DishInstance dish)
        {
            return new ScoreSource(
                ScoreSourceType.DishFlavor,
                tag?.Id,
                tag?.Name,
                dish != null ? dish.Id : 0,
                dish?.Def?.Id);
        }

        public static ScoreSource Material(IEffectDef tag, DishInstance dish, GridPos cell)
        {
            return new ScoreSource(
                ScoreSourceType.Material,
                tag?.Id,
                tag?.Name,
                dish != null ? dish.Id : 0,
                dish?.Def?.Id,
                cell);
        }

        public static ScoreSource TableTag(string id, string name)
        {
            return new ScoreSource(ScoreSourceType.TableTag, id, name);
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
