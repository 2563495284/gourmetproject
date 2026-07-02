using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Widgets
{
    /// <summary>
    /// 菜品 Tips 里的一张信息卡（标题 + 描述），技能卡与风味详情卡共用。
    /// 固定结构在 DishInfoCard.prefab，内容由 <see cref="Set"/> 数据驱动。
    /// 挂 ContentSizeFitter（竖向 Preferred），配合父级布局组自适应高度。
    /// </summary>
    public sealed class DishInfoCard : MonoBehaviour
    {
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _descText;

        public void Set(string title, string desc)
        {
            if (_titleText != null)
            {
                _titleText.text = title;
            }

            if (_descText != null)
            {
                _descText.text = desc;
            }
        }
    }
}
