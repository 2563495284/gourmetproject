using System;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Hud;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Editor
{
    internal static class BattleEntryPresentationPrefabBuilder
    {
        private const string OverlayPath =
            "Assets/GameMain/Content/Resources/Prefabs/UI/Battle/BattleEntryPresentationOverlay.prefab";
        private const string BattleFormPath =
            "Assets/GameMain/Content/Prefabs/UI/Battle/BattleForm.prefab";
        private const string FontPath =
            "Assets/GameMain/Content/Resources/Fonts/AlimamaShuHeiTi-Bold SDF.asset";
        private const string DishValueSpriteAssetPath =
            "Assets/GameMain/Content/Resources/Sprites/UI/FengKuangCanTing/Score/dish_value_icon.asset";
        private const string PanelPath =
            "Assets/GameMain/Content/Resources/Sprites/UI/FengKuangCanTing/Panels/panel_dialog_large.png";
        private const string TitlePath =
            "Assets/GameMain/Content/Resources/Sprites/UI/FengKuangCanTing/Panels/score_title.png";
        private const string TimelineThemePath =
            "Assets/GameMain/Content/Resources/Sprites/UI/TimelineFresh/TimelineAxisTheme.asset";

        private static readonly Color Ink = new Color32(91, 57, 38, 255);
        private static readonly Color Danger = new Color32(194, 72, 65, 255);

        [MenuItem("GourmetProject/UI/Battle/Rebuild Battle Entry Presentation")]
        public static void Rebuild()
        {
            GameObject overlayPrefab = BuildOverlayPrefab();
            WireBattleForm(overlayPrefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("BattleEntryPresentationOverlay rebuilt and wired to BattleForm.");
        }

        private static GameObject BuildOverlayPrefab()
        {
            EnsureAsset<TMP_FontAsset>(FontPath);
            EnsureAsset<TMP_SpriteAsset>(DishValueSpriteAssetPath);
            EnsureAsset<Sprite>(PanelPath);
            EnsureAsset<Sprite>(TitlePath);
            EnsureAsset<TimelineAxisTheme>(TimelineThemePath);

            var root = new GameObject(
                "BattleEntryPresentationOverlay",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup),
                typeof(BattleEntryPresentationView));
            root.layer = LayerMask.NameToLayer("UI");
            RectTransform overlay = root.GetComponent<RectTransform>();
            Stretch(overlay);

            Image inputCatcher = root.GetComponent<Image>();
            inputCatcher.color = Color.clear;
            inputCatcher.raycastTarget = true;
            CanvasGroup inputGroup = root.GetComponent<CanvasGroup>();
            inputGroup.alpha = 1f;
            inputGroup.interactable = false;
            inputGroup.blocksRaycasts = false;

            GameObject viewport = CreateUi("CenterViewport", root.transform);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = new Vector2(0.165f, 0f);
            viewportRect.anchorMax = new Vector2(0.835f, 1f);
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            GameObject content = CreateUi("Content", viewport.transform);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            Anchor(contentRect, new Vector2(0.5f, 0.5f), new Vector2(1120f, 860f), Vector2.zero);
            CanvasGroup contentGroup = content.AddComponent<CanvasGroup>();
            contentGroup.alpha = 0f;
            contentGroup.interactable = false;
            contentGroup.blocksRaycasts = false;

            GameObject target = CreateUi("TargetScore", content.transform);
            RectTransform targetRect = target.GetComponent<RectTransform>();
            Anchor(targetRect, new Vector2(0.5f, 0.5f), new Vector2(900f, 320f), Vector2.zero);

            Image targetPaper = CreateImage(
                "TargetPaper",
                target.transform,
                AssetDatabase.LoadAssetAtPath<Sprite>(PanelPath));
            Stretch(targetPaper.rectTransform);
            targetPaper.type = Image.Type.Sliced;

            Image titlePaper = CreateImage(
                "TargetTitlePaper",
                target.transform,
                AssetDatabase.LoadAssetAtPath<Sprite>(TitlePath));
            Anchor(
                titlePaper.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(520f, 120f),
                new Vector2(0f, 112f));
            titlePaper.type = Image.Type.Sliced;

            TMP_Text targetTitle = CreateText(
                "TargetTitle",
                titlePaper.transform,
                "目标美味值",
                38f,
                TextAlignmentOptions.Center,
                Ink);
            Stretch(targetTitle.rectTransform, 18f);
            targetTitle.fontStyle = FontStyles.Bold;

            TMP_Text targetScore = CreateText(
                "TargetScoreValue",
                target.transform,
                "<sprite name=\"dish_value_icon\"> 0",
                92f,
                TextAlignmentOptions.Center,
                Ink);
            Anchor(
                targetScore.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(760f, 150f),
                new Vector2(0f, -45f));
            targetScore.fontStyle = FontStyles.Bold;
            targetScore.spriteAsset = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(DishValueSpriteAssetPath);

            GameObject rule = CreateUi("StarEvaluationRule", content.transform);
            RectTransform ruleRect = rule.GetComponent<RectTransform>();
            Anchor(
                ruleRect,
                new Vector2(0.5f, 0.5f),
                new Vector2(1040f, 330f),
                new Vector2(0f, -215f));
            CanvasGroup ruleGroup = rule.AddComponent<CanvasGroup>();
            ruleGroup.alpha = 0f;
            ruleGroup.interactable = false;
            ruleGroup.blocksRaycasts = false;

            Image rulePaper = CreateImage(
                "RulePaper",
                rule.transform,
                AssetDatabase.LoadAssetAtPath<Sprite>(PanelPath));
            Stretch(rulePaper.rectTransform);
            rulePaper.type = Image.Type.Sliced;

            Image ruleIcon = CreateImage("RuleIcon", rule.transform, null);
            Anchor(
                ruleIcon.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(180f, 180f),
                new Vector2(-390f, -12f));
            ruleIcon.preserveAspect = true;

            TMP_Text ruleKicker = CreateText(
                "RuleKicker",
                rule.transform,
                "星级评鉴规则",
                30f,
                TextAlignmentOptions.Left,
                Danger);
            Anchor(
                ruleKicker.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(690f, 48f),
                new Vector2(105f, 105f));
            ruleKicker.fontStyle = FontStyles.Bold;

            TMP_Text ruleName = CreateText(
                "RuleName",
                rule.transform,
                "规则名称",
                42f,
                TextAlignmentOptions.Left,
                Ink);
            Anchor(
                ruleName.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(690f, 64f),
                new Vector2(105f, 48f));
            ruleName.fontStyle = FontStyles.Bold;
            ruleName.enableAutoSizing = true;
            ruleName.fontSizeMin = 30f;
            ruleName.fontSizeMax = 42f;

            TMP_Text ruleDescription = CreateText(
                "RuleDescription",
                rule.transform,
                "规则描述",
                30f,
                TextAlignmentOptions.TopLeft,
                new Color32(91, 57, 38, 235));
            Anchor(
                ruleDescription.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(690f, 150f),
                new Vector2(105f, -72f));
            ruleDescription.enableAutoSizing = true;
            ruleDescription.fontSizeMin = 24f;
            ruleDescription.fontSizeMax = 30f;
            ruleDescription.overflowMode = TextOverflowModes.Overflow;
            ruleDescription.richText = true;

            BattleEntryPresentationView view = root.GetComponent<BattleEntryPresentationView>();
            Set(view, "_overlay", overlay);
            Set(view, "_inputGroup", inputGroup);
            Set(view, "_contentGroup", contentGroup);
            Set(view, "_targetGroup", targetRect);
            Set(view, "_targetScoreText", targetScore);
            Set(view, "_ruleGroup", ruleRect);
            Set(view, "_ruleGroupCanvas", ruleGroup);
            Set(view, "_ruleIcon", ruleIcon);
            Set(view, "_ruleNameText", ruleName);
            Set(view, "_ruleDescriptionText", ruleDescription);
            Set(view, "_timelineTheme", AssetDatabase.LoadAssetAtPath<TimelineAxisTheme>(TimelineThemePath));

            root.SetActive(false);
            try
            {
                return PrefabUtility.SaveAsPrefabAsset(root, OverlayPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void WireBattleForm(GameObject overlayPrefab)
        {
            if (overlayPrefab == null)
            {
                throw new InvalidOperationException("Battle entry presentation prefab was not created.");
            }

            GameObject root = PrefabUtility.LoadPrefabContents(BattleFormPath);
            if (root == null)
            {
                throw new InvalidOperationException($"Could not load prefab: {BattleFormPath}");
            }

            try
            {
                Transform hudFrame = FindDeep(root.transform, "HudFrame")
                    ?? throw new InvalidOperationException("BattleForm/HudFrame is missing.");
                Transform existing = FindDirectChild(hudFrame, "BattleEntryPresentationOverlay");
                GameObject instance;
                GameObject existingSource = existing != null
                    ? PrefabUtility.GetCorrespondingObjectFromSource(existing.gameObject)
                    : null;
                if (existing != null && existingSource == overlayPrefab)
                {
                    instance = existing.gameObject;
                }
                else
                {
                    if (existing != null)
                    {
                        UnityEngine.Object.DestroyImmediate(existing.gameObject, true);
                    }

                    instance = (GameObject)PrefabUtility.InstantiatePrefab(overlayPrefab, hudFrame);
                }

                instance.name = "BattleEntryPresentationOverlay";
                RectTransform rect = instance.GetComponent<RectTransform>();
                Stretch(rect);
                rect.SetAsLastSibling();

                BattleForm form = root.GetComponent<BattleForm>()
                    ?? throw new InvalidOperationException("BattleForm component is missing.");
                var serialized = new SerializedObject(form);
                SerializedProperty property = serialized.FindProperty("_battleEntryPresentation")
                    ?? throw new InvalidOperationException("BattleForm._battleEntryPresentation is missing.");
                property.objectReferenceValue = instance.GetComponent<BattleEntryPresentationView>();
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(form);
                PrefabUtility.SaveAsPrefabAsset(root, BattleFormPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static GameObject CreateUi(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return go;
        }

        private static Image CreateImage(string name, Transform parent, Sprite sprite)
        {
            GameObject go = CreateUi(name, parent);
            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text CreateText(
            string name,
            Transform parent,
            string text,
            float fontSize,
            TextAlignmentOptions alignment,
            Color color)
        {
            GameObject go = CreateUi(name, parent);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            return label;
        }

        private static void Anchor(
            RectTransform rect,
            Vector2 anchor,
            Vector2 size,
            Vector2 position)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            rect.localScale = Vector3.one;
        }

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            rect.localScale = Vector3.one;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Transform FindDirectChild(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        private static T EnsureAsset<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            return asset != null
                ? asset
                : throw new InvalidOperationException($"Required asset is missing: {path}");
        }

        private static void Set(Component component, string propertyName, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(component);
            SerializedProperty property = serialized.FindProperty(propertyName)
                ?? throw new InvalidOperationException(
                    $"Missing property {propertyName} on {component.GetType().Name}.");
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
