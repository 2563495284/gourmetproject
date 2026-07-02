#if UNITY_EDITOR
using GourmetProject.Game.UI.Widgets;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.Editor
{
    /// <summary>
    /// 依据原型图 菜品Tips.png 重建菜品 hover Tips 预制体：
    /// 左列顶部框（名字/美味值/风味占位标签）+ 风味详情卡，右列技能卡滚动列表（&gt;3 出滚动条）。
    /// 先生成子预制体 DishInfoCard / DishFlavorTag，再拼 DishTooltipView 并回填 SerializedField 引用。
    /// </summary>
    public static class DishTooltipPrefabBuilder
    {
        private const string TooltipPath = "Assets/GameMain/UI/DishTooltipView.prefab";
        private const string CardPath = "Assets/GameMain/UI/DishInfoCard.prefab";
        private const string TagPath = "Assets/GameMain/UI/DishFlavorTag.prefab";

        private static readonly Color Ink = new Color(0.05f, 0.035f, 0.025f, 1f);
        private static readonly Color Paper = new Color(1f, 1f, 1f, 1f);
        private static readonly Color Cream = new Color(1f, 0.94f, 0.74f, 1f);
        private static readonly Color Alert = new Color(0.92f, 0.30f, 0.28f, 1f);
        private static readonly Color Track = new Color(0.90f, 0.90f, 0.90f, 1f);

        [MenuItem("GourmetProject/UI/Rebuild Dish Tooltip Prefab")]
        public static void RebuildDishTooltipPrefab()
        {
            DishInfoCard cardPrefab = EnsureCardPrefab();
            DishFlavorTag tagPrefab = EnsureFlavorTagPrefab();

            GameObject root = new GameObject(
                "DishTooltipView",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(DishTooltipView));
            try
            {
                RectTransform rootRect = (RectTransform)root.transform;
                rootRect.sizeDelta = new Vector2(860f, 540f);
                rootRect.localScale = Vector3.one;

                // ---------- 左列 ----------
                RectTransform leftColumn = CreatePanel(root.transform, "LeftColumn", new Color(0f, 0f, 0f, 0f),
                    new Vector2(0.015f, 0.02f), new Vector2(0.40f, 0.98f));

                // 顶部框：名字 / 美味值 / 风味占位标签
                RectTransform topBox = CreatePanel(leftColumn, "TopBox", Ink, new Vector2(0f, 0.60f), new Vector2(1f, 1f));
                CreatePanel(topBox, "Paper", Paper, Vector2.zero, Vector2.one, new Vector2(4f, 4f), new Vector2(-4f, -4f));

                Text nameText = CreateText(topBox, "Name", "食物名字", 34, TextAnchor.MiddleCenter, Ink);
                Place((RectTransform)nameText.transform, new Vector2(0.06f, 0.66f), new Vector2(0.94f, 0.96f));

                RectTransform deliciousRow = CreatePanel(topBox, "DeliciousRow", new Color(0f, 0f, 0f, 0f),
                    new Vector2(0.10f, 0.36f), new Vector2(0.90f, 0.62f));
                Image deliciousIcon = CreateImage(deliciousRow, "Icon", Cream);
                Place((RectTransform)deliciousIcon.transform, new Vector2(0.0f, 0.05f), new Vector2(0.26f, 0.95f));
                deliciousIcon.preserveAspect = true;
                Text deliciousText = CreateText(deliciousRow, "Value", "：100", 30, TextAnchor.MiddleLeft, Ink);
                Place((RectTransform)deliciousText.transform, new Vector2(0.30f, 0f), new Vector2(1f, 1f));

                RectTransform flavorTagContainer = CreatePanel(topBox, "FlavorTagContainer", new Color(0f, 0f, 0f, 0f),
                    new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.32f));
                HorizontalLayoutGroup tagLayout = flavorTagContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
                tagLayout.childAlignment = TextAnchor.MiddleCenter;
                tagLayout.childControlWidth = true;
                tagLayout.childControlHeight = true;
                tagLayout.childForceExpandWidth = false;
                tagLayout.childForceExpandHeight = false;
                tagLayout.spacing = 8f;

                // 风味详情卡容器（顶对齐、向下堆叠）
                RectTransform flavorDetailContainer = CreatePanel(leftColumn, "FlavorDetailContainer", new Color(0f, 0f, 0f, 0f),
                    new Vector2(0f, 0f), new Vector2(1f, 0.58f));
                flavorDetailContainer.pivot = new Vector2(0.5f, 1f);
                flavorDetailContainer.anchorMin = new Vector2(0f, 1f);
                flavorDetailContainer.anchorMax = new Vector2(1f, 1f);
                flavorDetailContainer.anchoredPosition = new Vector2(0f, -0.42f * leftColumn.rect.height);
                VerticalLayoutGroup flavorLayout = flavorDetailContainer.gameObject.AddComponent<VerticalLayoutGroup>();
                flavorLayout.childAlignment = TextAnchor.UpperCenter;
                flavorLayout.childControlWidth = true;
                flavorLayout.childControlHeight = true;
                flavorLayout.childForceExpandWidth = true;
                flavorLayout.childForceExpandHeight = false;
                flavorLayout.spacing = 10f;
                ContentSizeFitter flavorFitter = flavorDetailContainer.gameObject.AddComponent<ContentSizeFitter>();
                flavorFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                // ---------- 右列：技能卡滚动列表 ----------
                RectTransform scrollRoot = CreatePanel(root.transform, "SkillScroll", Ink,
                    new Vector2(0.42f, 0.02f), new Vector2(0.985f, 0.98f));
                CreatePanel(scrollRoot, "Paper", Paper, Vector2.zero, Vector2.one, new Vector2(4f, 4f), new Vector2(-4f, -4f));
                ScrollRect scrollRect = scrollRoot.gameObject.AddComponent<ScrollRect>();
                scrollRect.horizontal = false;
                scrollRect.vertical = true;
                scrollRect.movementType = ScrollRect.MovementType.Clamped;
                scrollRect.scrollSensitivity = 24f;
                scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

                RectTransform viewport = CreatePanel(scrollRoot, "Viewport", new Color(0f, 0f, 0f, 0f),
                    new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(10f, 10f), new Vector2(-34f, -10f));
                viewport.pivot = new Vector2(0f, 1f);
                viewport.gameObject.AddComponent<RectMask2D>();

                RectTransform content = CreatePanel(viewport, "Content", new Color(0f, 0f, 0f, 0f),
                    new Vector2(0f, 1f), new Vector2(1f, 1f));
                content.pivot = new Vector2(0.5f, 1f);
                content.anchoredPosition = Vector2.zero;
                VerticalLayoutGroup contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
                contentLayout.childAlignment = TextAnchor.UpperCenter;
                contentLayout.childControlWidth = true;
                contentLayout.childControlHeight = true;
                contentLayout.childForceExpandWidth = true;
                contentLayout.childForceExpandHeight = false;
                contentLayout.spacing = 10f;
                contentLayout.padding = new RectOffset(4, 4, 4, 4);
                ContentSizeFitter contentFitter = content.gameObject.AddComponent<ContentSizeFitter>();
                contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                Scrollbar scrollbar = BuildVerticalScrollbar(scrollRoot);

                scrollRect.content = content;
                scrollRect.viewport = viewport;
                scrollRect.verticalScrollbar = scrollbar;

                // ---------- 回填引用 ----------
                DishTooltipView view = root.GetComponent<DishTooltipView>();
                SerializedObject so = new SerializedObject(view);
                SetObject(so, "_canvasGroup", root.GetComponent<CanvasGroup>());
                SetObject(so, "_nameText", nameText);
                SetObject(so, "_deliciousIcon", deliciousIcon);
                SetObject(so, "_deliciousText", deliciousText);
                SetObject(so, "_flavorTagContainer", flavorTagContainer);
                SetObject(so, "_flavorTagPrefab", tagPrefab);
                SetObject(so, "_flavorDetailContainer", flavorDetailContainer);
                SetObject(so, "_flavorCardPrefab", cardPrefab);
                SetObject(so, "_skillScroll", scrollRect);
                SetObject(so, "_skillContent", content);
                SetObject(so, "_skillScrollbar", scrollbar);
                SetObject(so, "_skillCardPrefab", cardPrefab);
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, TooltipPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[DishTooltipPrefabBuilder] Rebuilt DishTooltipView.prefab / DishInfoCard.prefab / DishFlavorTag.prefab");
        }

        private static DishInfoCard EnsureCardPrefab()
        {
            GameObject root = new GameObject("DishInfoCard", typeof(RectTransform), typeof(Image), typeof(DishInfoCard));
            RectTransform rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(360f, 150f);
            root.GetComponent<Image>().color = Ink;

            RectTransform paper = CreatePanel(root.transform, "Paper", Paper, Vector2.zero, Vector2.one, new Vector2(3f, 3f), new Vector2(-3f, -3f));
            VerticalLayoutGroup layout = paper.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 6f;
            layout.padding = new RectOffset(12, 12, 10, 10);

            Text title = CreateLayoutText(paper, "Title", "技能技能", 26, TextAnchor.MiddleCenter, Ink);
            Text desc = CreateLayoutText(paper, "Desc", "描述描述描述描述描述描述描述描述描述描述描述描述", 22, TextAnchor.UpperCenter, Ink);

            // 卡片高度随描述行数自适应。
            ContentSizeFitter rootFitter = root.AddComponent<ContentSizeFitter>();
            rootFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            VerticalLayoutGroup rootLayout = root.AddComponent<VerticalLayoutGroup>();
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = true;
            rootLayout.padding = new RectOffset(0, 0, 0, 0);

            Assign(root.GetComponent<DishInfoCard>(), ("_titleText", title), ("_descText", desc));
            return SavePrefab<DishInfoCard>(root, CardPath);
        }

        private static DishFlavorTag EnsureFlavorTagPrefab()
        {
            GameObject root = new GameObject("DishFlavorTag", typeof(RectTransform), typeof(Image), typeof(DishFlavorTag));
            RectTransform rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(120f, 54f);
            root.GetComponent<Image>().color = Ink;

            RectTransform paper = CreatePanel(root.transform, "Paper", Paper, Vector2.zero, Vector2.one, new Vector2(3f, 3f), new Vector2(-3f, -3f));
            HorizontalLayoutGroup layout = paper.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.padding = new RectOffset(16, 16, 6, 6);

            Text label = CreateLayoutText(paper, "Label", "风味", 24, TextAnchor.MiddleCenter, Ink);

            LayoutElement le = root.AddComponent<LayoutElement>();
            le.minWidth = 96f;
            le.minHeight = 50f;
            le.preferredHeight = 50f;
            ContentSizeFitter fitter = root.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            Assign(root.GetComponent<DishFlavorTag>(), ("_labelText", label));
            return SavePrefab<DishFlavorTag>(root, TagPath);
        }

        private static Scrollbar BuildVerticalScrollbar(Transform parent)
        {
            Image bar = CreateImage(parent, "Scrollbar Vertical", Track);
            RectTransform barRect = (RectTransform)bar.transform;
            barRect.anchorMin = new Vector2(1f, 0f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(1f, 0.5f);
            barRect.sizeDelta = new Vector2(18f, 0f);
            barRect.anchoredPosition = new Vector2(-8f, 0f);

            RectTransform slidingArea = CreatePanel(bar.transform, "Sliding Area", new Color(0f, 0f, 0f, 0f),
                Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f));
            Image handle = CreateImage(slidingArea, "Handle", Alert);
            RectTransform handleRect = (RectTransform)handle.transform;
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;

            Scrollbar scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handle;
            return scrollbar;
        }

        private static RectTransform CreatePanel(Transform parent, string name, Color color, Vector2 min, Vector2 max)
        {
            return CreatePanel(parent, name, color, min, max, Vector2.zero, Vector2.zero);
        }

        private static RectTransform CreatePanel(Transform parent, string name, Color color, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            Image image = CreateImage(parent, name, color);
            RectTransform rect = (RectTransform)image.transform;
            Place(rect, min, max, offsetMin, offsetMax);
            return rect;
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static Text CreateText(Transform parent, string name, string text, int size, TextAnchor anchor, Color color)
        {
            Text label = CreateLayoutText(parent, name, text, size, anchor, color);
            Stretch((RectTransform)label.transform);
            return label;
        }

        private static Text CreateLayoutText(Transform parent, string name, string text, int size, TextAnchor anchor, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text label = go.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = text;
            label.fontSize = size;
            label.alignment = anchor;
            label.color = color;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        private static void Stretch(RectTransform rect)
        {
            Place(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        private static void Place(RectTransform rect, Vector2 min, Vector2 max)
        {
            Place(rect, min, max, Vector2.zero, Vector2.zero);
        }

        private static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.localScale = Vector3.one;
        }

        private static void Assign(Object target, params (string field, Object value)[] values)
        {
            SerializedObject so = new SerializedObject(target);
            foreach ((string field, Object value) in values)
            {
                SetObject(so, field, value);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObject(SerializedObject so, string field, Object value)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop != null)
            {
                prop.objectReferenceValue = value;
            }
        }

        private static T SavePrefab<T>(GameObject root, string path) where T : Component
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<T>();
        }
    }
}
#endif
