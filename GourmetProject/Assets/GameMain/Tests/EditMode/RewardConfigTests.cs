using System.Collections.Generic;
using System.IO;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    /// <summary>
    /// 覆盖「统一奖励发放走配置」：reward_slot/reward_pool 新配置语义、RewardGranter.BuildConfigOffer
    /// 按配置产出 offer，以及 EffectResolver 奖励类效果统一入通用领奖队列（而非直接改 run）。
    /// </summary>
    public sealed class RewardConfigTests
    {
        private cfg.Tables _tables;
        private GameRun _run;

        [SetUp]
        public void SetUp()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name => JSON.Parse(File.ReadAllText(Path.Combine(dir, name + ".json"))));
            GameplayDatabase db = GameplayContentBuilder.BuildDatabase(_tables);
            string characterId = _tables.TbCharacter.DataList[0].Id;
            _run = new GameRun(_tables, db, characterId, "reward-config-test-seed", weekIndex: 1);
        }

        private static IRandomStream Rng(ulong seed = 0x9E3779B97F4A7C15UL)
        {
            return new Xoshiro256SS(seed);
        }

        // ---------- 一、配置语义 ----------

        [Test]
        public void RewardSlot_DishChoice3_HasExpectedShape()
        {
            cfg.RewardSlot slot = _tables.TbRewardSlot.Get("dish_choice_3");
            Assert.NotNull(slot, "缺少 dish_choice_3 槽组");
            Assert.AreEqual(cfg.RewardKind.DishChoice, slot.Kind);
            Assert.AreEqual(3, slot.ChoiceCount);
            Assert.AreEqual(1, slot.RequiredPickCount);
            Assert.AreEqual("pool_dish", slot.PoolId);
        }

        [Test]
        public void RewardSlot_FlavoredDish3_IsGetAll()
        {
            cfg.RewardSlot slot = _tables.TbRewardSlot.Get("flavored_dish_3");
            Assert.NotNull(slot);
            Assert.AreEqual(cfg.RewardKind.DishChoice, slot.Kind);
            Assert.AreEqual(3, slot.ChoiceCount);
            Assert.AreEqual(3, slot.RequiredPickCount, "风味菜品应为『随机得 N』，requiredPickCount==choiceCount");
        }

        [Test]
        public void RewardPool_ExplicitIds_MinQuality_WithRandomFlavor()
        {
            cfg.RewardPool flavored = _tables.TbRewardPool.Get("pool_flavored_dish");
            Assert.IsTrue(flavored.WithRandomFlavor, "pool_flavored_dish 应开启 withRandomFlavor");

            cfg.RewardPool legendary = _tables.TbRewardPool.Get("pool_legendary");
            Assert.AreEqual((int)cfg.ItemQuality.Legendary, legendary.MinQuality);

            cfg.RewardPool wood = _tables.TbRewardPool.Get("pool_lay_wood");
            Assert.IsTrue(wood.ExplicitIds.Contains("item_active_lay_cherry"));
            Assert.IsTrue(wood.ExplicitIds.Contains("item_active_lay_walnut"));
        }

        // ---------- 二、BuildConfigOffer 按配置产出 offer ----------

        [Test]
        public void BuildConfigOffer_DishChoice3_Produces3Choices_Pick1()
        {
            RewardOffer offer = RewardGranter.BuildConfigOffer(_run, Rng(), "dish_choice_3");
            Assert.NotNull(offer, "dish_choice_3 应产出 offer");
            Assert.AreEqual(3, offer.MainChoices.Count);
            Assert.AreEqual(1, offer.MainRequiredChoiceCount);
            foreach (RewardChoice choice in offer.MainChoices)
            {
                Assert.AreEqual(cfg.RewardKind.DishChoice, choice.Kind);
            }
        }

        [Test]
        public void BuildConfigOffer_FlavoredDish_AttachesFlavorId()
        {
            RewardOffer offer = RewardGranter.BuildConfigOffer(_run, Rng(), "flavored_dish_3");
            Assert.NotNull(offer);
            Assert.Greater(offer.MainChoices.Count, 0);
            foreach (RewardChoice choice in offer.MainChoices)
            {
                Assert.AreEqual(cfg.RewardKind.DishChoice, choice.Kind);
                Assert.IsFalse(string.IsNullOrEmpty(choice.FlavorId), "withRandomFlavor 池应给每个菜品附带风味 id");
            }
        }

        [Test]
        public void BuildConfigOffer_ExplicitIdsPool_OnlyPicksListedIds()
        {
            RewardOffer offer = RewardGranter.BuildConfigOffer(_run, Rng(), "lay_wood");
            Assert.NotNull(offer);
            Assert.AreEqual(1, offer.MainChoices.Count);
            var allowed = new HashSet<string> { "item_active_lay_cherry", "item_active_lay_walnut" };
            foreach (RewardChoice choice in offer.MainChoices)
            {
                Assert.AreEqual(cfg.RewardKind.ActiveItemGrant, choice.Kind);
                Assert.IsTrue(allowed.Contains(choice.Id), $"lay_wood 只能抽显式列表内的道具，实际={choice.Id}");
            }
        }

        [Test]
        public void BuildConfigOffer_UnknownGroup_ReturnsNull()
        {
            RewardOffer offer = RewardGranter.BuildConfigOffer(_run, Rng(), "no_such_group");
            Assert.IsNull(offer);
        }

        // ---------- 三、EffectResolver 奖励类效果统一入通用队列 ----------

        [Test]
        public void EffectResolver_RewardEffect_EnqueuesGenericReward_NotDirectGrant()
        {
            int goldBefore = _run.Gold;
            Assert.IsFalse(_run.HasPendingGenericRewards);

            EffectResolver.Apply(_run, cfg.EffectType.EnqueueDishChoice, 0, "dish_choice_3|测试奖励", Rng());

            Assert.IsTrue(_run.HasPendingGenericRewards, "奖励类效果应入通用领奖队列");
            Assert.AreEqual(goldBefore, _run.Gold, "成功建包时不应直接折金币");
            Assert.IsTrue(_run.TryPeekPendingGenericReward(out _, out string title, out RewardOffer offer));
            Assert.AreEqual("测试奖励", title);
            Assert.AreEqual(3, offer.MainChoices.Count);
        }

        [Test]
        public void EffectResolver_MissingConfig_FallsBackToGold()
        {
            int goldBefore = _run.Gold;

            EffectResolver.Apply(_run, cfg.EffectType.GainItem, 0, "no_such_group", Rng());

            Assert.IsFalse(_run.HasPendingGenericRewards, "配置缺失时不应入队");
            Assert.Greater(_run.Gold, goldBefore, "配置缺失应折金币兜底");
        }
    }
}
