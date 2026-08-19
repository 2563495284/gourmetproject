using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    /// <summary>
    /// 「奖励翻倍单」契约：下一场符合条件的营业从基础食物、基础金币、特定奖励中
    /// 等概率选一类，独立重抽并额外发放完整一份。
    /// </summary>
    public sealed class NextBusinessRewardDoubleTests
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
        public void Stack_AddConsumeSaveRestoreAndLegacyMigration_PreserveExactCount()
        {
            GameRun run = CreateRun();

            Assert.That(run.NextBusinessRewardDoubleStacks, Is.Zero);
            Assert.That(run.TryConsumeNextBusinessRewardDoubleStack(), Is.False);

            run.AddNextBusinessRewardDoubleStack();
            run.AddNextBusinessRewardDoubleStack();
            Assert.That(run.NextBusinessRewardDoubleStacks, Is.EqualTo(2));
            Assert.That(run.TryConsumeNextBusinessRewardDoubleStack(), Is.True);

            RunSaveData save = run.ToSaveData();
            Assert.That(save.NextBusinessRewardDoubleStacks, Is.EqualTo(1));
            Assert.That(save.NextBusinessSpecificRewardDoubleStacks, Is.Zero,
                "旧实验字段只读迁移，不能继续写入。");

            GameRun restored = GameRun.FromSaveData(_tables, _database, save);
            Assert.That(restored.NextBusinessRewardDoubleStacks, Is.EqualTo(1));

            RunSaveData legacySave = CreateRun().ToSaveData();
            legacySave.NextBusinessRewardDoubleStacks = 0;
            legacySave.NextBusinessSpecificRewardDoubleStacks = 2;
            GameRun migrated = GameRun.FromSaveData(_tables, _database, legacySave);
            Assert.That(migrated.NextBusinessRewardDoubleStacks, Is.EqualTo(2));
            Assert.That(migrated.ToSaveData().NextBusinessSpecificRewardDoubleStacks, Is.Zero);
        }

        [TestCase(0, RewardDoubleTarget.BaseDish)]
        [TestCase(1, RewardDoubleTarget.BaseGold)]
        [TestCase(2, RewardDoubleTarget.Specific)]
        public void GenerateOffer_UniformThreeWayRoll_MapsEachOutcomeToOneRewardCategory(
            int forcedThreeWayIndex,
            RewardDoubleTarget expectedTarget)
        {
            const string actionId = "act_food_active_strengthen";
            cfg.GameAction action = _tables.TbAction.Get(actionId);
            RewardOffer baseline = GenerateOffer(action, stacks: 0, forcedThreeWayIndex, out GameRun baselineRun);
            RewardOffer doubled = GenerateOffer(action, stacks: 1, forcedThreeWayIndex, out GameRun doubledRun);

            Assert.That(doubled.DoubleRewardTarget, Is.EqualTo(expectedTarget));
            Assert.That(doubledRun.NextBusinessRewardDoubleStacks, Is.Zero);
            AssertOriginalGroupsUnchanged(baseline, doubled);

            switch (expectedTarget)
            {
                case RewardDoubleTarget.BaseDish:
                    Assert.That(doubled.HasBonusGold, Is.False);
                    Assert.That(doubled.FixedGroups, Has.Count.EqualTo(baseline.FixedGroups.Count + 1));
                    AssertRerolledGroupMatches(doubled.FixedGroups.Last(), baseline.MainGroup);
                    break;
                case RewardDoubleTarget.BaseGold:
                    Assert.That(doubled.HasBonusGold, Is.True);
                    Assert.That(doubled.GoldAmountsResolved, Is.True);
                    Assert.That(doubled.FixedGroups, Has.Count.EqualTo(baseline.FixedGroups.Count));
                    break;
                case RewardDoubleTarget.Specific:
                    Assert.That(doubled.HasBonusGold, Is.False);
                    Assert.That(doubled.FixedGroups, Has.Count.EqualTo(baseline.FixedGroups.Count + 1));
                    AssertRerolledGroupMatches(doubled.FixedGroups.Last(), baseline.SpecificGroup);
                    break;
            }
        }

        [TestCase("act_food_gold", cfg.FoodActionKind.Normal)]
        [TestCase("act_food_fragment", cfg.FoodActionKind.Normal)]
        [TestCase("act_food_passive", cfg.FoodActionKind.Normal)]
        [TestCase("act_food_active_strengthen", cfg.FoodActionKind.Normal)]
        [TestCase("act_food_active_adjust", cfg.FoodActionKind.Normal)]
        [TestCase("act_food_hard_gold", cfg.FoodActionKind.Super)]
        [TestCase("act_food_hard_fragment", cfg.FoodActionKind.Super)]
        [TestCase("act_food_hard_passive", cfg.FoodActionKind.Super)]
        [TestCase("act_food_hard_active_strengthen", cfg.FoodActionKind.Super)]
        [TestCase("act_food_hard_active_adjust", cfg.FoodActionKind.Super)]
        public void GenerateOffer_SpecificTarget_RerollsCompleteNormalOrSuperSpecificGroup(
            string actionId,
            cfg.FoodActionKind expectedKind)
        {
            cfg.GameAction action = _tables.TbAction.Get(actionId);
            Assert.That(FoodService.Resolve(_tables, action)?.ActionKind, Is.EqualTo(expectedKind));

            RewardOffer baseline = GenerateOffer(action, stacks: 0, forcedThreeWayIndex: 2, out _);
            RewardOffer doubled = GenerateOffer(action, stacks: 1, forcedThreeWayIndex: 2, out GameRun run);

            Assert.That(doubled.DoubleRewardTarget, Is.EqualTo(RewardDoubleTarget.Specific));
            Assert.That(doubled.RawBaseGold, Is.EqualTo(baseline.RawBaseGold),
                "新增随机只能发生在原奖励完整生成之后。");
            AssertOriginalGroupsUnchanged(baseline, doubled);
            Assert.That(doubled.FixedGroups, Has.Count.EqualTo(baseline.FixedGroups.Count + 1));
            AssertRerolledGroupMatches(doubled.FixedGroups.Last(), doubled.SpecificGroup);
            Assert.That(run.NextBusinessRewardDoubleStacks, Is.Zero);
        }

        [Test]
        public void GenerateOffer_BaseGoldTarget_LocksBothAmountsWithOneSharedMultiplierAndOneFixedBonus()
        {
            cfg.GameAction action = _tables.TbAction.Get("act_food_gold");
            GameRun run = CreateRun();
            ActionExecutionContext context = CreateDailyContext(action);
            run.SetLastActionContext(context);
            run.AddNextBusinessRewardDoubleStack();
            run.AddBusinessGoldPct(0.5f, nextBusiness: true);
            run.AddNextMealRewardGold(7);

            var rng = new ForcedThreeWayRandomStream(112233UL, forcedIndex: 1);
            RewardOffer offer = RewardGranter.GenerateOffer(run, run.CurrentWeek, rng, context);

            Assert.That(offer.DoubleRewardTarget, Is.EqualTo(RewardDoubleTarget.BaseGold));
            Assert.That(offer.GoldAmountsResolved, Is.True);
            Assert.That(offer.BaseGold, Is.EqualTo(RoundGold(offer.RawBaseGold, 1.5f) + 7));
            Assert.That(offer.BonusGold, Is.EqualTo(RoundGold(offer.RawBonusGold, 1.5f)));
            Assert.That(run.ConsumeNextBusinessGoldMultiplier(), Is.EqualTo(1f),
                "一次性倍率应在生成两条金币时只消费一次。");
            Assert.That(run.ConsumeNextMealRewardGold(), Is.Zero,
                "固定金币只能进入原基础金币。");

            int goldBefore = run.Gold;
            RewardGranter.ApplyBonusGold(run, offer);
            Assert.That(run.Gold, Is.EqualTo(goldBefore + offer.BonusGold));
            Assert.That(offer.BonusGoldClaimed, Is.True);
            Assert.That(offer.BaseGoldClaimed, Is.False);

            RewardGranter.ApplyBonusGold(run, offer);
            Assert.That(run.Gold, Is.EqualTo(goldBefore + offer.BonusGold),
                "额外金币不能重复领取。");

            RewardGranter.ApplyBaseGold(run, offer);
            Assert.That(run.Gold, Is.EqualTo(goldBefore + offer.BonusGold + offer.BaseGold));
            Assert.That(offer.BaseGoldClaimed, Is.True);
        }

        [Test]
        public void PendingOffer_MidClaimSaveRestore_KeepsTargetAmountsCandidatesAndClaimStates()
        {
            cfg.GameAction action = _tables.TbAction.Get("act_food_hard_active_strengthen");
            GameRun run = CreateRun();
            ActionExecutionContext context = CreateDailyContext(action);
            run.SetLastActionContext(context);
            run.AddNextBusinessRewardDoubleStack();
            RewardOffer offer = RewardGranter.GenerateOffer(
                run,
                run.CurrentWeek,
                new ForcedThreeWayRandomStream(445566UL, forcedIndex: 1),
                context);

            RewardGranter.ApplyBaseGold(run, offer);
            const string rewardKey = "reward-double-save-restore";
            run.SetPendingRewardOffer(rewardKey, offer);

            GameRun restoredRun = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            RewardOffer restored = restoredRun.GetPendingRewardOffer(rewardKey);

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.DoubleRewardTarget, Is.EqualTo(RewardDoubleTarget.BaseGold));
            Assert.That(restored.RawBaseGold, Is.EqualTo(offer.RawBaseGold));
            Assert.That(restored.RawBonusGold, Is.EqualTo(offer.RawBonusGold));
            Assert.That(restored.BaseGold, Is.EqualTo(offer.BaseGold));
            Assert.That(restored.BonusGold, Is.EqualTo(offer.BonusGold));
            Assert.That(restored.GoldAmountsResolved, Is.True);
            Assert.That(restored.BaseGoldClaimed, Is.True);
            Assert.That(restored.BonusGoldClaimed, Is.False);
            AssertGroupsEqual(offer.FixedGroups, restored.FixedGroups);
            AssertGroupEqual(offer.SpecificGroup, restored.SpecificGroup);

            int goldBeforeBonus = restoredRun.Gold;
            RewardGranter.ApplyBonusGold(restoredRun, restored);
            Assert.That(restoredRun.Gold, Is.EqualTo(goldBeforeBonus + restored.BonusGold));
        }

        [Test]
        public void GenerateOffer_TimelineSource_DoesNotDoubleOrConsume()
        {
            cfg.GameAction action = _tables.TbAction.Get("act_food_gold");
            var timelineContext = new ActionExecutionContext(action)
            {
                SourceKey = "timeline_reward_node_test",
            };

            AssertOfferIsNotDoubledAndStackRemains(action, timelineContext);
        }

        [Test]
        public void GenerateOffer_Boss_DoesNotDoubleOrConsume()
        {
            cfg.GameAction action = _tables.TbAction.Get("act_boss");
            Assert.That(FoodService.Resolve(_tables, action)?.ActionKind,
                Is.EqualTo(cfg.FoodActionKind.Feast));

            AssertOfferIsNotDoubledAndStackRemains(action, CreateDailyContext(action));
        }

        [Test]
        public void GenerateOffer_Event_DoesNotDoubleOrConsume()
        {
            cfg.GameAction action = _tables.TbAction.Get("act_event");
            Assert.That(FoodService.Resolve(_tables, action), Is.Null);

            AssertOfferIsNotDoubledAndStackRemains(action, CreateDailyContext(action));
        }

        [Test]
        public void GenerateOffer_TwoQueuedTickets_ConsumeAtMostOnePerEligibleBusiness()
        {
            cfg.GameAction action = _tables.TbAction.Get("act_food_gold");
            GameRun run = CreateRun();
            ActionExecutionContext context = CreateDailyContext(action);
            run.SetLastActionContext(context);
            run.AddNextBusinessRewardDoubleStack();
            run.AddNextBusinessRewardDoubleStack();

            RewardOffer first = RewardGranter.GenerateOffer(
                run, run.CurrentWeek, new ForcedThreeWayRandomStream(1UL, 1), context);
            Assert.That(first.DoubleRewardTarget, Is.EqualTo(RewardDoubleTarget.BaseGold));
            Assert.That(run.NextBusinessRewardDoubleStacks, Is.EqualTo(1));

            RewardOffer second = RewardGranter.GenerateOffer(
                run, run.CurrentWeek, new ForcedThreeWayRandomStream(2UL, 1), context);
            Assert.That(second.DoubleRewardTarget, Is.EqualTo(RewardDoubleTarget.BaseGold));
            Assert.That(run.NextBusinessRewardDoubleStacks, Is.Zero);

            RewardOffer third = RewardGranter.GenerateOffer(
                run, run.CurrentWeek, new ForcedThreeWayRandomStream(3UL, 1), context);
            Assert.That(third.DoubleRewardTarget, Is.EqualTo(RewardDoubleTarget.None));
        }

        [Test]
        public void RewardDoubleItem_ConfigUsesGeneralEffectAndAccurateDescription()
        {
            cfg.ActiveItem item = _tables.TbActiveItem.Get("item_active_reroll_action");

            Assert.That(item.Name, Is.EqualTo("奖励翻倍单"));
            Assert.That(item.EffectType, Is.EqualTo(ItemEffectTypes.DoubleNextBusinessReward));
            StringAssert.Contains("下次营业", item.Desc);
            StringAssert.Contains("随机奖励翻倍", item.Desc);
            Assert.That(item.BaseWeight, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void RewardDoubleItem_ActionSelectUseQueuesExactlyOneStack()
        {
            GameRun run = CreateRun();
            ItemDefinition item = ItemDefinition.Get(
                _tables,
                "item_active_reroll_action",
                cfg.ItemKind.Active);
            var context = new ActionSelectUseContext(run, null);

            ActiveItemUseResult result = ActiveItemEffectRegistry.Apply(
                context,
                item,
                Array.Empty<ActiveTarget>());

            Assert.That(result.Success, Is.True);
            Assert.That(run.NextBusinessRewardDoubleStacks, Is.EqualTo(1));
        }

        [TestCase("act_food_active_strengthen", 3, 2)]
        [TestCase("act_food_active_adjust", 3, 2)]
        [TestCase("act_food_hard_active_strengthen", 5, 2)]
        [TestCase("act_food_hard_active_adjust", 5, 2)]
        public void GenerateOffer_ActiveSpecificTarget_RerollsFullCandidateAndPickCounts(
            string actionId,
            int expectedChoiceCount,
            int expectedRequiredPickCount)
        {
            RewardOffer offer = GenerateOffer(
                _tables.TbAction.Get(actionId),
                stacks: 1,
                forcedThreeWayIndex: 2,
                out _);

            RewardChoiceGroup rerolled = offer.FixedGroups.Last();
            Assert.That(rerolled.Choices, Has.Count.EqualTo(expectedChoiceCount));
            Assert.That(rerolled.RequiredChoiceCount, Is.EqualTo(expectedRequiredPickCount));
        }

        [Test]
        public void AbandoningUnclaimableDoubledActiveGroup_DoesNotRefundConsumedTicket()
        {
            cfg.GameAction action = _tables.TbAction.Get("act_food_hard_active_strengthen");
            RewardOffer offer = GenerateOffer(
                action,
                stacks: 1,
                forcedThreeWayIndex: 2,
                out GameRun run);
            RewardChoiceGroup doubled = offer.FixedGroups.Last();

            while (run.HasFreeActiveSlot)
            {
                cfg.ActiveItem filler = _tables.TbActiveItem.DataList
                    .First(item => ItemPoolService.CanEnterPool(run, ItemDefinition.From(item)));
                run.AcquireItem(filler.Id, fallbackGold: 0, fireOnAcquire: false);
            }

            Assert.That(doubled.Choices, Is.Not.Empty);
            Assert.That(
                RewardGranter.TryClaimChoice(run, doubled.Choices[0], out _),
                Is.False,
                "主动栏满时额外重抽组应保留为可放弃状态，不能误领或折金币。");
            Assert.That(run.NextBusinessRewardDoubleStacks, Is.Zero,
                "奖励无法领取或被放弃时，翻倍单不应返还。");
        }

        [Test]
        public void RewardForm_BonusGoldUsesASecondIndependentlyClickableRow()
        {
            GameRun run = CreateRun();
            var offer = new RewardOffer(
                11,
                Array.Empty<RewardChoiceGroup>(),
                new RewardChoiceGroup("特定奖励", null, 0),
                doubleRewardTarget: RewardDoubleTarget.BaseGold,
                rawBaseGold: 11,
                rawBonusGold: 13,
                bonusGold: 13,
                bonusGoldClaimed: false,
                goldAmountsResolved: true);
            const string rewardKey = "reward-double-ui";
            run.SetPendingRewardOffer(rewardKey, offer);

            GameObject root = new GameObject("RewardFormTest", typeof(RectTransform), typeof(RewardForm));
            try
            {
                RewardForm form = root.GetComponent<RewardForm>();
                TMP_Text formTitle = CreateText(root.transform, "FormTitle");
                Button continueButton = CreateButton(root.transform, "Continue");
                RectTransform content = new GameObject("Content", typeof(RectTransform))
                    .GetComponent<RectTransform>();
                content.SetParent(root.transform, false);
                RewardChoiceRowView template = CreateRewardRowTemplate(content);

                SetPrivateField(form, "_run", run);
                SetPrivateField(form, "_offer", offer);
                SetPrivateField(form, "_rewardKey", rewardKey);
                SetPrivateField(form, "_titleText", formTitle);
                SetPrivateField(form, "_continueButton", continueButton);
                SetPrivateField(form, "_rewardListContent", content);
                SetPrivateField(form, "_rewardRowTemplate", template);

                InvokePrivate(form, "RebuildRewardRowsImmediate");
                List<RewardChoiceRowView> rows = GetSpawnedRows(form);
                Assert.That(rows, Has.Count.EqualTo(2));
                CollectionAssert.AreEquivalent(
                    new[] { "金币 +11", "翻倍金币 +13" },
                    rows.Select(RowTitle).ToArray());

                RewardChoiceRowView bonusRow = rows.Single(row => RowTitle(row) == "翻倍金币 +13");
                int goldBefore = run.Gold;
                bonusRow.GetComponent<Button>().onClick.Invoke();

                Assert.That(run.Gold, Is.EqualTo(goldBefore + 13));
                Assert.That(offer.BonusGoldClaimed, Is.True);
                Assert.That(offer.BaseGoldClaimed, Is.False,
                    "点击第二条金币不能顺带领取第一条。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RewardBadgeResolver_ClaimPageUsesDefaultBadgeInsteadOfSuperSpecific()
        {
            Assert.That(
                RewardBadgeResolver.DefaultSpriteNameFor(cfg.RewardKind.PassiveItemChoice),
                Is.EqualTo("reward_badge_passive_item"));
            Assert.That(
                RewardBadgeResolver.SpriteNameFor(cfg.FoodActionKind.Super, cfg.RewardKind.PassiveItemChoice),
                Is.EqualTo("reward_badge_passive_item_4"));
            Assert.That(
                RewardBadgeResolver.DefaultSpriteNameFor(cfg.RewardKind.Gold),
                Is.EqualTo("reward_badge_gold"));
            Assert.That(
                RewardBadgeResolver.SpriteNameFor(cfg.FoodActionKind.Super, cfg.RewardKind.Gold),
                Is.EqualTo("reward_badge_gold_large"));
        }

        [Test]
        public void RewardForm_SpecificPackUsesDefaultBadgeEvenForSuperFood()
        {
            GameRun run = CreateRun();
            cfg.GameAction action = _tables.TbAction.Get("act_food_hard_passive");
            run.SetLastActionContext(CreateDailyContext(action));

            var choices = new[]
            {
                new RewardChoice(cfg.RewardKind.PassiveItemChoice, "item_gold_random", "随机金币", string.Empty),
                new RewardChoice(cfg.RewardKind.PassiveItemChoice, "item_gold_meal_bonus", "餐后金币", string.Empty),
                new RewardChoice(cfg.RewardKind.PassiveItemChoice, "item_gold_boss", "评鉴金币", string.Empty),
            };
            var offer = new RewardOffer(
                0,
                Array.Empty<RewardChoiceGroup>(),
                new RewardChoiceGroup("特定奖励", choices, 1),
                baseGoldClaimed: true);

            GameObject root = new GameObject("RewardFormDefaultBadgeTest", typeof(RectTransform), typeof(RewardForm));
            try
            {
                RewardForm form = root.GetComponent<RewardForm>();
                RectTransform content = new GameObject("Content", typeof(RectTransform))
                    .GetComponent<RectTransform>();
                content.SetParent(root.transform, false);
                RewardChoiceRowView template = CreateFoodRewardRowTemplate(content);

                SetPrivateField(form, "_run", run);
                SetPrivateField(form, "_offer", offer);
                SetPrivateField(form, "_rewardListContent", content);
                SetPrivateField(form, "_rewardRowTemplate", template);

                InvokePrivate(form, "RebuildRewardRowsImmediate");
                RewardChoiceRowView row = GetSpawnedRows(form).Single();
                Image icon = row.transform.Find("IconFrame/Icon").GetComponent<Image>();
                Sprite defaultBadge = Resources.Load<Sprite>("Sprites/UI/reward_badge_passive_item");
                Sprite superBadge = Resources.Load<Sprite>("Sprites/UI/reward_badge_passive_item_4");

                Assert.That(icon.enabled, Is.True);
                Assert.That(defaultBadge, Is.Not.Null);
                Assert.That(icon.sprite, Is.SameAs(defaultBadge));
                Assert.That(icon.sprite, Is.Not.SameAs(superBadge));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RewardForm_DirectFoodRewardUsesFoodIconAsTipBounds()
        {
            GameRun run = CreateRun();
            const string dishId = "mango_sago";
            var choice = new RewardChoice(
                cfg.RewardKind.DishChoice,
                dishId,
                "芒果西米露",
                string.Empty);
            var offer = new RewardOffer(
                0,
                new[] { new RewardChoiceGroup("直接食物", new[] { choice }, 1) },
                new RewardChoiceGroup("特定奖励", null, 0),
                baseGoldClaimed: true);

            GameObject root = new GameObject("RewardFormFoodIconTest", typeof(RectTransform), typeof(RewardForm));
            try
            {
                RewardForm form = root.GetComponent<RewardForm>();
                RectTransform content = new GameObject("Content", typeof(RectTransform))
                    .GetComponent<RectTransform>();
                content.SetParent(root.transform, false);
                RewardChoiceRowView template = CreateFoodRewardRowTemplate(content);

                SetPrivateField(form, "_run", run);
                SetPrivateField(form, "_offer", offer);
                SetPrivateField(form, "_rewardListContent", content);
                SetPrivateField(form, "_rewardRowTemplate", template);

                InvokePrivate(form, "RebuildRewardRowsImmediate");
                RewardChoiceRowView row = GetSpawnedRows(form).Single();
                Image icon = row.transform.Find("IconFrame/Icon").GetComponent<Image>();
                DishIconRenderTexturePreview preview = row.transform
                    .Find("IconFrame/DishRenderTexture/Output")
                    .GetComponent<DishIconRenderTexturePreview>();
                TipHoverTrigger trigger = row.GetComponent<TipHoverTrigger>();

                Assert.That(icon.enabled, Is.True);
                Assert.That(icon.sprite, Is.Not.Null);
                Assert.That(icon.sprite.name, Is.EqualTo(dishId));
                Assert.That(preview.gameObject.activeSelf, Is.False);
                Assert.That(row.RewardIconTarget, Is.SameAs(icon.rectTransform));
                Assert.That(row.TipPlacementTarget, Is.SameAs(icon.rectTransform));
                Assert.That(trigger, Is.Not.Null);
                Assert.That(
                    GetPrivateField<RectTransform>(trigger, "_targetOverride"),
                    Is.SameAs(icon.rectTransform));
                Assert.That(
                    GetPrivateField<bool>(trigger, "_preferVerticalPlacement"),
                    Is.False);
                Assert.That(
                    GetPrivateField<bool>(trigger, "_followPointer"),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private RewardOffer GenerateOffer(
            cfg.GameAction action,
            int stacks,
            int forcedThreeWayIndex,
            out GameRun run)
        {
            run = CreateRun();
            for (int i = 0; i < stacks; i++)
            {
                run.AddNextBusinessRewardDoubleStack();
            }

            ActionExecutionContext context = CreateDailyContext(action);
            run.SetLastActionContext(context);
            return RewardGranter.GenerateOffer(
                run,
                run.CurrentWeek,
                new ForcedThreeWayRandomStream(778899UL, forcedThreeWayIndex),
                context);
        }

        private void AssertOfferIsNotDoubledAndStackRemains(
            cfg.GameAction action,
            ActionExecutionContext actionContext)
        {
            GameRun run = CreateRun();
            run.AddNextBusinessRewardDoubleStack();
            run.SetLastActionContext(actionContext);

            RewardOffer offer = RewardGranter.GenerateOffer(
                run,
                run.CurrentWeek,
                new ForcedThreeWayRandomStream(991122UL, forcedIndex: 2),
                actionContext);

            Assert.That(offer.DoubleRewardTarget, Is.EqualTo(RewardDoubleTarget.None));
            Assert.That(offer.HasBonusGold, Is.False);
            Assert.That(run.NextBusinessRewardDoubleStacks, Is.EqualTo(1));
        }

        private static ActionExecutionContext CreateDailyContext(cfg.GameAction action)
        {
            var context = new ActionExecutionContext(action);
            Assert.That(context.IsDailyAction, Is.True);
            return context;
        }

        private static int RoundGold(int rawGold, float multiplier)
        {
            return Math.Max(0, (int)Math.Round(
                rawGold * multiplier,
                MidpointRounding.AwayFromZero));
        }

        private static void AssertOriginalGroupsUnchanged(RewardOffer baseline, RewardOffer doubled)
        {
            Assert.That(doubled.RawBaseGold, Is.EqualTo(baseline.RawBaseGold));
            for (int i = 0; i < baseline.FixedGroups.Count; i++)
            {
                AssertGroupEqual(baseline.FixedGroups[i], doubled.FixedGroups[i]);
            }

            AssertGroupEqual(baseline.SpecificGroup, doubled.SpecificGroup);
        }

        private static void AssertRerolledGroupMatches(
            RewardChoiceGroup rerolled,
            RewardChoiceGroup sourceGroup)
        {
            Assert.That(rerolled, Is.Not.SameAs(sourceGroup));
            Assert.That(rerolled.SourceSlotId, Is.EqualTo(sourceGroup.SourceSlotId));
            Assert.That(rerolled.RequiredChoiceCount, Is.EqualTo(sourceGroup.RequiredChoiceCount));
            Assert.That(rerolled.Choices, Is.Not.Empty);
        }

        private static void AssertGroupsEqual(
            IReadOnlyList<RewardChoiceGroup> expected,
            IReadOnlyList<RewardChoiceGroup> actual)
        {
            Assert.That(actual, Has.Count.EqualTo(expected.Count));
            for (int i = 0; i < expected.Count; i++)
            {
                AssertGroupEqual(expected[i], actual[i]);
            }
        }

        private static void AssertGroupEqual(RewardChoiceGroup expected, RewardChoiceGroup actual)
        {
            Assert.That(actual.SourceSlotId, Is.EqualTo(expected.SourceSlotId));
            Assert.That(actual.RequiredChoiceCount, Is.EqualTo(expected.RequiredChoiceCount));
            Assert.That(actual.Skipped, Is.EqualTo(expected.Skipped));
            Assert.That(actual.ClaimedIndices, Is.EqualTo(expected.ClaimedIndices));
            Assert.That(actual.Choices, Has.Count.EqualTo(expected.Choices.Count));
            for (int i = 0; i < expected.Choices.Count; i++)
            {
                RewardChoice expectedChoice = expected.Choices[i];
                RewardChoice actualChoice = actual.Choices[i];
                Assert.That(actualChoice.Kind, Is.EqualTo(expectedChoice.Kind));
                Assert.That(actualChoice.Id, Is.EqualTo(expectedChoice.Id));
                Assert.That(actualChoice.GoldAmount, Is.EqualTo(expectedChoice.GoldAmount));
                Assert.That(actualChoice.FragmentRotation, Is.EqualTo(expectedChoice.FragmentRotation));
            }
        }

        private static TMP_Text CreateText(Transform parent, string name)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            return textObject.GetComponent<TMP_Text>();
        }

        private static Button CreateButton(Transform parent, string name)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            return buttonObject.GetComponent<Button>();
        }

        private static RewardChoiceRowView CreateRewardRowTemplate(Transform parent)
        {
            var rowObject = new GameObject(
                "RewardRowTemplate",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button),
                typeof(RewardChoiceRowView));
            rowObject.transform.SetParent(parent, false);
            CreateText(rowObject.transform, "Title");
            CreateText(rowObject.transform, "Description");
            CreateText(rowObject.transform, "State");
            return rowObject.GetComponent<RewardChoiceRowView>();
        }

        private static RewardChoiceRowView CreateFoodRewardRowTemplate(Transform parent)
        {
            RewardChoiceRowView row = CreateRewardRowTemplate(parent);
            Transform rowTransform = row.transform;
            var iconFrame = new GameObject("IconFrame", typeof(RectTransform));
            iconFrame.transform.SetParent(rowTransform, false);
            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            icon.transform.SetParent(iconFrame.transform, false);
            var dishRenderTexture = new GameObject("DishRenderTexture", typeof(RectTransform));
            dishRenderTexture.transform.SetParent(iconFrame.transform, false);
            var output = new GameObject(
                "Output",
                typeof(RectTransform),
                typeof(RawImage),
                typeof(DishIconRenderTexturePreview));
            output.transform.SetParent(dishRenderTexture.transform, false);
            return row;
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            return (T)field.GetValue(target);
        }

        private static string RowTitle(RewardChoiceRowView row)
        {
            return row.transform.Find("Title")?.GetComponent<TMP_Text>()?.text ?? string.Empty;
        }

        private static List<RewardChoiceRowView> GetSpawnedRows(RewardForm form)
        {
            return (List<RewardChoiceRowView>)typeof(RewardForm)
                .GetField("_spawnedRows", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(form);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        private static void InvokePrivate(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName);
            method.Invoke(target, null);
        }

        private GameRun CreateRun()
        {
            return new GameRun(
                _tables,
                _database,
                "glutton_dog",
                "reward-double-test",
                weekIndex: 1);
        }

        private sealed class ForcedThreeWayRandomStream : IRandomStream
        {
            private readonly Xoshiro256SS _inner;
            private readonly int _forcedIndex;

            public ForcedThreeWayRandomStream(ulong seed, int forcedIndex)
            {
                _inner = new Xoshiro256SS(seed);
                _forcedIndex = Math.Max(0, Math.Min(2, forcedIndex));
            }

            public RngState State
            {
                get => _inner.State;
                set => _inner.State = value;
            }

            public uint NextUInt() => _inner.NextUInt();

            public ulong NextULong() => _inner.NextULong();

            public int Range(int minInclusive, int maxExclusive)
            {
                return minInclusive == 0 && maxExclusive == 3
                    ? _forcedIndex
                    : _inner.Range(minInclusive, maxExclusive);
            }

            public float Range(float minInclusive, float maxExclusive) =>
                _inner.Range(minInclusive, maxExclusive);

            public float NextFloat() => _inner.NextFloat();

            public double NextDouble() => _inner.NextDouble();

            public bool NextBool(double probability = 0.5) => _inner.NextBool(probability);

            public void Shuffle<T>(IList<T> list) => _inner.Shuffle(list);

            public T Pick<T>(IReadOnlyList<T> list) => _inner.Pick(list);

            public int WeightedPickIndex(IReadOnlyList<float> weights) =>
                _inner.WeightedPickIndex(weights);
        }
    }
}
