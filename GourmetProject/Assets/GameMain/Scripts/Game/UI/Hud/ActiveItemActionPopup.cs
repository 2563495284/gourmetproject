using System;
using DG.Tweening;
using GourmetProject.Game;
using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Tooltips;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 主动道具点击后的使用/丢弃气泡。对标 STS2 药水 popup：只负责 UI 分流，不直接执行业务。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class ActiveItemActionPopup : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private RectTransform _panel;
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _reasonText;
        [SerializeField] private Button _useButton;
        [SerializeField] private Button _discardButton;
        [SerializeField] private ItemTipView _itemTipPrefab;
        [SerializeField] private float _fadeDuration = 0.1f;
        [SerializeField] private float _popDuration = 0.15f;
        [SerializeField] private Vector2 _screenOffset = new Vector2(-120f, -96f);

        private Action _onUse;
        private Action _onDiscard;
        private Action _onClose;
        private Tween _tween;
        private bool _closing;
        private ItemTipView _itemTip;
        private Image _itemIcon;

        private void Awake()
        {
            EnsureRefs();
        }

        private void OnDestroy()
        {
            _tween?.Kill();
        }

        private void Update()
        {
            if (_closing)
            {
                return;
            }

            if ((Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame))
            {
                Close();
            }
        }

        public void Open(
            ItemDefinition item,
            Vector2 anchorScreenPoint,
            bool canUse,
            string disabledReason,
            bool canDiscard,
            Action onUse,
            Action onDiscard,
            Action onClose)
        {
            EnsureRefs();
            _closing = false;
            _onUse = onUse;
            _onDiscard = onDiscard;
            _onClose = onClose;

            if (_titleText != null)
            {
                _titleText.text = item != null ? item.Name : "主动道具";
            }

            if (_reasonText != null)
            {
                _reasonText.text = canUse ? string.Empty : (disabledReason ?? "现在不能使用。");
                _reasonText.gameObject.SetActive(!canUse && !string.IsNullOrEmpty(_reasonText.text));
            }

            BuildItemTip(item);
            BindButton(_useButton, "使用", canUse, () =>
            {
                Action use = _onUse;
                Close(invokeClose: false);
                use?.Invoke();
            });
            BindButton(_discardButton, "丢弃", canDiscard, () =>
            {
                Action discard = _onDiscard;
                Close(invokeClose: false);
                discard?.Invoke();
            });

            StretchRoot();
            PositionPanel(anchorScreenPoint);
            PositionItemTip(anchorScreenPoint);
            PlayOpen();
        }

        public void Close(bool invokeClose = true)
        {
            if (_closing)
            {
                return;
            }

            _closing = true;
            _useButton?.onClick.RemoveAllListeners();
            _discardButton?.onClick.RemoveAllListeners();

            _tween?.Kill();
            if (_group == null)
            {
                FinishClose(invokeClose);
                return;
            }

            _tween = DOVirtual.Float(_group.alpha, 0f, _fadeDuration, a =>
                {
                    if (_group != null)
                    {
                        _group.alpha = a;
                    }
                })
                .SetEase(Ease.OutSine)
                .SetUpdate(true)
                .OnComplete(() => FinishClose(invokeClose));
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_panel == null || eventData == null)
            {
                return;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            bool insidePanel = RectTransformUtility.RectangleContainsScreenPoint(_panel, eventData.position, cam);
            bool insideTip = _itemTip != null
                && RectTransformUtility.RectangleContainsScreenPoint(
                    (RectTransform)_itemTip.transform,
                    eventData.position,
                    cam);
            if (!insidePanel && !insideTip)
            {
                Close();
            }
        }

        private void FinishClose(bool invokeClose)
        {
            if (invokeClose)
            {
                _onClose?.Invoke();
            }

            Destroy(gameObject);
        }

        private void BindButton(Button button, string label, bool interactable, Action action)
        {
            if (button == null)
            {
                return;
            }

            Text text = button.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                text.text = label;
            }

            button.onClick.RemoveAllListeners();
            button.interactable = interactable;
            if (action != null)
            {
                button.onClick.AddListener(() => action());
            }
        }

        private void StretchRoot()
        {
            RectTransform rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private void PositionPanel(Vector2 anchorScreenPoint)
        {
            if (_panel == null)
            {
                return;
            }

            RectTransform parent = _panel.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, anchorScreenPoint, cam, out Vector2 local))
            {
                _panel.anchoredPosition = local + _screenOffset;
                ClampPanel(parent);
            }
        }

        private void BuildItemTip(ItemDefinition item)
        {
            if (_itemTip != null)
            {
                Destroy(_itemTip.gameObject);
                _itemTip = null;
            }

            if (_itemTipPrefab == null)
            {
                Debug.LogError($"{nameof(ActiveItemActionPopup)} prefab 缺少 ItemTipView 引用。", this);
                return;
            }

            _itemTip = Instantiate(_itemTipPrefab, transform, false);
            _itemTip.name = "ItemTip";
            _itemTip.Bind(item);
            _itemTip.Show();

            if (_itemIcon == null)
            {
                var iconObject = new GameObject(
                    "ItemIcon",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                iconObject.transform.SetParent(transform, false);
                _itemIcon = iconObject.GetComponent<Image>();
                _itemIcon.preserveAspect = true;
                _itemIcon.raycastTarget = false;
            }

            _itemIcon.sprite = ContentIconLoader.LoadItem(item);
            _itemIcon.gameObject.SetActive(_itemIcon.sprite != null);
        }

        private void PositionItemTip(Vector2 anchorScreenPoint)
        {
            if (_itemTip == null || _panel == null || _itemTip.transform is not RectTransform tipRect)
            {
                return;
            }

            RectTransform parent = tipRect.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            tipRect.anchorMin = new Vector2(0.5f, 0.5f);
            tipRect.anchorMax = new Vector2(0.5f, 0.5f);
            tipRect.pivot = new Vector2(0.5f, 0.5f);

            float gap = 12f;
            Vector2 above = _panel.anchoredPosition
                + Vector2.up * (_panel.rect.height * 0.5f + tipRect.rect.height * 0.5f + gap);
            Rect bounds = parent.rect;
            float halfW = Mathf.Max(1f, tipRect.rect.width * 0.5f);
            float halfH = Mathf.Max(1f, tipRect.rect.height * 0.5f);
            if (above.y + halfH > bounds.yMax)
            {
                above.y = _panel.anchoredPosition.y
                    - (_panel.rect.height * 0.5f + tipRect.rect.height * 0.5f + gap);
            }

            above.x = Mathf.Clamp(above.x, bounds.xMin + halfW, bounds.xMax - halfW);
            above.y = Mathf.Clamp(above.y, bounds.yMin + halfH, bounds.yMax - halfH);
            tipRect.anchoredPosition = above;

            Canvas canvas = GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, anchorScreenPoint, cam, out Vector2 anchorLocal))
            {
                bool termsLeft = tipRect.anchoredPosition.x < anchorLocal.x;
                _itemTip.OnPlacedAroundTarget(termsLeft);
                PositionItemIcon(parent, tipRect, termsLeft);
            }
        }

        private void PositionItemIcon(RectTransform parent, RectTransform tipRect, bool termsLeft)
        {
            if (_itemIcon == null || !_itemIcon.gameObject.activeSelf)
            {
                return;
            }

            RectTransform iconRect = _itemIcon.rectTransform;
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(88f, 88f);

            float direction = termsLeft ? 1f : -1f;
            Vector2 position = tipRect.anchoredPosition
                + Vector2.right * direction * (tipRect.rect.width * 0.5f + 54f);
            Rect bounds = parent.rect;
            float half = iconRect.sizeDelta.x * 0.5f;
            position.x = Mathf.Clamp(position.x, bounds.xMin + half, bounds.xMax - half);
            position.y = Mathf.Clamp(position.y, bounds.yMin + half, bounds.yMax - half);
            iconRect.anchoredPosition = position;
            iconRect.SetAsLastSibling();
        }

        private void ClampPanel(RectTransform parent)
        {
            Rect bounds = parent.rect;
            Rect rect = _panel.rect;
            Vector2 pos = _panel.anchoredPosition;
            float halfW = Mathf.Max(1f, rect.width * 0.5f);
            float halfH = Mathf.Max(1f, rect.height * 0.5f);
            pos.x = Mathf.Clamp(pos.x, bounds.xMin + halfW, bounds.xMax - halfW);
            pos.y = Mathf.Clamp(pos.y, bounds.yMin + halfH, bounds.yMax - halfH);
            _panel.anchoredPosition = pos;
        }

        private void PlayOpen()
        {
            if (_group != null)
            {
                _group.alpha = 0f;
            }

            if (_panel != null)
            {
                _panel.localScale = Vector3.one * 0.92f;
            }

            _tween?.Kill();
            Sequence seq = DOTween.Sequence().SetUpdate(true);
            if (_group != null)
            {
                seq.Join(DOVirtual.Float(_group.alpha, 1f, _fadeDuration, a =>
                    {
                        if (_group != null)
                        {
                            _group.alpha = a;
                        }
                    }).SetEase(Ease.OutSine));
            }

            if (_panel != null)
            {
                seq.Join(_panel.DOScale(1f, _popDuration).SetEase(Ease.OutBack));
            }

            _tween = seq;
        }

        private void EnsureRefs()
        {
            if (_group == null)
            {
                _group = GetComponent<CanvasGroup>();
            }

            Image blocker = GetComponent<Image>();
            if (blocker != null)
            {
                blocker.raycastTarget = true;
            }

            if (_panel == null)
            {
                Transform existing = transform.Find("Panel");
                _panel = existing as RectTransform;
            }

            if (_titleText == null)
            {
                _titleText = FindText(_panel, "Title");
            }

            if (_reasonText == null)
            {
                _reasonText = FindText(_panel, "Reason");
            }

            if (_useButton == null)
            {
                _useButton = FindButton(_panel, "UseButton");
            }

            if (_discardButton == null)
            {
                _discardButton = FindButton(_panel, "DiscardButton");
            }

            if (_group == null || blocker == null || _panel == null || _titleText == null || _reasonText == null || _useButton == null || _discardButton == null || _itemTipPrefab == null)
            {
                Debug.LogError($"{nameof(ActiveItemActionPopup)} prefab 缺少固定 UI 结构或 ItemTipView 引用。", this);
            }
        }

        private static Text FindText(RectTransform parent, string name)
        {
            Transform existing = parent != null ? parent.Find(name) : null;
            return existing != null ? existing.GetComponent<Text>() : null;
        }

        private static Button FindButton(RectTransform parent, string name)
        {
            Transform existing = parent != null ? parent.Find(name) : null;
            return existing != null ? existing.GetComponent<Button>() : null;
        }
    }
}
