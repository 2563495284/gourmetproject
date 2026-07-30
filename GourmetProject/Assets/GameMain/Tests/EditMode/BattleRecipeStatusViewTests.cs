using GourmetProject.Game.UI.Meta;
using GourmetProject.Gameplay.Battle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BattleRecipeStatusViewTests
    {
        [TestCase(BattleRecipeEntryStatus.Normal, "正常")]
        [TestCase(BattleRecipeEntryStatus.CannotPlace, "不能放置")]
        [TestCase(BattleRecipeEntryStatus.WaitingForPlacement, "待摆放")]
        [TestCase(BattleRecipeEntryStatus.Served, "已上菜")]
        [TestCase(BattleRecipeEntryStatus.Discarded, "已丢弃")]
        [TestCase(BattleRecipeEntryStatus.Removed, "已移除")]
        public void Bind_WithBattleStatus_CreatesMatchingStatusBadge(
            BattleRecipeEntryStatus status,
            string expectedLabel)
        {
            var gameObject = new GameObject(
                "RecipeStatusViewTest",
                typeof(RectTransform),
                typeof(CanvasGroup));
            try
            {
                RecipeEditDishView view =
                    gameObject.AddComponent<RecipeEditDishView>();

                view.Bind(
                    "测试菜",
                    "X",
                    0,
                    0,
                    dragEnabled: false,
                    battleStatus: status);

                Transform badge =
                    gameObject.transform.Find("BattleStatusBadge");
                Assert.That(badge, Is.Not.Null);
                Assert.That(badge.gameObject.activeSelf, Is.True);
                Text label = badge.GetComponentInChildren<Text>(true);
                Assert.That(label, Is.Not.Null);
                Assert.That(label.text, Is.EqualTo(expectedLabel));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Bind_WithoutBattleStatus_PreservesNormalReadonlyVisual()
        {
            var gameObject = new GameObject(
                "RecipeStatusViewTest",
                typeof(RectTransform),
                typeof(CanvasGroup));
            try
            {
                RecipeEditDishView view =
                    gameObject.AddComponent<RecipeEditDishView>();

                view.Bind(
                    "测试菜",
                    "X",
                    0,
                    0,
                    dragEnabled: false);

                Assert.That(
                    gameObject.transform.Find("BattleStatusBadge"),
                    Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
