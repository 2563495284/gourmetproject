using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class DishIconPreviewLayoutTests
    {
        private const string CellPrefabPath =
            "Assets/GameMain/Content/Prefabs/Battle/Board/DiningTableCell.prefab";

        private static readonly string[] PreviewPrefabPaths =
        {
            "Assets/GameMain/Content/Prefabs/UI/Hud/ServingOutlet.prefab",
            "Assets/GameMain/Content/Prefabs/UI/Meta/Recipes/RecipeEditDishView.prefab",
            "Assets/GameMain/Content/Prefabs/UI/Meta/Rewards/RewardDishPanel.prefab",
            "Assets/GameMain/Content/Prefabs/UI/Meta/Rewards/RewardForm.prefab",
            "Assets/GameMain/Content/Prefabs/UI/Meta/Shop/ShopBuyCardView.prefab",
            "Assets/GameMain/Content/Prefabs/UI/Meta/Shop/ShopFoodBuyItemView.prefab",
        };

        [Test]
        public void AllPreviewPrefabs_BindSharedTableDishAndBadgePrefabs()
        {
            foreach (string path in PreviewPrefabPaths)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                DishIconRenderTexturePreview preview =
                    prefab.GetComponentInChildren<DishIconRenderTexturePreview>(true);
                Assert.That(preview, Is.Not.Null, path);

                var serialized = new SerializedObject(preview);
                Assert.That(
                    serialized.FindProperty("_cellPrefab").objectReferenceValue,
                    Is.Not.Null,
                    path + " table");
                Assert.That(
                    serialized.FindProperty("_dishPrefab").objectReferenceValue,
                    Is.Not.Null,
                    path + " dish");
                Assert.That(
                    serialized.FindProperty("_badgePrefab").objectReferenceValue,
                    Is.Not.Null,
                    path + " badge");
            }
        }

        [Test]
        public void PreviewTable_UsesPrefabVisualOffsetAndScale()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CellPrefabPath);
            Transform tableVisual = prefab.transform.Find("TableVisual");
            DishShape shape = DishShape.FromRows(new[] { "XXX" });

            Vector3 position = DishIconPreviewRenderer.PreviewTableVisualPosition(
                tableVisual,
                shape,
                new GridPos(1, 0));
            Vector3 scale = DishIconPreviewRenderer.PreviewTableVisualScale(tableVisual);

            Assert.That(position, Is.EqualTo(tableVisual.localPosition));
            Assert.That(scale, Is.EqualTo(tableVisual.localScale));
        }

        [Test]
        public void SharedDishScale_IncludesBattlePitchAndHasNoPreviewInset()
        {
            var texture = new Texture2D(400, 200, TextureFormat.RGBA32, false);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 400f, 200f),
                new Vector2(0.5f, 0.5f),
                100f);
            try
            {
                DishShape original = DishShape.FromRows(new[] { "XX", "X.", "X." });
                DishShape display = original.RotatedBy(1);
                const float cellSize = 1f;
                float pitch = cellSize
                              + DiningTableLayout.Gap / DiningTableLayout.MaxCellSize;

                Vector3 scale = DishVisualLayout.SpriteScale(
                    sprite,
                    display,
                    1,
                    cellSize,
                    pitch);

                float expectedWidth = (original.Width - 1) * pitch + cellSize;
                float expectedHeight = (original.Height - 1) * pitch + cellSize;
                Assert.That(scale.x * sprite.bounds.size.x, Is.EqualTo(expectedWidth).Within(0.0001f));
                Assert.That(scale.y * sprite.bounds.size.y, Is.EqualTo(expectedHeight).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }
    }
}
