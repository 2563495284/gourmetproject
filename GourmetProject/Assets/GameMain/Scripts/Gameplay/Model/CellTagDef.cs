using System.Collections.Generic;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 棋盘格子标签定义（挂在胃碎片格上）。纯数据，由 Game 层从 Luban TbCellTag 适配生成。
    /// </summary>
    public sealed class CellTagDef : IEffectDef
    {
        private static readonly IReadOnlyList<float> EmptyValues = new float[0];
        private static readonly IReadOnlyList<string> EmptyParams = new string[0];

        public CellTagDef(
            string id,
            string name,
            string desc,
            TagEffectType effectType,
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

        public TagEffectType EffectType { get; }

        public IReadOnlyList<float> EffectValues { get; }

        public IReadOnlyList<string> EffectParams { get; }

        public float EffectValue => EffectValues.Count > 0 ? EffectValues[0] : 0f;

        public string EffectParam => EffectParams.Count > 0 ? EffectParams[0] : string.Empty;

        public string TermId { get; }

        public bool HasTerm => !string.IsNullOrEmpty(TermId);
    }
}
