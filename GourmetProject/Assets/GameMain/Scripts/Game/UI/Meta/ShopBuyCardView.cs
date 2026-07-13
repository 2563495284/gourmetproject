using System;
using DG.Tweening;
using GourmetProject.Game;
using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 商店「进货」单张商品卡视图。固定结构在 ShopBuyCardView.prefab，
    /// 道具信息、价格与可购买状态由 <see cref="Bind"/> 数据驱动填充。
    /// </summary>
    public sealed class ShopBuyCardView : MonoBehaviour
    {
        [SerializeField] private Button _buyButton;

        [SerializeField] private Image _itemIcon;
        [SerializeField] private DishShapePreview _dishShapePreview;

        private RectTransform _rect;
        private RectTransform _iconRect;
        private Canvas _canvas;
        private bool _affordable;
        private bool _usesTargeting;
        private Func<ShopBuyCardView, bool> _onBuy;
        private Action<ShopBuyCardView> _onTargetPointerDown;
        private Action<ShopBuyCardView, Vector2> _onTargetPointerUp;
        private Tween _failureTween;
        private Selectable.Transition _defaultButtonTransition;
        private bool _hasDefaultButtonTransition;

        public void Bind(ItemDefinition item, int price, bool affordable, Action onBuy)
        {
            Sprite icon = ContentIconLoader.LoadItem(item);
            Bind(item?.Name, item?.Desc, price, affordable, icon, _ =>
            {
                onBuy?.Invoke();
                return true;
            });
        }

        /// <summary>通用商品绑定（道具 / 菜品 / 餐桌碎片）。</summary>
        public void Bind(
            string name,
            string desc,
            int price,
            bool affordable,
            Sprite icon,
            Func<ShopBuyCardView, bool> onBuy,
            Action<ShopBuyCardView> onTargetPointerDown = null,
            Action<ShopBuyCardView, Vector2> onTargetPointerUp = null)
        {
            EnsureDefaultButtonTransition();
            _affordable = affordable;
            _usesTargeting = onTargetPointerDown != null || onTargetPointerUp != null;
            _onBuy = onBuy;
            _onTargetPointerDown = onTargetPointerDown;
            _onTargetPointerUp = onTargetPointerUp;
            EnsurePointerProxy();
            _dishShapePreview?.Hide();
            SetIcon(icon);

            Text label = _buyButton.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = $"购买 {price}";
            }

            _buyButton.interactable = true;
            _buyButton.onClick.RemoveAllListeners();
            if (!_usesTargeting)
            {
                _buyButton.onClick.AddListener(HandleImmediateBuyClicked);
            }
        }

        /// <summary>菜品商品绑定：沿用购买卡交互，同时在图标区域显示占格网格。</summary>
        public void BindDish(
            string name,
            string desc,
            int price,
            bool affordable,
            Sprite icon,
            DishDef dish,
            Func<ShopBuyCardView, bool> onBuy,
            Action<ShopBuyCardView> onTargetPointerDown = null,
            Action<ShopBuyCardView, Vector2> onTargetPointerUp = null)
        {
            Bind(name, desc, price, affordable, icon, onBuy, onTargetPointerDown, onTargetPointerUp);
            _dishShapePreview?.Bind(dish, icon);
            UseIconAsHitTargetOnly();
        }

        public Vector2 IconScreenCenter()
        {
            RectTransform target = _iconRect != null ? _iconRect : (RectTransform)transform;
            Camera cam = ResolveEventCamera();
            return RectTransformUtility.WorldToScreenPoint(cam, target.TransformPoint(target.rect.center));
        }

        public RectTransform PurchaseFlySource => _iconRect != null ? _iconRect : Rect;

        public Sprite PurchaseFlySprite => _itemIcon != null && _itemIcon.enabled ? _itemIcon.sprite : null;

        public bool ContainsScreenPoint(Vector2 screenPoint)
        {
            Camera cam = ResolveEventCamera();
            return RectTransformUtility.RectangleContainsScreenPoint(Rect, screenPoint, cam);
        }

        public void PlayPurchaseFailed()
        {
            if (_iconRect == null && _itemIcon != null)
            {
                _iconRect = _itemIcon.rectTransform;
            }

            if (_iconRect == null)
            {
                return;
            }

            _failureTween?.Kill();
            Vector2 origin = _iconRect.anchoredPosition;
            _failureTween = DOVirtual.Float(0f, 1f, 0.25f, t =>
            {
                if (_iconRect == null)
                {
                    return;
                }

                float offset = Mathf.Sin(t * Mathf.PI * 12f) * 9f * (1f - t);
                _iconRect.anchoredPosition = origin + new Vector2(offset, 0f);
            })
                .SetEase(Ease.Linear)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    if (_iconRect != null)
                    {
                        _iconRect.anchoredPosition = origin;
                    }
                });
        }

        private RectTransform Rect
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

        private void OnDestroy()
        {
            _failureTween?.Kill();
        }

        private void HandleImmediateBuyClicked()
        {
            if (!_affordable)
            {
                PlayPurchaseFailed();
                return;
            }

            if (_onBuy != null && !_onBuy.Invoke(this))
            {
                PlayPurchaseFailed();
            }
        }

        private void HandlePointerDown(PointerEventData eventData)
        {
            if (!_usesTargeting)
            {
                return;
            }

            if (!_affordable)
            {
                PlayPurchaseFailed();
                return;
            }

            _onTargetPointerDown?.Invoke(this);
        }

        private void HandlePointerUp(PointerEventData eventData)
        {
            if (!_usesTargeting || !_affordable)
            {
                return;
            }

            _onTargetPointerUp?.Invoke(this, eventData.position);
        }

        private void SetIcon(Sprite icon)
        {
            if (_itemIcon == null)
            {
                return;
            }

            _iconRect = _itemIcon.rectTransform;
            _itemIcon.preserveAspect = true;
            _itemIcon.enabled = icon != null;
            _itemIcon.sprite = icon;
            _itemIcon.color = Color.white;
            _itemIcon.raycastTarget = true;
            RestoreButtonTransition();
        }

        private void UseIconAsHitTargetOnly()
        {
            if (_itemIcon == null)
            {
                return;
            }

            _iconRect = _itemIcon.rectTransform;
            _itemIcon.enabled = true;
            _itemIcon.sprite = null;
            _itemIcon.color = new Color(1f, 1f, 1f, 0f);
            _itemIcon.raycastTarget = true;

            if (_buyButton != null)
            {
                _buyButton.transition = Selectable.Transition.None;
            }
        }

        private void EnsureDefaultButtonTransition()
        {
            if (_hasDefaultButtonTransition || _buyButton == null)
            {
                return;
            }

            _defaultButtonTransition = _buyButton.transition;
            _hasDefaultButtonTransition = true;
        }

        private void RestoreButtonTransition()
        {
            if (_buyButton != null && _hasDefaultButtonTransition)
            {
                _buyButton.transition = _defaultButtonTransition;
            }
        }

        private Camera ResolveEventCamera()
        {
            if (_canvas == null)
            {
                _canvas = GetComponentInParent<Canvas>();
            }

            return _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;
        }

        private void EnsurePointerProxy()
        {
            if (_buyButton == null)
            {
                return;
            }

            PointerProxy proxy = _buyButton.GetComponent<PointerProxy>();
            if (proxy == null)
            {
                proxy = _buyButton.gameObject.AddComponent<PointerProxy>();
            }

            proxy.Bind(this);
        }

        private sealed class PointerProxy : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
        {
            private ShopBuyCardView _owner;

            public void Bind(ShopBuyCardView owner)
            {
                _owner = owner;
            }

            public void OnPointerDown(PointerEventData eventData)
            {
                _owner?.HandlePointerDown(eventData);
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                _owner?.HandlePointerUp(eventData);
            }
        }
    }
}
