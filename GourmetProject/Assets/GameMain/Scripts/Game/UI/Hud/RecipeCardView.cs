using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 扇形菜谱条里的一本菜谱本卡：普通态显示数量；战斗态额外显示上餐铃、可放/不可放计数与剩余食物列表。
    /// 卡片点击始终用于打开菜谱详情，上菜只由独立上餐铃触发。
    /// 固定结构在 Recipe.prefab，由 <see cref="RecipeView"/> 数据驱动实例化并做扇形排布/补间动画。
    /// </summary>
    public sealed class RecipeCardView : MonoBehaviour, IPointerClickHandler
    {
        private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");
        private static readonly int PaddingId = Shader.PropertyToID("_Padding");

        private const float GlowPadding = 28f;
        private const int MaxVisibleDishEntries = 6;
        private const int DishColumns = 2;
        private const int DishRows = 3;

        [FormerlySerializedAs("_capacityText")]
        [SerializeField] private Text _infoText;
        [SerializeField] private Button _button;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Image _glowBorder;
        [SerializeField] private Button _serveButton;
        [SerializeField] private GameObject _battleContent;
        [SerializeField] private RectTransform _dishListRoot;
        [SerializeField] private Text _overflowText;

        private RectTransform _rect;
        private Material _glowMaterial;
        private Action _onRightClick;
        private bool _clickSuppressed;
        private int _clickSuppressedUntilFrame = -1;
        private readonly List<DishEntryVisual> _dishEntries = new();

        private sealed class DishEntryVisual
        {
            public GameObject Root;
            public Text Name;
            public Text UnavailableMark;
        }

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
                DishEntryVisual visual = _dishEntries[i];
                bool active = i < shown;
                visual.Root.SetActive(active);
                if (!active)
                {
                    continue;
                }

                RecipeDishDisplayData dish = dishes[i];
                visual.Name.text = dish.Name ?? string.Empty;
                visual.UnavailableMark.gameObject.SetActive(!dish.CanPlace);
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
            if (_dishListRoot == null)
            {
                return;
            }

            while (_dishEntries.Count < count)
            {
                _dishEntries.Add(CreateDishEntryVisual(_dishEntries.Count));
            }
        }

        private DishEntryVisual CreateDishEntryVisual(int index)
        {
            var root = new GameObject($"Dish_{index + 1}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            root.layer = gameObject.layer;
            var rect = (RectTransform)root.transform;
            rect.SetParent(_dishListRoot, false);

            int column = index % DishColumns;
            int row = index / DishColumns;
            const float horizontalGap = 4f;
            const float verticalGap = 3f;
            float width = (_dishListRoot.rect.width - horizontalGap) / DishColumns;
            float height = (_dishListRoot.rect.height - verticalGap * (DishRows - 1)) / DishRows;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(column * (width + horizontalGap), -row * (height + verticalGap));
            rect.sizeDelta = new Vector2(width, height);

            Image image = root.GetComponent<Image>();
            image.sprite = Resources.Load<Sprite>("Sprites/UI/ui_recipe_dish_tile");
            image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = Color.white;
            image.raycastTarget = false;

            Text name = CreateText("Name", rect, 11, TextAnchor.MiddleCenter, new Color(0.22f, 0.12f, 0.04f));
            RectTransform nameRect = name.rectTransform;
            nameRect.anchorMin = Vector2.zero;
            nameRect.anchorMax = Vector2.one;
            nameRect.offsetMin = new Vector2(5f, 2f);
            nameRect.offsetMax = new Vector2(-5f, -2f);
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 8;
            name.resizeTextMaxSize = 11;

            Text mark = CreateText("Unavailable", rect, 16, TextAnchor.UpperRight, new Color(0.9f, 0.1f, 0.08f));
            mark.text = "×";
            mark.fontStyle = FontStyle.Bold;
            RectTransform markRect = mark.rectTransform;
            markRect.anchorMin = new Vector2(1f, 1f);
            markRect.anchorMax = new Vector2(1f, 1f);
            markRect.pivot = new Vector2(1f, 1f);
            markRect.anchoredPosition = new Vector2(2f, 3f);
            markRect.sizeDelta = new Vector2(20f, 20f);

            return new DishEntryVisual
            {
                Root = root,
                Name = name,
                UnavailableMark = mark,
            };
        }

        private Text CreateText(string objectName, Transform parent, int fontSize, TextAnchor alignment, Color color)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            Text text = go.GetComponent<Text>();
            text.font = _infoText != null ? _infoText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
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

        public bool ContainsScreenPoint(Vector2 screenPoint, Camera eventCamera)
        {
            return RectTransformUtility.RectangleContainsScreenPoint(Rect, screenPoint, eventCamera);
        }

        public Vector2 CenterScreenPoint(Camera eventCamera)
        {
            return RectTransformUtility.WorldToScreenPoint(eventCamera, Rect.TransformPoint(Rect.rect.center));
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
