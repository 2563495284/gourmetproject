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
        public void RecipeCard_ShowsOnlyCountAndOpensInspect()
        {
            RecipeCardView prefab = AssetDatabase.LoadAssetAtPath<RecipeCardView>(
                "Assets/GameMain/Content/Prefabs/UI/Recipe/Recipe.prefab");
            RecipeCardView view = Object.Instantiate(prefab);
            try
            {
                int inspectCount = 0;
                view.Bind(
                    "7",
                    true,
                    () => inspectCount++);

                view.GetComponent<Button>().onClick.Invoke();
                Assert.That(inspectCount, Is.EqualTo(1));

                Text info = view.transform.Find("Info").GetComponent<Text>();
                Assert.That(info.text, Is.EqualTo("7"));
                Assert.That(view.transform.Find("ServeBell"), Is.Null);
                Assert.That(view.transform.Find("BattleContent"), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(view.gameObject);
            }
        }
    }
}
