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
        /// <summary>默认恶魔 emoji 兜底（缺 Boss 头像图时用）。</summary>
        private const string DefaultEmoji = "\U0001F608";

        /// <summary>直接用配置 Boss 绑定：标题取 Boss 名，描述取机制文案，底行取目标分。</summary>
        public void Bind(cfg.Boss boss, string mechanicDesc, long requiredScore, Sprite icon = null)
        {
            string bossName = boss != null ? boss.Name : string.Empty;
            Bind(bossName, mechanicDesc, requiredScore, icon);
        }

        /// <summary>字段级绑定，供无完整配置时使用。</summary>
        public void Bind(string bossName, string mechanicDesc, long requiredScore, Sprite icon = null)
        {
            ApplyIcon(icon, DefaultEmoji);
            ApplyTexts($"盛宴主题：{bossName}", mechanicDesc);
            ApplyFooter($"美味度要求：{requiredScore:N0}");
        }
    }
}
