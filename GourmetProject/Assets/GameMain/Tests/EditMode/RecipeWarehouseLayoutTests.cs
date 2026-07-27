using System;
using System.Collections.Generic;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class RecipeWarehouseLayoutTests
    {
        [Test]
        public void Pack_EmptyInputReturnsMinimalGrid()
        {
            RecipeWarehouseLayout.Result result =
                RecipeWarehouseLayout.Pack(Array.Empty<Vector2Int>(), 12);

            Assert.That(result.Placements, Is.Empty);
            Assert.That(result.Columns, Is.EqualTo(12));
            Assert.That(result.Rows, Is.Zero);
        }

        [Test]
        public void Pack_PreservesOrderAndNeverOverlaps()
        {
            Vector2Int[] sizes =
            {
                new(3, 2),
                new(1, 4),
                new(2, 2),
                new(3, 1),
                new(2, 3),
            };

            RecipeWarehouseLayout.Result result =
                RecipeWarehouseLayout.Pack(sizes, 12);

            Assert.That(result.Placements.Count, Is.EqualTo(sizes.Length));
            Assert.That(result.Columns, Is.EqualTo(12));
            var occupied = new HashSet<Vector2Int>();
            for (int i = 0; i < result.Placements.Count; i++)
            {
                RecipeWarehouseLayout.Placement placement = result.Placements[i];
                Assert.That(placement.Size, Is.EqualTo(sizes[i]));
                Assert.That(placement.Position.x, Is.GreaterThanOrEqualTo(0));
                Assert.That(placement.Position.y, Is.GreaterThanOrEqualTo(0));
                Assert.That(
                    placement.Position.x + placement.Size.x,
                    Is.LessThanOrEqualTo(result.Columns));
                for (int y = 0; y < placement.Size.y; y++)
                {
                    for (int x = 0; x < placement.Size.x; x++)
                    {
                        Assert.That(
                            occupied.Add(placement.Position + new Vector2Int(x, y)),
                            Is.True,
                            $"Placement {i} overlaps another item.");
                    }
                }
            }
        }

        [Test]
        public void Pack_IsDeterministic()
        {
            Vector2Int[] sizes =
            {
                new(2, 2),
                new(3, 1),
                new(1, 3),
                new(2, 1),
            };

            RecipeWarehouseLayout.Result first =
                RecipeWarehouseLayout.Pack(sizes, 12);
            RecipeWarehouseLayout.Result second =
                RecipeWarehouseLayout.Pack(sizes, 12);

            Assert.That(second.Columns, Is.EqualTo(first.Columns));
            Assert.That(second.Rows, Is.EqualTo(first.Rows));
            for (int i = 0; i < first.Placements.Count; i++)
            {
                Assert.That(
                    second.Placements[i].Position,
                    Is.EqualTo(first.Placements[i].Position));
                Assert.That(
                    second.Placements[i].Size,
                    Is.EqualTo(first.Placements[i].Size));
            }
        }

        [Test]
        public void Pack_FiftyLargeDishesKeepsFixedWidthAndExtendsDownward()
        {
            var sizes = new Vector2Int[50];
            for (int i = 0; i < sizes.Length; i++)
            {
                sizes[i] = new Vector2Int(3, 2);
            }

            RecipeWarehouseLayout.Result result =
                RecipeWarehouseLayout.Pack(sizes, 12);

            Assert.That(result.Columns, Is.EqualTo(12));
            Assert.That(result.Rows, Is.EqualTo(26));
        }

        [Test]
        public void Pack_ItemWiderThanConfiguredWidthExpandsSafely()
        {
            RecipeWarehouseLayout.Result result =
                RecipeWarehouseLayout.Pack(
                    new[] { new Vector2Int(14, 2) },
                    12);

            Assert.That(result.Columns, Is.EqualTo(14));
            Assert.That(result.Rows, Is.EqualTo(2));
            Assert.That(result.Placements[0].Position, Is.EqualTo(Vector2Int.zero));
        }

        [TestCase(1920f)]
        [TestCase(1280f)]
        public void WidthScale_AlwaysFillsAvailableViewport(float viewportWidth)
        {
            const int columns = 12;
            const float cellSize = 88f;
            const float padding = 24f;
            float scale = RecipeWarehouseLayout.ScaleForViewportWidth(
                columns,
                viewportWidth,
                cellSize,
                padding);

            float renderedWidth =
                (padding * 2f + columns * cellSize) * scale;
            Assert.That(renderedWidth, Is.EqualTo(viewportWidth).Within(0.001f));
        }

        [Test]
        public void ContentRows_UsesOccupiedRowsPlusTenOrViewportMinimum()
        {
            Assert.That(
                RecipeWarehouseLayout.ContentRows(
                    7,
                    10,
                    500f,
                    50f,
                    20f),
                Is.EqualTo(17));
            Assert.That(
                RecipeWarehouseLayout.ContentRows(
                    0,
                    10,
                    760f,
                    50f,
                    20f),
                Is.EqualTo(15));
        }

        [Test]
        public void DisplayedGridSizeFor_UsesFinalFlavorRotation()
        {
            var dish = new DishDef(
                "warehouse_rotation",
                "Warehouse Rotation",
                10,
                DishShape.FromRows(new[] { "XXX", "X.." }),
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                false,
                rotationIndex: 1);

            Assert.That(
                DishIconRenderTexturePreview.DisplayedGridSizeFor(dish),
                Is.EqualTo(new Vector2Int(2, 3)));
            Assert.That(
                DishIconRenderTexturePreview.DisplayedGridSizeFor(
                    dish,
                    new[] { "t_numb" }),
                Is.EqualTo(new Vector2Int(3, 2)));
        }

        [Test]
        public void WarehousePrefab_BuildsFixedWidthVerticalViewportAndGrid()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/GameMain/UI/RecipeWarehouseView.prefab");
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                RecipeWarehouseView warehouse =
                    instance.GetComponent<RecipeWarehouseView>();
                warehouse.RefreshLayout();

                RecipeWarehouseScrollRect scrollRect =
                    instance.GetComponent<RecipeWarehouseScrollRect>();
                Assert.That(scrollRect, Is.Not.Null);
                Assert.That(scrollRect.horizontal, Is.False);
                Assert.That(scrollRect.vertical, Is.True);
                Assert.That(scrollRect.viewport, Is.Not.Null);
                Assert.That(
                    scrollRect.content,
                    Is.SameAs(warehouse.DishContainer));
                Assert.That(
                    warehouse.DishContainer
                        .GetComponentInChildren<RecipeWarehouseGridGraphic>(true),
                    Is.Not.Null);
                Assert.That(
                    warehouse.DishContainer.GetComponent<GridLayoutGroup>(),
                    Is.Null);
                Assert.That(
                    warehouse.DishContainer.rect.width,
                    Is.EqualTo(scrollRect.viewport.rect.width).Within(0.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void DishDrag_PansWarehouseAndSuppressesSelectionClick()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/GameMain/UI/RecipeWarehouseView.prefab");
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            var eventSystemObject = new GameObject(
                "Warehouse Test EventSystem",
                typeof(EventSystem));
            try
            {
                RectTransform bookRect = (RectTransform)instance.transform;
                bookRect.sizeDelta = new Vector2(300f, 220f);
                RecipeWarehouseView warehouse =
                    instance.GetComponent<RecipeWarehouseView>();
                int clickCount = 0;
                RecipeEditDishView firstDish = null;
                for (int i = 0; i < 30; i++)
                {
                    var dishObject = new GameObject(
                        $"Dish_{i}",
                        typeof(RectTransform),
                        typeof(CanvasGroup),
                        typeof(RecipeEditDishView));
                    dishObject.transform.SetParent(
                        warehouse.DishContainer,
                        false);
                    RecipeEditDishView dish =
                        dishObject.GetComponent<RecipeEditDishView>();
                    dish.Bind(
                        $"Dish {i}",
                        string.Empty,
                        0,
                        i,
                        false,
                        _ => clickCount++,
                        previewMode: DishIconPreviewMode.Warehouse);
                    firstDish ??= dish;
                }

                warehouse.RefreshLayout();
                Canvas.ForceUpdateCanvases();
                Vector2 before = warehouse.DishContainer.anchoredPosition;
                var pointer = new PointerEventData(
                    eventSystemObject.GetComponent<EventSystem>())
                {
                    button = PointerEventData.InputButton.Left,
                    position = new Vector2(200f, 100f),
                    pressPosition = new Vector2(200f, 100f),
                };

                firstDish.OnPointerDown(pointer);
                firstDish.OnInitializePotentialDrag(pointer);
                firstDish.OnBeginDrag(pointer);
                pointer.delta = new Vector2(-80f, 80f);
                pointer.position += pointer.delta;
                firstDish.OnDrag(pointer);
                firstDish.OnEndDrag(pointer);

                Assert.That(
                    warehouse.DishContainer.anchoredPosition.x,
                    Is.EqualTo(before.x).Within(0.001f));
                Assert.That(
                    warehouse.DishContainer.anchoredPosition.y,
                    Is.Not.EqualTo(before.y));
                firstDish.OnPointerClick(pointer);
                Assert.That(clickCount, Is.Zero);

                firstDish.OnPointerDown(pointer);
                firstDish.OnPointerClick(pointer);
                Assert.That(clickCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(eventSystemObject);
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void WarehouseAssets_AreRenamedAndReadonlyPrefabReferencesWarehouse()
        {
            Assert.That(
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/GameMain/UI/RecipeEditBookView.prefab"),
                Is.Null);
            Assert.That(
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/GameMain/UI/RecipeBookGridView.prefab"),
                Is.Null);

            GameObject warehousePrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/GameMain/UI/RecipeWarehouseView.prefab");
            GameObject readonlyPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/GameMain/UI/RecipeReadonlyBookView.prefab");
            Assert.That(warehousePrefab, Is.Not.Null);
            Assert.That(readonlyPrefab, Is.Not.Null);
            Assert.That(
                MissingScriptCount(warehousePrefab),
                Is.Zero);
            Assert.That(
                MissingScriptCount(readonlyPrefab),
                Is.Zero);

            RecipeReadonlyBookView readonlyView =
                readonlyPrefab.GetComponent<RecipeReadonlyBookView>();
            var serializedView = new SerializedObject(readonlyView);
            RecipeWarehouseView referencedWarehouse =
                serializedView.FindProperty("_warehousePrefab")
                    .objectReferenceValue as RecipeWarehouseView;
            Assert.That(referencedWarehouse, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(referencedWarehouse),
                Is.EqualTo("Assets/GameMain/UI/RecipeWarehouseView.prefab"));
        }

        private static int MissingScriptCount(GameObject root)
        {
            int count = 0;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                count += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                    child.gameObject);
            }

            return count;
        }
    }
}
