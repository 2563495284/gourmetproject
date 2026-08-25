using System;
using DG.Tweening;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Hud
{
    public enum ServingOutletState
    {
        WaitingForDishDrag,
        WaitingForPendingConfirmation,
        NoDishCanServe,
    }

    /// <summary>
    /// 经营挑战底部出菜口：负责显示等待玩家拖到餐桌的自动出菜食物。
    /// 具体餐桌预览与提交由 BattleWorldController 完成。
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class ServingOutletView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const float PreparedDishRaycastPadding = 24f;
        private const float ValueBadgeFadeDuration = 0.16f;
        private const float StateFadeOutDuration = 0.10f;
        private const float StateFadeInDuration = 0.16f;
        private const float PreparedDishPopDuration = 0.22f;
        private const float PreparedDishPopStartScale = 0.86f;

        [SerializeField] private RectTransform _preparedDishRoot;
        [SerializeField] private DishIconRenderTexturePreview _dishPreview;
        [SerializeField] private ServingOutletDishHoverTrigger _dishHoverTrigger;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private CanvasGroup _canvasGroup;

        [Header("State Colors")]
        [SerializeField] private Color _readyColor = new Color(0.86f, 0.98f, 0.76f, 1f);
        [SerializeField] private Color _dragColor = new Color(1f, 0.94f, 0.70f, 1f);
        [SerializeField] private Color _blockedColor = new Color(0.72f, 0.72f, 0.68f, 1f);
        [SerializeField] private Image _background;

        private Action<Vector2> _beginDrag;
        private Action<Vector2> _drag;
        private Func<Vector2, bool> _endDrag;
        private bool _dragging;
        private bool _activeItemTargeting;
        private bool _activeItemTargetHighlighted;
        private bool _activeItemTransforming;
        private bool _stateTransitioning;
        private int? _preparedDishId;
        private bool _hasPresentation;
        private OutletPresentation _targetPresentation;
        private Sequence _stateTransition;
        private bool _preparedDishScaleCaptured;
        private Vector3 _preparedDishBaseScale = Vector3.one;

        public ServingOutletState State { get; private set; }

        public RectTransform TipPlacementTarget =>
            _preparedDishRoot != null ? _preparedDishRoot : transform as RectTransform;

        private void Awake()
        {
            EnsureReferences();
        }

        public bool IsPreparedDishAtScreenPoint(Vector2 screenPoint)
        {
            if (State != ServingOutletState.WaitingForDishDrag
                || _stateTransitioning
                || !_preparedDishId.HasValue
                || _preparedDishRoot == null
                || !_preparedDishRoot.gameObject.activeInHierarchy)
            {
                return false;
            }

            Canvas canvas = _preparedDishRoot.GetComponentInParent<Canvas>();
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            return RectTransformUtility.RectangleContainsScreenPoint(
                _preparedDishRoot,
                screenPoint,
                eventCamera);
        }

        public void SetActiveItemTargeting(bool active)
        {
            _activeItemTargeting = active;
            SetActiveItemTargetHighlighted(false);
            RefreshInteractionState();
        }

        public void SetActiveItemTargetHighlighted(bool highlighted)
        {
            _activeItemTargetHighlighted = highlighted
                && State == ServingOutletState.WaitingForDishDrag
                && !_stateTransitioning;
            if (State == ServingOutletState.WaitingForDishDrag)
            {
                SetBackground(_activeItemTargetHighlighted ? _readyColor : _dragColor);
            }
        }

        public bool PlayActiveItemFlavorApplied(DishInstance dish, Action onComplete)
        {
            if (dish == null
                || _stateTransitioning
                || !_preparedDishId.HasValue
                || _preparedDishId.Value != dish.Id
                || _dishPreview == null)
            {
                return false;
            }

            _dragging = false;
            _activeItemTransforming = true;
            _dishHoverTrigger?.CancelHover();
            RefreshInteractionState();

            _dishPreview.FadeValueBadge(false, ValueBadgeFadeDuration, () =>
            {
                if (_dishPreview == null)
                {
                    RestoreInteraction();
                    return;
                }

                _dishPreview.PlayTransformTo(dish, () =>
                {
                    onComplete?.Invoke();
                    if (_dishPreview == null)
                    {
                        RestoreInteraction();
                        return;
                    }

                    _dishPreview.FadeValueBadge(
                        true,
                        ValueBadgeFadeDuration,
                        RestoreInteraction);
                });
            });
            return true;

            void RestoreInteraction()
            {
                _activeItemTransforming = false;
                RefreshInteractionState();
            }
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
            }
        }

        public void Bind(
            BattleSession session,
            Func<bool> onDishHoverEntered,
            Action onDishHoverExited,
            Action<Vector2> beginDrag,
            Action<Vector2> drag,
            Func<Vector2, bool> endDrag)
        {
            EnsureReferences();
            _beginDrag = beginDrag;
            _drag = drag;
            _endDrag = endDrag;
            _dragging = false;
            if (_dishHoverTrigger == null)
            {
                Debug.LogError(
                    $"{nameof(ServingOutletView)} prefab 未绑定 {nameof(ServingOutletDishHoverTrigger)}。",
                    this);
            }
            else
            {
                _dishHoverTrigger.Bind(onDishHoverEntered, onDishHoverExited);
            }

            RecipeSlot slot = session != null && session.Slots.Count > 0 ? session.Slots[0] : null;

            bool limitReached = session != null
                && session.MaxServes >= 0
                && session.ServesUsed >= session.MaxServes;
            if (session?.PreparedServe != null)
            {
                Present(new OutletPresentation(
                    ServingOutletState.WaitingForDishDrag,
                    session.PreparedServe,
                    "拖到餐桌，或拖进垃圾桶丢弃"));
            }
            else if (session != null && !session.IsSettled && session.HasPendingTablePlacements)
            {
                Present(new OutletPresentation(
                    ServingOutletState.WaitingForPendingConfirmation,
                    null,
                    "请先完成餐桌上的上菜或确认"));
            }
            else
            {
                string reason = limitReached
                    ? "本场经营挑战已达到上菜上限"
                    : slot == null || slot.IsEmpty
                        ? "剩余食物不足"
                        : "剩余食物无法摆入餐桌";
                Present(new OutletPresentation(
                    ServingOutletState.NoDishCanServe,
                    null,
                    reason ?? "没有食物可以出菜"));
            }
        }

        private void Present(OutletPresentation presentation)
        {
            State = presentation.State;
            _preparedDishId = presentation.PreparedDishId;
            if (!_hasPresentation)
            {
                _hasPresentation = true;
                _targetPresentation = presentation;
                ApplyPresentation(presentation, prepareDishPop: false);
                NormalizeTransitionVisuals();
                RefreshInteractionState();
                return;
            }

            bool visibleContentChanged = !_targetPresentation.MatchesVisibleContent(presentation);
            _targetPresentation = presentation;
            if (!visibleContentChanged)
            {
                if (_stateTransition == null)
                {
                    ApplyPresentation(presentation, prepareDishPop: false);
                    NormalizeTransitionVisuals();
                    RefreshInteractionState();
                }

                return;
            }

            PlayStateTransition();
        }

        private void PlayStateTransition()
        {
            _stateTransition?.Kill();
            _stateTransition = null;
            _dragging = false;
            _stateTransitioning = true;
            _activeItemTargetHighlighted = false;
            _dishHoverTrigger?.CancelHover();
            RefreshInteractionState();

            var sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetTarget(this)
                .SetLink(gameObject);
            if (_canvasGroup != null)
            {
                sequence.Append(_canvasGroup
                    .DOFade(0f, StateFadeOutDuration)
                    .SetEase(Ease.InQuad));
            }
            else
            {
                sequence.AppendInterval(StateFadeOutDuration);
            }

            sequence.AppendCallback(() =>
                ApplyPresentation(_targetPresentation, prepareDishPop: true));
            if (_canvasGroup != null)
            {
                sequence.Append(_canvasGroup
                    .DOFade(1f, StateFadeInDuration)
                    .SetEase(Ease.OutQuad));
            }
            else
            {
                sequence.AppendInterval(StateFadeInDuration);
            }

            if (_targetPresentation.HasPreparedDish && _preparedDishRoot != null)
            {
                sequence.Join(_preparedDishRoot
                    .DOScale(_preparedDishBaseScale, PreparedDishPopDuration)
                    .SetEase(Ease.OutBack));
            }

            _stateTransition = sequence;
            sequence.OnComplete(() =>
                {
                    _stateTransitioning = false;
                    NormalizeTransitionVisuals();
                    RefreshInteractionState();
                })
                .OnKill(() =>
                {
                    if (ReferenceEquals(_stateTransition, sequence))
                    {
                        _stateTransition = null;
                    }
                });
        }

        private void ApplyPresentation(
            OutletPresentation presentation,
            bool prepareDishPop)
        {
            State = presentation.State;
            PreparedServeDish prepared = presentation.Prepared;
            bool waitingForDrag = presentation.HasPreparedDish;
            _preparedDishId = presentation.PreparedDishId;

            _activeItemTargetHighlighted = false;
            if (_preparedDishRoot != null)
            {
                _preparedDishRoot.gameObject.SetActive(waitingForDrag);
                _preparedDishRoot.localScale = prepareDishPop && waitingForDrag
                    ? _preparedDishBaseScale * PreparedDishPopStartScale
                    : _preparedDishBaseScale;
            }

            if (!waitingForDrag)
            {
                _dishHoverTrigger?.CancelHover();
            }

            if (_dishPreview != null)
            {
                _dishPreview.gameObject.SetActive(waitingForDrag);
                _dishPreview.SetRaycastTarget(waitingForDrag);
                _dishPreview.SetRaycastPadding(waitingForDrag
                    ? Vector4.one * -PreparedDishRaycastPadding
                    : Vector4.zero);
                if (waitingForDrag)
                {
                    _dishPreview.SetDisplayLockedToDefaultFoodCell(true);
                    _dishPreview.Bind(
                        DishPreviewRequest.FromInstance(prepared.Dish));
                    SetDishAlpha(1f);
                }
                else
                {
                    _dishPreview.SetDisplayLockedToDefaultFoodCell(false);
                    _dishPreview.Hide();
                }
            }

            SetText(_statusText, presentation.StatusText);
            switch (presentation.State)
            {
                case ServingOutletState.WaitingForPendingConfirmation:
                    SetBackground(_readyColor);
                    break;
                case ServingOutletState.WaitingForDishDrag:
                    SetBackground(_dragColor);
                    break;
                default:
                    SetBackground(_blockedColor);
                    break;
            }
        }

        private void NormalizeTransitionVisuals()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
            }

            if (_preparedDishRoot != null)
            {
                _preparedDishRoot.localScale = _preparedDishBaseScale;
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (State != ServingOutletState.WaitingForDishDrag
                || _stateTransitioning
                || _activeItemTargeting
                || _activeItemTransforming
                || eventData == null)
            {
                return;
            }

            _dragging = true;
            _dishHoverTrigger?.CancelHover();
            SetDishAlpha(0.35f);
            _beginDrag?.Invoke(eventData.position);
            eventData.Use();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging || eventData == null)
            {
                return;
            }

            _drag?.Invoke(eventData.position);
            eventData.Use();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            bool placed = eventData != null && _endDrag?.Invoke(eventData.position) == true;
            if (!placed)
            {
                SetDishAlpha(1f);
            }

            eventData?.Use();
        }

        private void SetDishAlpha(float alpha)
        {
            if (_dishPreview == null)
            {
                return;
            }

            _dishPreview.SetAlpha(alpha);
        }

        private void SetBackground(Color color)
        {
            if (_background != null)
            {
                _background.color = color;
            }
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }

        private void EnsureReferences()
        {
            _canvasGroup ??= GetComponent<CanvasGroup>();
            if (!_preparedDishScaleCaptured && _preparedDishRoot != null)
            {
                _preparedDishBaseScale = _preparedDishRoot.localScale;
                _preparedDishScaleCaptured = true;
            }
        }

        private void RefreshInteractionState()
        {
            bool allowInteraction = !_stateTransitioning && !_activeItemTransforming;
            if (_canvasGroup != null)
            {
                _canvasGroup.interactable = allowInteraction;
                _canvasGroup.blocksRaycasts = allowInteraction;
            }

            if (_dishHoverTrigger != null)
            {
                _dishHoverTrigger.enabled = allowInteraction && !_activeItemTargeting;
            }
        }

        private void OnDisable()
        {
            Sequence stateTransition = _stateTransition;
            _stateTransition = null;
            stateTransition?.Kill();
            _stateTransitioning = false;
            if (_hasPresentation)
            {
                ApplyPresentation(_targetPresentation, prepareDishPop: false);
            }

            NormalizeTransitionVisuals();
            _dragging = false;
            _activeItemTargeting = false;
            _activeItemTargetHighlighted = false;
            _activeItemTransforming = false;
            _dishPreview?.SetValueBadgeVisible(true);
            if (_dishHoverTrigger != null)
            {
                _dishHoverTrigger.enabled = true;
            }

            RefreshInteractionState();
        }

        private readonly struct OutletPresentation
        {
            public OutletPresentation(
                ServingOutletState state,
                PreparedServeDish prepared,
                string statusText)
            {
                State = state;
                Prepared = prepared;
                StatusText = statusText ?? string.Empty;
            }

            public ServingOutletState State { get; }

            public PreparedServeDish Prepared { get; }

            public string StatusText { get; }

            public bool HasPreparedDish =>
                State == ServingOutletState.WaitingForDishDrag
                && Prepared?.Dish != null;

            public int? PreparedDishId => HasPreparedDish
                ? Prepared.Dish.Id
                : null;

            public bool MatchesVisibleContent(OutletPresentation other)
            {
                return State == other.State
                    && ReferenceEquals(Prepared?.Dish, other.Prepared?.Dish)
                    && string.Equals(
                        StatusText,
                        other.StatusText,
                        StringComparison.Ordinal);
            }
        }
    }
}
