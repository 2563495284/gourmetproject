using System;
using System.Collections.Generic;
using GourmetProject.Game.UI.Hud;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Editor
{
    internal static class TimelineAxisPrefabBuilder
    {
        private const string SpriteRoot = "Assets/GameMain/Content/Resources/Sprites/UI/TimelineFresh/";
        private const string PrefabRoot = "Assets/GameMain/Content/Resources/Prefabs/UI/Hud/";
        private const string ThemePath = SpriteRoot + "TimelineAxisTheme.asset";
        private const string MainPath = PrefabRoot + "TimelineAxisView.prefab";
        private const string DayPath = PrefabRoot + "TimelineDayPointView.prefab";
        private const string GroupPath = PrefabRoot + "TimelineDayNodeGroupView.prefab";
        private const string BubblePath = PrefabRoot + "TimelineNodeBubbleView.prefab";
        private const string BattleFormPath = "Assets/GameMain/Content/Prefabs/UI/Battle/BattleForm.prefab";

        private static readonly string[] BossIds =
        {
            "indulgent", "binge", "kids_meal", "weight_loss", "gluttony", "omakase",
            "light_meal", "vegan_meal", "dine_and_dash", "fine_dining", "vegetarian",
            "carb_meal", "dark_cuisine", "late_night", "appetizer", "tasting", "buffet",
        };

        [MenuItem("GourmetProject/UI/Timeline/Rebuild Fresh Timeline Axis")]
        public static void Rebuild()
        {
            ConfigureImporters();
            TimelineAxisTheme theme = BuildTheme();
            BuildDayPoint(theme);
            BuildNodeGroup();
            BuildNodeBubble(theme);
            BuildMain(theme);
            ResaveBattleForm();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("TimelineAxisView fresh theme and prefabs rebuilt.");
        }

        private static void ConfigureImporters()
        {
            ConfigureSprite("timeline_panel_fresh.png", new Vector4(230f, 210f, 230f, 210f));
            ConfigureSprite("timeline_track_fresh.png", new Vector4(70f, 50f, 70f, 50f));
            ConfigureSprite("timeline_progress_fresh.png", new Vector4(48f, 35f, 48f, 35f));
            ConfigureSprite("timeline_tick_fresh.png", Vector4.zero);
            ConfigureSprite("timeline_cursor_fresh.png", Vector4.zero);
            ConfigureSprite("timeline_day_badge_fresh.png", new Vector4(150f, 120f, 150f, 120f));
            ConfigureSprite("timeline_node_bubble_fresh.png", Vector4.zero);
        }

        private static void ConfigureSprite(string fileName, Vector4 border)
        {
            string path = SpriteRoot + fileName;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"Timeline sprite was not imported: {path}");
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
            settings.spriteBorder = border;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        private static TimelineAxisTheme BuildTheme()
        {
            TimelineAxisTheme theme = AssetDatabase.LoadAssetAtPath<TimelineAxisTheme>(ThemePath);
            if (theme == null)
            {
                theme = ScriptableObject.CreateInstance<TimelineAxisTheme>();
                AssetDatabase.CreateAsset(theme, ThemePath);
            }

            var serialized = new SerializedObject(theme);
            Set(serialized, "_panel", Sprite("timeline_panel_fresh"));
            Set(serialized, "_track", Sprite("timeline_track_fresh"));
            Set(serialized, "_progress", Sprite("timeline_progress_fresh"));
            Set(serialized, "_tick", Sprite("timeline_tick_fresh"));
            Set(serialized, "_cursor", Sprite("timeline_cursor_fresh"));
            Set(serialized, "_dayBadge", Sprite("timeline_day_badge_fresh"));
            Set(serialized, "_nodeBubble", Sprite("timeline_node_bubble_fresh"));
            Set(serialized, "_shop", UiSprite("icon_axis_shop"));
            Set(serialized, "_interest", UiSprite("icon_axis_interest"));
            Set(serialized, "_boss", UiSprite("icon_axis_boss"));
            Set(serialized, "_event", UiSprite("icon_axis_event"));
            Set(serialized, "_reward", UiSprite("icon_axis_reward"));
            Set(serialized, "_slot", UiSprite("icon_axis_slot"));
            Set(serialized, "_negative", UiSprite("icon_axis_negative"));

            SerializedProperty bossIcons = serialized.FindProperty("_bossIcons");
            bossIcons.arraySize = BossIds.Length;
            for (int i = 0; i < BossIds.Length; i++)
            {
                SerializedProperty entry = bossIcons.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("DebuffId").stringValue = BossIds[i];
                entry.FindPropertyRelative("Sprite").objectReferenceValue =
                    UiSprite("icon_axis_boss_" + BossIds[i]);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(theme);
            return theme;
        }

        private static void BuildDayPoint(TimelineAxisTheme theme)
        {
            EditPrefab(DayPath, root =>
            {
                ClearChildren(root.transform);
                RectTransform rect = root.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(46f, 58f);
                Image hit = Ensure<Image>(root);
                hit.sprite = null;
                hit.color = new Color(1f, 1f, 1f, 0.001f);
                hit.raycastTarget = false;
                TimelineDayPointView view = Ensure<TimelineDayPointView>(root);
                RemoveUnexpected(root, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TimelineDayPointView));

                Image tick = CreateImage("Tick", rect, theme.Tick);
                RectTransform tickRect = tick.rectTransform;
                Anchor(tickRect, new Vector2(0.5f, 0.44f), new Vector2(18f, 18f));
                tick.preserveAspect = true;
                tick.raycastTarget = false;
                TMP_Text label = CreateText("DayLabel", rect, "1", 16f);
                Anchor(label.rectTransform, new Vector2(0.5f, 0.08f), new Vector2(40f, 20f));

                Set(view, "_rect", rect);
                Set(view, "_hitArea", hit);
                Set(view, "_tick", tick);
                Set(view, "_dayLabel", label);
            });
        }

        private static void BuildNodeGroup()
        {
            EditPrefab(GroupPath, root =>
            {
                ClearChildren(root.transform);
                RectTransform rect = root.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(176f, 112f);
                rect.pivot = new Vector2(0.5f, 0f);
                Image hit = Ensure<Image>(root);
                hit.sprite = null;
                hit.color = new Color(1f, 1f, 1f, 0.001f);
                hit.raycastTarget = false;
                TimelineDayNodeGroupView view = Ensure<TimelineDayNodeGroupView>(root);
                RemoveUnexpected(root, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TimelineDayNodeGroupView));
                Set(view, "_rect", rect);
                Set(view, "_hitArea", hit);
            });
        }

        private static void BuildNodeBubble(TimelineAxisTheme theme)
        {
            EditPrefab(BubblePath, root =>
            {
                ClearChildren(root.transform);
                RectTransform rect = root.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(60f, 60f);
                rect.pivot = new Vector2(0.5f, 0f);
                Image hit = Ensure<Image>(root);
                hit.sprite = null;
                hit.color = new Color(1f, 1f, 1f, 0.001f);
                CanvasGroup canvasGroup = Ensure<CanvasGroup>(root);
                TimelineNodeBubbleView view = Ensure<TimelineNodeBubbleView>(root);
                RemoveUnexpected(
                    root,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(CanvasGroup),
                    typeof(TimelineNodeBubbleView));

                GameObject tailObject = CreateUi("Tail", rect);
                TimelineNodeTailGraphic tail = tailObject.AddComponent<TimelineNodeTailGraphic>();
                Stretch(tail.rectTransform);
                tail.raycastTarget = false;

                Image stateRing = CreateImage("StateRing", rect, theme.NodeBubble);
                Anchor(stateRing.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(68f, 68f));
                stateRing.raycastTarget = false;
                Image shell = CreateImage("Shell", rect, theme.NodeBubble);
                Anchor(shell.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(60f, 60f));
                shell.raycastTarget = false;
                Image icon = CreateImage("Icon", rect, null);
                Anchor(icon.rectTransform, new Vector2(0.5f, 0.52f), new Vector2(40f, 40f));
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                TMP_Text skip = CreateText("SkipStamp", rect, "跳过", 18f);
                Anchor(skip.rectTransform, new Vector2(0.5f, 0.52f), new Vector2(58f, 26f));
                skip.fontStyle = FontStyles.Bold;
                skip.gameObject.SetActive(false);

                Set(view, "_rect", rect);
                Set(view, "_hitArea", hit);
                Set(view, "_tail", tail);
                Set(view, "_shell", shell);
                Set(view, "_stateRing", stateRing);
                Set(view, "_icon", icon);
                Set(view, "_canvasGroup", canvasGroup);
                Set(view, "_skipStamp", skip);
            });
        }

        private static void BuildMain(TimelineAxisTheme theme)
        {
            EditPrefab(MainPath, root =>
            {
                ClearChildren(root.transform);
                TimelineAxisView view = Ensure<TimelineAxisView>(root);
                CanvasGroup group = Ensure<CanvasGroup>(root);
                group.alpha = 1f;
                RemoveUnexpected(root, typeof(RectTransform), typeof(CanvasRenderer), typeof(CanvasGroup), typeof(TimelineAxisView));
                RectTransform rootRect = root.GetComponent<RectTransform>();

                Image background = CreateImage("Background", rootRect, theme.Panel);
                Stretch(background.rectTransform);
                background.type = Image.Type.Sliced;
                background.pixelsPerUnitMultiplier = 3.25f;

                RectTransform content = CreateUi("AxisContent", rootRect).GetComponent<RectTransform>();
                content.anchorMin = Vector2.zero;
                content.anchorMax = Vector2.one;
                content.offsetMin = new Vector2(56f, 22f);
                content.offsetMax = new Vector2(-56f, -18f);

                RectTransform trackRoot = CreateUi("Track", content).GetComponent<RectTransform>();
                trackRoot.anchorMin = new Vector2(0f, 0.36f);
                trackRoot.anchorMax = new Vector2(1f, 0.36f);
                trackRoot.pivot = new Vector2(0.5f, 0.5f);
                trackRoot.sizeDelta = new Vector2(0f, 12f);
                trackRoot.anchoredPosition = Vector2.zero;
                Image track = CreateImage("TrackBackground", trackRoot, theme.Track);
                Stretch(track.rectTransform);
                track.type = Image.Type.Sliced;
                track.pixelsPerUnitMultiplier = 12f;
                Image progress = CreateImage("TrackProgress", trackRoot, theme.Progress);
                Stretch(progress.rectTransform);
                progress.type = Image.Type.Sliced;
                progress.pixelsPerUnitMultiplier = 7.2f;
                progress.rectTransform.anchorMax = new Vector2(0f, 1f);

                RectTransform dayLayer = CreateUi("DayLayer", content).GetComponent<RectTransform>();
                Stretch(dayLayer);
                RectTransform nodeLayer = CreateUi("NodeLayer", content).GetComponent<RectTransform>();
                Stretch(nodeLayer);
                RectTransform cursorLayer = CreateUi("CursorLayer", content).GetComponent<RectTransform>();
                Stretch(cursorLayer);

                RectTransform cursor = CreateUi("Cursor", cursorLayer).GetComponent<RectTransform>();
                cursor.anchorMin = cursor.anchorMax = new Vector2(0f, 0.36f);
                cursor.pivot = new Vector2(0.5f, 0.5f);
                cursor.sizeDelta = new Vector2(112f, 82f);
                cursor.anchoredPosition = new Vector2(0f, -2f);
                Image cursorImage = CreateImage("Pointer", cursor, theme.Cursor);
                Anchor(cursorImage.rectTransform, new Vector2(0.5f, 0.72f), new Vector2(24f, 32f));
                cursorImage.preserveAspect = true;
                Image badge = CreateImage("DayBadge", cursor, theme.DayBadge);
                Anchor(badge.rectTransform, new Vector2(0.5f, 0.16f), new Vector2(112f, 42f));
                badge.type = Image.Type.Sliced;
                badge.pixelsPerUnitMultiplier = 16f;
                TMP_Text current = CreateText("CurrentDay", badge.rectTransform, "第0天", 20f);
                Stretch(current.rectTransform, 5f);
                current.fontStyle = FontStyles.Bold;

                Set(view, "_theme", theme);
                Set(view, "_background", background);
                Set(view, "_axisContent", content);
                Set(view, "_track", track);
                Set(view, "_progress", progress);
                Set(view, "_progressRect", progress.rectTransform);
                Set(view, "_dayLayer", dayLayer);
                Set(view, "_nodeLayer", nodeLayer);
                Set(view, "_cursorLayer", cursorLayer);
                Set(view, "_cursor", cursor);
                Set(view, "_cursorImage", cursorImage);
                Set(view, "_dayBadge", badge);
                Set(view, "_currentDayText", current);
                Set(view, "_dayPointPrefab", AssetDatabase.LoadAssetAtPath<TimelineDayPointView>(DayPath));
                Set(view, "_dayNodeGroupPrefab", AssetDatabase.LoadAssetAtPath<TimelineDayNodeGroupView>(GroupPath));
                Set(view, "_nodeBubblePrefab", AssetDatabase.LoadAssetAtPath<TimelineNodeBubbleView>(BubblePath));
            });
        }

        private static void ResaveBattleForm()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(BattleFormPath);
            if (root == null)
            {
                throw new InvalidOperationException($"Could not load prefab: {BattleFormPath}");
            }

            try
            {
                Component form = root.GetComponent("BattleForm");
                if (form == null)
                {
                    throw new InvalidOperationException("BattleForm component is missing.");
                }

                var serialized = new SerializedObject(form);
                serialized.UpdateIfRequiredOrScript();
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(form);
                PrefabUtility.SaveAsPrefabAsset(root, BattleFormPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void EditPrefab(string path, Action<GameObject> edit)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null)
            {
                throw new InvalidOperationException($"Could not load prefab: {path}");
            }

            try
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
                edit(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static T Ensure<T>(GameObject root) where T : Component
        {
            return root.GetComponent<T>() ?? root.AddComponent<T>();
        }

        private static void RemoveUnexpected(GameObject root, params Type[] retained)
        {
            var keep = new HashSet<Type>(retained);
            Component[] components = root.GetComponents<Component>();
            foreach (Component component in components)
            {
                if (component != null && !keep.Contains(component.GetType()))
                {
                    UnityEngine.Object.DestroyImmediate(component, true);
                }
            }
        }

        private static void ClearChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject, true);
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
            float fontSize)
        {
            GameObject go = CreateUi(name, parent);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/GameMain/Content/Resources/Fonts/AlimamaShuHeiTi-Bold SDF.asset");
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color32(91, 57, 38, 255);
            label.raycastTarget = false;
            return label;
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
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

        private static Sprite Sprite(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(SpriteRoot + name + ".png");
        }

        private static Sprite UiSprite(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/GameMain/Content/Resources/Sprites/UI/" + name + ".png");
        }

        private static void Set(SerializedObject serialized, string propertyName, UnityEngine.Object value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName)
                ?? throw new InvalidOperationException($"Missing property {propertyName} on {serialized.targetObject}");
            property.objectReferenceValue = value;
        }

        private static void Set(Component component, string propertyName, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(component);
            Set(serialized, propertyName, value);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
