using UnityEngine;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 行动轴 Boss 节点 hover Tips（原型图第 1 张）：
    /// 标题「盛宴主题：{Boss名}」+ 机制/技能描述框 + 底部「美味度要求：{目标分}」。
    /// 固定结构在 BossFeastTipView.prefab，内容由 <see cref="Bind"/> 数据驱动。
    /// </summary>
    public sealed class BossFeastTipView : ActionTipView
    {
        /// <summary>字段级绑定，供无完整配置时使用。</summary>
        public void Bind(string debuffName, string debuffDesc, long requiredScore, Sprite icon = null)
        {
            ApplyTexts(debuffName, debuffDesc);
            ApplyFooter($"美味度要求：{requiredScore:N0}");
        }
    }
}
