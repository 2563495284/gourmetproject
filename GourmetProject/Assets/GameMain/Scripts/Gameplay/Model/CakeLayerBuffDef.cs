namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 欢乐蛋糕层数分段 buff 的一档定义（纯数据，由 Game 层从 Luban 表 TbCakeLayerBuff 适配）。
    /// 结算时（所有食物/风味/标签之后、汇总之前）读全局层数，
    /// 全局层数 &gt;= <see cref="Threshold"/> 的每一档都累计应用到 <see cref="Category"/> 分类的所有食物；
    /// 效果按 <see cref="EffectType"/> 分派：
    /// <list type="bullet">
    /// <item><see cref="SkillActionType.AddFlat"/>：基础分 += <see cref="ValuePerLayer"/> × 层数。</item>
    /// <item><see cref="SkillActionType.AddMultFlat"/>：倍率 += <see cref="ValuePerLayer"/> × 层数。</item>
    /// <item><see cref="SkillActionType.AddMult"/>：倍率 ×= (<see cref="ValuePerLayer"/> × 层数)。</item>
    /// </list>
    /// </summary>
    public sealed class CakeLayerBuffDef
    {
        public CakeLayerBuffDef(string id, int order, int threshold, string category, SkillActionType effectType, float valuePerLayer, string desc)
        {
            Id = id ?? string.Empty;
            Order = order;
            Threshold = threshold;
            Category = category ?? string.Empty;
            EffectType = effectType;
            ValuePerLayer = valuePerLayer;
            Desc = desc ?? string.Empty;
        }

        public string Id { get; }

        /// <summary>同分类内多档的执行顺序（升序）。</summary>
        public int Order { get; }

        /// <summary>层数阈值：全局层数 &gt;= 该值时本档生效。</summary>
        public int Threshold { get; }

        /// <summary>作用分类（如 cake）。</summary>
        public string Category { get; }

        /// <summary>效果类型（复用 SkillActionType：AddFlat / AddMultFlat / AddMult）。</summary>
        public SkillActionType EffectType { get; }

        /// <summary>每层系数（实际数值 = ValuePerLayer × 层数）。</summary>
        public float ValuePerLayer { get; }

        public string Desc { get; }
    }
}
