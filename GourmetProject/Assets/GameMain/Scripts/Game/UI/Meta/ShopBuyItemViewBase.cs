using System;
using DG.Tweening;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    public sealed class ShopBuyItemViewContext
    {
        public ShopBuyItemViewContext(
            GameRun run,
            ShopEntry entry,
            bool affordable,
            Sprite icon,
            DishDef dish,
            Func<ShopEntry, ShopBuyItemViewBase, bool> onBuy,
            Action<ShopBuyItemViewBase, ShopEntry> onTargetPointerDown = null,
            Action<ShopBuyItemViewBase, ShopEntry, Vector2> onTargetPointerUp = null)
        {
            Run = run;
            Entry = entry;
            Affordable = affordable;
            Icon = icon;
            Dish = dish;
            OnBuy = onBuy;
            OnTargetPointerDown = onTargetPointerDown;
            OnTargetPointerUp = onTargetPointerUp;
        }

        public GameRun Run { get; }
        public ShopEntry Entry { get; }
        public bool Affordable { get; }
        public Sprite Icon { get; }
        public DishDef Dish { get; }
        public Func<ShopEntry, ShopBuyItemViewBase, bool> OnBuy { get; }
        public Action<ShopBuyItemViewBase, ShopEntry> OnTargetPointerDown { get; }
        public Action<ShopBuyItemViewBase, ShopEntry, Vector2> OnTargetPointerUp { get; }
    }

    /// <summary>
    /// 商店单个商品视图基类：统一处理购买按钮、可购买判定、拖拽定向、失败反馈和购买飞行动画源。
    /// 具体商品只负责把自身 prefab 结构绑定成对应表现。
    /// </summary>
    public abstract class ShopBuyItemViewBase : MonoBehaviour
    {
        [SerializeField] private Button _buyButton;
        [SerializeField] private Image _itemIcon;
        [SerializeField] private Text _buyLabel;

        private RectTransform _rect;
        private RectTransform _iconRect;
        private Canvas _canvas;
        private bool _affordable;
        private bool _usesTargeting;
        private ShopBuyItemViewContext _context;
        private Tween _failureTween;
        private Selectable.Transition _defaultButtonTransition;
        private bool _hasDefaultButtonTransition;

        public ShopEntry Entry => _context?.Entry;

        public virtual void Bind(ShopBuyItemViewContext context)
        {
            _context = context;
            EnsureDefaultButtonTransition();
            _affordable = context?.Affordable == true;
            _usesTargeting = UsesTargeting(context);
            EnsurePointerProxy();
            ConfigureContent(context);
            SetBuyLabel(context?.Entry != null ? context.Entry.Price : 0);

            if (_buyButton == null)
            {
                return;
            }

            _buyButton.interactable = true;
            _buyButton.onClick.RemoveAllListeners();
            if (!_usesTargeting)
            {
                _buyButton.onClick.AddListener(HandleImmediateBuyClicked);
            }
        }

        public Vector2 IconScreenCenter()
        {
            RectTransform target = _iconRect != null ? _iconRect : Rect;
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

        protected abstract void ConfigureContent(ShopBuyItemViewContext context);

        protected virtual bool UsesTargeting(ShopBuyItemViewContext context)
        {
            return context?.OnTargetPointerDown != null || context?.OnTargetPointerUp != null;
        }

        protected void SetIcon(Sprite icon)
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

        protected void UseIconAsHitTargetOnly()
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

            if (_context?.OnBuy == null || !_context.OnBuy.Invoke(_context.Entry, this))
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

            _context?.OnTargetPointerDown?.Invoke(this, _context.Entry);
        }

        private void HandlePointerUp(PointerEventData eventData)
        {
            if (!_usesTargeting || !_affordable)
            {
                return;
            }

            _context?.OnTargetPointerUp?.Invoke(this, _context.Entry, eventData.position);
        }

        private void SetBuyLabel(int price)
        {
            Text label = BuyLabel;
            if (label != null)
            {
                label.text = $"购买 {price}";
            }
        }

        private Text BuyLabel
        {
            get
            {
                if (_buyLabel == null)
                {
                    _buyLabel = ResolveBuyLabel();
                }

                return _buyLabel;
            }
        }

        private Text ResolveBuyLabel()
        {
            if (_buyButton != null)
            {
                Text label = _buyButton.GetComponentInChildren<Text>(true);
                if (label != null)
                {
                    return label;
                }
            }

            return GetComponentInChildren<Text>(true);
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
            private ShopBuyItemViewBase _owner;

            public void Bind(ShopBuyItemViewBase owner)
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
