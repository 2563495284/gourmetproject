using GourmetProject.Game;
using GourmetProject.Game.Meta;
using UnityEngine;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 被动 / 主动道具 hover Tips（原型图第 4 张）：
    /// 标题「道具名」+ 效果描述框；无底部信息行。
    /// 固定结构在 ItemTipView.prefab，内容由 <see cref="Bind"/> 数据驱动。
    /// </summary>
    public sealed class ItemTipView : ActionTipView
    {
        private const string DefaultEmoji = "\U0001F9EA";

        /// <summary>用配置道具绑定：标题取道具名，描述取效果说明，图标按名称约定加载。</summary>
        public void Bind(ItemDefinition item)
        {
            if (item == null)
            {
                Bind(string.Empty, string.Empty, null);
                return;
            }

            Sprite icon = ContentIconLoader.LoadItem(item);
            Bind(item.Name, item.Desc, icon);
        }

        /// <summary>字段级绑定。</summary>
        public void Bind(string itemName, string desc, Sprite icon = null)
        {
            ApplyTexts(itemName, desc);
            ApplyFooter(null);
        }
    }
}
