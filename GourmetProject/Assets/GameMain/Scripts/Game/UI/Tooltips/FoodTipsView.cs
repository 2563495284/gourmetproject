using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Scoring;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 食物 hover Tips 组合器：只负责组合/定位 1、2、3 等独立模块。
    /// </summary>
    public sealed class FoodTipsView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private FoodMaterialTipsView _materialsView;
        [SerializeField] private FoodScoreTipsView _scoreView;
        [SerializeField] private FoodSummaryTipsView _summaryView;
        [SerializeField] private RectTransform _flavorDetailsRoot;
        [SerializeField] private RectTransform _specialTagsRoot;
        [SerializeField] private RectTransform _transferredSubSkillsRoot;

        [Header("Layout")]
        [SerializeField] private float _targetGap = 18f;
        [SerializeField] private float _screenPadding = 16f;
        [SerializeField] private float _detailGap = 10f;

        public FoodMaterialTipsView MaterialsView
        {
            get
            {
                EnsureStructure();
                return _materialsView;
            }
        }

        public FoodScoreTipsView ScoreView
        {
            get
            {
                EnsureStructure();
                return _scoreView;
            }
        }

        public FoodSummaryTipsView SummaryView
        {
            get
            {
                EnsureStructure();
                return _summaryView;
            }
        }

        public void Bind(DishInstance dish, DiningTable table, GameplayDatabase db, ScoreResult scoreResult = null)
        {
            Bind(FoodTipsDataFactory.Build(dish, table, db, scoreResult));
        }

        public void Bind(FoodTipsData data)
        {
            EnsureStructure();
            data ??= new FoodTipsData(null, null, null, null, null, null);

            _materialsView.Bind(data.Materials);
            _scoreView.Bind(data.Score);
            _summaryView.Bind(data.Summary);
            BuildInfoCards(_flavorDetailsRoot, data.FlavorDetails, "FlavorDetail");
            BuildBadges(_specialTagsRoot, data.SpecialTags);
            BuildInfoCards(_transferredSubSkillsRoot, data.TransferredSubSkills, "TransferredSubSkill");
        }

        public void BindMaterialsOnly(IReadOnlyList<FoodMaterialTipsEntry> materials)
        {
            EnsureStructure();
            _materialsView.Bind(materials);
            _scoreView.Hide();
            _summaryView.Hide();
            FoodTipUiUtility.ClearChildren(_flavorDetailsRoot);
            FoodTipUiUtility.ClearChildren(_specialTagsRoot);
            FoodTipUiUtility.ClearChildren(_transferredSubSkillsRoot);
            _flavorDetailsRoot.gameObject.SetActive(false);
            _specialTagsRoot.gameObject.SetActive(false);
            _transferredSubSkillsRoot.gameObject.SetActive(false);
        }

        public void Show()
        {
            EnsureStructure();
            gameObject.SetActive(true);
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
            }
        }

        public void Hide()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
            }

            gameObject.SetActive(false);
        }

        public void PlaceAroundWorldBounds(Bounds worldBounds, Camera worldCamera, Canvas canvas)
        {
            if (worldCamera == null)
            {
                worldCamera = Camera.main;
            }

            if (worldCamera == null)
            {
                return;
            }

            canvas ??= GetComponentInParent<Canvas>();
            RectTransform parent = transform as RectTransform;
            if (parent == null)
            {
                return;
            }

            RectTransform canvasRect = parent.parent as RectTransform;
            if (canvasRect == null)
            {
                return;
            }

            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            Rect targetRect = WorldBoundsToLocalRect(worldBounds, worldCamera, canvasRect, uiCamera);
            Canvas.ForceUpdateCanvases();

            PlaceLeft(_materialsView.transform as RectTransform, targetRect, canvasRect.rect);
            PlaceAbove(_scoreView.transform as RectTransform, targetRect, canvasRect.rect);
            PlaceRightTop(_summaryView.transform as RectTransform, targetRect, canvasRect.rect);
            PlaceBelow(_flavorDetailsRoot, _summaryView.transform as RectTransform, canvasRect.rect);
            PlaceAbove(_specialTagsRoot, _summaryView.transform as RectTransform, canvasRect.rect);
            PlaceRight(_transferredSubSkillsRoot, _summaryView.transform as RectTransform, canvasRect.rect);
        }

        private void Awake()
        {
            EnsureStructure();
            Hide();
        }

        private void Reset()
        {
            EnsureStructure();
        }

        private void EnsureStructure()
        {
            RectTransform root = FoodTipUiUtility.EnsureRect(gameObject);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            _canvasGroup ??= gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            _materialsView = EnsureModule(_materialsView, "1_Materials");
            _scoreView = EnsureModule(_scoreView, "2_Score");
            _summaryView = EnsureModule(_summaryView, "3_Summary");

            _flavorDetailsRoot = EnsurePanelRoot(_flavorDetailsRoot, "4_FlavorDetails", 220f);
            _specialTagsRoot = EnsurePanelRoot(_specialTagsRoot, "4_SpecialTags", 180f);
            _transferredSubSkillsRoot = EnsurePanelRoot(_transferredSubSkillsRoot, "4_TransferredSubSkills", 320f);
        }

        private T EnsureModule<T>(T current, string name) where T : Component
        {
            if (current != null)
            {
                return current;
            }

            RectTransform root = transform as RectTransform;
            Transform found = transform.Find(name);
            GameObject go = found != null ? found.gameObject : FoodTipUiUtility.CreateChild(root, name).gameObject;
            T component = go.GetComponent<T>();
            if (component == null)
            {
                component = go.AddComponent<T>();
            }

            return component;
        }

        private RectTransform EnsurePanelRoot(RectTransform current, string name, float width)
        {
            if (current == null)
            {
                current = transform.Find(name) as RectTransform;
                if (current == null)
                {
                    current = FoodTipUiUtility.CreateChild(transform as RectTransform, name);
                }
            }

            current.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            var layout = current.gameObject.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
            {
                layout = current.gameObject.AddComponent<VerticalLayoutGroup>();
            }

            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = current.gameObject.GetComponent<ContentSizeFitter>();
            if (fitter == null)
            {
                fitter = current.gameObject.AddComponent<ContentSizeFitter>();
            }

            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            current.gameObject.SetActive(false);
            return current;
        }

        private void BuildInfoCards(RectTransform root, IReadOnlyList<FoodInfoEntry> entries, string prefix)
        {
            FoodTipUiUtility.ClearChildren(root);
            int count = entries != null ? entries.Count : 0;
            root.gameObject.SetActive(count > 0);
            for (int i = 0; i < count; i++)
            {
                FoodInfoEntry entry = entries[i];
                RectTransform cardRect = FoodTipUiUtility.CreateChild(root, $"{prefix}_{i}");
                var card = cardRect.gameObject.AddComponent<FoodTipCardView>();
                card.Bind(entry.Title, entry.Desc);
            }
        }

        private void BuildBadges(RectTransform root, IReadOnlyList<string> tags)
        {
            FoodTipUiUtility.ClearChildren(root);
            int count = tags != null ? tags.Count : 0;
            root.gameObject.SetActive(count > 0);
            for (int i = 0; i < count; i++)
            {
                RectTransform badge = FoodTipUiUtility.CreateChild(root, $"Tag_{i}");
                FoodTipUiUtility.EnsurePanelImage(badge.gameObject, new Color(1f, 1f, 1f, 0.96f));
                Text text = FoodTipUiUtility.EnsureTextChild(badge, null, "Label", 20, FontStyle.Bold, TextAnchor.MiddleCenter);
                text.text = tags[i] ?? string.Empty;
                LayoutElement layout = badge.gameObject.GetComponent<LayoutElement>() ?? badge.gameObject.AddComponent<LayoutElement>();
                layout.minHeight = 44f;
                layout.preferredHeight = 44f;
            }
        }

        private Rect WorldBoundsToLocalRect(Bounds bounds, Camera worldCamera, RectTransform parent, Camera uiCamera)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Vector3[] world =
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, max.y, min.z),
            };

            bool has = false;
            Vector2 localMin = Vector2.zero;
            Vector2 localMax = Vector2.zero;
            foreach (Vector3 point in world)
            {
                Vector2 screen = worldCamera.WorldToScreenPoint(point);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, uiCamera, out Vector2 local))
                {
                    continue;
                }

                if (!has)
                {
                    localMin = localMax = local;
                    has = true;
                }
                else
                {
                    localMin = Vector2.Min(localMin, local);
                    localMax = Vector2.Max(localMax, local);
                }
            }

            return has
                ? Rect.MinMaxRect(localMin.x, localMin.y, localMax.x, localMax.y)
                : new Rect(Vector2.zero, Vector2.one);
        }

        private void PlaceLeft(RectTransform rect, Rect target, Rect bounds)
        {
            if (rect == null || !rect.gameObject.activeSelf)
            {
                return;
            }

            Vector2 size = PreferredSize(rect);
            Vector2 center = new Vector2(target.xMin - _targetGap - size.x * 0.5f, target.center.y);
            SetCenter(rect, Clamp(center, size, bounds));
        }

        private void PlaceAbove(RectTransform rect, Rect target, Rect bounds)
        {
            if (rect == null || !rect.gameObject.activeSelf)
            {
                return;
            }

            Vector2 size = PreferredSize(rect);
            Vector2 center = new Vector2(target.center.x, target.yMax + _targetGap + size.y * 0.5f);
            SetCenter(rect, Clamp(center, size, bounds));
        }

        private void PlaceRightTop(RectTransform rect, Rect target, Rect bounds)
        {
            if (rect == null || !rect.gameObject.activeSelf)
            {
                return;
            }

            Vector2 size = PreferredSize(rect);
            Vector2 center = new Vector2(target.xMax + _targetGap + size.x * 0.5f, target.yMax - size.y * 0.5f);
            SetCenter(rect, Clamp(center, size, bounds));
        }

        private void PlaceBelow(RectTransform rect, RectTransform anchor, Rect bounds)
        {
            if (rect == null || anchor == null || !rect.gameObject.activeSelf || !anchor.gameObject.activeSelf)
            {
                return;
            }

            Vector2 size = PreferredSize(rect);
            Vector2 anchorCenter = anchor.anchoredPosition;
            Vector2 anchorSize = PreferredSize(anchor);
            Vector2 center = new Vector2(anchorCenter.x, anchorCenter.y - anchorSize.y * 0.5f - _detailGap - size.y * 0.5f);
            SetCenter(rect, Clamp(center, size, bounds));
        }

        private void PlaceAbove(RectTransform rect, RectTransform anchor, Rect bounds)
        {
            if (rect == null || anchor == null || !rect.gameObject.activeSelf || !anchor.gameObject.activeSelf)
            {
                return;
            }

            Vector2 size = PreferredSize(rect);
            Vector2 anchorCenter = anchor.anchoredPosition;
            Vector2 anchorSize = PreferredSize(anchor);
            Vector2 center = new Vector2(anchorCenter.x, anchorCenter.y + anchorSize.y * 0.5f + _detailGap + size.y * 0.5f);
            SetCenter(rect, Clamp(center, size, bounds));
        }

        private void PlaceRight(RectTransform rect, RectTransform anchor, Rect bounds)
        {
            if (rect == null || anchor == null || !rect.gameObject.activeSelf || !anchor.gameObject.activeSelf)
            {
                return;
            }

            Vector2 size = PreferredSize(rect);
            Vector2 anchorCenter = anchor.anchoredPosition;
            Vector2 anchorSize = PreferredSize(anchor);
            Vector2 center = new Vector2(anchorCenter.x + anchorSize.x * 0.5f + _detailGap + size.x * 0.5f, anchorCenter.y);
            SetCenter(rect, Clamp(center, size, bounds));
        }

        private Vector2 PreferredSize(RectTransform rect)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            Vector2 size = rect.rect.size;
            size.x = Mathf.Max(size.x, LayoutUtility.GetPreferredWidth(rect));
            size.y = Mathf.Max(size.y, LayoutUtility.GetPreferredHeight(rect));
            size.x = Mathf.Max(1f, size.x);
            size.y = Mathf.Max(1f, size.y);
            return size;
        }

        private Vector2 Clamp(Vector2 center, Vector2 size, Rect bounds)
        {
            float halfW = size.x * 0.5f;
            float halfH = size.y * 0.5f;
            return new Vector2(
                Mathf.Clamp(center.x, bounds.xMin + halfW + _screenPadding, bounds.xMax - halfW - _screenPadding),
                Mathf.Clamp(center.y, bounds.yMin + halfH + _screenPadding, bounds.yMax - halfH - _screenPadding));
        }

        private static void SetCenter(RectTransform rect, Vector2 center)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = center;
        }
    }
}
