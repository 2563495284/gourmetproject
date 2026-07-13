using System.Collections.Generic;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 携带结算展示数据的共享契约（风味 / 餐桌材质）。
    /// 风味通过 FlavorEffectType 派发，材质通过 MaterialEffectType 派发；两者复用效果数值、参数与明细展示字段。
    /// </summary>
    public interface IEffectDef
    {
        string Id { get; }

        string Name { get; }

        string Desc { get; }

        FlavorEffectType EffectType { get; }

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
