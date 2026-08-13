using System.Collections.Generic;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class DiningTableCellViewTests
    {
        private const string PrefabPath =
            "Assets/GameMain/Content/Prefabs/Battle/Board/DiningTableCell.prefab";
        private const string SpriteRoot =
            "Assets/GameMain/Content/Resources/Sprites/UI/";

        private static readonly string[] Suffixes =
        {
            string.Empty,
            "_m_silver",
            "_m_gold",
            "_m_obsidian",
            "_m_emerald",
            "_m_cherry",
            "_m_walnut",
            "_m_marble",
        };

        [Test]
        public void Prefab_HasFixedTablePlateAndLogicalColliderBindings()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);

            DiningTableCellView view = prefab.GetComponent<DiningTableCellView>();
            BoxCollider2D collider = prefab.GetComponent<BoxCollider2D>();
            Transform tableVisual = prefab.transform.Find("TableVisual");
            Transform plateVisual = tableVisual?.Find("PlateVisual");

            Assert.That(view, Is.Not.Null);
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.size, Is.EqualTo(Vector2.one));
            Assert.That(tableVisual, Is.Not.Null);
            Assert.That(tableVisual.GetComponent<SpriteRenderer>(), Is.Not.Null);
            Assert.That(plateVisual, Is.Not.Null);
            Assert.That(plateVisual.GetComponent<SpriteRenderer>(), Is.Not.Null);
            Assert.That(plateVisual.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(plateVisual.localScale, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void AllEightSpritePairs_HaveMatchingCanvasPivotAndPpu()
        {
            foreach (string suffix in Suffixes)
            {
                Sprite table = LoadSprite($"board_cell{suffix}");
                Sprite plate = LoadSprite($"board_cell_plate{suffix}");
                Assert.That(table, Is.Not.Null, $"table missing: {suffix}");
                Assert.That(plate, Is.Not.Null, $"plate missing: {suffix}");

                AssertSpriteSpec(table, suffix + " table");
                AssertSpriteSpec(plate, suffix + " plate");
                Assert.That(plate.rect, Is.EqualTo(table.rect), suffix);
                Assert.That(plate.pivot, Is.EqualTo(table.pivot), suffix);
                Assert.That(plate.pixelsPerUnit, Is.EqualTo(table.pixelsPerUnit), suffix);
            }
        }

        [Test]
        public void Configure_AssignsRowInterleavedSortingOrders()
        {
            DiningTableCellSprites sprites = DefaultSprites();
            var instances = new List<GameObject>();
            try
            {
                for (int row = 0; row < 4; row++)
                {
                    DiningTableCellView view = InstantiateView(instances);
                    view.Configure(new GridPos(0, row), Vector3.zero, 1f, sprites, null);
                    GetRenderers(view, out SpriteRenderer table, out SpriteRenderer plate);

                    Assert.That(table.sortingOrder, Is.EqualTo(row * 2));
                    Assert.That(plate.sortingOrder, Is.EqualTo(row * 2 + 1));
                }
            }
            finally
            {
                DestroyAll(instances);
            }
        }

        [Test]
        public void WorldBounds_ReflectsImmediateTransformChangesWithoutPhysicsSync()
        {
            var instances = new List<GameObject>();
            try
            {
                DiningTableCellView view = InstantiateView(instances);
                var parent = new GameObject("CellParent");
                instances.Add(parent);
                parent.transform.position = new Vector3(4f, -3f, 0f);
                view.transform.SetParent(parent.transform, worldPositionStays: false);
                view.Configure(new GridPos(0, 0), Vector3.zero, 0.6f, DefaultSprites(), null);
                view.transform.localRotation = Quaternion.Euler(0f, 0f, 30f);

                Bounds bounds = view.WorldBounds;

                Assert.That(bounds.center.x, Is.EqualTo(4f).Within(0.0001f));
                Assert.That(bounds.center.y, Is.EqualTo(-3f).Within(0.0001f));
                Assert.That(bounds.size.x, Is.GreaterThan(0.6f));
                Assert.That(bounds.size.y, Is.GreaterThan(0.6f));
            }
            finally
            {
                DestroyAll(instances);
            }
        }

        [Test]
        public void PlateFeedback_TintsOnlyPlateAndClearRestoresBaseColor()
        {
            var instances = new List<GameObject>();
            try
            {
                DiningTableCellView view = InstantiateView(instances);
                view.Configure(new GridPos(0, 0), Vector3.zero, 1f, DefaultSprites(), null);
                GetRenderers(view, out SpriteRenderer table, out SpriteRenderer plate);

                Color baseColor = new(0.8f, 0.7f, 0.6f, 0.5f);
                Color feedback = GridPlacementFeedbackPalette.Blocked;
                view.SetColor(baseColor);
                Material tableMaterial = table.sharedMaterial;
                Material plateMaterial = plate.sharedMaterial;

                view.SetPlateFeedbackColor(feedback);

                AssertColor(table.color, baseColor);
                AssertColor(
                    plate.color,
                    new Color(feedback.r, feedback.g, feedback.b, baseColor.a * feedback.a));
                Assert.That(table.sharedMaterial, Is.SameAs(tableMaterial));
                Assert.That(plate.sharedMaterial, Is.SameAs(plateMaterial));
                Assert.That(plate.sharedMaterial.shader.name, Does.Not.Contain("Outline"));

                view.ClearPlateFeedbackColor();
                AssertColor(table.color, baseColor);
                AssertColor(plate.color, baseColor);
            }
            finally
            {
                DestroyAll(instances);
            }
        }

        private static DiningTableCellView InstantiateView(ICollection<GameObject> instances)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject instance = Object.Instantiate(prefab);
            instances.Add(instance);
            return instance.GetComponent<DiningTableCellView>();
        }

        private static DiningTableCellSprites DefaultSprites()
        {
            return new DiningTableCellSprites(
                LoadSprite("board_cell"),
                LoadSprite("board_cell_plate"));
        }

        private static Sprite LoadSprite(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>($"{SpriteRoot}{name}.png");
        }

        private static void AssertSpriteSpec(Sprite sprite, string label)
        {
            Assert.That(sprite.texture.width, Is.EqualTo(138), label);
            Assert.That(sprite.texture.height, Is.EqualTo(178), label);
            Assert.That(sprite.rect, Is.EqualTo(new Rect(0f, 0f, 138f, 178f)), label);
            Assert.That(sprite.pixelsPerUnit, Is.EqualTo(100f), label);
            Assert.That(sprite.pivot, Is.EqualTo(new Vector2(69f, 89f)), label);

            string path = AssetDatabase.GetAssetPath(sprite);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(importer, Is.Not.Null, label);
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single), label);
            Assert.That(importer.mipmapEnabled, Is.False, label);
        }

        private static void GetRenderers(
            DiningTableCellView view,
            out SpriteRenderer table,
            out SpriteRenderer plate)
        {
            Transform tableVisual = view.transform.Find("TableVisual");
            table = tableVisual.GetComponent<SpriteRenderer>();
            plate = tableVisual.Find("PlateVisual").GetComponent<SpriteRenderer>();
        }

        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.0001f));
        }

        private static void DestroyAll(IEnumerable<GameObject> instances)
        {
            foreach (GameObject instance in instances)
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }
    }
}
