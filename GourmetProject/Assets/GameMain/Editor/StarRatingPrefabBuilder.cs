using System;
using System.Collections.Generic;
using System.IO;
using Coffee.UIEffects;
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
        private const string ProgressPanelPath = AssetRoot + "star_progress_panel.png";
        private const string BattleFormPath = "Assets/GameMain/Content/Prefabs/UI/Battle/BattleForm.prefab";
        private const string AwardFormPath = "Assets/GameMain/Content/Prefabs/UI/Meta/Rewards/StarAwardForm.prefab";
        private const string TimelineThemePath = "Assets/GameMain/Content/Resources/Sprites/UI/TimelineFresh/TimelineAxisTheme.asset";
        private const string FontPath = "Assets/GameMain/Content/Resources/Fonts/AlimamaShuHeiTi-Bold SDF.asset";
        private const string UiSpriteRoot = "Assets/GameMain/Content/Resources/Sprites/UI/";
        private const string ShinyTexturePath =
            "Packages/com.coffee.ui-effect/UIEffectPresets/Textures/Transition-Horizontal.png";

        [MenuItem("GourmetProject/UI/Star Rating/Rebuild Six-Star Rating UI")]
        public static void Rebuild()
        {
            ConfigureSprite(MedalPath);
            ConfigureSprite(NodeBubblePath);
            ConfigureSprite(ProgressPanelPath);
            Sprite medal = AssetDatabase.LoadAssetAtPath<Sprite>(MedalPath)
                ?? throw new InvalidOperationException($"Missing star medal sprite: {MedalPath}");
            Sprite nodeBubble = AssetDatabase.LoadAssetAtPath<Sprite>(NodeBubblePath)
                ?? throw new InvalidOperationException($"Missing star node sprite: {NodeBubblePath}");
            Sprite progressPanel = AssetDatabase.LoadAssetAtPath<Sprite>(ProgressPanelPath)
                ?? throw new InvalidOperationException($"Missing star progress panel sprite: {ProgressPanelPath}");

            RebuildBattleForm(medal, progressPanel);
            RebuildAwardForm(medal);
            WireTimelineTheme(nodeBubble);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Six-star rating HUD, award dialog, and timeline boss bubble rebuilt.");
        }

        private static void ConfigureSprite(string path, Vector4? border = null)
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
            settings.spriteBorder = border ?? Vector4.zero;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        private static void RebuildBattleForm(Sprite medal, Sprite progressPanel)
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
                RemoveOwnedStarCardChildren(weekCard);
                Image panelImage = Ensure<Image>(weekCard.gameObject);
                panelImage.sprite = progressPanel;
                panelImage.type = Image.Type.Simple;
                panelImage.preserveAspect = false;
                panelImage.raycastTarget = false;
                panelImage.color = Color.white;

                RemovePanelEffect(weekCard.gameObject);
                StarProgressView progress = weekCard.GetComponent<StarProgressView>()
                    ?? weekCard.gameObject.AddComponent<StarProgressView>();

                GameObject gridObject = CreateUi("StarGrid", weekCard, false);
                RectTransform gridRect = gridObject.GetComponent<RectTransform>();
                Anchor(gridRect, new Vector2(0.5f, 0.5f), new Vector2(164f, 96f), Vector2.zero);
                GridLayoutGroup grid = gridObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(44f, 44f);
                grid.spacing = new Vector2(16f, 8f);
                grid.childAlignment = TextAnchor.MiddleCenter;
                grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
                grid.startAxis = GridLayoutGroup.Axis.Horizontal;
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = 3;

                Image[] slots = CreateGridStarSlots(gridObject.transform, medal);
                var effects = new UIEffect[slots.Length];
                var shinyTweeners = new UIEffectTweener[slots.Length];
                for (int i = 0; i < slots.Length; i++)
                {
                    ConfigureStarEffect(slots[i], i, out effects[i], out shinyTweeners[i]);
                }

                GameObject sparkleObject = CreateUi("SparkleOverlay", weekCard);
                Stretch(sparkleObject.GetComponent<RectTransform>());
                StarCardSparkleGraphic sparkles = sparkleObject.AddComponent<StarCardSparkleGraphic>();
                sparkles.raycastTarget = false;

                ConfigureProgress(
                    progress,
                    slots,
                    medal,
                    enableHudEffects: true,
                    effects,
                    shinyTweeners,
                    sparkles);

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

        private static Image[] CreateGridStarSlots(Transform parent, Sprite medal)
        {
            var result = new Image[6];
            for (int i = 0; i < result.Length; i++)
            {
                Image star = CreateImage($"Star{i + 1}", parent, medal);
                star.rectTransform.sizeDelta = new Vector2(56f, 56f);
                star.preserveAspect = true;
                result[i] = star;
            }

            return result;
        }

        private static void RemoveOwnedStarCardChildren(Transform starCard)
        {
            for (int i = starCard.childCount - 1; i >= 0; i--)
            {
                Transform child = starCard.GetChild(i);
                bool isLegacySlot = child.name.Length == 5
                    && child.name.StartsWith("Star", StringComparison.Ordinal)
                    && child.name[4] >= '1'
                    && child.name[4] <= '6';
                if (child.name == "StarGrid" || child.name == "SparkleOverlay" || isLegacySlot)
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject, true);
                }
            }
        }

        private static void ConfigureProgress(
            StarProgressView progress,
            Image[] slots,
            Sprite medal,
            bool enableHudEffects = false,
            UIEffect[] effects = null,
            UIEffectTweener[] shinyTweeners = null,
            StarCardSparkleGraphic sparkles = null)
        {
            var serialized = new SerializedObject(progress);
            SerializedProperty stars = serialized.FindProperty("_stars");
            stars.arraySize = slots.Length;
            for (int i = 0; i < slots.Length; i++)
            {
                stars.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
            }

            serialized.FindProperty("_starSprite").objectReferenceValue = medal;
            serialized.FindProperty("_enableHudEffects").boolValue = enableHudEffects;
            if (enableHudEffects)
            {
                serialized.FindProperty("_lockedColor").colorValue =
                    new Color(0.68f, 0.62f, 0.52f, 0.48f);
            }
            SetObjectArray(serialized.FindProperty("_starEffects"), effects);
            SetObjectArray(serialized.FindProperty("_starShinyTweeners"), shinyTweeners);
            serialized.FindProperty("_sparkles").objectReferenceValue = sparkles;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            progress.Bind(0);
            EditorUtility.SetDirty(progress);
        }

        private static void RemovePanelEffect(GameObject panelObject)
        {
            if (panelObject.TryGetComponent(out UIEffectTweener tweener))
            {
                UnityEngine.Object.DestroyImmediate(tweener, true);
            }

            if (panelObject.TryGetComponent(out UIEffect effect))
            {
                UnityEngine.Object.DestroyImmediate(effect, true);
            }
        }

        private static void ConfigureStarEffect(
            Image star,
            int index,
            out UIEffect effect,
            out UIEffectTweener tweener)
        {
            effect = star.gameObject.AddComponent<UIEffect>();
            effect.transitionFilter = TransitionFilter.Shiny;
            effect.transitionTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(ShinyTexturePath);
            effect.transitionRotation = 25f;
            effect.transitionWidth = 0.18f;
            effect.transitionSoftness = 0.45f;
            effect.transitionColorFilter = ColorFilter.MultiplyAdditive;
            effect.transitionColor = new Color(0.58f, 0.42f, 0.18f, 0.88f);
            effect.transitionColorGlow = true;
            effect.transitionAutoPlaySpeed = 0f;
            effect.shadowMode = ShadowMode.Outline8;
            effect.shadowDistance = new Vector2(2f, -2f);
            effect.shadowIteration = 1;
            effect.shadowFade = 0.52f;
            effect.shadowBlurIntensity = 0.46f;
            effect.shadowColorFilter = ColorFilter.Replace;
            effect.shadowColor = new Color(1f, 0.58f, 0.08f, 0.48f);
            effect.shadowColorGlow = true;
            effect.enabled = false;

            tweener = star.gameObject.AddComponent<UIEffectTweener>();
            ConfigureTweener(
                tweener,
                UIEffectTweener.CullingMask.Transition,
                duration: 0.65f,
                interval: 4.2f,
                delay: index * 0.12f);
            tweener.enabled = false;
        }

        private static void ConfigureTweener(
            UIEffectTweener tweener,
            UIEffectTweener.CullingMask mask,
            float duration,
            float interval,
            float delay)
        {
            tweener.cullingMask = mask;
            tweener.direction = UIEffectTweener.Direction.Forward;
            tweener.curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            tweener.duration = duration;
            tweener.interval = interval;
            tweener.delay = delay;
            tweener.wrapMode = UIEffectTweener.WrapMode.Loop;
            tweener.updateMode = UIEffectTweener.UpdateMode.Unscaled;
            tweener.playOnEnable = UIEffectTweener.PlayOnEnable.Forward;
            tweener.resetTimeOnEnable = true;
        }

        private static void SetObjectArray<T>(SerializedProperty property, T[] values)
            where T : UnityEngine.Object
        {
            values ??= Array.Empty<T>();
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
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
