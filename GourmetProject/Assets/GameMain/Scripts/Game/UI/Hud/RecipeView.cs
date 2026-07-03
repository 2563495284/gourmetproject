using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 底部扇形菜谱条：把各本菜谱本（+商店态末尾的「购买空菜谱」）按扇形排布，替代原 RecipeDrawer(BottomDrawer)。
    /// 三态：隐藏 / 显示(完全展开) / 收缩(只露一点，鼠标移到底部区时从左到右逐张弹起)。
    /// 状态切换与菜谱本增减都走协程补间动画。卡片固定结构在 <see cref="RecipeCardView"/> / <see cref="RecipeAddCardView"/>。
    /// </summary>
    public sealed class RecipeView : MonoBehaviour
    {
        public enum RecipeState
        {
            Hidden,
            Collapsed,
            Shown,
        }

        /// <summary>一本菜谱本的数据（标题、容量文本、是否可点、点击回调）。</summary>
        public readonly struct BookEntry
        {
            public BookEntry(string title, string capacity, bool interactable, Action onClick)
            {
                Title = title;
                Capacity = capacity;
                Interactable = interactable;
                OnClick = onClick;
            }

            public string Title { get; }
            public string Capacity { get; }
            public bool Interactable { get; }
            public Action OnClick { get; }
        }

        [Header("Prefabs")]
        [SerializeField] private RecipeCardView _recipePrefab;
        [SerializeField] private RecipeAddCardView _recipeAddPrefab;

        [Header("扇形布局")]
        [Tooltip("扇形圆弧半径（越大越平）")]
        [SerializeField] private float _arcRadius = 760f;
        [Tooltip("相邻卡之间的张角（度），越大扇越开")]
        [SerializeField] private float _anglePerCard = 11f;
        [Tooltip("卡片朝外倾斜的系数（1=贴着弧线，0=不倾斜）")]
        [SerializeField] private float _tiltFactor = 1f;
        [Tooltip("中间卡的基准 Y（相对 RecipeView 中心）")]
        [SerializeField] private float _baseY = -10f;

        [Header("三态偏移")]
        [Tooltip("收缩态整体下沉、只露一点点的量（像素），可自行调")]
        [SerializeField] private float _peekOffset = 150f;
        [Tooltip("隐藏态整体下沉的量（像素）")]
        [SerializeField] private float _hiddenOffset = 340f;

        [Header("动画")]
        [SerializeField] private float _moveDuration = 0.28f;
        [Tooltip("逐张弹起的错峰延迟（秒）")]
        [SerializeField] private float _revealStagger = 0.05f;

        [Header("收缩态悬停触发")]
        [Tooltip("底部触发区高度占 RecipeView 自身高度的比例（0~1，从底边向上）；<=0 表示用整块区域")]
        [SerializeField] private float _triggerHeightRatio = 1f;

        private sealed class CardSlot
        {
            public GameObject Go;
            public RectTransform Rect;
            public CanvasGroup Group;
            public Coroutine Tween;
        }

        private readonly List<CardSlot> _bookSlots = new();
        private CardSlot _addSlot;
        private readonly List<CardSlot> _ordered = new();

        private RectTransform _rect;
        private Canvas _canvas;
        private RecipeState _state = RecipeState.Hidden;
        private bool _hovering;
        private bool _initialized;

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

        private void Awake()
        {
            _initialized = true;
        }

        private void Update()
        {
            if (_state != RecipeState.Collapsed || _ordered.Count == 0)
            {
                return;
            }

            bool inside = IsPointerInsideTrigger();
            if (inside != _hovering)
            {
                _hovering = inside;
                RefreshPoses(animated: true, staggered: _hovering);
            }
        }

        /// <summary>切换三态。</summary>
        public void SetState(RecipeState state)
        {
            _state = state;
            if (state != RecipeState.Collapsed)
            {
                _hovering = false;
            }

            RefreshPoses(animated: _initialized, staggered: state == RecipeState.Shown);
        }

        /// <summary>用菜谱本数据重建扇形（增减自动做调整动画）。showAdd=true 时末尾追加购买空菜谱卡。</summary>
        public void SetBooks(IReadOnlyList<BookEntry> books, bool showAdd = false, Action onAdd = null, string addCost = null)
        {
            int want = books?.Count ?? 0;

            // 补齐 / 淘汰菜谱本卡。
            while (_bookSlots.Count < want)
            {
                _bookSlots.Add(SpawnBookSlot());
            }

            while (_bookSlots.Count > want)
            {
                int last = _bookSlots.Count - 1;
                RetireSlot(_bookSlots[last]);
                _bookSlots.RemoveAt(last);
            }

            for (int i = 0; i < want; i++)
            {
                BookEntry entry = books[i];
                CardSlot slot = _bookSlots[i];
                slot.Go.name = $"RecipeBook_{i + 1}";
                var view = slot.Go.GetComponent<RecipeCardView>();
                view?.Bind(entry.Capacity, entry.Interactable, entry.OnClick);
            }

            // 购买空菜谱卡（仅商店态）。
            if (showAdd && _recipeAddPrefab != null)
            {
                if (_addSlot == null)
                {
                    _addSlot = SpawnAddSlot();
                }

                _addSlot.Go.name = "RecipeAdd";
                var addView = _addSlot.Go.GetComponent<RecipeAddCardView>();
                addView?.Bind(addCost ?? string.Empty, onAdd != null, onAdd);
            }
            else if (_addSlot != null)
            {
                RetireSlot(_addSlot);
                _addSlot = null;
            }

            RebuildOrdered();
            RefreshPoses(animated: _initialized, staggered: false);
        }

        private void RebuildOrdered()
        {
            _ordered.Clear();
            _ordered.AddRange(_bookSlots);
            if (_addSlot != null)
            {
                _ordered.Add(_addSlot);
            }

            // 保证扇形从左到右的 sibling 顺序，右侧卡叠在上层。
            for (int i = 0; i < _ordered.Count; i++)
            {
                _ordered[i].Rect.SetSiblingIndex(i);
            }
        }

        private CardSlot SpawnBookSlot()
        {
            RecipeCardView view = Instantiate(_recipePrefab, Rect);
            var slot = new CardSlot
            {
                Go = view.gameObject,
                Rect = view.Rect,
                Group = view.CanvasGroup,
            };
            InitSlotTransform(slot);
            return slot;
        }

        private CardSlot SpawnAddSlot()
        {
            RecipeAddCardView view = Instantiate(_recipeAddPrefab, Rect);
            var slot = new CardSlot
            {
                Go = view.gameObject,
                Rect = view.Rect,
                Group = view.CanvasGroup,
            };
            InitSlotTransform(slot);
            return slot;
        }

        private void InitSlotTransform(CardSlot slot)
        {
            RectTransform rt = slot.Rect;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            // 新卡从下沉 + 缩小 + 透明起步，之后 RefreshPoses 会把它弹入目标位姿。
            rt.anchoredPosition = new Vector2(0f, _baseY - _hiddenOffset);
            rt.localScale = new Vector3(0.6f, 0.6f, 1f);
            rt.localRotation = Quaternion.identity;
            if (slot.Group != null)
            {
                slot.Group.alpha = 0f;
            }
        }

        private void RetireSlot(CardSlot slot)
        {
            if (slot == null || slot.Go == null)
            {
                return;
            }

            if (slot.Tween != null)
            {
                StopCoroutine(slot.Tween);
                slot.Tween = null;
            }

            StartCoroutine(RetireRoutine(slot));
        }

        private IEnumerator RetireRoutine(CardSlot slot)
        {
            Vector2 to = new(slot.Rect.anchoredPosition.x, _baseY - _hiddenOffset);
            yield return TweenRoutine(slot, to, slot.Rect.localEulerAngles.z, 0.5f, 0f, 0f, _moveDuration);
            if (slot.Go != null)
            {
                Destroy(slot.Go);
            }
        }

        private void RefreshPoses(bool animated, bool staggered)
        {
            int n = _ordered.Count;
            for (int i = 0; i < n; i++)
            {
                CardSlot slot = _ordered[i];
                Vector2 basePos = FanPose(i, n, out float rot);
                Vector2 pos = basePos;
                float alpha = 1f;
                bool active = true;

                switch (_state)
                {
                    case RecipeState.Hidden:
                        pos = new Vector2(basePos.x, basePos.y - _hiddenOffset);
                        alpha = 0f;
                        active = false;
                        break;
                    case RecipeState.Collapsed:
                        if (!_hovering)
                        {
                            pos = new Vector2(basePos.x, basePos.y - _peekOffset);
                        }

                        break;
                    case RecipeState.Shown:
                        break;
                }

                float delay = staggered ? i * _revealStagger : 0f;

                if (!animated)
                {
                    ApplyImmediate(slot, pos, rot, alpha, active);
                    continue;
                }

                if (slot.Tween != null)
                {
                    StopCoroutine(slot.Tween);
                }

                if (active)
                {
                    slot.Go.SetActive(true);
                }

                slot.Tween = StartCoroutine(TweenRoutine(slot, pos, rot, 1f, alpha, delay, _moveDuration, deactivateAtEnd: !active));
            }
        }

        private void ApplyImmediate(CardSlot slot, Vector2 pos, float rot, float alpha, bool active)
        {
            slot.Go.SetActive(active);
            slot.Rect.anchoredPosition = pos;
            slot.Rect.localRotation = Quaternion.Euler(0f, 0f, rot);
            slot.Rect.localScale = Vector3.one;
            if (slot.Group != null)
            {
                slot.Group.alpha = alpha;
            }
        }

        private Vector2 FanPose(int index, int count, out float rotZ)
        {
            float mid = (count - 1) * 0.5f;
            float angDeg = (index - mid) * _anglePerCard;
            float ang = angDeg * Mathf.Deg2Rad;
            float x = _arcRadius * Mathf.Sin(ang);
            float y = _baseY + _arcRadius * (Mathf.Cos(ang) - 1f);
            rotZ = -angDeg * _tiltFactor;
            return new Vector2(x, y);
        }

        private IEnumerator TweenRoutine(CardSlot slot, Vector2 toPos, float toRot, float toScale, float toAlpha, float delay, float duration, bool deactivateAtEnd = false)
        {
            if (delay > 0f)
            {
                float d = 0f;
                while (d < delay)
                {
                    d += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            RectTransform rt = slot.Rect;
            Vector2 fromPos = rt.anchoredPosition;
            float fromRot = rt.localEulerAngles.z;
            float fromScale = rt.localScale.x;
            float fromAlpha = slot.Group != null ? slot.Group.alpha : 1f;

            float dur = Mathf.Max(0.01f, duration);
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                float easePos = EaseOutBack(k);
                float easeFade = Mathf.SmoothStep(0f, 1f, k);

                rt.anchoredPosition = Vector2.LerpUnclamped(fromPos, toPos, easePos);
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpAngle(fromRot, toRot, easePos));
                float s = Mathf.LerpUnclamped(fromScale, toScale, easePos);
                rt.localScale = new Vector3(s, s, 1f);
                if (slot.Group != null)
                {
                    slot.Group.alpha = Mathf.Lerp(fromAlpha, toAlpha, easeFade);
                }

                yield return null;
            }

            rt.anchoredPosition = toPos;
            rt.localRotation = Quaternion.Euler(0f, 0f, toRot);
            rt.localScale = new Vector3(toScale, toScale, 1f);
            if (slot.Group != null)
            {
                slot.Group.alpha = toAlpha;
            }

            if (deactivateAtEnd && slot.Go != null)
            {
                slot.Go.SetActive(false);
            }

            slot.Tween = null;
        }

        private bool IsPointerInsideTrigger()
        {
            if (Mouse.current == null)
            {
                return false;
            }

            Vector2 screen = Mouse.current.position.ReadValue();
            Camera cam = ResolveEventCamera();
            if (!RectTransformUtility.RectangleContainsScreenPoint(Rect, screen, cam))
            {
                return false;
            }

            if (_triggerHeightRatio >= 1f || _triggerHeightRatio <= 0f)
            {
                return true;
            }

            // 只把底部 _triggerHeightRatio 高度算作触发区。
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, screen, cam, out Vector2 local))
            {
                return false;
            }

            Rect r = Rect.rect;
            float triggerTop = r.yMin + r.height * _triggerHeightRatio;
            return local.y <= triggerTop;
        }

        private Camera ResolveEventCamera()
        {
            if (_canvas == null)
            {
                _canvas = GetComponentInParent<Canvas>();
            }

            if (_canvas == null || _canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            return _canvas.worldCamera;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }
    }
}
