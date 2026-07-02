using System;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 菜谱抽屉里的一条菜品条目：名称 + 副信息（剩余/份数）+ 点击回调（战斗中用于上菜，展示态置灰）。
    /// 固定结构在 RecipeEntryView.prefab，由 <see cref="RecipeDrawer"/> 数据驱动实例化。
    /// </summary>
    public sealed class RecipeEntryView : MonoBehaviour
    {
        [SerializeField] private Text _nameText;
        [SerializeField] private Text _subText;
        [SerializeField] private Button _button;

        public void Bind(string name, string sub, bool interactable, Action onClick)
        {
            if (_nameText != null)
            {
                _nameText.text = name ?? string.Empty;
            }

            if (_subText != null)
            {
                _subText.text = sub ?? string.Empty;
                _subText.gameObject.SetActive(!string.IsNullOrEmpty(sub));
            }

            if (_button != null)
            {
                _button.onClick.RemoveAllListeners();
                _button.interactable = interactable;
                if (onClick != null)
                {
                    _button.onClick.AddListener(() => onClick());
                }
            }
        }
    }
}
