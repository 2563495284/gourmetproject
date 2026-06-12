namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 标签（技能）定义。纯数据，由 Game 层从 Luban TbTag 适配生成。
    /// </summary>
    public sealed class TagDef
    {
        public TagDef(
            string id,
            string name,
            string desc,
            TagCategory category,
            TagEffectType effectType,
            float effectValue,
            string effectParam,
            string termId)
        {
            Id = id;
            Name = name;
            Desc = desc;
            Category = category;
            EffectType = effectType;
            EffectValue = effectValue;
            EffectParam = effectParam ?? string.Empty;
            TermId = termId ?? string.Empty;
        }

        public string Id { get; }

        public string Name { get; }

        public string Desc { get; }

        public TagCategory Category { get; }

        public TagEffectType EffectType { get; }

        public float EffectValue { get; }

        /// <summary>效果次要参数（如分类过滤、目标标签 id），多数效果不需要。</summary>
        public string EffectParam { get; }

        /// <summary>关联的专有名词 id，非空时菜品详情额外展示。</summary>
        public string TermId { get; }

        public bool HasTerm => !string.IsNullOrEmpty(TermId);
    }
}
