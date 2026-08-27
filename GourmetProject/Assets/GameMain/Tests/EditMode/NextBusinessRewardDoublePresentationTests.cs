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
        public void TitleBadge_IsContainedNonBlockingAndUsesFixedCopy()
        {
            GameObject instance = InstantiateCard();

            try
            {
                WeekEventCardView view = instance.GetComponent<WeekEventCardView>();
                RectTransform badgeRoot = FindRect(instance, "RewardDoubleTitleBadgeRoot");
                RectTransform titleBacking = FindRect(instance, "TitleBacking");
                GameObject titleRuleRight = FindObject(instance, "TitleRuleRight");

                Assert.That(badgeRoot.gameObject.activeSelf, Is.False);
                Assert.That(titleRuleRight.activeSelf, Is.True);
                view.SetNextBusinessRewardDoubleTicket(true);
                Canvas.ForceUpdateCanvases();

                Assert.That(badgeRoot.gameObject.activeSelf, Is.True);
                Assert.That(titleRuleRight.activeSelf, Is.True);
                Assert.That(badgeRoot.parent, Is.EqualTo(titleBacking));
                Assert.That(badgeRoot.anchorMin, Is.EqualTo(new Vector2(1f, 0.5f)));
                Assert.That(badgeRoot.anchorMax, Is.EqualTo(new Vector2(1f, 0.5f)));
                Assert.That(badgeRoot.anchoredPosition, Is.EqualTo(new Vector2(-32f, 0f)));
                Assert.That(badgeRoot.rect.size, Is.EqualTo(new Vector2(64f, 64f)));
                Assert.That(badgeRoot.localRotation, Is.EqualTo(Quaternion.identity));

                TMP_Text badgeText = FindText(instance, "RewardDoubleTitleBadgeText");
                badgeText.ForceMeshUpdate(
                    ignoreActiveState: true,
                    forceTextReparsing: true);
                Assert.That(badgeText.text, Is.EqualTo(WeekEventCardView.RewardDoubleTicketText));
                Assert.That(badgeText.text, Is.EqualTo("待触发"));
                Assert.That(badgeText.isTextOverflowing, Is.False);
                Assert.That(
                    badgeText.preferredWidth,
                    Is.LessThanOrEqualTo(badgeText.rectTransform.rect.width));
                Assert.That(
                    badgeText.preferredHeight,
                    Is.LessThanOrEqualTo(badgeText.rectTransform.rect.height));

                Image badgeIcon = FindImage(instance, "RewardDoubleTitleBadgeIcon");
                Assert.That(badgeIcon.sprite, Is.Not.Null);
                Assert.That(badgeIcon.preserveAspect, Is.True);
                Assert.That(badgeIcon.rectTransform.rect.size, Is.EqualTo(new Vector2(52f, 52f)));

                Image badgeBacking = FindImage(instance, "RewardDoubleTitleBadgeBacking");
                Assert.That(badgeBacking.sprite, Is.Not.Null);
                Assert.That(badgeBacking.sprite.name, Does.StartWith("recipe_book_title_tab"));
                Assert.That(
                    badgeRoot.GetComponentsInChildren<Graphic>(true).All(graphic => !graphic.raycastTarget),
                    Is.True);

                Assert.That(
                    RectEdgeInLocalSpace(titleBacking, badgeRoot, right: true),
                    Is.LessThanOrEqualTo(titleBacking.rect.xMax + 0.01f));
                Assert.That(
                    RectEdgeInLocalSpace(titleBacking, badgeRoot, right: false),
                    Is.GreaterThanOrEqualTo(titleBacking.rect.xMin - 0.01f));
                Assert.That(
                    RectVerticalEdgeInLocalSpace(titleBacking, badgeRoot, top: true),
                    Is.LessThanOrEqualTo(titleBacking.rect.yMax + 0.01f));
                Assert.That(
                    RectVerticalEdgeInLocalSpace(titleBacking, badgeRoot, top: false),
                    Is.GreaterThanOrEqualTo(titleBacking.rect.yMin - 0.01f));

                view.BindEventOption("测试事件", onPick: null);
                Assert.That(badgeRoot.gameObject.activeSelf, Is.False);
                Assert.That(titleRuleRight.activeSelf, Is.True);
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

        private static GameObject FindObject(GameObject root, string name)
        {
            return root
                .GetComponentsInChildren<Transform>(true)
                .Single(transform => transform.gameObject.name == name)
                .gameObject;
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
