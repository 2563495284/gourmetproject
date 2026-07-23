using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>战斗菜谱卡内一条剩余食物的显示数据。</summary>
    public readonly struct RecipeDishDisplayData
    {
        public RecipeDishDisplayData(string name, bool canPlace)
        {
            Name = name;
            CanPlace = canPlace;
        }

        public string Name { get; }

        public bool CanPlace { get; }
    }

    /// <summary>
    /// BattleForm 中固定显示的唯一菜谱：普通态显示数量；战斗态额外显示上餐铃、
    /// 可放/不可放计数与剩余食物列表。
    /// 卡片点击始终用于打开菜谱详情，上菜只由独立上餐铃触发。
    /// 固定结构在 Recipe.prefab，由 BattleForm 直接持有，不再经过动态容器或状态动画。
    /// </summary>
    public sealed class RecipeCardView : MonoBehaviour, IPointerClickHandler
    {
        private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");
        private static readonly int PaddingId = Shader.PropertyToID("_Padding");

        private const float GlowPadding = 28f;
        private const int MaxVisibleDishEntries = 6;

        [FormerlySerializedAs("_capacityText")]
        [SerializeField] private Text _infoText;
        [SerializeField] private Button _button;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Image _glowBorder;
        [SerializeField] private Button _serveButton;
        [SerializeField] private GameObject _battleContent;
        [SerializeField] private RectTransform _dishListRoot;
        [SerializeField] private RecipeDishEntryView _dishEntryPrefab;
        [SerializeField] private Text _overflowText;

        private RectTransform _rect;
        private Material _glowMaterial;
        private Action _onRightClick;
        private bool _clickSuppressed;
        private int _clickSuppressedUntilFrame = -1;
        private readonly List<RecipeDishEntryView> _dishEntries = new();

        public RectTransform Rect
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

        public CanvasGroup CanvasGroup
        {
            get
            {
                if (_canvasGroup == null)
                {
                    _canvasGroup = GetComponent<CanvasGroup>();
                    if (_canvasGroup == null)
                    {
                        _canvasGroup = gameObject.AddComponent<CanvasGroup>();
                    }
                }

                return _canvasGroup;
            }
        }

        public void OnDestroy()
        {
            if (_glowMaterial != null)
            {
                Destroy(_glowMaterial);
                _glowMaterial = null;
            }
        }

        public void Bind(
            string capacity,
            bool interactable,
            Action onClick,
            Action onRightClick = null,
            bool showBattleContent = false,
            bool serveInteractable = false,
            Action onServe = null,
            IReadOnlyList<RecipeDishDisplayData> dishes = null)
        {
            _onRightClick = onRightClick;

            if (_infoText != null)
            {
                _infoText.text = showBattleContent ? BuildPlacementSummary(dishes) : capacity ?? string.Empty;
            }

            if (_button != null)
            {
                _button.onClick.RemoveAllListeners();
                _button.interactable = interactable;
                if (onClick != null)
                {
                    _button.onClick.AddListener(() =>
                    {
                        if (!ShouldSuppressClick())
                        {
                            onClick();
                        }
                    });
                }
            }

            BindBattleContent(showBattleContent, serveInteractable, onServe, dishes);
        }

        private void BindBattleContent(
            bool visible,
            bool serveInteractable,
            Action onServe,
            IReadOnlyList<RecipeDishDisplayData> dishes)
        {
            if (_battleContent != null)
            {
                _battleContent.SetActive(visible);
            }

            if (_serveButton != null)
            {
                _serveButton.gameObject.SetActive(visible);
                _serveButton.onClick.RemoveAllListeners();
                _serveButton.interactable = visible && serveInteractable;
                if (onServe != null)
                {
                    _serveButton.onClick.AddListener(() =>
                    {
                        if (!ShouldSuppressClick())
                        {
                            onServe();
                        }
                    });
                }
            }

            if (!visible)
            {
                return;
            }

            int count = dishes?.Count ?? 0;
            int shown = Mathf.Min(count, MaxVisibleDishEntries);
            EnsureDishEntryVisuals(shown);
            for (int i = 0; i < _dishEntries.Count; i++)
            {
                RecipeDishEntryView visual = _dishEntries[i];
                bool active = i < shown;
                visual.gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                RecipeDishDisplayData dish = dishes[i];
                visual.Bind(dish.Name, dish.CanPlace);
            }

            if (_overflowText != null)
            {
                _overflowText.gameObject.SetActive(count > MaxVisibleDishEntries);
            }
        }

        private static string BuildPlacementSummary(IReadOnlyList<RecipeDishDisplayData> dishes)
        {
            int placeable = 0;
            int blocked = 0;
            int count = dishes?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                if (dishes[i].CanPlace)
                {
                    placeable++;
                }
                else
                {
                    blocked++;
                }
            }

            return $"{placeable}<color=#35B84A>✓</color>  {blocked}<color=#E33A3A>×</color>";
        }

        private void EnsureDishEntryVisuals(int count)
        {
            if (_dishListRoot == null || _dishEntryPrefab == null)
            {
                return;
            }

            while (_dishEntries.Count < count)
            {
                RecipeDishEntryView entry = Instantiate(_dishEntryPrefab, _dishListRoot);
                entry.gameObject.name = $"Dish_{_dishEntries.Count + 1}";
                _dishEntries.Add(entry);
            }
        }

        public void SetClickSuppressed(bool suppressed)
        {
            _clickSuppressed = suppressed;
            if (!suppressed)
            {
                _clickSuppressedUntilFrame = Time.frameCount;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null)
            {
                return;
            }

            if (ShouldSuppressClick())
            {
                eventData.Use();
                return;
            }

            if (eventData.button != PointerEventData.InputButton.Right)
            {
                return;
            }

            _onRightClick?.Invoke();
            eventData.Use();
        }

        private bool ShouldSuppressClick()
        {
            return _clickSuppressed || Time.frameCount <= _clickSuppressedUntilFrame;
        }

        public void SetTargetHighlight(bool visible, bool emphasized)
        {
            EnsureGlowBorder();
            if (_glowBorder == null)
            {
                return;
            }

            _glowBorder.gameObject.SetActive(visible);
            Color color = new Color(0.25f, 1f, 0.35f, emphasized ? 0.9f : 0.38f);
            _glowBorder.color = visible ? color : Color.clear;
            UpdateGlowMaterial();
        }

        private void EnsureGlowBorder()
        {
            if (_glowBorder != null)
            {
                EnsureGlowMaterial();
                return;
            }

            Debug.LogError($"{nameof(RecipeCardView)} prefab 缺少 TargetGlow。", this);
        }

        private void EnsureGlowMaterial()
        {
            if (_glowBorder == null || _glowMaterial != null)
            {
                return;
            }

            Material baseMaterial = _glowBorder.material;
            if (baseMaterial == null || baseMaterial.shader == null || baseMaterial.shader.name != "GourmetProject/UIOuterGlow")
            {
                baseMaterial = Resources.Load<Material>("Materials/UIOuterGlow");
            }

            if (baseMaterial == null)
            {
                return;
            }

            _glowMaterial = new Material(baseMaterial);
            _glowBorder.material = _glowMaterial;
            UpdateGlowMaterial();
        }

        private void UpdateGlowMaterial()
        {
            if (_glowMaterial == null || _glowBorder == null)
            {
                return;
            }

            Rect r = _glowBorder.rectTransform.rect;
            _glowMaterial.SetVector(QuadSizeId, new Vector4(r.width, r.height, 0f, 0f));
            _glowMaterial.SetFloat(PaddingId, GlowPadding);
        }
    }
}
