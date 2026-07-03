using System;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 扇形菜谱条里的一本菜谱本卡：显示容量文本（如 10/12）+ 点击回调（战斗态用于上菜，非战斗态置灰）。
    /// 固定结构在 Recipe.prefab，由 <see cref="RecipeView"/> 数据驱动实例化并做扇形排布/补间动画。
    /// </summary>
    public sealed class RecipeCardView : MonoBehaviour
    {
        [SerializeField] private Text _capacityText;
        [SerializeField] private Button _button;
        [SerializeField] private CanvasGroup _canvasGroup;

        private RectTransform _rect;

        public RectTransform Rect
        {
            get
            {
                if (_rect == null)
                {
                    _rect = (RectTransform)transform;
                }

                return _rect;
            }
        }

        public CanvasGroup CanvasGroup
        {
            get
            {
                if (_canvasGroup == null)
                {
                    _canvasGroup = GetComponent<CanvasGroup>();
                    if (_canvasGroup == null)
                    {
                        _canvasGroup = gameObject.AddComponent<CanvasGroup>();
                    }
                }

                return _canvasGroup;
            }
        }

        public void Bind(string capacity, bool interactable, Action onClick)
        {
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
