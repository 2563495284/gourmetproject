using System;
using System.Reflection;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Battle;
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
                trigger.Bind(
                    () =>
                    {
                        entered++;
                        return true;
                    },
                    () => exited++);
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

        [Test]
        public void PreparedDish_RetriesFailedFastHoverWithoutReenter()
        {
            ServingOutletView prefab = AssetDatabase.LoadAssetAtPath<ServingOutletView>(
                "Assets/GameMain/UI/Hud/ServingOutlet.prefab");
            ServingOutletView view = UnityEngine.Object.Instantiate(prefab);
            try
            {
                ServingOutletDishHoverTrigger trigger =
                    view.transform.Find("PreparedDish").GetComponent<ServingOutletDishHoverTrigger>();
                int attempts = 0;
                int exited = 0;
                trigger.Bind(() => ++attempts >= 2, () => exited++);

                trigger.OnPointerEnter(null);
                Assert.That(attempts, Is.EqualTo(1));

                trigger.OnPointerMove(null);
                Assert.That(attempts, Is.EqualTo(2));

                trigger.OnPointerMove(null);
                Assert.That(attempts, Is.EqualTo(2));

                trigger.OnPointerExit(null);
                Assert.That(exited, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(view.gameObject);
            }
        }

        [Test]
        public void PreparedDishHover_IgnoresDelayedWorldHoverExits()
        {
            var host = new GameObject("BattleFormHoverOwnershipTest");
            BattleForm form = host.AddComponent<BattleForm>();
            try
            {
                const BindingFlags instancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
                Type ownerType = typeof(BattleForm).GetNestedType("FoodTipsHoverOwner", BindingFlags.NonPublic);
                FieldInfo ownerField = typeof(BattleForm).GetField("_foodTipsHoverOwner", instancePrivate);
                MethodInfo dishExit = typeof(BattleForm).GetMethod("OnDishHoverExited", instancePrivate);
                MethodInfo cellExit = typeof(BattleForm).GetMethod("OnCellHoverExited", instancePrivate);
                MethodInfo outletExit = typeof(BattleForm).GetMethod("OnServingOutletDishHoverExited", instancePrivate);

                Assert.That(ownerType, Is.Not.Null);
                Assert.That(ownerField, Is.Not.Null);
                Assert.That(dishExit, Is.Not.Null);
                Assert.That(cellExit, Is.Not.Null);
                Assert.That(outletExit, Is.Not.Null);

                object servingOutletOwner = Enum.Parse(ownerType, "ServingOutlet");
                object noOwner = Enum.Parse(ownerType, "None");
                ownerField.SetValue(form, servingOutletOwner);

                dishExit.Invoke(form, new object[] { null });
                Assert.That(ownerField.GetValue(form), Is.EqualTo(servingOutletOwner));

                cellExit.Invoke(form, new object[] { null });
                Assert.That(ownerField.GetValue(form), Is.EqualTo(servingOutletOwner));

                outletExit.Invoke(form, null);
                Assert.That(ownerField.GetValue(form), Is.EqualTo(noOwner));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
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
