using System;
using System.Collections;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class DishPieceViewTemporaryAreaPlayModeTests
    {
        [UnityTest]
        public IEnumerator NumbTransform_KeepsVisualCenterWhenPlacementOrientationChanges()
        {
            DishPieceView piece = BuildPiece(out GameObject root, out Sprite sprite);
            Vector3 centerBefore = piece.OccupiedCellCenterWorld();
            DishShape rotatedShape = piece.Instance.Def.Shape.RotatedBy(3);
            var rotatedPlacement = new Placement(rotatedShape, 3, new GridPos(0, 0));
            bool completed = false;

            piece.PlayActiveItemNumbTransform(rotatedPlacement, () => completed = true);
            float deadline = Time.realtimeSinceStartup + 2f;
            while (!completed && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(completed, Is.True, "麻风味变化动画应在测试超时前结束。");
            Assert.That(
                Vector3.Distance(centerBefore, piece.OccupiedCellCenterWorld()),
                Is.LessThan(0.001f),
                "切换真实朝向时必须锁住同一个世界视觉中心，不能先跳位再飞行。");

            UnityEngine.Object.Destroy(root);
            UnityEngine.Object.Destroy(sprite.texture);
            UnityEngine.Object.Destroy(sprite);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SortingOffset_KeepsNewestTemporaryDishAboveOlderDishAsAWhole()
        {
            DishPieceView piece = BuildPiece(out GameObject root, out Sprite sprite);
            piece.SetFlying(true);
            piece.SetSortingOrderOffset(40);
            yield return null;

            SpriteRenderer body = root.transform.Find("VisualPivot/Sprite").GetComponent<SpriteRenderer>();
            SpriteRenderer glow = root.transform.Find("VisualPivot/Sprite/PlacementGlow").GetComponent<SpriteRenderer>();
            SpriteRenderer shadow = root.transform.Find("Shadow").GetComponent<SpriteRenderer>();
            SpriteRenderer halo = root.transform.Find("ShadowHalo").GetComponent<SpriteRenderer>();

            Assert.That(body.sortingOrder, Is.EqualTo(BattleSorting.OrderBody + 40));
            Assert.That(glow.sortingOrder, Is.EqualTo(BattleSorting.OrderBody + 41));
            Assert.That(shadow.sortingOrder, Is.EqualTo(BattleSorting.OrderShadow + 40));
            Assert.That(halo.sortingOrder, Is.EqualTo(BattleSorting.OrderShadow - 1 + 40));
            Assert.That(body.sortingLayerName, Is.EqualTo(BattleSorting.PiecesFlying));
            Assert.That(shadow.sortingLayerName, Is.EqualTo(BattleSorting.PiecesFlying));

            UnityEngine.Object.Destroy(root);
            UnityEngine.Object.Destroy(sprite.texture);
            UnityEngine.Object.Destroy(sprite);
            yield return null;
        }

        private static DishPieceView BuildPiece(out GameObject root, out Sprite sprite)
        {
            root = new GameObject("DishPiece");
            root.AddComponent<BoxCollider2D>();

            CreateRenderer(root.transform, "Shadow");
            CreateRenderer(root.transform, "ShadowHalo");
            GameObject pivot = new GameObject("VisualPivot");
            pivot.transform.SetParent(root.transform, false);
            GameObject spriteObject = CreateRenderer(pivot.transform, "Sprite");
            CreateRenderer(spriteObject.transform, "PlacementGlow");

            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var pixels = new Color[16 * 16];
            Array.Fill(pixels, Color.white);
            texture.SetPixels(pixels);
            texture.Apply();
            sprite = Sprite.Create(texture, new Rect(0f, 0f, 16f, 16f), new Vector2(0.5f, 0.5f), 16f);

            DishShape shape = DishShape.FromRows(new[] { "XXX", "X.." });
            var definition = new DishDef(
                "temporary_playmode",
                "临时桌表现测试菜",
                1,
                shape,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                allowRotate: true);
            var placement = new Placement(shape, 0, new GridPos(0, 0));
            var instance = new DishInstance(
                1,
                definition,
                placement,
                Array.Empty<string>(),
                Array.Empty<string>());

            DishPieceView piece = root.AddComponent<DishPieceView>();
            root.transform.position = new Vector3(2.3f, -1.7f, 0f);
            piece.BuildPlaced(instance, sprite, 1f, 1.1f, null);
            return piece;
        }

        private static GameObject CreateRenderer(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.AddComponent<SpriteRenderer>();
            return child;
        }
    }
}
