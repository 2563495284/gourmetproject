using System;
using System.Collections.Generic;
using System.IO;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Meta;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Editor
{
    internal static class StarRatingPrefabBuilder
    {
        private const string AssetRoot = "Assets/GameMain/Content/Resources/Sprites/UI/StarRating/";
        private const string MedalPath = AssetRoot + "star_rating_medal.png";
        private const string NodeBubblePath = AssetRoot + "timeline_node_star_bubble.png";
        private const string BattleFormPath = "Assets/GameMain/Content/Prefabs/UI/Battle/BattleForm.prefab";
        private const string AwardFormPath = "Assets/GameMain/Content/Prefabs/UI/Meta/Rewards/StarAwardForm.prefab";
        private const string TimelineThemePath = "Assets/GameMain/Content/Resources/Sprites/UI/TimelineFresh/TimelineAxisTheme.asset";
        private const string FontPath = "Assets/GameMain/Content/Resources/Fonts/AlimamaShuHeiTi-Bold SDF.asset";
        private const string UiSpriteRoot = "Assets/GameMain/Content/Resources/Sprites/UI/";

        [MenuItem("GourmetProject/UI/Star Rating/Rebuild Six-Star Rating UI")]
        public static void Rebuild()
        {
            ConfigureSprite(MedalPath);
            ConfigureSprite(NodeBubblePath);
            Sprite medal = AssetDatabase.LoadAssetAtPath<Sprite>(MedalPath)
                ?? throw new InvalidOperationException($"Missing star medal sprite: {MedalPath}");
            Sprite nodeBubble = AssetDatabase.LoadAssetAtPath<Sprite>(NodeBubblePath)
                ?? throw new InvalidOperationException($"Missing star node sprite: {NodeBubblePath}");

            RebuildBattleForm(medal);
            RebuildAwardForm(medal);
            WireTimelineTheme(nodeBubble);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Six-star rating HUD, award dialog, and timeline boss bubble rebuilt.");
        }

        private static void ConfigureSprite(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                throw new InvalidOperationException($"Star rating sprite was not imported: {path}");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.spritePixelsPerUnit = 100f;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteBorder = Vector4.zero;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        private static void RebuildBattleForm(Sprite medal)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(BattleFormPath);
            if (root == null)
            {
                throw new InvalidOperationException($"Could not load prefab: {BattleFormPath}");
            }

            try
            {
                Transform weekCard = FindByName(root.transform, "WeekCard");
                if (weekCard == null)
                {
                    weekCard = FindByName(root.transform, "StarCard");
                }

                if (weekCard == null)
                {
                    throw new InvalidOperationException("BattleForm WeekCard/StarCard is missing.");
                }

                Transform weekText = FindByName(root.transform, "WeekText");
                if (weekText != null)
                {
                    UnityEngine.Object.DestroyImmediate(weekText.gameObject, true);
                }

                weekCard.name = "StarCard";
                RectTransform cardRect = weekCard as RectTransform;
                cardRect.sizeDelta = new Vector2(222f, 106f);
                ClearChildren(weekCard);
                StarProgressView progress = weekCard.GetComponent<StarProgressView>()
                    ?? weekCard.gameObject.AddComponent<StarProgressView>();
                Image[] slots = CreateSixStarSlots(weekCard, medal, 42f, 48f, 23f);
                ConfigureProgress(progress, slots, medal);

                BattleInfoColumn column = root.GetComponentInChildren<BattleInfoColumn>(true)
                    ?? throw new InvalidOperationException("BattleInfoColumn is missing from BattleForm.");
                Set(column, "_starProgress", progress);
                EditorUtility.SetDirty(column);
                PrefabUtility.SaveAsPrefabAsset(root, BattleFormPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void RebuildAwardForm(Sprite medal)
        {
            EnsureAwardPrefabAsset();
            GameObject root = PrefabUtility.LoadPrefabContents(AwardFormPath);
            if (root == null)
            {
                throw new InvalidOperationException($"Could not load prefab: {AwardFormPath}");
            }

            try
            {
                root.name = "StarAwardForm";
                root.layer = LayerMask.NameToLayer("UI");
                ClearChildren(root.transform);
                RectTransform rootRect = Ensure<RectTransform>(root);
                Stretch(rootRect);
                StarAwardForm form = Ensure<StarAwardForm>(root);
                RemoveUnexpected(root, typeof(RectTransform), typeof(StarAwardForm));

                Image dim = CreateImage("InputBlocker", root.transform, null);
                Stretch(dim.rectTransform);
                dim.color = new Color(0.025f, 0.018f, 0.012f, 0.86f);
                dim.raycastTarget = true;

                Image panel = CreateImage("AwardPanel", root.transform, UiSprite("ui_result_panel"));
                Anchor(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(720f, 760f), Vector2.zero);
                panel.type = Image.Type.Sliced;
                panel.raycastTarget = true;
                panel.color = new Color(1f, 0.96f, 0.82f, 1f);
                CanvasGroup transitionGroup = panel.gameObject.AddComponent<CanvasGroup>();

                TMP_Text title = CreateText("Title", panel.transform, "星级评鉴完成", 52f, FontStyles.Bold);
                Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(590f, 78f), new Vector2(0f, 245f));
                title.color = new Color32(88, 48, 25, 255);

                TMP_Text message = CreateText("Message", panel.transform, "获得 1 颗星", 36f, FontStyles.Normal);
                Anchor(message.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(520f, 58f), new Vector2(0f, 175f));
                message.color = new Color32(154, 83, 30, 255);

                Image glow = CreateImage("StarGlow", panel.transform, medal);
                Anchor(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(330f, 330f), new Vector2(0f, 35f));
                glow.color = new Color(1f, 0.72f, 0.20f, 0.16f);
                glow.preserveAspect = true;

                Image largeStar = CreateImage("AwardStar", panel.transform, medal);
                Anchor(largeStar.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(252f, 252f), new Vector2(0f, 35f));
                largeStar.preserveAspect = true;

                GameObject progressRoot = CreateUi("SixStarProgress", panel.transform, false);
                RectTransform progressRect = progressRoot.GetComponent<RectTransform>();
                Anchor(progressRect, new Vector2(0.5f, 0.5f), new Vector2(300f, 128f), new Vector2(0f, -170f));
                StarProgressView progress = progressRoot.AddComponent<StarProgressView>();
                Image[] slots = CreateSixStarSlots(progressRoot.transform, medal, 58f, 70f, 31f);
                ConfigureProgress(progress, slots, medal);

                Button continueButton = CreateButton("ContinueButton", panel.transform, "继续");
                Anchor(continueButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(230f, 68f), new Vector2(0f, -300f));

                Set(form, "_transitionGroup", transitionGroup);
                Set(form, "_transitionPanel", panel.rectTransform);
                Set(form, "_titleText", title);
                Set(form, "_messageText", message);
                Set(form, "_largeStar", largeStar);
                Set(form, "_glow", glow.rectTransform);
                Set(form, "_progress", progress);
                Set(form, "_continueButton", continueButton);

                PrefabUtility.SaveAsPrefabAsset(root, AwardFormPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void WireTimelineTheme(Sprite nodeBubble)
        {
            TimelineAxisTheme theme = AssetDatabase.LoadAssetAtPath<TimelineAxisTheme>(TimelineThemePath)
                ?? throw new InvalidOperationException($"Missing TimelineAxisTheme: {TimelineThemePath}");
            Set(theme, "_bossNodeBubble", nodeBubble);
            EditorUtility.SetDirty(theme);
        }

        private static Image[] CreateSixStarSlots(
            Transform parent,
            Sprite medal,
            float size,
            float xStep,
            float yOffset)
        {
            var result = new Image[6];
            for (int i = 0; i < result.Length; i++)
            {
                int row = i / 3;
                int column = i % 3;
                Image star = CreateImage($"Star{i + 1}", parent, medal);
                Anchor(
                    star.rectTransform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(size, size),
                    new Vector2((column - 1) * xStep, row == 0 ? yOffset : -yOffset));
                star.preserveAspect = true;
                result[i] = star;
            }

            return result;
        }

        private static void ConfigureProgress(StarProgressView progress, Image[] slots, Sprite medal)
        {
            var serialized = new SerializedObject(progress);
            SerializedProperty stars = serialized.FindProperty("_stars");
            stars.arraySize = slots.Length;
            for (int i = 0; i < slots.Length; i++)
            {
                stars.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
            }

            serialized.FindProperty("_starSprite").objectReferenceValue = medal;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            progress.Bind(0);
            EditorUtility.SetDirty(progress);
        }

        private static void EnsureAwardPrefabAsset()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(AwardFormPath) != null)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(AwardFormPath));
            var seed = new GameObject("StarAwardForm", typeof(RectTransform), typeof(StarAwardForm));
            try
            {
                seed.layer = LayerMask.NameToLayer("UI");
                PrefabUtility.SaveAsPrefabAsset(seed, AwardFormPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(seed);
            }
        }

        private static Button CreateButton(string name, Transform parent, string label)
        {
            Image image = CreateImage(name, parent, UiSprite("ui_btn_primary_compact"));
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            TMP_Text text = CreateText("Label", button.transform, label, 32f, FontStyles.Bold);
            Stretch(text.rectTransform);
            text.color = new Color32(82, 45, 25, 255);
            return button;
        }

        private static TMP_Text CreateText(
            string name,
            Transform parent,
            string content,
            float fontSize,
            FontStyles style)
        {
            GameObject go = CreateUi(name, parent);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
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

        private static GameObject CreateUi(string name, Transform parent, bool withRenderer = true)
        {
            GameObject go = withRenderer
                ? new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer))
                : new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 position)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            rect.localScale = Vector3.one;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private static Transform FindByName(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        private static void ClearChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject, true);
            }
        }

        private static T Ensure<T>(GameObject root) where T : Component
        {
            return root.GetComponent<T>() ?? root.AddComponent<T>();
        }

        private static void RemoveUnexpected(GameObject root, params Type[] retained)
        {
            var keep = new HashSet<Type>(retained);
            foreach (Component component in root.GetComponents<Component>())
            {
                if (component != null && !keep.Contains(component.GetType()))
                {
                    UnityEngine.Object.DestroyImmediate(component, true);
                }
            }
        }

        private static Sprite UiSprite(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(UiSpriteRoot + name + ".png");
        }

        private static void Set(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName)
                ?? throw new InvalidOperationException($"Missing property {propertyName} on {target}");
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
