using System;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 扇形菜谱条里的「购买空菜谱」卡（仅商店态出现）：显示花费文本（如 + 20）+ 点击购买回调。
    /// 固定结构在 RecipeAdd.prefab，由 <see cref="RecipeView"/> 在商店态追加到扇形末尾。
    /// </summary>
    public sealed class RecipeAddCardView : MonoBehaviour
    {
        [SerializeField] private Text _costText;
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

        public void Bind(string cost, bool interactable, Action onClick)
        {
            if (_costText != null)
            {
                _costText.text = cost ?? string.Empty;
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
