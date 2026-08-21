using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class RecipeReadonlyBookViewTests
    {
        private const string ViewPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Meta/Recipes/RecipeReadonlyBookView.prefab";
        private const string WarehousePrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Meta/Recipes/RecipeWarehouseView.prefab";
        private const string DishPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Meta/Recipes/RecipeEditDishView.prefab";
        private const string BattlePrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Battle/BattleForm.prefab";
        private const string CharacterPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Menu/CharacterSelectForm.prefab";
        private const string SpriteRoot =
            "Assets/GameMain/Content/Resources/Sprites/UI/FengKuangCanTing/RecipeReadonlyBook/";

        [Test]
        public void Sessions_DescribeAllFiveModes_AndRouteExitCallbacks()
        {
            int readonlyExit = 0;
            var readonlySession = Session(
                RecipeReadonlyBookRequest.ReadonlyBook(
                    3,
                    () => readonlyExit++,
                    null));
            AssertSession(
                readonlySession,
                "查看食谱",
                "返回",
                false,
                true,
                3);
            readonlySession.OnExitClicked();
            Assert.That(readonlyExit, Is.EqualTo(1));

            var poolSession = Session(
                RecipeReadonlyBookRequest.ReadonlyDishPool("候选食物"));
            AssertSession(
                poolSession,
                "候选食物",
                "取消",
                false,
                false,
                0);

            int shopExit = 0;
            var shopSession = Session(
                RecipeReadonlyBookRequest.ShopDeleteDish(
                    () => shopExit++,
                    null));
            AssertSession(
                shopSession,
                "选择一个食物进行删除",
                "返回商店",
                true,
                true,
                -1);
            shopSession.OnExitClicked();
            Assert.That(shopExit, Is.EqualTo(1));

            int activeCancel = 0;
            var activeSession = Session(
                RecipeReadonlyBookRequest.ActiveItemTarget(
                    null,
                    () => activeCancel++,
                    null,
                    null));
            AssertSession(
                activeSession,
                "选择食物",
                "取消",
                true,
                true,
                -1);
            activeSession.OnExitClicked();
            Assert.That(activeCancel, Is.EqualTo(1));

            int eventCancel = 0;
            var eventSession = Session(
                RecipeReadonlyBookRequest.EventDeleteDish(
                    "从食谱删除",
                    () => eventCancel++,
                    null,
                    null));
            AssertSession(
                eventSession,
                "从食谱删除",
                "返回事件",
                true,
                true,
                -1);
            eventSession.OnExitClicked();
            Assert.That(eventCancel, Is.EqualTo(1));
        }

        [Test]
        public void Layout_PacksRectangles_InOriginalOrder_WithoutOverlap()
        {
            Vector2Int[] sizes =
            {
                new(3, 2),
                new(2, 1),
                new(5, 1),
                new(1, 3),
            };

            RecipeWarehouseLayout.Result result =
                RecipeWarehouseLayout.Pack(sizes, 12);

            Assert.That(result.Columns, Is.EqualTo(12));
            Assert.That(result.Placements.Count, Is.EqualTo(sizes.Length));
            for (int i = 0; i < sizes.Length; i++)
            {
                Assert.That(result.Placements[i].Size, Is.EqualTo(sizes[i]));
                Assert.That(
                    result.Placements[i].Position.x + sizes[i].x,
                    Is.LessThanOrEqualTo(result.Columns));
                for (int j = i + 1; j < sizes.Length; j++)
                {
                    Assert.That(
                        Overlaps(result.Placements[i], result.Placements[j]),
                        Is.False,
                        $"placements {i} and {j}");
                }
            }
        }

        [Test]
        public void Layout_HandlesEmptyAndOversizedEntries_AndOneTrailingRow()
        {
            RecipeWarehouseLayout.Result empty =
                RecipeWarehouseLayout.Pack(Array.Empty<Vector2Int>(), 12);
            Assert.That(empty.Columns, Is.EqualTo(12));
            Assert.That(empty.Rows, Is.Zero);
            Assert.That(empty.Placements, Is.Empty);

            RecipeWarehouseLayout.Result wide =
                RecipeWarehouseLayout.Pack(new[] { new Vector2Int(15, 2) }, 12);
            Assert.That(wide.Columns, Is.EqualTo(15));
            Assert.That(wide.Rows, Is.EqualTo(2));
            Assert.That(wide.Placements[0].Position, Is.EqualTo(Vector2Int.zero));

            Assert.That(
                RecipeWarehouseLayout.ContentRows(8, 1, 400f, 88f, 20f),
                Is.EqualTo(9));
            Assert.That(
                RecipeWarehouseLayout.ContentRows(2, 1, 400f, 88f, 20f),
                Is.EqualTo(5));
        }

        [Test]
        public void View_ReusesOneWarehouseInstanceAcrossReadonlyPoolSessions()
        {
            GameObject prefab = LoadPrefab(ViewPrefabPath);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var view = instance.GetComponent<RecipeReadonlyBookView>();
                var database = new GameplayDatabase(
                    Array.Empty<DishDef>(),
                    Array.Empty<SkillDef>(),
                    Array.Empty<FlavorDef>(),
                    Array.Empty<RecipeDef>());

                view.OpenForReadonlyDishPool(
                    database,
                    Array.Empty<string>(),
                    "第一组");
                RecipeWarehouseView first = instance
                    .GetComponentInChildren<RecipeWarehouseView>(true);
                view.OpenForReadonlyDishPool(
                    database,
                    Array.Empty<string>(),
                    "第二组");
                RecipeWarehouseView[] warehouses = instance
                    .GetComponentsInChildren<RecipeWarehouseView>(true);

                Assert.That(first, Is.Not.Null);
                Assert.That(warehouses, Has.Length.EqualTo(1));
                Assert.That(warehouses[0], Is.SameAs(first));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void DishRelease_ClearsBindingAndInteractionState()
        {
            GameObject prefab = LoadPrefab(DishPrefabPath);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var dish = instance.GetComponent<RecipeEditDishView>();
                dish.Bind("测试", string.Empty, 2, 7, false, _ => { });
                dish.ReleaseForReuse();

                Assert.That(dish.BookIndex, Is.EqualTo(-1));
                Assert.That(dish.DishIndex, Is.EqualTo(-1));
                Assert.That(dish.DishDef, Is.Null);
                Assert.That(dish.DisplayedGridSize, Is.EqualTo(Vector2Int.one));
                CanvasGroup group = instance.GetComponent<CanvasGroup>();
                Assert.That(group.blocksRaycasts, Is.False);
                Assert.That(group.interactable, Is.False);
                Assert.That(instance.GetComponent<Button>().interactable, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void PassiveRemovalIndexMap_RemovesTargetsAndCompactsSurvivors()
        {
            var result = new RecipeMutationResult();
            foreach (string dishId in new[] { "a", "cookie_1", "b", "cookie_2", "c" })
            {
                result.BeforeRecipe.Add(new RecipeDishSnapshot { DishId = dishId });
            }

            result.Entries.Add(RemoveEntry(1, "cookie_1"));
            result.Entries.Add(RemoveEntry(3, "cookie_2"));

            bool mapped = RecipeReadonlyBookView.TryBuildPassiveRemovalIndexMap(
                result,
                3,
                out int[] oldToNewIndex);

            Assert.That(mapped, Is.True);
            Assert.That(oldToNewIndex, Is.EqualTo(new[] { 0, -1, 1, -1, 2 }));
        }

        [Test]
        public void PassiveRemovalIndexMap_RejectsNonRemovalMutation()
        {
            var result = new RecipeMutationResult();
            result.BeforeRecipe.Add(new RecipeDishSnapshot { DishId = "a" });
            result.Entries.Add(new RecipeMutationEntry
            {
                BookIndex = 0,
                DishIndex = 0,
                Before = new RecipeDishSnapshot { DishId = "a" },
                After = new RecipeDishSnapshot { DishId = "b" },
            });

            Assert.That(
                RecipeReadonlyBookView.TryBuildPassiveRemovalIndexMap(
                    result,
                    0,
                    out _),
                Is.False);
        }

        [Test]
        public void PassiveRemovalCommit_ReusesSurvivorCardsAndReindexesThem()
        {
            GameObject instance = UnityEngine.Object.Instantiate(
                LoadPrefab(ViewPrefabPath));
            try
            {
                string[] beforeIds = { "a", "cookie_1", "b", "cookie_2", "c" };
                DishDef[] dishes = beforeIds
                    .Select((dishId, index) => new DishDef(
                        dishId,
                        dishId,
                        deliciousness: 1,
                        shape: DishShape.FromRows(new[] { "X" }),
                        hiddenMin: 0,
                        hiddenMax: 0,
                        baseWeight: 1f,
                        skillIds: Array.Empty<string>(),
                        flavorId: string.Empty,
                        sortOrder: index))
                    .ToArray();
                var database = new GameplayDatabase(
                    dishes,
                    Array.Empty<SkillDef>(),
                    Array.Empty<FlavorDef>(),
                    Array.Empty<RecipeDef>());
                var view = instance.GetComponent<RecipeReadonlyBookView>();
                view.OpenForReadonlyDishPool(database, beforeIds, "删除预览");

                Dictionary<int, RecipeEditDishView> originalCards = instance
                    .GetComponentsInChildren<RecipeEditDishView>(false)
                    .ToDictionary(dish => dish.DishIndex);
                var result = new RecipeMutationResult();
                foreach (string dishId in beforeIds)
                {
                    result.BeforeRecipe.Add(
                        new RecipeDishSnapshot { DishId = dishId });
                }

                result.Entries.Add(RemoveEntry(1, "cookie_1"));
                result.Entries.Add(RemoveEntry(3, "cookie_2"));
                var afterEntries = new List<RecipeReadonlyDishEntry>
                {
                    new(new RecipeBookSlot("a")),
                    new(new RecipeBookSlot("b")),
                    new(new RecipeBookSlot("c")),
                };

                bool applied = view.TryApplyPassiveRemovalEntries(
                    result,
                    afterEntries,
                    null);

                Assert.That(applied, Is.True);
                Assert.That(originalCards[1].gameObject.activeSelf, Is.False);
                Assert.That(originalCards[3].gameObject.activeSelf, Is.False);
                Assert.That(originalCards[0].DishIndex, Is.EqualTo(0));
                Assert.That(originalCards[2].DishIndex, Is.EqualTo(1));
                Assert.That(originalCards[4].DishIndex, Is.EqualTo(2));
                Assert.That(
                    instance.GetComponentsInChildren<RecipeEditDishView>(false),
                    Has.Length.EqualTo(3));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void Prefabs_HaveResponsiveChrome_CompleteBindings_AndSafeRaycasts()
        {
            GameObject prefab = LoadPrefab(ViewPrefabPath);
            var view = prefab.GetComponent<RecipeReadonlyBookView>();
            var serialized = new SerializedObject(view);
            foreach (string field in new[]
                     {
                         "_bookChrome",
                         "_titleText",
                         "_warehouseContainer",
                         "_warehousePrefab",
                         "_dishPrefab",
                         "_backButton",
                     })
            {
                Assert.That(
                    serialized.FindProperty(field).objectReferenceValue,
                    Is.Not.Null,
                    field);
            }

            Transform chrome = prefab.transform.Find("BookChrome");
            Assert.That(chrome, Is.Not.Null);
            var fitter = chrome.GetComponent<AspectRatioFitter>();
            Assert.That(fitter, Is.Null);
            AssertRect(chrome, new Vector2(1286.4f, 714.6667f));

            AssertRect(chrome.Find("Frame"), new Vector2(1240f, 565f));
            AssertRect(chrome.Find("Paper"), new Vector2(1180f, 515f));
            AssertRect(chrome.Find("BookContainer"), new Vector2(1130f, 465f));
            AssertRect(chrome.Find("TitleTab"), new Vector2(560f, 112f));
            AssertRect(prefab.transform.Find("BackButton"), new Vector2(180f, 66f));

            Image frame = chrome.Find("Frame").GetComponent<Image>();
            Image paper = chrome.Find("Paper").GetComponent<Image>();
            Image tab = chrome.Find("TitleTab").GetComponent<Image>();
            Assert.That(frame.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(paper.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(tab.type, Is.EqualTo(Image.Type.Simple));
            Assert.That(frame.fillCenter, Is.True);
            Assert.That(paper.fillCenter, Is.True);
            Assert.That(
                frame.pixelsPerUnitMultiplier,
                Is.EqualTo(800f / 565f).Within(0.001f));
            Assert.That(
                paper.pixelsPerUnitMultiplier,
                Is.EqualTo(800f / 515f).Within(0.001f));
            Assert.That(tab.preserveAspect, Is.True);
            AssertNativeAspect(frame);
            AssertNativeAspect(paper);
            AssertNativeAspect(tab);
            Assert.That(frame.raycastTarget, Is.False);
            Assert.That(paper.raycastTarget, Is.False);
            Assert.That(tab.raycastTarget, Is.False);

            var backRect = (RectTransform)prefab.transform.Find("BackButton");
            Assert.That(backRect.anchorMin, Is.EqualTo(new Vector2(0.5f, 0f)));
            Assert.That(backRect.anchorMax, Is.EqualTo(new Vector2(0.5f, 0f)));
            Assert.That(backRect.anchoredPosition.x, Is.EqualTo(0f).Within(0.01f));
            Assert.That(backRect.anchoredPosition.y, Is.EqualTo(45f).Within(0.01f));
            Image backImage = backRect.GetComponent<Image>();
            Assert.That(backImage.type, Is.EqualTo(Image.Type.Simple));
            Assert.That(backImage.preserveAspect, Is.True);

            TMP_Text title = chrome.Find("TitleTab/Title").GetComponent<TMP_Text>();
            Assert.That(title.fontSize, Is.EqualTo(36f).Within(0.001f));
            Assert.That(title.raycastTarget, Is.False);

            GameObject warehousePrefab = LoadPrefab(WarehousePrefabPath);
            var warehouse = warehousePrefab.GetComponent<RecipeWarehouseView>();
            var warehouseSo = new SerializedObject(warehouse);
            Assert.That(
                warehouseSo.FindProperty("_warehouseColumns").intValue,
                Is.EqualTo(12));
            Assert.That(
                warehouseSo.FindProperty("_warehouseTrailingRows").intValue,
                Is.EqualTo(1));
            var grid = warehousePrefab
                .GetComponentInChildren<RecipeWarehouseGridGraphic>(true);
            Assert.That(grid.raycastTarget, Is.False);
        }

        [Test]
        public void HostPrefabs_ContainExpectedInstances_WithoutLegacyChildLayoutOverrides()
        {
            AssertHostPrefab(BattlePrefabPath, 2);
            AssertHostPrefab(CharacterPrefabPath, 1);
        }

        [TestCase(1920f, 1080f)]
        [TestCase(1920f, 1200f)]
        [TestCase(2560f, 1080f)]
        [TestCase(1000f, 500f)]
        public void ResponsiveChrome_KeepsReferenceHeight_AndOnlyShrinksToFit(
            float width,
            float height)
        {
            var viewportObject = new GameObject(
                "Viewport",
                typeof(RectTransform));
            var viewport = (RectTransform)viewportObject.transform;
            viewport.sizeDelta = new Vector2(width, height);
            GameObject instance = UnityEngine.Object.Instantiate(
                LoadPrefab(ViewPrefabPath),
                viewport,
                false);
            try
            {
                instance.SetActive(false);
                var root = (RectTransform)instance.transform;
                root.anchorMin = Vector2.zero;
                root.anchorMax = Vector2.one;
                root.offsetMin = Vector2.zero;
                root.offsetMax = Vector2.zero;
                instance.SetActive(true);
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(root);
                instance.GetComponent<RecipeReadonlyBookView>()
                    .UpdateBookChromeScale();

                var chrome = (RectTransform)root.Find("BookChrome");
                Assert.That(
                    chrome.rect.width / chrome.rect.height,
                    Is.EqualTo(1.8f).Within(0.001f));
                Assert.That(
                    chrome.rect.height,
                    Is.EqualTo(714.6667f).Within(0.01f));
                float expectedScale = Mathf.Clamp01(Mathf.Min(
                    width / 1286.4f,
                    height / 714.6667f));
                Assert.That(
                    chrome.localScale.x,
                    Is.EqualTo(expectedScale).Within(0.001f));
                Assert.That(
                    chrome.localScale.y,
                    Is.EqualTo(expectedScale).Within(0.001f));
                Assert.That(
                    chrome.rect.width * chrome.localScale.x,
                    Is.LessThanOrEqualTo(width + 0.01f));
                Assert.That(
                    chrome.rect.height * chrome.localScale.y,
                    Is.LessThanOrEqualTo(height + 0.01f));
                AssertChildInside(chrome, (RectTransform)chrome.Find("Frame"));
                AssertChildInside(chrome, (RectTransform)chrome.Find("TitleTab"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(viewportObject);
            }
        }

        [Test]
        public void Sprites_UseRequiredImportPolicy_NineSliceBorders_AndRealAlpha()
        {
            string[] names =
            {
                "recipe_book_frame.png",
                "recipe_book_paper.png",
                "recipe_book_title_tab.png",
                "recipe_book_cell.png",
            };

            foreach (string name in names)
            {
                string path = SpriteRoot + name;
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(importer, Is.Not.Null, name);
                Assert.That(
                    importer.textureType,
                    Is.EqualTo(TextureImporterType.Sprite),
                    name);
                Assert.That(
                    importer.spriteImportMode,
                    Is.EqualTo(SpriteImportMode.Single),
                    name);
                Assert.That(importer.alphaIsTransparency, Is.True, name);
                Assert.That(importer.mipmapEnabled, Is.False, name);
                Assert.That(
                    importer.wrapMode,
                    Is.EqualTo(TextureWrapMode.Clamp),
                    name);
                Assert.That(
                    importer.filterMode,
                    Is.EqualTo(FilterMode.Bilinear),
                    name);
                AssertRealAlpha(path, name);
            }

            AssertValidBorder("recipe_book_frame.png");
            AssertValidBorder("recipe_book_paper.png");
            Assert.That(
                ((TextureImporter)AssetImporter.GetAtPath(
                    SpriteRoot + "recipe_book_title_tab.png")).spriteBorder,
                Is.EqualTo(Vector4.zero));
            Assert.That(
                ((TextureImporter)AssetImporter.GetAtPath(
                    SpriteRoot + "recipe_book_cell.png")).spriteBorder,
                Is.EqualTo(Vector4.zero));
            AssertTextureAspect("recipe_book_frame.png", 1240f / 565f);
            AssertTextureAspect("recipe_book_paper.png", 1180f / 515f);
            AssertTextureAspect("recipe_book_title_tab.png", 560f / 112f);
            AssertTextureAspect("recipe_book_cell.png", 1f);
        }

        private static RecipeReadonlyBookView.RecipeReadonlyBookSession Session(
            RecipeReadonlyBookRequest request)
        {
            return new RecipeReadonlyBookView.RecipeReadonlyBookSession(request);
        }

        private static RecipeMutationEntry RemoveEntry(int dishIndex, string dishId)
        {
            return new RecipeMutationEntry
            {
                BookIndex = 0,
                DishIndex = dishIndex,
                Before = new RecipeDishSnapshot { DishId = dishId },
                After = new RecipeDishSnapshot(),
            };
        }

        private static void AssertSession(
            RecipeReadonlyBookView.RecipeReadonlyBookSession session,
            string title,
            string exitText,
            bool clickable,
            bool showExit,
            int bookIndex)
        {
            Assert.That(session.PanelTitle, Is.EqualTo(title));
            Assert.That(session.ExitButtonText, Is.EqualTo(exitText));
            Assert.That(session.CanClickDish, Is.EqualTo(clickable));
            Assert.That(session.ShowExitButton, Is.EqualTo(showExit));
            Assert.That(session.BookIndexFilter, Is.EqualTo(bookIndex));
        }

        private static bool Overlaps(
            RecipeWarehouseLayout.Placement left,
            RecipeWarehouseLayout.Placement right)
        {
            return left.Position.x < right.Position.x + right.Size.x
                && left.Position.x + left.Size.x > right.Position.x
                && left.Position.y < right.Position.y + right.Size.y
                && left.Position.y + left.Size.y > right.Position.y;
        }

        private static GameObject LoadPrefab(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            return prefab;
        }

        private static void AssertRect(Transform transform, Vector2 expected)
        {
            Assert.That(transform, Is.Not.Null);
            var rect = (RectTransform)transform;
            Assert.That(rect.sizeDelta.x, Is.EqualTo(expected.x).Within(0.01f));
            Assert.That(rect.sizeDelta.y, Is.EqualTo(expected.y).Within(0.01f));
        }

        private static void AssertHostPrefab(string path, int expectedViews)
        {
            GameObject prefab = LoadPrefab(path);
            RecipeReadonlyBookView[] views = prefab
                .GetComponentsInChildren<RecipeReadonlyBookView>(true);
            Assert.That(views, Has.Length.EqualTo(expectedViews));
            foreach (RecipeReadonlyBookView view in views)
            {
                GameObject instanceRoot =
                    PrefabUtility.GetNearestPrefabInstanceRoot(view.gameObject);
                PropertyModification[] modifications =
                    PrefabUtility.GetPropertyModifications(instanceRoot);
                Assert.That(
                    modifications.Any(modification =>
                        modification?.target is RectTransform rect
                        && IsLegacyLayoutChild(rect.name)
                        && IsLayoutProperty(modification.propertyPath)),
                    Is.False,
                    view.name);
            }
        }

        private static bool IsLegacyLayoutChild(string name)
        {
            return name == "Frame"
                || name == "Paper"
                || name == "BookContainer"
                || name == "TitleTab";
        }

        private static bool IsLayoutProperty(string propertyPath)
        {
            return propertyPath.StartsWith("m_Anchor", StringComparison.Ordinal)
                || propertyPath.StartsWith(
                    "m_AnchoredPosition",
                    StringComparison.Ordinal)
                || propertyPath.StartsWith("m_SizeDelta", StringComparison.Ordinal)
                || propertyPath.StartsWith("m_Offset", StringComparison.Ordinal)
                || propertyPath.StartsWith("m_Pivot", StringComparison.Ordinal);
        }

        private static void AssertNativeAspect(Image image)
        {
            var rect = (RectTransform)image.transform;
            float displayAspect = rect.sizeDelta.x / rect.sizeDelta.y;
            float spriteAspect = image.sprite.rect.width / image.sprite.rect.height;
            Assert.That(spriteAspect, Is.EqualTo(displayAspect).Within(0.002f), image.name);
        }

        private static void AssertValidBorder(string name)
        {
            string path = SpriteRoot + name;
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Vector4 border = importer.spriteBorder;
            Assert.That(border.x, Is.GreaterThan(0f), name);
            Assert.That(border.y, Is.GreaterThan(0f), name);
            Assert.That(border.z, Is.GreaterThan(0f), name);
            Assert.That(border.w, Is.GreaterThan(0f), name);
            Assert.That(border.x + border.z, Is.LessThan(texture.width), name);
            Assert.That(border.y + border.w, Is.LessThan(texture.height), name);
        }

        private static void AssertTextureAspect(string name, float expected)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                SpriteRoot + name);
            Assert.That(texture, Is.Not.Null, name);
            Assert.That(
                (float)texture.width / texture.height,
                Is.EqualTo(expected).Within(0.002f),
                name);
        }

        private static void AssertChildInside(
            RectTransform parent,
            RectTransform child)
        {
            Rect parentRect = parent.rect;
            Rect childRect = child.rect;
            Vector2 position = child.anchoredPosition;
            Assert.That(
                childRect.xMin + position.x,
                Is.GreaterThanOrEqualTo(parentRect.xMin - 0.01f));
            Assert.That(
                childRect.xMax + position.x,
                Is.LessThanOrEqualTo(parentRect.xMax + 0.01f));
            Assert.That(
                childRect.yMin + position.y,
                Is.GreaterThanOrEqualTo(parentRect.yMin - 0.01f));
            Assert.That(
                childRect.yMax + position.y,
                Is.LessThanOrEqualTo(parentRect.yMax + 0.01f));
        }

        private static void AssertRealAlpha(string assetPath, string name)
        {
            string absolutePath = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                assetPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(
                    ImageConversion.LoadImage(
                        texture,
                        File.ReadAllBytes(absolutePath),
                        false),
                    Is.True,
                    name);
                Color32[] pixels = texture.GetPixels32();
                Assert.That(pixels.Any(pixel => pixel.a == 0), Is.True, name);
                Assert.That(
                    pixels.Any(pixel => pixel.a == byte.MaxValue),
                    Is.True,
                    name);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
