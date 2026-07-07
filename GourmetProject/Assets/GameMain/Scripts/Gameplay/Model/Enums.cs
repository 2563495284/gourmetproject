namespace GourmetProject.Gameplay.Model
{
    /// <summary>简易效果类型。与 Luban 的 cfg.TagEffectType 一一对应，风味/格子标签共用。</summary>
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

    /// <summary>技能前提类型。与 cfg.SkillConditionType 一一对应。见 docs/design/技能.md。</summary>
    public enum SkillConditionType
    {
        None = 0,
        PositionFilled = 1,
        EmptyCell = 2,
        Edge = 3,
        DishCount = 4,
        DishSize = 5,
        ServeOrder = 6,
        SameDish = 7,
        SameKindInRun = 8,
        SameKindInMeal = 9,
        TagCount = 10,
        ShapeMatch = 11,
        RecipeCount = 12,
        LayerCount = 13,
        OccupiedCell = 14,
        CategoryCount = 15,
        SkillTypeCount = 16,
    }

    /// <summary>技能作用域（前提「在哪数」/ 行为「作用到谁」）。与 cfg.SkillScope 一一对应。</summary>
    public enum SkillScope
    {
        Self = 0,
        Adjacent = 1,
        Row = 2,
        Column = 3,
        All = 4,
        Empty = 5,
        Edge = 6,
        Before = 7,
        After = 8,
        Recipe = 9,
        Category = 10,
    }

    /// <summary>比较符。与 cfg.CompareOp 一一对应。</summary>
    public enum CompareOp
    {
        None = 0,
        Gte = 1,
        Lte = 2,
        Eq = 3,
        Gt = 4,
        Lt = 5,
    }

    /// <summary>计数模式：把前提 raw 折算成 count。与 cfg.CountMode 一一对应。</summary>
    public enum CountMode
    {
        Per = 0,
        Reach = 1,
        Gate = 2,
    }

    /// <summary>计数单位：按实例(个) / 按 BaseId 去重(种)。与 cfg.CountUnit 一一对应。</summary>
    public enum CountUnit
    {
        Instances = 0,
        Kinds = 1,
    }

    /// <summary>技能行为类型。与 cfg.SkillActionType 一一对应。</summary>
    public enum SkillActionType
    {
        None = 0,
        AddFlat = 1,
        AddMult = 2,
        TransferScore = 3,
        ExtraSettlement = 4,
        TransferSkills = 5,
        AddLayer = 6,
        ConsumeLayer = 7,
        GrantGold = 8,
        AddMultFlat = 9,
        PermanentAddFlat = 10,
        PermanentAddMult = 11,
        AddCountAs = 12,
        CopySkill = 13,
        TempCopyDish = 14,
    }

    /// <summary>技能触发时机。与 cfg.SkillTrigger 一一对应。</summary>
    public enum SkillTrigger
    {
        OnSettle = 0,
        OnServe = 1,
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
