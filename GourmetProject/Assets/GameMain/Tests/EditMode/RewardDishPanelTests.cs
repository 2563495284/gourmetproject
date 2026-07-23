using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Meta;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class RewardDishPanelTests
    {
        private const string PrefabPath = "Assets/GameMain/UI/RewardDishPanel.prefab";

        [Test]
        public void Prefab_KeepsRequiredChoiceCardRefs()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            Assert.That(prefab, Is.Not.Null);

            RewardDishPackPanel panel = prefab.GetComponent<RewardDishPackPanel>();
            var serializedPanel = new SerializedObject(panel);
            Assert.That(serializedPanel.FindProperty("_choiceContainer").objectReferenceValue, Is.Not.Null);
            Assert.That(serializedPanel.FindProperty("_cardTemplate").objectReferenceValue, Is.Not.Null);
        }

        [Test]
        public void ChoiceCard_ClicksOnceAndDoesNotSupportDragging()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                RewardDishChoiceCardView card = instance
                    .transform
                    .Find("ChoiceContainer/RewardDishChoiceCardTemplate")
                    .GetComponent<RewardDishChoiceCardView>();
                card.gameObject.SetActive(true);

                int clickedIndex = -1;
                var choice = new RewardChoice(
                    cfg.RewardKind.DishChoice,
                    "test_dish",
                    "测试菜品",
                    "测试描述");
                card.Bind(choice, null, 2, (_, index) => clickedIndex = index);

                card.GetComponent<Button>().onClick.Invoke();
                Assert.That(clickedIndex, Is.EqualTo(2));
                Assert.That(card, Is.Not.InstanceOf<IBeginDragHandler>());
                Assert.That(card, Is.Not.InstanceOf<IDragHandler>());

                card.SetResolved(true);
                clickedIndex = -1;
                card.GetComponent<Button>().onClick.Invoke();
                Assert.That(clickedIndex, Is.EqualTo(-1));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
