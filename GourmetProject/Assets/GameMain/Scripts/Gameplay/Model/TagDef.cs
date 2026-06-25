using System.Collections.Generic;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 标签（技能）定义。纯数据，由 Game 层从 Luban TbTag 适配生成。
    /// </summary>
    public sealed class TagDef
    {
        private static readonly IReadOnlyList<float> EmptyValues = new float[0];
        private static readonly IReadOnlyList<string> EmptyParams = new string[0];

        public TagDef(
            string id,
            string name,
            string desc,
            TagCategory category,
            TagEffectType effectType,
            IReadOnlyList<float> effectValues,
            IReadOnlyList<string> effectParams,
            string termId)
        {
            Id = id;
            Name = name;
            Desc = desc;
            Category = category;
            EffectType = effectType;
            EffectValues = effectValues ?? EmptyValues;
            EffectParams = effectParams ?? EmptyParams;
            TermId = termId ?? string.Empty;
        }

        public string Id { get; }

        public string Name { get; }

        public string Desc { get; }

        public TagCategory Category { get; }

        public TagEffectType EffectType { get; }

        /// <summary>效果数值列表；不同效果类型可按约定使用多个数值。</summary>
        public IReadOnlyList<float> EffectValues { get; }

        /// <summary>效果次要参数列表（如分类过滤、目标标签 id），多数效果不需要。</summary>
        public IReadOnlyList<string> EffectParams { get; }

        /// <summary>首个效果数值；列表为空时为 0。多数效果只用单个数值，可直接用此便捷属性。</summary>
        public float EffectValue => EffectValues.Count > 0 ? EffectValues[0] : 0f;

        /// <summary>首个效果参数；列表为空时为空串。</summary>
        public string EffectParam => EffectParams.Count > 0 ? EffectParams[0] : string.Empty;

        /// <summary>关联的专有名词 id，非空时菜品详情额外展示。</summary>
        public string TermId { get; }

        public bool HasTerm => !string.IsNullOrEmpty(TermId);
    }
}
