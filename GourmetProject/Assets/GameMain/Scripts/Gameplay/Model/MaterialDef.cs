using System.Collections.Generic;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 餐桌格材质定义（挂在餐桌格上，一格唯一）。纯数据，由 Game 层从 Luban TbMaterial 适配生成。
    /// 材质用独立的 <see cref="Model.MaterialEffectType"/>，按「食物×材质」聚合结算（携带占格数）。
    /// 仍实现 <see cref="IEffectDef"/> 以复用结算明细/来源展示（其 <see cref="EffectType"/> 恒为 None）。
    /// </summary>
    public sealed class MaterialDef : IEffectDef
    {
        private const float LegacyItemRollProbability = 0.5f;
        private static readonly IReadOnlyList<float> EmptyValues = new float[0];
        private static readonly IReadOnlyList<string> EmptyParams = new string[0];

        public MaterialDef(
            string id,
            string name,
            string desc,
            MaterialEffectType materialEffect,
            IReadOnlyList<float> effectValues,
            IReadOnlyList<string> effectParams,
            string termId)
        {
            Id = id;
            Name = name;
            Desc = desc;
            MaterialEffect = materialEffect;
            EffectValues = effectValues ?? EmptyValues;
            EffectParams = effectParams ?? EmptyParams;
            TermId = termId ?? string.Empty;
        }

        public string Id { get; }

        public string Name { get; }

        public string Desc { get; }

        /// <summary>材质效果类型（真正决定结算行为）。</summary>
        public MaterialEffectType MaterialEffect { get; }

        /// <summary>IEffectDef 契约：材质不走风味效果注册表，恒为 None。</summary>
        public FlavorEffectType EffectType => FlavorEffectType.None;

        public IReadOnlyList<float> EffectValues { get; }

        public IReadOnlyList<string> EffectParams { get; }

        public float EffectValue => EffectValues.Count > 0 ? EffectValues[0] : 0f;

        /// <summary>
        /// 获得消耗品判定的配置概率，取 EffectValue[0] 并裁剪到 [0, 1]。
        /// 旧定义未提供 EffectValue 时沿用兼容默认概率。
        /// </summary>
        public float ItemRollProbability
        {
            get
            {
                if (EffectValues.Count == 0 || float.IsNaN(EffectValues[0]))
                {
                    return LegacyItemRollProbability;
                }

                float probability = EffectValues[0];
                if (probability <= 0f)
                {
                    return 0f;
                }

                return probability >= 1f ? 1f : probability;
            }
        }

        public string EffectParam => EffectParams.Count > 0 ? EffectParams[0] : string.Empty;

        /// <summary>阈值参数（金/银「占 N 格」判定），取 EffectParam[0]，缺省 2，下限 1。</summary>
        public int ThresholdParam
        {
            get
            {
                if (EffectParams.Count > 0 && int.TryParse(EffectParams[0], out int n) && n > 0)
                {
                    return n;
                }

                return 2;
            }
        }

        public string TermId { get; }

        public bool HasTerm => !string.IsNullOrEmpty(TermId);
    }
}
