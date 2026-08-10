using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class RewardActiveItemCapacityTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            string configDirectory = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name =>
                JSON.Parse(File.ReadAllText(Path.Combine(configDirectory, name + ".json"))));
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void TryClaimChoice_FullActiveSlotsRejectsWithoutGoldConversion()
        {
            var run = new GameRun(_tables, _database, "glutton_dog", "reward-capacity-test", 1);
            cfg.ActiveItem activeItem = _tables.TbActiveItem.DataList[0];

            while (run.HasFreeActiveSlot)
            {
                ItemAcquireResult result = run.AcquireItem(activeItem.Id, fallbackGold: 0, fireOnAcquire: false);
                Assert.That(result.Outcome, Is.EqualTo(ItemAcquireOutcome.Stacked));
            }

            int countBefore = run.ActiveItemCount;
            int goldBefore = run.Gold;
            var choice = new RewardChoice(
                cfg.RewardKind.ActiveItemGrant,
                activeItem.Id,
                activeItem.Name,
                activeItem.Desc,
                goldAmount: 40);

            bool claimed = RewardGranter.TryClaimChoice(run, choice, out string rewardText);

            Assert.That(claimed, Is.False);
            Assert.That(rewardText, Is.Empty);
            Assert.That(run.ActiveItemCount, Is.EqualTo(countBefore));
            Assert.That(run.Gold, Is.EqualTo(goldBefore));
        }

        [Test]
        public void ItemChoicePanel_RejectedPickStaysUnclaimedAndStartsFailureShake()
        {
            var panelObject = new GameObject("RewardItemChoicePanel", typeof(RectTransform));
            var cardObject = new GameObject("RewardItemChoiceCard", typeof(RectTransform));
            RewardItemChoicePanel panel = panelObject.AddComponent<RewardItemChoicePanel>();
            RewardItemChoiceCardView card = cardObject.AddComponent<RewardItemChoiceCardView>();

            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var cards = (List<RewardItemChoiceCardView>)typeof(RewardItemChoicePanel)
                    .GetField("_cards", flags)
                    ?.GetValue(panel);
                cards?.Add(card);
                typeof(RewardItemChoicePanel)
                    .GetField("_onPick", flags)
                    ?.SetValue(panel, new Func<RewardItemChoiceCardView, int, bool>((_, _) => false));

                typeof(RewardItemChoicePanel)
                    .GetMethod("OnCardClicked", flags)
                    ?.Invoke(panel, new object[] { 0 });

                var claimed = (HashSet<int>)typeof(RewardItemChoicePanel)
                    .GetField("_claimedOnPage", flags)
                    ?.GetValue(panel);
                object failureTween = typeof(RewardItemChoiceCardView)
                    .GetField("_failureTween", flags)
                    ?.GetValue(card);

                Assert.That(claimed, Is.Empty);
                Assert.That(failureTween, Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cardObject);
                UnityEngine.Object.DestroyImmediate(panelObject);
            }
        }
    }
}
