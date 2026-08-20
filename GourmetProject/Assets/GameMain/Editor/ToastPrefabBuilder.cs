using System;
using System.Collections.Generic;
using System.IO;
using GourmetProject.Game.UI.Common;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Editor
{
    internal static class ToastPrefabBuilder
    {
        private const string SpriteRoot = "Assets/GameMain/Content/Resources/Sprites/UI/ToastFresh/";
        private const string PrefabPath = "Assets/GameMain/Content/Prefabs/UI/Common/ToastForm.prefab";
        private const string ThemePath = SpriteRoot + "ToastTheme.asset";
        private const string FontPath = "Assets/GameMain/Content/Resources/Fonts/AlimamaShuHeiTi-Bold SDF.asset";

        [MenuItem("GourmetProject/UI/Toast/Rebuild Fresh Toast")]
        public static void Rebuild()
        {
            ConfigureImporters();
            ToastTheme theme = BuildTheme();
            BuildPrefab(theme);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Fresh global Toast theme and prefab rebuilt.");
        }

        private static void ConfigureImporters()
        {
            ConfigureSprite("toast_bubble_fresh.png", new Vector4(128f, 128f, 128f, 128f));
            ConfigureSprite("toast_tail_fresh.png", Vector4.zero);
            ConfigureSprite("toast_icon_info_fresh.png", Vector4.zero);
            ConfigureSprite("toast_icon_success_fresh.png", Vector4.zero);
            ConfigureSprite("toast_icon_warning_fresh.png", Vector4.zero);
        }

        private static void ConfigureSprite(string fileName, Vector4 border)
        {
            string path = SpriteRoot + fileName;
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                throw new InvalidOperationException($"Toast sprite was not imported: {path}");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.spritePixelsPerUnit = SpriteImportPolicy.ProjectSpritePixelsPerUnit;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteBorder = border;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        private static ToastTheme BuildTheme()
        {
            ToastTheme theme = AssetDatabase.LoadAssetAtPath<ToastTheme>(ThemePath);
            if (theme == null)
            {
                theme = ScriptableObject.CreateInstance<ToastTheme>();
                AssetDatabase.CreateAsset(theme, ThemePath);
            }

            var serialized = new SerializedObject(theme);
            Set(serialized, "_bubble", Sprite("toast_bubble_fresh"));
            Set(serialized, "_tail", Sprite("toast_tail_fresh"));
            Set(serialized, "_infoIcon", Sprite("toast_icon_info_fresh"));
            Set(serialized, "_successIcon", Sprite("toast_icon_success_fresh"));
            Set(serialized, "_warningIcon", Sprite("toast_icon_warning_fresh"));
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(theme);
            return theme;
        }

        private static void BuildPrefab(ToastTheme theme)
        {
            EnsurePrefabAsset();
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null)
            {
                throw new InvalidOperationException($"Could not load Toast prefab: {PrefabPath}");
            }

            try
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
                root.name = "ToastForm";
                root.layer = LayerMask.NameToLayer("UI");
                ClearChildren(root.transform);

                RectTransform rootRect = Ensure<RectTransform>(root);
                Stretch(rootRect);
                ToastForm form = Ensure<ToastForm>(root);
                RemoveUnexpected(root, typeof(RectTransform), typeof(ToastForm));

                GameObject anchor = CreateUi("ToastAnchor", root.transform, withRenderer: false);
                RectTransform anchorRect = anchor.GetComponent<RectTransform>();
                Anchor(anchorRect, new Vector2(0.5f, 0.5f), new Vector2(320f, 104f));
                CanvasGroup group = anchor.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
                ToastPresenter presenter = anchor.AddComponent<ToastPresenter>();

                Image tail = CreateImage("Tail", anchor.transform, theme.Tail);
                Anchor(tail.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(32f, 32f));
                tail.preserveAspect = true;

                Image bubble = CreateImage("Bubble", anchor.transform, theme.Bubble);
                Stretch(bubble.rectTransform);
                bubble.type = Image.Type.Sliced;
                bubble.fillCenter = true;
                bubble.pixelsPerUnitMultiplier = 4f;

                Image icon = CreateImage("Icon", anchor.transform, theme.ResolveIcon(ToastKind.Info));
                Anchor(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(44f, 44f));
                icon.preserveAspect = true;

                TMP_Text message = CreateText("Message", anchor.transform);
                Anchor(message.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(190f, 72f));

                Set(presenter, "_theme", theme);
                Set(presenter, "_rect", anchorRect);
                Set(presenter, "_group", group);
                Set(presenter, "_background", bubble);
                Set(presenter, "_tail", tail);
                Set(presenter, "_icon", icon);
                Set(presenter, "_message", message);
                Set(form, "_presenter", presenter);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void EnsurePrefabAsset()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            var seed = new GameObject("ToastForm", typeof(RectTransform), typeof(ToastForm));
            try
            {
                seed.layer = LayerMask.NameToLayer("UI");
                PrefabUtility.SaveAsPrefabAsset(seed, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(seed);
            }
        }

        private static TMP_Text CreateText(string name, Transform parent)
        {
            GameObject go = CreateUi(name, parent);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            text.text = "获得了新的料理灵感";
            text.fontSize = 32f;
            text.enableAutoSizing = true;
            text.fontSizeMin = 26f;
            text.fontSizeMax = 32f;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = false;
            text.color = new Color32(91, 57, 38, 255);
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

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
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

        private static Sprite Sprite(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(SpriteRoot + name + ".png");
        }

        private static void Set(Component component, string propertyName, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(component);
            Set(serialized, propertyName, value);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(SerializedObject serialized, string propertyName, UnityEngine.Object value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName)
                ?? throw new InvalidOperationException(
                    $"Missing property {propertyName} on {serialized.targetObject}");
            property.objectReferenceValue = value;
        }
    }
}
