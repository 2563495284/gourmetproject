using System.Collections.Generic;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 携带简易结算效果的定义的共享契约（风味 / 餐桌格子标签）。
    /// 两者各有独立配置表与运行时类型，但效果字段与效果引擎一致，通过本接口统一被结算管线消费。
    /// </summary>
    public interface IEffectDef
    {
        string Id { get; }

        string Name { get; }

        string Desc { get; }

        TagEffectType EffectType { get; }

        /// <summary>效果数值列表；不同效果类型可按约定使用多个数值。</summary>
        IReadOnlyList<float> EffectValues { get; }

        /// <summary>效果次要参数列表（如分类过滤、目标 id），多数效果不需要。</summary>
        IReadOnlyList<string> EffectParams { get; }

        /// <summary>首个效果数值；列表为空时为 0。</summary>
        float EffectValue { get; }

        /// <summary>首个效果参数；列表为空时为空串。</summary>
        string EffectParam { get; }

        /// <summary>关联的专有名词 id，非空时菜品详情额外展示。</summary>
        string TermId { get; }

        bool HasTerm { get; }
    }
}
