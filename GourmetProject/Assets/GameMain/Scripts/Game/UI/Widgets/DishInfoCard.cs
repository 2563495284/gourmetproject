using GourmetProject.Game.UI.Common;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Widgets
{
    /// <summary>
    /// 食物 Tips 里的一张信息卡（标题 + 描述），技能卡与风味详情卡共用。
    /// 固定结构在 DishInfoCard.prefab，内容由 <see cref="Set"/> 数据驱动。
    /// 挂 ContentSizeFitter（竖向 Preferred），配合父级布局组自适应高度。
    /// </summary>
    public sealed class DishInfoCard : MonoBehaviour
    {
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _descText;

        public void Set(string title, string desc)
        {
            if (_titleText != null)
            {
                // 空标题（如无术语名的技能）隐藏标题行，只显示描述。
                bool hasTitle = !string.IsNullOrEmpty(title);
                _titleText.text = title;
                if (_titleText.gameObject.activeSelf != hasTitle)
                {
                    _titleText.gameObject.SetActive(hasTitle);
                }
            }

            if (_descText != null)
            {
                SemanticDescriptionFormatter.Set(_descText, desc);
            }
        }
    }
}
