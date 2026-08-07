using System;
using System.Collections.Generic;
using DG.Tweening;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Game.Visual;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 食谱中的单个食物卡。奖励流程支持拖拽；查看/删除/消耗品模式禁用拖拽并响应点击。
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class RecipeEditDishView : MonoBehaviour, IPointerDownHandler, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private const float ReturnFlyDuration = 0.22f;
        private const float FinishedDishAlpha = 0.35f;
        private static readonly Vector2 FloatingAnchor = new(0.5f, 0.5f);
        private static readonly Color WaitingForPlacementOverlayColor =
            new(0.20f, 0.58f, 0.27f, 0.12f);

        [SerializeField] private Button _button;
        [FormerlySerializedAs("_shapePreview")]
        [SerializeField] private DishIconRenderTexturePreview _dishPreview;
        [SerializeField] private RecipeWarehouseItemFrameGraphic _warehouseHighlight;
        [SerializeField] private Image _battleStatusOverlay;

        private CanvasGroup _canvasGroup;
        private RectTransform _rect;
        private Transform _originalParent;
        private Vector2 _originalAnchorMin;
        private Vector2 _originalAnchorMax;
        private Vector2 _originalPivot;
        private Vector2 _originalSizeDelta;
        private Vector2 _originalAnchoredPosition;
        private Vector3 _originalLocalScale;
        private Quaternion _originalLocalRotation;
        private Vector3 _originalWorldCenter;
        private int _originalSiblingIndex;
        private Canvas _dragCanvas;
        private Action<RecipeEditDishView> _onClick;
        private Action<RecipeEditDishView> _onBeginDrag;
        private Func<RecipeEditDishView, bool> _onDragCancelled;
        private Action<RecipeEditDishView> _onHoverEnter;
        private Action<RecipeEditDishView> _onHoverExit;
        private bool _dragEnabled = true;
        private bool _dragging;
        private bool _dropHandled;
        private bool _hovered;
        private ScrollRect _panScrollRect;
        private DishIconPreviewMode _previewMode;
        private int? _deliciousnessOverride;
        private bool _warehouseClickable;
        private bool _cannotPlace;
        private bool _suppressClick;
        private Tween _failureTween;
        private RectTransform _failureFeedbackRect;
        private Vector2 _failureFeedbackOrigin;

        public int BookIndex { get; private set; }
        public int DishIndex { get; private set; }
        public DishDef DishDef { get; private set; }
        public Vector2Int DisplayedGridSize { get; private set; } = Vector2Int.one;

        public bool ContainsScreenPoint(Vector2 screenPoint)
        {
            RectTransform rect = _rect != null ? _rect : transform as RectTransform;
            if (rect == null)
            {
                return false;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, cam);
        }

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _rect = (RectTransform)transform;
            EnsureButton();
            ResolveDishPreview();
        }

        public void Bind(
            string name,
            string shape,
            int bookIndex,
            int dishIndex,
            bool dragEnabled = true,
            Action<RecipeEditDishView> onClick = null,
            DishDef dishDef = null,
            Action<RecipeEditDishView> onBeginDrag = null,
            Func<RecipeEditDishView, bool> onDragCancelled = null,
            Action<RecipeEditDishView> onHoverEnter = null,
            Action<RecipeEditDishView> onHoverExit = null,
            IReadOnlyList<string> flavorIds = null,
            DishIconPreviewMode previewMode = DishIconPreviewMode.Card,
            BattleRecipeEntryStatus? battleStatus = null,
            int? deliciousnessOverride = null)
        {
            BookIndex = bookIndex;
            DishIndex = dishIndex;
            DishDef = dishDef;
            _dragEnabled = dragEnabled;
            _onClick = onClick;
            _onBeginDrag = onBeginDrag;
            _onDragCancelled = onDragCancelled;
            _onHoverEnter = onHoverEnter;
            _onHoverExit = onHoverExit;
            _dropHandled = false;
            _hovered = false;
            _previewMode = previewMode;
            _deliciousnessOverride = deliciousnessOverride;
            _warehouseClickable = onClick != null;
            DisplayedGridSize = DishIconRenderTexturePreview.DisplayedGridSizeFor(
                dishDef,
                flavorIds);

            if (_dishPreview != null)
            {
                if (dishDef != null)
                {
                    _dishPreview.Bind(
                        dishDef,
                        deliciousnessOverride: deliciousnessOverride,
                        flavorIds: flavorIds,
                        mode: previewMode);
                    DisplayedGridSize = _dishPreview.DisplayedGridSize;
                }
                else
                {
                    _dishPreview.Hide();
                }
            }

            ConfigureBattleStatus(battleStatus);
            ConfigureWarehouseStyle();
            EnsureButton();
            if (_button != null)
            {
                _button.interactable = dragEnabled || onClick != null;
            }
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (_dragEnabled)
            {
                return;
            }

            ResolvePanScrollRect();
            _panScrollRect?.OnInitializePotentialDrag(eventData);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _suppressClick = false;
        }

        public void MarkDropHandled()
        {
            _dropHandled = true;
            _dragging = false;
            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.alpha = 1f;
            }
        }

        public void PrepareAsFloating()
        {
            Vector3 center = _rect.TransformPoint(_rect.rect.center);
            PrepareAsFloating(center);
        }

        public void SetInteractableAfterAnimation(bool blocksRaycasts)
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = blocksRaycasts;
                _canvasGroup.alpha = 1f;
            }
        }

        public void PlayReturnToOriginalPosition(Action onComplete = null)
        {
            EnsureDragStateRefs();
            if (_rect == null || _originalParent == null)
            {
                SetInteractableAfterAnimation(true);
                onComplete?.Invoke();
                return;
            }

            Vector3 start = _rect.position;
            Vector3 startScale = _rect.localScale;
            Vector3 targetScale = FloatingScaleForOriginalParent();
            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.alpha = 1f;
            }

            DOTween.Kill(_rect);
            DOVirtual.Float(0f, 1f, ReturnFlyDuration, t =>
                {
                    if (_rect == null)
                    {
                        return;
                    }

                    _rect.position = Vector3.LerpUnclamped(start, _originalWorldCenter, t);
                    _rect.localScale = Vector3.LerpUnclamped(startScale, targetScale, t);
                })
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .SetTarget(_rect)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    RestoreOriginalTransform();
                    onComplete?.Invoke();
                });
        }

        public void PlayFlavorTransform(DishDef dishDef, IReadOnlyList<string> flavorIds, Action onComplete)
        {
            HideHover();
            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.alpha = 1f;
            }

            if (_dishPreview == null)
            {
                onComplete?.Invoke();
                return;
            }

            _dishPreview.PlayTransformTo(
                dishDef,
                flavorIds,
                _deliciousnessOverride,
                () =>
                {
                    if (_canvasGroup != null)
                    {
                        _canvasGroup.blocksRaycasts = true;
                        _canvasGroup.alpha = 1f;
                    }

                    onComplete?.Invoke();
                });
        }

        internal RectTransform PassiveMutationFlySource =>
            _dishPreview != null
                ? _dishPreview.transform as RectTransform
                : transform as RectTransform;

        internal RenderTexture CapturePassiveMutationFlyTexture()
        {
            return _dishPreview != null
                ? _dishPreview.CopyCurrentTexture()
                : null;
        }

        internal void PlayPassiveFlavorTransform(
            DishDef dishDef,
            IReadOnlyList<string> flavorIds,
            int deliciousness,
            Action onComplete)
        {
            HideHover();
            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.alpha = 1f;
            }

            if (_dishPreview == null)
            {
                onComplete?.Invoke();
                return;
            }

            _dishPreview.PlayTransformTo(
                dishDef,
                flavorIds,
                deliciousness,
                () =>
                {
                    _deliciousnessOverride = deliciousness;
                    if (_canvasGroup != null)
                    {
                        _canvasGroup.blocksRaycasts = true;
                        _canvasGroup.alpha = 1f;
                    }

                    onComplete?.Invoke();
                });
        }

        public void PreparePassiveMutationHidden()
        {
            EnsureDragStateRefs();
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.blocksRaycasts = false;
            }

            if (_rect != null)
            {
                _rect.localScale = Vector3.one * 0.72f;
            }
        }

        public void PlayPassiveMutationAppear(Action onComplete)
        {
            EnsureDragStateRefs();
            if (_canvasGroup == null || _rect == null)
            {
                onComplete?.Invoke();
                return;
            }

            DOTween.Kill(_rect);
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.alpha = 0f;
            _rect.localScale = Vector3.one * 0.72f;
            float progress = 0f;
            DOTween.To(
                    () => progress,
                    value =>
                    {
                        progress = value;
                        _canvasGroup.alpha = value;
                        _rect.localScale = Vector3.LerpUnclamped(
                            Vector3.one * 0.72f,
                            Vector3.one,
                            value);
                    },
                    1f,
                    0.32f)
                .SetEase(Ease.OutBack)
                .SetUpdate(true)
                .SetTarget(_rect)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    _canvasGroup.alpha = 1f;
                    _canvasGroup.blocksRaycasts = true;
                    onComplete?.Invoke();
                });
        }

        /// <summary>播放与商店购买失败一致的横向衰减晃动。</summary>
        public void PlayInteractionFailed()
        {
            EnsureDragStateRefs();
            ResolveDishPreview();
            StopInteractionFailedFeedback();

            _failureFeedbackRect = _dishPreview != null
                ? _dishPreview.transform as RectTransform
                : _rect;
            if (_failureFeedbackRect == null)
            {
                return;
            }

            _failureFeedbackOrigin = _failureFeedbackRect.anchoredPosition;
            _failureTween = DOVirtual.Float(0f, 1f, 0.25f, t =>
                {
                    if (_failureFeedbackRect == null)
                    {
                        return;
                    }

                    float offset = Mathf.Sin(t * Mathf.PI * 12f)
                        * 9f
                        * (1f - t);
                    _failureFeedbackRect.anchoredPosition =
                        _failureFeedbackOrigin + new Vector2(offset, 0f);
                })
                .SetEase(Ease.Linear)
                .SetUpdate(true)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    if (_failureFeedbackRect != null)
                    {
                        _failureFeedbackRect.anchoredPosition =
                            _failureFeedbackOrigin;
                    }

                    _failureTween = null;
                    _failureFeedbackRect = null;
                });
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!_dragEnabled)
            {
                _suppressClick = true;
                HideHover();
                ResolvePanScrollRect();
                _panScrollRect?.OnBeginDrag(eventData);
                return;
            }

            DOTween.Kill(_rect);
            _dragging = true;
            _dropHandled = false;
            _originalParent = transform.parent;
            _originalAnchorMin = _rect.anchorMin;
            _originalAnchorMax = _rect.anchorMax;
            _originalPivot = _rect.pivot;
            _originalSizeDelta = _rect.sizeDelta;
            _originalAnchoredPosition = _rect.anchoredPosition;
            _originalLocalScale = _rect.localScale;
            _originalLocalRotation = _rect.localRotation;
            _originalSiblingIndex = transform.GetSiblingIndex();
            _dragCanvas = GetComponentInParent<Canvas>();
            Vector3 center = _rect.TransformPoint(_rect.rect.center);
            _originalWorldCenter = center;
            HideHover();
            transform.SetParent(_dragCanvas != null ? _dragCanvas.transform : transform.root, true);
            PrepareAsFloating(center);
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.alpha = 1f;
            MoveToPointer(eventData);
            _onBeginDrag?.Invoke(this);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragEnabled)
            {
                ResolvePanScrollRect();
                _panScrollRect?.OnDrag(eventData);
                return;
            }

            if (!_dragging)
            {
                return;
            }

            MoveToPointer(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragEnabled)
            {
                ResolvePanScrollRect();
                _panScrollRect?.OnEndDrag(eventData);
                return;
            }

            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            if (_dropHandled)
            {
                return;
            }

            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.alpha = 1f;
            if (_onDragCancelled != null && _onDragCancelled.Invoke(this))
            {
                return;
            }

            PlayReturnToOriginalPosition();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_suppressClick)
            {
                _suppressClick = false;
                return;
            }

            if (!_dragging && eventData != null && eventData.button == PointerEventData.InputButton.Left)
            {
                _onClick?.Invoke(this);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_dragging || _dropHandled)
            {
                return;
            }

            _hovered = true;
            SetWarehouseHighlight(true);
            _onHoverEnter?.Invoke(this);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            HideHover();
        }

        private void OnDisable()
        {
            StopInteractionFailedFeedback();
        }

        private void HideHover()
        {
            if (!_hovered)
            {
                return;
            }

            _hovered = false;
            SetWarehouseHighlight(false);
            _onHoverExit?.Invoke(this);
        }

        private void StopInteractionFailedFeedback()
        {
            _failureTween?.Kill();
            _failureTween = null;
            if (_failureFeedbackRect != null)
            {
                _failureFeedbackRect.anchoredPosition =
                    _failureFeedbackOrigin;
            }

            _failureFeedbackRect = null;
        }

        private void PrepareAsFloating(Vector3 worldCenter)
        {
            DOTween.Kill(_rect);
            _rect.anchorMin = FloatingAnchor;
            _rect.anchorMax = FloatingAnchor;
            _rect.pivot = FloatingAnchor;
            _rect.sizeDelta = _originalSizeDelta.sqrMagnitude > 0.0001f ? _originalSizeDelta : _rect.sizeDelta;
            _rect.position = worldCenter;
            transform.SetAsLastSibling();
            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.alpha = 1f;
            }
        }

        private void MoveToPointer(PointerEventData eventData)
        {
            RectTransform parentRect = _rect.parent as RectTransform;
            Camera camera = _dragCanvas != null && _dragCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _dragCanvas.worldCamera
                : eventData.pressEventCamera;
            if (parentRect != null
                && RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    parentRect,
                    eventData.position,
                    camera,
                    out Vector3 worldPoint))
            {
                _rect.position = worldPoint;
                return;
            }

            _rect.position = eventData.position;
        }

        private void RestoreOriginalTransform()
        {
            if (_rect == null || _originalParent == null)
            {
                SetInteractableAfterAnimation(true);
                return;
            }

            transform.SetParent(_originalParent, false);
            transform.SetSiblingIndex(_originalSiblingIndex);
            _rect.anchorMin = _originalAnchorMin;
            _rect.anchorMax = _originalAnchorMax;
            _rect.pivot = _originalPivot;
            _rect.sizeDelta = _originalSizeDelta;
            _rect.localScale = _originalLocalScale;
            _rect.localRotation = _originalLocalRotation;
            _rect.anchoredPosition = _originalAnchoredPosition;
            _dragging = false;
            _dropHandled = false;
            SetInteractableAfterAnimation(true);
        }

        private Vector3 FloatingScaleForOriginalParent()
        {
            Transform floatingParent = transform.parent;
            Vector3 parentScale = floatingParent != null ? floatingParent.lossyScale : Vector3.one;
            Vector3 targetWorldScale = _originalParent != null
                ? Vector3.Scale(_originalParent.lossyScale, _originalLocalScale)
                : _originalLocalScale;
            return new Vector3(
                SafeScaleDiv(targetWorldScale.x, parentScale.x),
                SafeScaleDiv(targetWorldScale.y, parentScale.y),
                SafeScaleDiv(targetWorldScale.z, parentScale.z));
        }

        private static float SafeScaleDiv(float value, float divisor)
        {
            return Mathf.Abs(divisor) <= 0.0001f ? value : value / divisor;
        }

        private void EnsureDragStateRefs()
        {
            _rect ??= transform as RectTransform;
            if (_canvasGroup == null)
            {
                _canvasGroup = GetComponent<CanvasGroup>();
            }
        }

        private void EnsureButton()
        {
            if (_button != null)
            {
                return;
            }

            _button = GetComponent<Button>();
            if (_button == null)
            {
                return;
            }

            if (_button.targetGraphic == null)
            {
                _button.targetGraphic = GetComponentInChildren<Graphic>(true);
            }
        }

        private void ResolveDishPreview()
        {
            _dishPreview ??= GetComponentInChildren<DishIconRenderTexturePreview>(true);
        }

        private void ResolvePanScrollRect()
        {
            if (_panScrollRect == null)
            {
                _panScrollRect = GetComponentInParent<RecipeWarehouseScrollRect>();
            }
        }

        private void ConfigureWarehouseStyle()
        {
            if (_previewMode != DishIconPreviewMode.Warehouse)
            {
                if (_warehouseHighlight != null)
                {
                    _warehouseHighlight.gameObject.SetActive(false);
                }

                return;
            }

            if (_warehouseHighlight == null)
            {
                Transform existing = transform.Find("WarehouseHighlight");
                if (existing != null)
                {
                    _warehouseHighlight = existing.GetComponent<RecipeWarehouseItemFrameGraphic>();
                }
            }

            if (_warehouseHighlight == null)
            {
                return;
            }

            _warehouseHighlight.gameObject.SetActive(true);
            SetWarehouseHighlight(false);
        }

        private void SetWarehouseHighlight(bool highlighted)
        {
            if (_previewMode != DishIconPreviewMode.Warehouse || _warehouseHighlight == null)
            {
                return;
            }

            _warehouseHighlight.Configure(
                highlighted,
                _warehouseClickable,
                _cannotPlace);
        }

        private void ConfigureBattleStatus(BattleRecipeEntryStatus? status)
        {
            _cannotPlace = status == BattleRecipeEntryStatus.CannotPlace;
            ResolveDishPreview();
            EnsureBattleStatusVisuals();

            if (_battleStatusOverlay != null)
            {
                _battleStatusOverlay.gameObject.SetActive(false);
            }

            RawImage foodImage = _dishPreview != null
                ? _dishPreview.TargetImage
                : null;
            _dishPreview?.SetAlpha(1f);
            if (DebuffVisualStyle.IsAppliedToGraphic(foodImage))
            {
                DebuffVisualStyle.ClearGraphic(foodImage);
            }

            switch (status)
            {
                case BattleRecipeEntryStatus.WaitingForPlacement:
                    if (_battleStatusOverlay != null)
                    {
                        _battleStatusOverlay.color =
                            WaitingForPlacementOverlayColor;
                        _battleStatusOverlay.gameObject.SetActive(true);
                    }

                    break;
                case BattleRecipeEntryStatus.Served:
                case BattleRecipeEntryStatus.Discarded:
                case BattleRecipeEntryStatus.Removed:
                    _dishPreview?.SetAlpha(FinishedDishAlpha);
                    break;
            }
        }

        private void EnsureBattleStatusVisuals()
        {
            if (_battleStatusOverlay == null)
            {
                Transform existing = transform.Find("BattleStatusOverlay");
                _battleStatusOverlay = existing != null
                    ? existing.GetComponent<Image>()
                    : null;
            }
        }

    }
}
