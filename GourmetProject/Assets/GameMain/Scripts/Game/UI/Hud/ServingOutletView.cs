using System;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Battle;
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

        public ServingOutletState State { get; private set; }

        public RectTransform TipPlacementTarget =>
            _preparedDishRoot != null ? _preparedDishRoot : transform as RectTransform;

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
                ApplyState(ServingOutletState.WaitingForDishDrag, session.PreparedServe, null);
            }
            else if (session != null && !session.IsSettled && session.HasPendingTablePlacements)
            {
                ApplyState(ServingOutletState.WaitingForPendingConfirmation, null, null);
            }
            else
            {
                string reason = limitReached
                    ? "本场经营挑战已达到上菜上限"
                    : slot == null || slot.IsEmpty
                        ? "剩余食物不足"
                        : "剩余食物无法摆入餐桌";
                ApplyState(ServingOutletState.NoDishCanServe, null, reason);
            }
        }

        private void ApplyState(ServingOutletState state, PreparedServeDish prepared, string blockedReason)
        {
            State = state;

            bool waitingForDrag = state == ServingOutletState.WaitingForDishDrag && prepared != null;
            if (_preparedDishRoot != null)
            {
                _preparedDishRoot.gameObject.SetActive(waitingForDrag);
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

            switch (state)
            {
                case ServingOutletState.WaitingForPendingConfirmation:
                    SetText(_statusText, "请先完成餐桌上的上菜或确认");
                    SetBackground(_readyColor);
                    break;
                case ServingOutletState.WaitingForDishDrag:
                    SetText(_statusText, "拖到餐桌，或拖进垃圾桶丢弃");
                    SetBackground(_dragColor);
                    break;
                default:
                    SetText(_statusText, blockedReason ?? "没有食物可以出菜");
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
    }
}
