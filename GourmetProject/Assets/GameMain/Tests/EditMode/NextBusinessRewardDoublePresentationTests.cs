#if UNITY_EDITOR
using System.IO;
using System.Linq;
using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Game.UI.Meta;
using Luban.SimpleJSON;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class NextBusinessRewardDoublePresentationTests
    {
        private cfg.Tables _tables;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            string directory = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name =>
                JSON.Parse(File.ReadAllText(Path.Combine(directory, name + ".json"))));
        }

        [TestCase("act_food_gold", true)]
        [TestCase("act_food_hard_gold", true)]
        [TestCase("act_boss", false)]
        [TestCase("act_event", false)]
        [TestCase("act_reward", false)]
        [TestCase("act_shop", false)]
        public void TicketEligibility_MatchesRewardSettlementRules(
            string actionId,
            bool expectedEligible)
        {
            cfg.GameAction action = _tables.TbAction.GetOrDefault(actionId);
            Assert.That(action, Is.Not.Null, actionId);

            Assert.That(
                RewardGranter.IsNextBusinessRewardDoubleEligible(_tables, action),
                Is.EqualTo(expectedEligible));
            Assert.That(
                ActionCardDeck.ShouldShowNextBusinessRewardDoubleTicket(
                    _tables,
                    action,
                    buffActive: true),
                Is.EqualTo(expectedEligible));
            Assert.That(
                ActionCardDeck.ShouldShowNextBusinessRewardDoubleTicket(
                    _tables,
                    action,
                    buffActive: false),
                Is.False);
        }

        [Test]
        public void TicketPrefab_IsExternalNonBlockingAndUsesFixedCopy()
        {
            GameObject instance = InstantiateCard();

            try
            {
                WeekEventCardView view = instance.GetComponent<WeekEventCardView>();
                RectTransform cardRect = (RectTransform)instance.transform;
                RectTransform ticketRoot = FindRect(instance, "RewardDoubleTicketRoot");
                RectTransform titleBacking = FindRect(instance, "TitleBacking");

                Assert.That(ticketRoot.gameObject.activeSelf, Is.False);
                view.SetNextBusinessRewardDoubleTicket(true);
                Canvas.ForceUpdateCanvases();

                Assert.That(ticketRoot.gameObject.activeSelf, Is.True);
                Assert.That(ticketRoot.anchorMin, Is.EqualTo(new Vector2(1f, 0.78f)));
                Assert.That(ticketRoot.anchorMax, Is.EqualTo(new Vector2(1f, 0.78f)));
                Assert.That(ticketRoot.anchoredPosition.x, Is.EqualTo(12f).Within(0.01f));
                Assert.That(ticketRoot.rect.size, Is.EqualTo(new Vector2(156f, 64f)));

                TMP_Text ticketText = FindText(instance, "RewardDoubleTicketText");
                ticketText.ForceMeshUpdate(
                    ignoreActiveState: true,
                    forceTextReparsing: true);
                Assert.That(ticketText.text, Is.EqualTo(WeekEventCardView.RewardDoubleTicketText));
                Assert.That(ticketText.text, Does.Not.Contain("余"));
                Assert.That(ticketText.isTextOverflowing, Is.False);
                Assert.That(
                    ticketText.preferredWidth,
                    Is.LessThanOrEqualTo(ticketText.rectTransform.rect.width));
                Assert.That(
                    ticketText.preferredHeight,
                    Is.LessThanOrEqualTo(ticketText.rectTransform.rect.height));

                Image ticketIcon = FindImage(instance, "RewardDoubleTicketIcon");
                Assert.That(ticketIcon.sprite, Is.Not.Null);
                Assert.That(ticketIcon.preserveAspect, Is.True);
                Assert.That(
                    ticketRoot.GetComponentsInChildren<Graphic>(true).All(graphic => !graphic.raycastTarget),
                    Is.True);

                float ticketRight = RectEdgeInLocalSpace(cardRect, ticketRoot, right: true);
                float ticketTop = RectVerticalEdgeInLocalSpace(cardRect, ticketRoot, top: true);
                float titleBottom = RectVerticalEdgeInLocalSpace(cardRect, titleBacking, top: false);
                Assert.That(
                    ticketRight,
                    Is.EqualTo(cardRect.rect.xMax + 12f).Within(0.01f));
                Assert.That(ticketTop, Is.LessThan(titleBottom));

                view.BindEventOption("测试事件", onPick: null);
                Assert.That(ticketRoot.gameObject.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static GameObject InstantiateCard()
        {
            const string prefabPath =
                "Assets/GameMain/Content/Prefabs/UI/Meta/Events/WeekEventCardView.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = Object.Instantiate(prefab);
            instance.transform.localScale = Vector3.one;
            Canvas canvas = instance.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.ForceUpdateCanvases();
            return instance;
        }

        private static RectTransform FindRect(GameObject root, string name)
        {
            return root
                .GetComponentsInChildren<RectTransform>(true)
                .Single(rect => rect.gameObject.name == name);
        }

        private static TMP_Text FindText(GameObject root, string name)
        {
            return root
                .GetComponentsInChildren<TMP_Text>(true)
                .Single(text => text.gameObject.name == name);
        }

        private static Image FindImage(GameObject root, string name)
        {
            return root
                .GetComponentsInChildren<Image>(true)
                .Single(image => image.gameObject.name == name);
        }

        private static float RectEdgeInLocalSpace(
            RectTransform container,
            RectTransform child,
            bool right)
        {
            var corners = new Vector3[4];
            child.GetWorldCorners(corners);
            Vector3 world = right ? corners[2] : corners[0];
            return container.InverseTransformPoint(world).x;
        }

        private static float RectVerticalEdgeInLocalSpace(
            RectTransform container,
            RectTransform child,
            bool top)
        {
            var corners = new Vector3[4];
            child.GetWorldCorners(corners);
            Vector3 world = top ? corners[1] : corners[0];
            return container.InverseTransformPoint(world).y;
        }
    }
}
#endif
