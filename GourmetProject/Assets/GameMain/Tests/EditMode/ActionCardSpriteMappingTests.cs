using System.Collections.Generic;
using GourmetProject.Game.UI.Meta;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ActionCardSpriteMappingTests
    {
        private static readonly object[][] ConfiguredActions =
        {
            Entry("act_food_gold", cfg.ActionBehavior.Food, "card_action_food_normal_gold"),
            Entry("act_food_fragment", cfg.ActionBehavior.Food, "card_action_food_normal_fragment"),
            Entry("act_food_passive", cfg.ActionBehavior.Food, "card_action_food_normal_passive"),
            Entry("act_food_active_strengthen", cfg.ActionBehavior.Food, "card_action_food_normal_active_strengthen"),
            Entry("act_food_active_adjust", cfg.ActionBehavior.Food, "card_action_food_normal_active_adjust"),
            Entry("act_food_hard_gold", cfg.ActionBehavior.Food, "card_action_food_hard_gold"),
            Entry("act_food_hard_fragment", cfg.ActionBehavior.Food, "card_action_food_hard_fragment"),
            Entry("act_food_hard_passive", cfg.ActionBehavior.Food, "card_action_food_hard_passive"),
            Entry("act_food_hard_active_strengthen", cfg.ActionBehavior.Food, "card_action_food_hard_active_strengthen"),
            Entry("act_food_hard_active_adjust", cfg.ActionBehavior.Food, "card_action_food_hard_active_adjust"),
            Entry("act_event", cfg.ActionBehavior.Event, "card_action_event"),
            Entry("act_reward", cfg.ActionBehavior.Reward, "card_action_reward"),
            Entry("act_shop", cfg.ActionBehavior.Shop, "card_action_shop"),
            Entry("act_interest", cfg.ActionBehavior.Interest, "card_node_interest"),
            Entry("act_boss", cfg.ActionBehavior.Food, "card_node_boss"),
            Entry("act_slot", cfg.ActionBehavior.Slot, "card_action_slot"),
            Entry("act_loan_repay", cfg.ActionBehavior.Effect, "card_action_loan_repay"),
            Entry("act_gold_clear", cfg.ActionBehavior.Effect, "card_action_gold_clear"),
            Entry("act_restore_heart", cfg.ActionBehavior.Effect, "card_action_restore_heart"),
        };

        private static readonly HashSet<string> CompatibilityFallbacks = new HashSet<string>
        {
            "card_action_food_gold",
            "card_action_food_fragment",
            "card_action_food_passive",
            "card_action_food_active",
            "card_action_food_dish",
            "card_action_negative",
        };

        [TestCaseSource(nameof(ConfiguredActions))]
        public void ConfiguredAction_UsesSameDedicatedSprite_InEveryCardEntry(
            string actionId,
            cfg.ActionBehavior behavior,
            string expectedSprite)
        {
            cfg.GameAction action = Action(actionId, behavior);

            Assert.That(
                WeekEventCardView.ConfiguredActionSpriteNameFor(actionId),
                Is.EqualTo(expectedSprite));
            Assert.That(WeekEventCardView.CardSpriteNameFor(action), Is.EqualTo(expectedSprite));
            Assert.That(WeekEventCardView.NodeCardSpriteNameFor(action), Is.EqualTo(expectedSprite));
            Assert.That(CompatibilityFallbacks, Does.Not.Contain(expectedSprite));
        }

        [Test]
        public void ConfiguredActions_HaveNineteenUniqueLoadableSprites()
        {
            var spriteNames = new HashSet<string>();
            foreach (object[] entry in ConfiguredActions)
            {
                string actionId = (string)entry[0];
                string spriteName = (string)entry[2];

                Assert.That(spriteNames.Add(spriteName), Is.True, $"Duplicate sprite for {actionId}: {spriteName}");
                Assert.That(
                    Resources.Load<Sprite>($"Sprites/UI/{spriteName}"),
                    Is.Not.Null,
                    $"Missing Resources sprite for {actionId}: {spriteName}");
            }

            Assert.That(spriteNames.Count, Is.EqualTo(19));
        }

        [TestCase(cfg.FoodActionKind.Normal, cfg.RewardKind.Gold, "card_action_food_normal_gold")]
        [TestCase(cfg.FoodActionKind.Normal, cfg.RewardKind.FragmentChoice, "card_action_food_normal_fragment")]
        [TestCase(cfg.FoodActionKind.Normal, cfg.RewardKind.PassiveItemChoice, "card_action_food_normal_passive")]
        [TestCase(cfg.FoodActionKind.Normal, cfg.RewardKind.ActiveItemStrengthen, "card_action_food_normal_active_strengthen")]
        [TestCase(cfg.FoodActionKind.Normal, cfg.RewardKind.ActiveItemAdjust, "card_action_food_normal_active_adjust")]
        [TestCase(cfg.FoodActionKind.Super, cfg.RewardKind.Gold, "card_action_food_hard_gold")]
        [TestCase(cfg.FoodActionKind.Super, cfg.RewardKind.FragmentChoice, "card_action_food_hard_fragment")]
        [TestCase(cfg.FoodActionKind.Super, cfg.RewardKind.PassiveItemChoice, "card_action_food_hard_passive")]
        [TestCase(cfg.FoodActionKind.Super, cfg.RewardKind.ActiveItemStrengthen, "card_action_food_hard_active_strengthen")]
        [TestCase(cfg.FoodActionKind.Super, cfg.RewardKind.ActiveItemAdjust, "card_action_food_hard_active_adjust")]
        public void FoodRewardCombination_UsesDedicatedBusinessSprite(
            cfg.FoodActionKind actionKind,
            cfg.RewardKind rewardKind,
            string expectedSprite)
        {
            Assert.That(
                WeekEventCardView.FoodRewardSpriteName(actionKind, rewardKind),
                Is.EqualTo(expectedSprite));
        }

        [Test]
        public void UnknownNegativeAction_UsesCompatibilityFallback()
        {
            cfg.GameAction action = Action("act_unknown_negative", cfg.ActionBehavior.Negative);

            Assert.That(WeekEventCardView.CardSpriteNameFor(action), Is.EqualTo("card_action_negative"));
            Assert.That(WeekEventCardView.NodeCardSpriteNameFor(action), Is.EqualTo("card_action_negative"));
        }

        private static object[] Entry(string id, cfg.ActionBehavior behavior, string spriteName)
        {
            return new object[] { id, behavior, spriteName };
        }

        private static cfg.GameAction Action(string id, cfg.ActionBehavior behavior)
        {
            string json = $@"{{
                ""id"":""{id}"",
                ""name"":""Test action"",
                ""desc"":"""",
                ""behavior"":{(int)behavior},
                ""foodId"":"""",
                ""effectType"":0,
                ""effectValue"":0,
                ""effectParam"":"""",
                ""minCostDays"":0,
                ""maxCostDays"":0,
                ""rewardTitle"":"""",
                ""rewardDesc"":""""
            }}";
            return new cfg.GameAction(JSON.Parse(json));
        }
    }
}
