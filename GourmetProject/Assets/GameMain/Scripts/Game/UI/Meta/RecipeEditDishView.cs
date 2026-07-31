using System;
using System.Collections.Generic;
using DG.Tweening;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 菜谱中的单个菜品卡。奖励流程支持拖拽；查看/删除/主动道具模式禁用拖拽并响应点击。
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class RecipeEditDishView : MonoBehaviour, IPointerDownHandler, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private const float ReturnFlyDuration = 0.22f;
        private static readonly Vector2 FloatingAnchor = new(0.5f, 0.5f);

        [SerializeField] private Button _button;
        [FormerlySerializedAs("_shapePreview")]
        [SerializeField] private DishIconRenderTexturePreview _dishPreview;

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
        private RecipeWarehouseItemFrameGraphic _warehouseHighlight;
        private Image _battleStatusOverlay;
        private Image _battleStatusBadge;
        private Text _battleStatusText;
        private DishIconPreviewMode _previewMode;
        private bool _warehouseClickable;
        private bool _suppressClick;

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
            BattleRecipeEntryStatus? battleStatus = null)
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
            _warehouseClickable = onClick != null;
            DisplayedGridSize = DishIconRenderTexturePreview.DisplayedGridSizeFor(
                dishDef,
                flavorIds);

            if (_dishPreview != null)
            {
                if (dishDef != null)
                {
                    _dishPreview.Bind(
                        DishPreviewRequest.FromDefinition(
                            dishDef,
                            flavorIds: flavorIds,
                            mode: previewMode));
                    DisplayedGridSize = _dishPreview.DisplayedGridSize;
                }
                else
                {
                    _dishPreview.Hide();
                }
            }

            ConfigureWarehouseStyle();
            ConfigureBattleStatus(battleStatus);
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

            _dishPreview.PlayTransformTo(dishDef, flavorIds, () =>
            {
                if (_canvasGroup != null)
                {
                    _canvasGroup.blocksRaycasts = true;
                    _canvasGroup.alpha = 1f;
                }

                onComplete?.Invoke();
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
                var highlightObject = new GameObject(
                    "WarehouseHighlight",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(RecipeWarehouseItemFrameGraphic));
                highlightObject.layer = gameObject.layer;
                RectTransform highlightRect = highlightObject.GetComponent<RectTransform>();
                highlightRect.SetParent(transform, false);
                highlightRect.anchorMin = Vector2.zero;
                highlightRect.anchorMax = Vector2.one;
                highlightRect.offsetMin = Vector2.zero;
                highlightRect.offsetMax = Vector2.zero;
                highlightRect.SetAsFirstSibling();
                _warehouseHighlight =
                    highlightObject.GetComponent<RecipeWarehouseItemFrameGraphic>();
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

            _warehouseHighlight.Configure(highlighted, _warehouseClickable);
        }

        private void ConfigureBattleStatus(BattleRecipeEntryStatus? status)
        {
            if (!status.HasValue)
            {
                if (_battleStatusOverlay != null)
                {
                    _battleStatusOverlay.gameObject.SetActive(false);
                }

                if (_battleStatusBadge != null)
                {
                    _battleStatusBadge.gameObject.SetActive(false);
                }

                return;
            }

            EnsureBattleStatusVisuals();
            if (_battleStatusOverlay == null || _battleStatusBadge == null || _battleStatusText == null)
            {
                return;
            }

            (string label, Color color, float overlayAlpha) = BattleStatusStyle(status.Value);
            Color overlayColor = color;
            overlayColor.a = overlayAlpha;
            _battleStatusOverlay.color = overlayColor;
            _battleStatusOverlay.gameObject.SetActive(true);

            Color badgeColor = color;
            badgeColor.a = 0.94f;
            _battleStatusBadge.color = badgeColor;
            _battleStatusBadge.gameObject.SetActive(true);
            _battleStatusBadge.transform.SetAsLastSibling();
            _battleStatusText.text = label;
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

            if (_battleStatusOverlay == null)
            {
                var overlayObject = new GameObject(
                    "BattleStatusOverlay",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                overlayObject.layer = gameObject.layer;
                RectTransform overlayRect = overlayObject.GetComponent<RectTransform>();
                overlayRect.SetParent(transform, false);
                overlayRect.anchorMin = Vector2.zero;
                overlayRect.anchorMax = Vector2.one;
                overlayRect.offsetMin = Vector2.zero;
                overlayRect.offsetMax = Vector2.zero;
                _battleStatusOverlay = overlayObject.GetComponent<Image>();
                _battleStatusOverlay.raycastTarget = false;
            }

            if (_battleStatusBadge == null)
            {
                Transform existing = transform.Find("BattleStatusBadge");
                _battleStatusBadge = existing != null
                    ? existing.GetComponent<Image>()
                    : null;
            }

            if (_battleStatusBadge == null)
            {
                var badgeObject = new GameObject(
                    "BattleStatusBadge",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                badgeObject.layer = gameObject.layer;
                RectTransform badgeRect = badgeObject.GetComponent<RectTransform>();
                badgeRect.SetParent(transform, false);
                badgeRect.anchorMin = new Vector2(1f, 1f);
                badgeRect.anchorMax = new Vector2(1f, 1f);
                badgeRect.pivot = new Vector2(1f, 1f);
                badgeRect.anchoredPosition = new Vector2(-6f, -6f);
                badgeRect.sizeDelta = new Vector2(96f, 28f);
                _battleStatusBadge = badgeObject.GetComponent<Image>();
                _battleStatusBadge.raycastTarget = false;
            }

            if (_battleStatusText == null)
            {
                _battleStatusText =
                    _battleStatusBadge.GetComponentInChildren<Text>(true);
            }

            if (_battleStatusText == null)
            {
                var textObject = new GameObject(
                    "Label",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Text));
                textObject.layer = gameObject.layer;
                RectTransform textRect = textObject.GetComponent<RectTransform>();
                textRect.SetParent(_battleStatusBadge.transform, false);
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(4f, 1f);
                textRect.offsetMax = new Vector2(-4f, -1f);
                _battleStatusText = textObject.GetComponent<Text>();
                _battleStatusText.font =
                    Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                _battleStatusText.fontSize = 14;
                _battleStatusText.alignment = TextAnchor.MiddleCenter;
                _battleStatusText.color = Color.white;
                _battleStatusText.raycastTarget = false;
            }
        }

        private static (string Label, Color Color, float OverlayAlpha) BattleStatusStyle(
            BattleRecipeEntryStatus status)
        {
            return status switch
            {
                BattleRecipeEntryStatus.Normal =>
                    ("正常", new Color(0.20f, 0.58f, 0.27f, 1f), 0.04f),
                BattleRecipeEntryStatus.CannotPlace =>
                    ("不能放置", new Color(0.38f, 0.38f, 0.38f, 1f), 0.28f),
                BattleRecipeEntryStatus.WaitingForPlacement =>
                    ("待摆放", new Color(0.88f, 0.55f, 0.10f, 1f), 0.12f),
                BattleRecipeEntryStatus.Served =>
                    ("已上菜", new Color(0.16f, 0.48f, 0.72f, 1f), 0.18f),
                BattleRecipeEntryStatus.Discarded =>
                    ("已丢弃", new Color(0.72f, 0.22f, 0.16f, 1f), 0.34f),
                BattleRecipeEntryStatus.Removed =>
                    ("已移除", new Color(0.42f, 0.30f, 0.52f, 1f), 0.34f),
                _ => ("正常", new Color(0.20f, 0.58f, 0.27f, 1f), 0.04f),
            };
        }
    }
}
