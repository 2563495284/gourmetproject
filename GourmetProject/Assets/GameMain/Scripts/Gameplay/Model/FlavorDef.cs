using System.Collections.Generic;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 菜品风味定义（单槽，后者替换前者）。纯数据，由 Game 层从 Luban TbFlavor 适配生成。
    /// </summary>
    public sealed class FlavorDef : IEffectDef
    {
        private static readonly IReadOnlyList<float> EmptyValues = new float[0];
        private static readonly IReadOnlyList<string> EmptyParams = new string[0];

        public FlavorDef(
            string id,
            string name,
            string desc,
            FlavorEffectType effectType,
            IReadOnlyList<float> effectValues,
            IReadOnlyList<string> effectParams,
            string termId,
            int sortOrder = 0)
        {
            Id = id;
            Name = name;
            Desc = desc;
            EffectType = effectType;
            EffectValues = effectValues ?? EmptyValues;
            EffectParams = effectParams ?? EmptyParams;
            TermId = termId ?? string.Empty;
            SortOrder = sortOrder;
        }

        public string Id { get; }

        public string Name { get; }

        public string Desc { get; }

        public FlavorEffectType EffectType { get; }

        public IReadOnlyList<float> EffectValues { get; }

        public IReadOnlyList<string> EffectParams { get; }

        public float EffectValue => EffectValues.Count > 0 ? EffectValues[0] : 0f;

        public string EffectParam => EffectParams.Count > 0 ? EffectParams[0] : string.Empty;

        public string TermId { get; }

        /// <summary>同一菜品本体下的风味展示顺序；数值越小越靠前。</summary>
        public int SortOrder { get; }

        public bool HasTerm => !string.IsNullOrEmpty(TermId);
    }
}
