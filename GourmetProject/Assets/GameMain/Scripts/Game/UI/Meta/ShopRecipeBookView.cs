using System;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 商店底部的一本菜谱条。结构固定在 ShopRecipeBookView.prefab，运行时只绑定标题和容量。
    /// </summary>
    public sealed class ShopRecipeBookView : MonoBehaviour
    {
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _capacityText;
        [SerializeField] private Button _button;

        public void Bind(string title, string capacity, bool interactable, Action onClick)
        {
            if (_titleText != null)
            {
                _titleText.text = title ?? string.Empty;
            }

            if (_capacityText != null)
            {
                _capacityText.text = capacity ?? string.Empty;
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
