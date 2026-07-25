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

        [Test]
        public void ChoiceCard_HitSurfaceDispatchesHoverAndClick()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var canvasObject = new GameObject(
                "RewardDishPanelTestCanvas",
                typeof(RectTransform),
                typeof(Canvas));
            var eventSystemObject = new GameObject(
                "RewardDishPanelTestEventSystem",
                typeof(EventSystem));
            GameObject instance = null;
            try
            {
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                EventSystem eventSystem = eventSystemObject.GetComponent<EventSystem>();
                instance = Object.Instantiate(prefab, canvasObject.transform);

                RewardDishChoiceCardView card = instance
                    .transform
                    .Find("ChoiceContainer/RewardDishChoiceCardTemplate")
                    .GetComponent<RewardDishChoiceCardView>();
                card.gameObject.SetActive(true);
                Canvas.ForceUpdateCanvases();

                Image hitSurface = card.GetComponent<Image>();
                Button button = card.GetComponent<Button>();
                Assert.That(hitSurface, Is.Not.Null);
                Assert.That(hitSurface.raycastTarget, Is.True);
                Assert.That(button.targetGraphic, Is.SameAs(hitSurface));

                var pointer = new PointerEventData(eventSystem)
                {
                    button = PointerEventData.InputButton.Left,
                    position = RectTransformUtility.WorldToScreenPoint(
                        null,
                        ((RectTransform)card.transform).TransformPoint(
                            ((RectTransform)card.transform).rect.center)),
                };
                Assert.That(hitSurface.Raycast(pointer.position, null), Is.True);

                int hovered = 0;
                int clickedIndex = -1;
                var choice = new RewardChoice(
                    cfg.RewardKind.DishChoice,
                    "test_dish",
                    "测试菜品",
                    "测试描述");
                card.Bind(
                    choice,
                    null,
                    3,
                    (_, index) => clickedIndex = index,
                    _ => hovered++);

                ExecuteEvents.Execute(
                    card.gameObject,
                    pointer,
                    ExecuteEvents.pointerEnterHandler);
                ExecuteEvents.Execute(
                    card.gameObject,
                    pointer,
                    ExecuteEvents.pointerClickHandler);

                Assert.That(hovered, Is.EqualTo(1));
                Assert.That(clickedIndex, Is.EqualTo(3));
            }
            finally
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }

                Object.DestroyImmediate(eventSystemObject);
                Object.DestroyImmediate(canvasObject);
            }
        }
    }
}
