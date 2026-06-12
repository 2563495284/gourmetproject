namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 标签分类。与 Luban 的 cfg.TagCategory 一一对应，由 Game 层适配映射，保持本程序集纯净。
    /// </summary>
    public enum TagCategory
    {
        Inherent = 0,
        UniqueA = 1,
        UniqueB = 2,
    }

    /// <summary>
    /// 标签效果类型。与 Luban 的 cfg.TagEffectType 一一对应。
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
}
