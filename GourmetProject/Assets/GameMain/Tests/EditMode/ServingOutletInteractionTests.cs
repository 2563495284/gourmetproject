using System;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ServingOutletInteractionTests
    {
        [Test]
        public void FlyingDish_KeepsFlyingSortingLayerAfterPlacementRebuild()
        {
            DishPieceView prefab = AssetDatabase.LoadAssetAtPath<DishPieceView>(
                "Assets/GameMain/Prefabs/Battle/DishPiece.prefab");
            DishPieceView view = UnityEngine.Object.Instantiate(prefab);
            try
            {
                DishShape shape = DishShape.FromRows(new[] { "XX" });
                var def = new DishDef(
                    "test_dish",
                    "测试食物",
                    10,
                    shape,
                    0,
                    0,
                    1f,
                    Array.Empty<string>(),
                    string.Empty,
                    false);
                var dish = new DishInstance(
                    1,
                    def,
                    new Placement(shape, 0, new GridPos(0, 0)),
                    Array.Empty<string>(),
                    Array.Empty<string>());

                view.BuildPlaced(dish, null, 1f, 1.05f, null);
                view.SetFlying(true);
                view.UpdatePlacement(new Placement(shape, 0, new GridPos(1, 0)));

                SpriteRenderer body = FindRenderer(view, "Sprite");
                Assert.That(body, Is.Not.Null);
                Assert.That(body.sortingLayerName, Is.EqualTo("PiecesFlying"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(view.gameObject);
            }
        }

        [Test]
        public void PreparedDish_HasDedicatedHoverTrigger()
        {
            ServingOutletView prefab = AssetDatabase.LoadAssetAtPath<ServingOutletView>(
                "Assets/GameMain/UI/Hud/ServingOutlet.prefab");
            ServingOutletView view = UnityEngine.Object.Instantiate(prefab);
            try
            {
                ServingOutletDishHoverTrigger trigger =
                    view.transform.Find("PreparedDish").GetComponent<ServingOutletDishHoverTrigger>();
                Assert.That(trigger, Is.Not.Null);

                int entered = 0;
                int exited = 0;
                trigger.Bind(() => entered++, () => exited++);
                trigger.OnPointerEnter(null);
                trigger.OnPointerEnter(null);
                trigger.OnPointerExit(null);

                Assert.That(entered, Is.EqualTo(1));
                Assert.That(exited, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(view.gameObject);
            }
        }

        private static SpriteRenderer FindRenderer(DishPieceView view, string objectName)
        {
            foreach (SpriteRenderer renderer in view.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer.name == objectName)
                {
                    return renderer;
                }
            }

            return null;
        }
    }
}
