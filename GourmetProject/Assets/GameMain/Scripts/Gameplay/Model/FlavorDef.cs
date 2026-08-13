using System.Collections.Generic;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 食物风味定义（单槽，后者替换前者）。纯数据，由 Game 层从 Luban TbFlavor 适配生成。
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
            string termId)
        {
            Id = id;
            Name = name;
            Desc = desc;
            EffectType = effectType;
            EffectValues = effectValues ?? EmptyValues;
            EffectParams = effectParams ?? EmptyParams;
            TermId = termId ?? string.Empty;
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

        public bool HasTerm => !string.IsNullOrEmpty(TermId);
    }
}
