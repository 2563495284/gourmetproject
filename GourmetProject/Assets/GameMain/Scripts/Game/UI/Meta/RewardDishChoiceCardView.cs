using System;
using DG.Tweening;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Model;
using GourmetProject.Game.Meta;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 菜品奖励选择页中的候选卡片。负责展示、点击和悬浮事件，不再参与拖拽。
    /// </summary>
    public sealed class RewardDishChoiceCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Button _button;
        [SerializeField] private DishIconRenderTexturePreview _dishPreview;
        [SerializeField] private CanvasGroup _canvasGroup;

        private int _choiceIndex = -1;
        private bool _resolved;
        private bool _hovered;
        private Action<RewardDishChoiceCardView, int> _onClick;
        private Action<RewardDishChoiceCardView> _onHoverEntered;
        private Action<RewardDishChoiceCardView> _onHoverExited;
        private Tween _failureTween;

        public int ChoiceIndex => _choiceIndex;

        public RectTransform TipPlacementTarget
        {
            get
            {
                EnsureRefs();
                return _dishPreview != null
                    ? _dishPreview.transform as RectTransform
                    : transform as RectTransform;
            }
        }

        public void Bind(
            RewardChoice choice,
            Sprite icon,
            int choiceIndex,
            Action<RewardDishChoiceCardView, int> onClick,
            Action<RewardDishChoiceCardView> onHoverEntered = null,
            Action<RewardDishChoiceCardView> onHoverExited = null)
        {
            Bind(choice, null, icon, choiceIndex, onClick, onHoverEntered, onHoverExited);
        }

        public void Bind(
            RewardChoice choice,
            DishDef dish,
            Sprite icon,
            int choiceIndex,
            Action<RewardDishChoiceCardView, int> onClick,
            Action<RewardDishChoiceCardView> onHoverEntered = null,
            Action<RewardDishChoiceCardView> onHoverExited = null)
        {
            EnsureRefs();
            _choiceIndex = choiceIndex;
            _resolved = false;
            _onClick = onClick;
            _onHoverEntered = onHoverEntered;
            _onHoverExited = onHoverExited;

            if (_dishPreview != null && dish != null)
            {
                _dishPreview.Bind(dish, icon, dish.Deliciousness);
            }
            else
            {
                _dishPreview?.Hide();
            }

            SetResolved(false);
            WireButton();
        }

        public void SetResolved(bool resolved)
        {
            EnsureRefs();
            _resolved = resolved;
            if (_button != null)
            {
                _button.interactable = !resolved;
            }

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = resolved ? 0.45f : 1f;
                _canvasGroup.interactable = !resolved;
                _canvasGroup.blocksRaycasts = !resolved;
            }
        }

        public void PlayTargetFailed()
        {
            RectTransform target = _dishPreview != null
                ? _dishPreview.transform as RectTransform
                : transform as RectTransform;
            if (target == null)
            {
                return;
            }

            _failureTween?.Kill();
            Vector2 origin = target.anchoredPosition;
            _failureTween = DOVirtual.Float(0f, 1f, 0.25f, t =>
                {
                    if (target == null)
                    {
                        return;
                    }

                    float offset = Mathf.Sin(t * Mathf.PI * 12f) * 9f * (1f - t);
                    target.anchoredPosition = origin + new Vector2(offset, 0f);
                })
                .SetEase(Ease.Linear)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    if (target != null)
                    {
                        target.anchoredPosition = origin;
                    }
                });
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_resolved || _hovered)
            {
                return;
            }

            _hovered = true;
            _onHoverEntered?.Invoke(this);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            EndHover();
        }

        private void OnDisable()
        {
            EndHover();
        }

        private void OnDestroy()
        {
            _failureTween?.Kill();
        }

        private void EnsureRefs()
        {
            if (_button == null)
            {
                _button = GetComponent<Button>() ?? GetComponentInChildren<Button>(true);
            }

            if (_dishPreview == null)
            {
                _dishPreview = GetComponentInChildren<DishIconRenderTexturePreview>(true);
            }

            if (_canvasGroup == null)
            {
                _canvasGroup = GetComponent<CanvasGroup>();
                if (_canvasGroup == null)
                {
                    _canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }
        }

        private void WireButton()
        {
            if (_button == null)
            {
                return;
            }

            _button.onClick.RemoveAllListeners();
            _button.onClick.AddListener(() =>
            {
                if (!_resolved)
                {
                    _onClick?.Invoke(this, _choiceIndex);
                }
            });
        }

        private void EndHover()
        {
            if (!_hovered)
            {
                return;
            }

            _hovered = false;
            _onHoverExited?.Invoke(this);
        }
    }
}
