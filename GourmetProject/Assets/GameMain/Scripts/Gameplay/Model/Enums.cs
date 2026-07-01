namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 效果类型。与 Luban 的 cfg.TagEffectType 一一对应，技能/风味/格子标签共用。
    /// </summary>
    public enum TagEffectType
    {
        None = 0,
        AddFlat = 1,
        AddMult = 2,
        PerAdjacentDish = 3,
        PerEmptyCell = 4,
        PerOccupiedCell = 5,
        PerDishOnBoard = 6,
    }

    /// <summary>
    /// 道具类型。与 Luban 的 cfg.ItemKind 一一对应。
    /// </summary>
    public enum ItemKind
    {
        Passive = 0,
        Active = 1,
    }

    public static class TagEffectTypeExtensions
    {
        /// <summary>
        /// 倍率类效果：数值是乘数，描述里用 ×，回填时不补正负号（如 ×1.5）。
        /// 其余（加减类）回填时正数补"+"、负数自带"-"，策划模板无需手写符号。
        /// </summary>
        public static bool IsMultiplier(this TagEffectType type)
        {
            return type == TagEffectType.AddMult || type == TagEffectType.PerDishOnBoard;
        }
    }
}
