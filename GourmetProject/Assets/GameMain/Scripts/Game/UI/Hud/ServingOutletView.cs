using System;
using GourmetProject.Core.Utility;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Battle;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    public enum ServingOutletState
    {
        WaitingForServe,
        WaitingForDishDrag,
        NoDishCanServe,
    }

    /// <summary>
    /// 战斗底部出餐口：负责显示菜谱可放统计、准备出餐按钮，以及等待玩家拖到餐桌的食物。
    /// 具体餐桌预览与提交由 <see cref="BattleWorldController"/> 完成。
    /// </summary>
    [RequireComponent(typeof(Canvas), typeof(GraphicRaycaster))]
    public sealed class ServingOutletView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const float PreparedDishRaycastPadding = 24f;
        private const float FlavorStainScale = 8f;
        private const float FlavorStainThreshold = 0.62f;
        private const float FlavorStainSoftness = 0.12f;
        private const float FlavorStainDarken = 0.12f;

        [SerializeField] private RecipeCardView _recipeSummary;
        [SerializeField] private Button _serveButton;
        [SerializeField] private Image _serveBellImage;
        [SerializeField] private Image _dishImage;
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _statusText;
        [SerializeField] private CanvasGroup _canvasGroup;

        [Header("State Colors")]
        [SerializeField] private Color _readyColor = new Color(0.86f, 0.98f, 0.76f, 1f);
        [SerializeField] private Color _dragColor = new Color(1f, 0.94f, 0.70f, 1f);
        [SerializeField] private Color _blockedColor = new Color(0.72f, 0.72f, 0.68f, 1f);
        [SerializeField] private Image _background;

        private readonly DishSpriteProvider _spriteProvider = new DishSpriteProvider();
        private Action<Vector2> _beginDrag;
        private Action<Vector2> _drag;
        private Func<Vector2, bool> _endDrag;
        private bool _dragging;
        private Camera _worldCamera;
        private Canvas _worldCanvas;
        private ServingOutletDishHoverTrigger _dishHoverTrigger;
        private Material _dishFlavorMaterial;

        public ServingOutletState State { get; private set; }

        public void ConfigureWorldSpace(Camera worldCamera)
        {
            _worldCamera = worldCamera != null ? worldCamera : Camera.main;
            _worldCanvas = GetComponent<Canvas>();
            if (_worldCanvas == null)
            {
                Debug.LogError($"{nameof(ServingOutletView)} 缺少 World Space Canvas。", this);
                return;
            }

            _worldCanvas.renderMode = RenderMode.WorldSpace;
            _worldCanvas.worldCamera = _worldCamera;
            _worldCanvas.overrideSorting = true;
            _worldCanvas.sortingLayerName = BattleSorting.WorldUi;
            _worldCanvas.sortingOrder = 0;
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
            Action onServe,
            Action onInspect,
            Func<bool> onDishHoverEntered,
            Action onDishHoverExited,
            Action<Vector2> beginDrag,
            Action<Vector2> drag,
            Func<Vector2, bool> endDrag)
        {
            _beginDrag = beginDrag;
            _drag = drag;
            _endDrag = endDrag;
            _dragging = false;
            EnsureDishHoverTrigger();
            _dishHoverTrigger?.Bind(onDishHoverEntered, onDishHoverExited);

            int placeable = 0;
            int blocked = 0;
            RecipeSlot slot = session != null && session.Slots.Count > 0 ? session.Slots[0] : null;
            if (session != null && slot != null)
            {
                for (int i = 0; i < slot.Entries.Count; i++)
                {
                    if (session.CanFitRecipeEntry(0, i))
                    {
                        placeable++;
                    }
                    else
                    {
                        blocked++;
                    }
                }
            }

            _recipeSummary?.Bind(
                $"{placeable}<color=#35B84A>✓</color> {blocked}<color=#E33A3A>×</color>",
                onInspect != null,
                onInspect);

            bool limitReached = session != null
                && session.MaxServes >= 0
                && session.ServesUsed >= session.MaxServes;
            if (session?.PreparedServe != null)
            {
                ApplyState(ServingOutletState.WaitingForDishDrag, session.PreparedServe, null);
            }
            else if (session != null && !session.IsSettled && !limitReached && placeable > 0)
            {
                ApplyState(ServingOutletState.WaitingForServe, null, null);
            }
            else
            {
                string reason = limitReached
                    ? "本次品鉴已达到上菜上限"
                    : slot == null || slot.IsEmpty
                        ? "剩余食物不足"
                        : "剩余食物无法摆入餐桌";
                ApplyState(ServingOutletState.NoDishCanServe, null, reason);
            }

            if (_serveButton != null)
            {
                _serveButton.onClick.RemoveAllListeners();
                _serveButton.interactable = State == ServingOutletState.WaitingForServe;
                if (onServe != null)
                {
                    _serveButton.onClick.AddListener(() => onServe());
                }
            }
        }

        private void ApplyState(ServingOutletState state, PreparedServeDish prepared, string blockedReason)
        {
            State = state;
            SetText(_titleText, "出餐口");

            bool waitingForDrag = state == ServingOutletState.WaitingForDishDrag && prepared != null;
            if (!waitingForDrag)
            {
                _dishHoverTrigger?.CancelHover();
            }

            if (_serveBellImage != null)
            {
                _serveBellImage.gameObject.SetActive(!waitingForDrag);
                Color bellColor = _serveBellImage.color;
                bellColor.a = state == ServingOutletState.NoDishCanServe ? 0.38f : 1f;
                _serveBellImage.color = bellColor;
            }

            if (_dishImage != null)
            {
                _dishImage.gameObject.SetActive(waitingForDrag);
                _dishImage.raycastTarget = waitingForDrag;
                _dishImage.raycastPadding = waitingForDrag
                    ? Vector4.one * -PreparedDishRaycastPadding
                    : Vector4.zero;
                if (waitingForDrag)
                {
                    _dishImage.sprite = _spriteProvider.Get(prepared.Definition);
                    _dishImage.preserveAspect = true;
                    ApplyPreparedDishVisual(prepared);
                    SetDishAlpha(1f);
                }
                else
                {
                    ResetPreparedDishVisual();
                }
            }

            switch (state)
            {
                case ServingOutletState.WaitingForServe:
                    SetText(_statusText, "点击出餐");
                    SetBackground(_readyColor);
                    break;
                case ServingOutletState.WaitingForDishDrag:
                    SetText(_statusText, "拖到餐桌，或拖进垃圾桶丢弃");
                    SetBackground(_dragColor);
                    break;
                default:
                    SetText(_statusText, blockedReason ?? "没有食物可以出餐");
                    SetBackground(_blockedColor);
                    break;
            }

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.interactable = true;
                _canvasGroup.blocksRaycasts = true;
            }
        }

        private void ApplyPreparedDishVisual(PreparedServeDish prepared)
        {
            if (_dishImage == null || prepared?.Dish == null)
            {
                ResetPreparedDishVisual();
                return;
            }

            // 使用本次已准备菜品的实际摆放朝向，与拖拽 ghost/最终落桌保持一致。
            // 「麻」旋转后若无合法位置，玩法层会回退原朝向，不能在这里仅按风味重新计算。
            int rotationIndex = prepared.Dish.Placement.RotationIndex;
            _dishImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f * rotationIndex);

            var settings = new FlavorStainPalette.Settings(
                FlavorStainScale,
                FlavorStainThreshold,
                FlavorStainSoftness,
                FlavorStainDarken,
                (float)(StableHash.Fnv1a64(prepared.Definition.Id) & 0xFFFFFF));
            FlavorStainPalette.ApplyToGraphic(
                _dishImage,
                prepared.Dish.FlavorIds,
                ref _dishFlavorMaterial,
                settings);
        }

        private void ResetPreparedDishVisual()
        {
            if (_dishImage == null)
            {
                return;
            }

            _dishImage.rectTransform.localRotation = Quaternion.identity;
            _dishImage.material = null;
        }

        private void OnDestroy()
        {
            FlavorStainPalette.ReleaseMaterial(ref _dishFlavorMaterial);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (State != ServingOutletState.WaitingForDishDrag || eventData == null)
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
            if (_dishImage == null)
            {
                return;
            }

            Color color = _dishImage.color;
            color.a = alpha;
            _dishImage.color = color;
        }

        public Bounds DishWorldBounds
        {
            get
            {
                if (_dishImage == null)
                {
                    return new Bounds(transform.position, Vector3.zero);
                }

                RectTransform rect = _dishImage.rectTransform;
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                var bounds = new Bounds(corners[0], Vector3.zero);
                for (int i = 1; i < corners.Length; i++)
                {
                    bounds.Encapsulate(corners[i]);
                }

                return bounds;
            }
        }

        private void EnsureDishHoverTrigger()
        {
            if (_dishHoverTrigger == null && _dishImage != null)
            {
                _dishHoverTrigger = _dishImage.GetComponent<ServingOutletDishHoverTrigger>();
            }

            if (_dishHoverTrigger == null)
            {
                Debug.LogError($"{nameof(ServingOutletView)} prefab 的 PreparedDish 缺少 {nameof(ServingOutletDishHoverTrigger)}。", this);
            }
        }

        private void SetBackground(Color color)
        {
            if (_background != null)
            {
                _background.color = color;
            }
        }

        private static void SetText(Text text, string value)
        {
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }
    }
}
