using System.Collections.Generic;
using GourmetProject.Game.UI.Meta;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class WeekEventCardArtMappingTests
    {
        [Test]
        public void FoodActions_MapNormalAndHardRewardsToTenDistinctCardResources()
        {
            var expected = new Dictionary<(cfg.FoodActionKind, cfg.RewardKind), string>
            {
                {
                    (cfg.FoodActionKind.Normal, cfg.RewardKind.Gold),
                    "card_action_food_normal_gold"
                },
                {
                    (cfg.FoodActionKind.Normal, cfg.RewardKind.FragmentChoice),
                    "card_action_food_normal_fragment"
                },
                {
                    (cfg.FoodActionKind.Normal, cfg.RewardKind.PassiveItemChoice),
                    "card_action_food_normal_passive"
                },
                {
                    (cfg.FoodActionKind.Normal, cfg.RewardKind.ActiveItemStrengthen),
                    "card_action_food_normal_active_strengthen"
                },
                {
                    (cfg.FoodActionKind.Normal, cfg.RewardKind.ActiveItemAdjust),
                    "card_action_food_normal_active_adjust"
                },
                {
                    (cfg.FoodActionKind.Super, cfg.RewardKind.Gold),
                    "card_action_food_hard_gold"
                },
                {
                    (cfg.FoodActionKind.Super, cfg.RewardKind.FragmentChoice),
                    "card_action_food_hard_fragment"
                },
                {
                    (cfg.FoodActionKind.Super, cfg.RewardKind.PassiveItemChoice),
                    "card_action_food_hard_passive"
                },
                {
                    (cfg.FoodActionKind.Super, cfg.RewardKind.ActiveItemStrengthen),
                    "card_action_food_hard_active_strengthen"
                },
                {
                    (cfg.FoodActionKind.Super, cfg.RewardKind.ActiveItemAdjust),
                    "card_action_food_hard_active_adjust"
                },
            };
            var actualNames = new HashSet<string>();

            foreach (KeyValuePair<(cfg.FoodActionKind, cfg.RewardKind), string> mapping in expected)
            {
                string actual = WeekEventCardView.FoodRewardSpriteName(
                    mapping.Key.Item1,
                    mapping.Key.Item2);

                Assert.That(actual, Is.EqualTo(mapping.Value));
                Assert.That(actualNames.Add(actual), Is.True, $"{mapping.Key} 与其他行动复用了同一张卡面");

                Sprite sprite = Resources.Load<Sprite>($"Sprites/UI/{actual}");
                Assert.That(sprite, Is.Not.Null, $"{mapping.Key} 对应卡面资源未能加载：{actual}");
                Assert.That(sprite.rect.width, Is.EqualTo(1024f), $"{actual} 宽度不是 1024");
                Assert.That(sprite.rect.height, Is.EqualTo(1536f), $"{actual} 高度不是 1536");
            }

            Assert.That(actualNames.Count, Is.EqualTo(10));
        }

        [Test]
        public void FoodActions_MapNormalAndSuperRewardsToTenDistinctBadgeResources()
        {
            var expected = new Dictionary<(cfg.FoodActionKind, cfg.RewardKind), string>
            {
                {
                    (cfg.FoodActionKind.Normal, cfg.RewardKind.Gold),
                    "reward_badge_gold"
                },
                {
                    (cfg.FoodActionKind.Normal, cfg.RewardKind.FragmentChoice),
                    "reward_badge_table_cell"
                },
                {
                    (cfg.FoodActionKind.Normal, cfg.RewardKind.PassiveItemChoice),
                    "reward_badge_passive_item"
                },
                {
                    (cfg.FoodActionKind.Normal, cfg.RewardKind.ActiveItemStrengthen),
                    "reward_badge_active_strengthen"
                },
                {
                    (cfg.FoodActionKind.Normal, cfg.RewardKind.ActiveItemAdjust),
                    "reward_badge_active_adjust"
                },
                {
                    (cfg.FoodActionKind.Super, cfg.RewardKind.Gold),
                    "reward_badge_gold_large"
                },
                {
                    (cfg.FoodActionKind.Super, cfg.RewardKind.FragmentChoice),
                    "reward_badge_table_cell_large"
                },
                {
                    (cfg.FoodActionKind.Super, cfg.RewardKind.PassiveItemChoice),
                    "reward_badge_passive_item_4"
                },
                {
                    (cfg.FoodActionKind.Super, cfg.RewardKind.ActiveItemStrengthen),
                    "reward_badge_active_strengthen_4"
                },
                {
                    (cfg.FoodActionKind.Super, cfg.RewardKind.ActiveItemAdjust),
                    "reward_badge_active_adjust_4"
                },
            };
            var actualNames = new HashSet<string>();

            foreach (KeyValuePair<(cfg.FoodActionKind, cfg.RewardKind), string> mapping in expected)
            {
                string actual = WeekEventCardView.RewardIconSpriteName(
                    mapping.Key.Item1,
                    mapping.Key.Item2);

                Assert.That(actual, Is.EqualTo(mapping.Value));
                Assert.That(actualNames.Add(actual), Is.True, $"{mapping.Key} 与其他行动复用了同一张奖励徽章");

                Sprite sprite = Resources.Load<Sprite>($"Sprites/UI/{actual}");
                Assert.That(sprite, Is.Not.Null, $"{mapping.Key} 对应奖励徽章未能加载：{actual}");
            }

            Assert.That(actualNames.Count, Is.EqualTo(10));
        }
    }
}
