using System;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Common;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    public sealed partial class RecipeReadonlyBookView
    {
        private static Font s_defaultFont;

        [Header("Compare Popup Templates")]
        [SerializeField] private RectTransform _compareOverlayPrefab;
        [SerializeField] private Image _comparePanelPrefab;
        [SerializeField] private Image _compareCardPrefab;
        [SerializeField] private Text _compareTextPrefab;
        [SerializeField] private Button _compareButtonPrefab;

        [Header("Compare Popup Style")]
        [SerializeField] private Color _compareOverlayColor =
            new(0f, 0f, 0f, 0.58f);
        [SerializeField] private Color _comparePanelColor =
            new(0.96f, 0.91f, 0.82f, 1f);
        [SerializeField] private Color _compareCardColor =
            new(1f, 0.97f, 0.9f, 1f);

        private GameObject _compareOverlay;
        private bool _compareTemplateMissingReported;

        private void ShowActiveItemCompare(
            ItemDefinition item,
            ActiveTarget target,
            Action onBack,
            Action onConfirm)
        {
            ClearCompareOverlay();
            RecipeBookSlot slot = RecipeSlot(target);
            DishDef def = slot == null
                ? null
                : _run.Database.GetDish(slot.DishId);
            if (def == null)
            {
                return;
            }

            RectTransform overlay = CreateStretchChild(
                "ActiveItemDishCompare_Runtime",
                transform);
            if (overlay == null)
            {
                return;
            }

            _compareOverlay = overlay.gameObject;
            Image overlayImage = _compareOverlay.GetComponent<Image>();
            if (overlayImage != null)
            {
                overlayImage.color = _compareOverlayColor;
                overlayImage.raycastTarget = true;
            }

            _compareOverlay.transform.SetAsLastSibling();
            Image panelImage = CreateImage(
                _comparePanelPrefab,
                "Panel",
                _compareOverlay.transform,
                new Vector2(960f, 520f));
            if (panelImage == null)
            {
                ClearCompareOverlay();
                return;
            }

            panelImage.color = _comparePanelColor;
            Transform panel = panelImage.transform;
            CreateText(
                "Title",
                panel,
                $"{item.Name}：确认目标菜品",
                new Vector2(0f, 216f),
                new Vector2(860f, 44f),
                24,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            CreateDishInfoCard(
                panel,
                "原菜品",
                BuildDishInfo(
                    def,
                    ComposeFlavorIds(def, slot.ExtraFlavorIds),
                    slot),
                new Vector2(-260f, 28f));
            CreateText(
                "Arrow",
                panel,
                "=>",
                new Vector2(0f, 40f),
                new Vector2(92f, 60f),
                32,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);

            var previewExtraFlavors = PreviewExtraFlavors(
                slot.ExtraFlavorIds,
                item.EffectParam);
            CreateDishInfoCard(
                panel,
                "使用后",
                BuildDishInfo(
                    def,
                    ComposeFlavorIds(def, previewExtraFlavors),
                    slot),
                new Vector2(260f, 28f));
            CreateButton(
                "BackButton",
                panel,
                "返回",
                new Vector2(-260f, -214f),
                new Vector2(180f, 48f),
                onBack);
            CreateButton(
                "ConfirmButton",
                panel,
                "确认",
                new Vector2(260f, -214f),
                new Vector2(180f, 48f),
                () => onConfirm?.Invoke());
        }

        private void ShowEventDeleteConfirm(
            string title,
            ActiveTarget target,
            Action onCancel,
            Action<ActiveTarget> onConfirm)
        {
            RecipeBookSlot slot = RecipeSlot(target);
            DishDef def = slot == null
                ? null
                : _run.Database.GetDish(slot.DishId);
            string dishName = def != null ? def.Name : target.Id;
            var data = new ConfirmDialogData
            {
                Title = string.IsNullOrWhiteSpace(title)
                    ? "确认删除菜品"
                    : title,
                Message = $"确定要从菜谱中删除「{dishName}」吗？",
                ConfirmText = "删除",
                CancelText = "返回",
                OnConfirm = () => onConfirm?.Invoke(target),
                OnCancel = onCancel,
            };
            GameApp.UI.OpenUIForm(
                UIForms.ConfirmDialog,
                UIForms.GroupDialog,
                data);
        }

        private void ShowShopDeleteConfirm(ActiveTarget target)
        {
            RecipeBookSlot slot = RecipeSlot(target);
            DishDef def = slot == null
                ? null
                : _run.Database.GetDish(slot.DishId);
            if (slot == null || def == null)
            {
                return;
            }

            int cost = ShopService.DeleteCost(_run);
            var data = new ConfirmDialogData
            {
                Title = "确认删除食物",
                Message = $"花费 {cost} 金币，从菜谱中删除「{def.Name}」？",
                ConfirmText = $"删除 -{cost}",
                CancelText = "返回",
                OnConfirm = () =>
                {
                    if (!ShopService.DeleteDishAt(_run, target.Y))
                    {
                        RebuildWarehouseForCurrentState();
                        return;
                    }

                    _onChanged?.Invoke();
                    RebuildWarehouseForCurrentState();
                },
            };
            GameApp.UI.OpenUIForm(
                UIForms.ConfirmDialog,
                UIForms.GroupDialog,
                data);
        }

        private void CreateDishInfoCard(
            Transform parent,
            string title,
            string info,
            Vector2 center)
        {
            Image image = CreateImage(
                _compareCardPrefab,
                title,
                parent,
                new Vector2(350f, 330f),
                center);
            if (image == null)
            {
                return;
            }

            image.color = _compareCardColor;
            Transform card = image.transform;
            CreateText(
                "Title",
                card,
                title,
                new Vector2(0f, 132f),
                new Vector2(310f, 36f),
                21,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);

            Text body = CreateText(
                "Body",
                card,
                info,
                new Vector2(0f, -28f),
                new Vector2(300f, 250f),
                17,
                FontStyle.Normal,
                TextAnchor.UpperLeft);
            if (body != null)
            {
                body.horizontalOverflow = HorizontalWrapMode.Wrap;
                body.verticalOverflow = VerticalWrapMode.Truncate;
            }
        }

        private void ClearCompareOverlay()
        {
            if (_compareOverlay != null)
            {
                Destroy(_compareOverlay);
                _compareOverlay = null;
            }
        }

        private RectTransform CreateStretchChild(
            string name,
            Transform parent)
        {
            if (_compareOverlayPrefab == null)
            {
                ReportMissingCompareTemplate(nameof(_compareOverlayPrefab));
                return null;
            }

            RectTransform rect = Instantiate(
                _compareOverlayPrefab,
                parent,
                false);
            rect.gameObject.name = name;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.gameObject.SetActive(true);
            return rect;
        }

        private static void ConfigureRect(
            RectTransform rect,
            string name,
            Vector2 size,
            Vector2 anchoredPosition)
        {
            if (rect == null)
            {
                return;
            }

            rect.gameObject.name = name;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private Image CreateImage(
            Image prefab,
            string name,
            Transform parent,
            Vector2 size,
            Vector2 anchoredPosition = default)
        {
            if (prefab == null)
            {
                ReportMissingCompareTemplate(name);
                return null;
            }

            Image image = Instantiate(prefab, parent, false);
            ConfigureRect(
                (RectTransform)image.transform,
                name,
                size,
                anchoredPosition);
            image.gameObject.SetActive(true);
            return image;
        }

        private Text CreateText(
            string name,
            Transform parent,
            string text,
            Vector2 anchoredPosition,
            Vector2 size,
            int fontSize,
            FontStyle style,
            TextAnchor alignment)
        {
            if (_compareTextPrefab == null)
            {
                ReportMissingCompareTemplate(nameof(_compareTextPrefab));
                return null;
            }

            Text label = Instantiate(_compareTextPrefab, parent, false);
            ConfigureRect(
                (RectTransform)label.transform,
                name,
                size,
                anchoredPosition);
            if (label.font == null)
            {
                label.font = ResolveFont();
            }

            label.text = text ?? string.Empty;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.alignment = alignment;
            label.color = Color.black;
            label.raycastTarget = false;
            label.gameObject.SetActive(true);
            return label;
        }

        private Button CreateButton(
            string name,
            Transform parent,
            string label,
            Vector2 anchoredPosition,
            Vector2 size,
            Action onClick)
        {
            if (_compareButtonPrefab == null)
            {
                ReportMissingCompareTemplate(nameof(_compareButtonPrefab));
                return null;
            }

            Button button = Instantiate(
                _compareButtonPrefab,
                parent,
                false);
            ConfigureRect(
                (RectTransform)button.transform,
                name,
                size,
                anchoredPosition);
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick?.Invoke());
            if (button.targetGraphic == null)
            {
                button.targetGraphic = button.GetComponent<Image>();
            }

            SetButtonText(button, label);
            Text text = button.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                ConfigureRect(
                    (RectTransform)text.transform,
                    "Label",
                    size,
                    Vector2.zero);
                if (text.font == null)
                {
                    text.font = ResolveFont();
                }

                text.fontSize = 20;
                text.fontStyle = FontStyle.Bold;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = Color.black;
                text.raycastTarget = false;
                text.gameObject.SetActive(true);
            }

            button.gameObject.SetActive(true);
            return button;
        }

        private void ReportMissingCompareTemplate(string templateName)
        {
            if (_compareTemplateMissingReported)
            {
                return;
            }

            Debug.LogError(
                $"{nameof(RecipeReadonlyBookView)} "
                + $"缺少对比弹窗模板：{templateName}。",
                this);
            _compareTemplateMissingReported = true;
        }

        private static Font ResolveFont()
        {
            if (s_defaultFont == null)
            {
                s_defaultFont =
                    Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (s_defaultFont == null)
                {
                    s_defaultFont =
                        Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
            }

            return s_defaultFont;
        }
    }
}
