using System.Collections.Generic;
using GourmetProject.Game.UI.Hud;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class RecipeCardViewTests
    {
        [Test]
        public void BattleCard_SeparatesInspectAndServeAndShowsOverflow()
        {
            RecipeCardView prefab = AssetDatabase.LoadAssetAtPath<RecipeCardView>(
                "Assets/GameMain/UI/Recipe/Recipe.prefab");
            RecipeCardView view = Object.Instantiate(prefab);
            try
            {
                int inspectCount = 0;
                int serveCount = 0;
                var dishes = new List<RecipeDishDisplayData>
                {
                    new RecipeDishDisplayData("一", true),
                    new RecipeDishDisplayData("二", true),
                    new RecipeDishDisplayData("三", false),
                    new RecipeDishDisplayData("四", true),
                    new RecipeDishDisplayData("五", true),
                    new RecipeDishDisplayData("六", false),
                    new RecipeDishDisplayData("七", true),
                };

                view.Bind(
                    "剩 7",
                    true,
                    () => inspectCount++,
                    showBattleContent: true,
                    serveInteractable: true,
                    onServe: () => serveCount++,
                    dishes: dishes);

                view.GetComponent<Button>().onClick.Invoke();
                Assert.That(inspectCount, Is.EqualTo(1));
                Assert.That(serveCount, Is.Zero);

                Button serveButton = view.transform.Find("ServeBell").GetComponent<Button>();
                serveButton.onClick.Invoke();
                Assert.That(inspectCount, Is.EqualTo(1));
                Assert.That(serveCount, Is.EqualTo(1));

                Text info = view.transform.Find("Info").GetComponent<Text>();
                Assert.That(info.text, Does.Contain("5"));
                Assert.That(info.text, Does.Contain("2"));
                Assert.That(view.transform.Find("BattleContent/Overflow").gameObject.activeSelf, Is.True);
                Assert.That(view.transform.Find("BattleContent/DishList").childCount, Is.EqualTo(6));
                Assert.That(
                    view.transform.Find("BattleContent/DishList/Dish_1").GetComponent<RecipeDishEntryView>(),
                    Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(view.gameObject);
            }
        }
    }
}
