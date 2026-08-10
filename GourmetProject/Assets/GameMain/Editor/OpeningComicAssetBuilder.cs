#if UNITY_EDITOR
using System;
using System.IO;
using GourmetProject.Core.Save;
using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Common;
using GourmetProject.Runtime;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.Editor
{
    /// <summary>从两张漫画页提取八格透明 Sprite，并构建已连线的开场漫画 Prefab。</summary>
    public static class OpeningComicAssetBuilder
    {
        private const string SourceRoot = "Assets/GameMain/Content/Art/OpeningComic/Source";
        private const string OutputRoot = "Assets/GameMain/Content/Resources/Sprites/UI/OpeningComic";
        private const string PrefabPath = "Assets/GameMain/Content/Prefabs/UI/OpeningComicForm.prefab";
        private const string FontPath = "Assets/GameMain/Content/Resources/Fonts/AlimamaShuHeiTi-Bold SDF.asset";
        private const int TransparentPadding = 12;

        private static readonly PanelDefinition[] Panels =
        {
            new(1, new Vector2(25, 82), new Vector2(859, 14), new Vector2(851, 483), new Vector2(67, 525)),
            new(1, new Vector2(895, 69), new Vector2(1635, 30), new Vector2(1617, 414), new Vector2(865, 454)),
            new(1, new Vector2(67, 543), new Vector2(936, 474), new Vector2(854, 926), new Vector2(33, 889)),
            // 第四格左下被第三格压住，使用五边形保留完整字幕条，同时排除被遮挡的画面区域。
            new(1, new Vector2(976, 477), new Vector2(1642, 427), new Vector2(1597, 907), new Vector2(867, 902), new Vector2(866, 853)),
            new(2, new Vector2(29, 59), new Vector2(854, 11), new Vector2(839, 450), new Vector2(30, 458)),
            new(2, new Vector2(893, 49), new Vector2(1631, 11), new Vector2(1630, 445), new Vector2(850, 447)),
            new(2, new Vector2(30, 483), new Vector2(826, 466), new Vector2(769, 903), new Vector2(8, 885)),
            new(2, new Vector2(864, 470), new Vector2(1647, 458), new Vector2(1601, 914), new Vector2(766, 901)),
        };

        [MenuItem("Tools/GourmetProject/Opening Comic/Rebuild Assets And Prefab")]
        public static void RebuildAssetsAndPrefab()
        {
            GeneratePanelSprites();
            BuildPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Opening comic rebuilt: {Panels.Length} panels and {PrefabPath}.");
        }

        [MenuItem("Tools/GourmetProject/Opening Comic/Reset Seen Flag")]
        public static void ResetSeenFlag()
        {
            ISaveService save = GameApp.Save;
            if (save == null)
            {
                var options = new SaveServiceOptions
                {
                    CurrentVersion = 1,
                    EnableChecksum = true,
                    Indented = Debug.isDebugBuild,
                };
                save = new JsonSaveService(Path.Combine(Application.persistentDataPath, "saves"), options);
            }

            MetaProgressSaveData progress = MetaProgressPersistence.Load(save);
            progress.OpeningComicCompletedVersion = 0;
            MetaProgressPersistence.Save(save, progress);
            Debug.Log("Opening comic seen flag reset. The comic will play on the next menu entry.");
        }

        private static void GeneratePanelSprites()
        {
            Directory.CreateDirectory(ToAbsolutePath(OutputRoot));

            Texture2D[] sourcePages =
            {
                LoadTexture($"{SourceRoot}/opening_comic_page_01.jpg"),
                LoadTexture($"{SourceRoot}/opening_comic_page_02.jpg"),
            };

            try
            {
                for (int i = 0; i < Panels.Length; i++)
                {
                    PanelDefinition definition = Panels[i];
                    Texture2D panel = ExtractPanel(sourcePages[definition.Page - 1], definition.Points);
                    string assetPath = $"{OutputRoot}/panel_{i + 1:00}.png";
                    File.WriteAllBytes(ToAbsolutePath(assetPath), panel.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(panel);
                }
            }
            finally
            {
                foreach (Texture2D source in sourcePages)
                {
                    UnityEngine.Object.DestroyImmediate(source);
                }
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            for (int i = 0; i < Panels.Length; i++)
            {
                ConfigurePanelImporter($"{OutputRoot}/panel_{i + 1:00}.png");
            }
        }

        private static Texture2D LoadTexture(string assetPath)
        {
            string absolutePath = ToAbsolutePath(assetPath);
            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException($"Opening comic source page not found: {assetPath}", absolutePath);
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!texture.LoadImage(File.ReadAllBytes(absolutePath), false))
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw new InvalidOperationException($"Could not decode opening comic source page: {assetPath}");
            }

            return texture;
        }

        private static Texture2D ExtractPanel(Texture2D source, Vector2[] polygon)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(MinCoordinate(polygon, true)) - TransparentPadding);
            int maxX = Mathf.Min(source.width - 1, Mathf.CeilToInt(MaxCoordinate(polygon, true)) + TransparentPadding);
            int minTopY = Mathf.Max(0, Mathf.FloorToInt(MinCoordinate(polygon, false)) - TransparentPadding);
            int maxTopY = Mathf.Min(source.height - 1, Mathf.CeilToInt(MaxCoordinate(polygon, false)) + TransparentPadding);
            int width = maxX - minX + 1;
            int height = maxTopY - minTopY + 1;

            Color32[] sourcePixels = source.GetPixels32();
            Color32[] outputPixels = new Color32[width * height];
            var transparent = new Color32(0, 0, 0, 0);
            Array.Fill(outputPixels, transparent);

            for (int sourceTopY = minTopY; sourceTopY <= maxTopY; sourceTopY++)
            {
                int sourceBottomY = source.height - 1 - sourceTopY;
                int destinationTopY = sourceTopY - minTopY;
                int destinationBottomY = height - 1 - destinationTopY;

                for (int sourceX = minX; sourceX <= maxX; sourceX++)
                {
                    if (!ContainsPoint(polygon, new Vector2(sourceX + 0.5f, sourceTopY + 0.5f)))
                    {
                        continue;
                    }

                    int destinationX = sourceX - minX;
                    outputPixels[destinationBottomY * width + destinationX] =
                        sourcePixels[sourceBottomY * source.width + sourceX];
                }
            }

            var output = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            output.SetPixels32(outputPixels);
            output.Apply(false, false);
            return output;
        }

        private static bool ContainsPoint(Vector2[] polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, previous = polygon.Length - 1; i < polygon.Length; previous = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[previous];
                bool intersects = (a.y > point.y) != (b.y > point.y) &&
                                  point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
                if (intersects)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static float MinCoordinate(Vector2[] points, bool xAxis)
        {
            float value = float.PositiveInfinity;
            foreach (Vector2 point in points)
            {
                value = Mathf.Min(value, xAxis ? point.x : point.y);
            }

            return value;
        }

        private static float MaxCoordinate(Vector2[] points, bool xAxis)
        {
            float value = float.NegativeInfinity;
            foreach (Vector2 point in points)
            {
                value = Mathf.Max(value, xAxis ? point.x : point.y);
            }

            return value;
        }

        private static void ConfigurePanelImporter(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
            {
                throw new InvalidOperationException($"Texture importer missing for {assetPath}");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static void BuildPrefab()
        {
            var root = new GameObject(
                "OpeningComicForm",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(OpeningComicForm));
            root.layer = LayerMask.NameToLayer("UI");
            RectTransform rootRect = root.GetComponent<RectTransform>();
            Stretch(rootRect);

            CanvasGroup formCanvasGroup = root.GetComponent<CanvasGroup>();
            formCanvasGroup.alpha = 1f;
            formCanvasGroup.interactable = true;
            formCanvasGroup.blocksRaycasts = true;

            try
            {
                Image backdrop = CreateImage("PaperBackdrop", rootRect, new Color(0.957f, 0.925f, 0.855f, 1f));
                Stretch(backdrop.rectTransform);
                backdrop.raycastTarget = false;

                RectTransform designRoot = CreateRect("DesignRoot", rootRect);
                designRoot.sizeDelta = new Vector2(1920f, 1080f);
                var scaler = designRoot.gameObject.AddComponent<ReferenceResolutionScaler>();
                SetSerializedReference(scaler, "fitParent", rootRect);

                RectTransform panelLayer = CreateRect("PanelLayer", designRoot);
                Stretch(panelLayer);

                var panelRoots = new RectTransform[3];
                var revealRoots = new RectTransform[3];
                var artRoots = new RectTransform[3];
                var panelImages = new Image[3];
                var panelCanvasGroups = new CanvasGroup[3];

                for (int i = 0; i < 3; i++)
                {
                    CreatePanelView(
                        panelLayer,
                        i,
                        out panelRoots[i],
                        out revealRoots[i],
                        out artRoots[i],
                        out panelImages[i],
                        out panelCanvasGroups[i]);
                }

                TMP_Text prompt = CreatePrompt(designRoot);

                Image inputCatcher = CreateImage("InputCatcher", rootRect, new Color(1f, 1f, 1f, 0.001f));
                Stretch(inputCatcher.rectTransform);
                inputCatcher.raycastTarget = true;

                var form = root.GetComponent<OpeningComicForm>();
                var serializedForm = new SerializedObject(form);
                SetObjectArray(serializedForm.FindProperty("_panels"), LoadPanelSprites());
                SetObjectArray(serializedForm.FindProperty("_panelRoots"), panelRoots);
                SetObjectArray(serializedForm.FindProperty("_revealRoots"), revealRoots);
                SetObjectArray(serializedForm.FindProperty("_artRoots"), artRoots);
                SetObjectArray(serializedForm.FindProperty("_panelImages"), panelImages);
                SetObjectArray(serializedForm.FindProperty("_panelCanvasGroups"), panelCanvasGroups);
                serializedForm.FindProperty("_formCanvasGroup").objectReferenceValue = formCanvasGroup;
                serializedForm.FindProperty("_promptText").objectReferenceValue = prompt;
                serializedForm.ApplyModifiedPropertiesWithoutUndo();

                Directory.CreateDirectory(Path.GetDirectoryName(ToAbsolutePath(PrefabPath)) ?? string.Empty);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void CreatePanelView(
            RectTransform parent,
            int index,
            out RectTransform panelRoot,
            out RectTransform revealRoot,
            out RectTransform artRoot,
            out Image image,
            out CanvasGroup canvasGroup)
        {
            panelRoot = CreateRect($"PanelView_{index + 1}", parent);
            panelRoot.anchorMin = panelRoot.anchorMax = new Vector2(0.5f, 0.5f);
            panelRoot.sizeDelta = new Vector2(1120f, 660f);
            canvasGroup = panelRoot.gameObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            revealRoot = CreateRect("RevealViewport", panelRoot);
            revealRoot.anchorMin = revealRoot.anchorMax = new Vector2(0.5f, 0.5f);
            revealRoot.sizeDelta = panelRoot.sizeDelta;
            revealRoot.gameObject.AddComponent<RectMask2D>();

            artRoot = CreateRect("ArtRoot", revealRoot);
            artRoot.anchorMin = artRoot.anchorMax = new Vector2(0.5f, 0.5f);
            artRoot.sizeDelta = panelRoot.sizeDelta;

            image = artRoot.gameObject.AddComponent<Image>();
            image.color = Color.white;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.maskable = true;

            var shadow = artRoot.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.20f, 0.12f, 0.07f, 0.28f);
            shadow.effectDistance = new Vector2(9f, -9f);
            shadow.useGraphicAlpha = true;

            panelRoot.gameObject.SetActive(false);
        }

        private static TMP_Text CreatePrompt(RectTransform parent)
        {
            RectTransform rect = CreateRect("ContinuePrompt", parent);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-68f, 48f);
            rect.sizeDelta = new Vector2(420f, 60f);

            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = "点击继续";
            text.fontSize = 28f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.MidlineRight;
            text.color = new Color(0.22f, 0.14f, 0.08f, 0.92f);
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font != null)
            {
                text.font = font;
            }

            var shadow = rect.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(1f, 1f, 1f, 0.75f);
            shadow.effectDistance = new Vector2(2f, -2f);
            shadow.useGraphicAlpha = true;
            return text;
        }

        private static Sprite[] LoadPanelSprites()
        {
            var sprites = new Sprite[Panels.Length];
            for (int i = 0; i < sprites.Length; i++)
            {
                string path = $"{OutputRoot}/panel_{i + 1:00}.png";
                sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprites[i] == null)
                {
                    throw new InvalidOperationException($"Generated panel Sprite missing: {path}");
                }
            }

            return sprites;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.layer = LayerMask.NameToLayer("UI");
            var rect = gameObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private static void SetSerializedReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            var serializedObject = new SerializedObject(target);
            serializedObject.FindProperty(propertyName).objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectArray<T>(SerializedProperty property, T[] values) where T : UnityEngine.Object
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static string ToAbsolutePath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }

        private readonly struct PanelDefinition
        {
            internal PanelDefinition(int page, params Vector2[] points)
            {
                Page = page;
                Points = points;
            }

            internal int Page { get; }
            internal Vector2[] Points { get; }
        }
    }
}
#endif
