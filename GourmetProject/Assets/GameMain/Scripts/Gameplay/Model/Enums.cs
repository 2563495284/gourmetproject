namespace GourmetProject.Gameplay.Model
{
    /// <summary>风味效果类型。与 Luban 的 cfg.FlavorEffectType 一一对应；餐桌材质使用 MaterialEffectType。</summary>
    public enum FlavorEffectType
    {
        None = 0,
        AddFlat = 1,
        AddMult = 2,
        PerAdjacentDish = 3,
        PerEmptyCell = 4,
        PerOccupiedCell = 5,
        PerDishOnBoard = 6,

        /// <summary>结算时获得 EffectValue 金币（锈）。</summary>
        GrantGold = 7,

        /// <summary>贡献 EffectValue 到该菜结算优先级层级（甜=+1、苦=-1）。不产生分数效果，由结算前排序读取。</summary>
        SettlementLayer = 8,

        /// <summary>麻：使食物逆时针旋转 EffectValue×90 度（默认 1）。不产生分数效果，由上菜时读取并旋转形状。</summary>
        Rotate = 9,

        /// <summary>酸：整体结算末尾，未上菜时使场上同菜谱食物倍率 ×EffectValue（1.5）。由未上菜结算源读取。</summary>
        SourRecipeMult = 10,

        /// <summary>咸：整体结算末尾，未上菜时使场上每个同菜谱食物获得 EffectValue 金币（2）。由未上菜结算源读取。</summary>
        SaltyRecipeGold = 11,
    }

    /// <summary>
    /// 餐桌材质效果类型。与 Luban 的 cfg.MaterialEffectType 一一对应。
    /// 材质按「食物×材质」聚合结算（携带该食物占据本材质的格数 cellCount），在 Materials 阶段（技能结算后）触发。
    /// </summary>
    public enum MaterialEffectType
    {
        None = 0,

        /// <summary>分数 +EffectValue（临时，樱桃木）。每食物每材质触发一次。</summary>
        AddFlat = 1,

        /// <summary>分数永久 +EffectValue（胡桃木）。</summary>
        PermanentAddFlat = 2,

        /// <summary>倍率 +EffectValue（临时，大理石）。</summary>
        AddMultFlat = 3,

        /// <summary>倍率 ×EffectValue（黑曜石）。</summary>
        AddMult = 4,

        /// <summary>本食物每占 1 格本材质，倍率 +EffectValue（翡翠）。</summary>
        AddMultFlatPerCell = 5,

        /// <summary>本食物占据 &gt;= 阈值(EffectParam) 格本材质时，获得 EffectValue 金币（金）。</summary>
        GrantGoldIfCellCount = 6,

        /// <summary>本食物占据 &gt;= 阈值(EffectParam) 格本材质时，登记一次 1/3 获得主动道具的掷骰请求（银）。</summary>
        GrantItemRollIfCellCount = 7,
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
        SkillCount = 17,
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
        Round = 11,
        RoundAndSelf = 12,
        RowAndSelf = 13,
        ColumnAndSelf = 14,
        Other = 15,
        CakeBuff = 16,
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
        TransferSkills = 5,
        AddLayer = 6,
        ConsumeLayer = 7,
        GrantGold = 8,
        AddMultFlat = 9,
        PermanentAddFlat = 10,
        AddCountAs = 12,
        CopySkill = 13,
        TriggerSweetTransfer = 15,
        AddCurrentMult = 17,
    }

    /// <summary>技能触发时机。与 cfg.SkillTrigger 一一对应。</summary>
    public enum SkillTrigger
    {
        OnSettle = 0,
        OnServe = 1,
    }

    public static class FlavorEffectTypeExtensions
    {
        /// <summary>
        /// 倍率类效果：数值是乘数，描述里用 ×，回填时不补正负号（如 ×1.5）。
        /// 其余（加减类）回填时正数补"+"、负数自带"-"，策划模板无需手写符号。
        /// </summary>
        public static bool IsMultiplier(this FlavorEffectType type)
        {
            return type == FlavorEffectType.AddMult || type == FlavorEffectType.PerDishOnBoard;
        }
    }

    public static class MaterialEffectTypeExtensions
    {
        /// <summary>倍率类效果（数值是乘数，描述用 ×，不补正负号）。仅黑曜石 AddMult。</summary>
        public static bool IsMultiplier(this MaterialEffectType type)
        {
            return type == MaterialEffectType.AddMult;
        }
    }
}
