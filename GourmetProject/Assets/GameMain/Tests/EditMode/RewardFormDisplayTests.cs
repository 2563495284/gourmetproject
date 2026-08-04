using System;
using System.Collections.Generic;
using System.Reflection;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Tests.EditMode
{
    public sealed class RewardFormDisplayTests
    {
        private const string RewardFormPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/RewardForm.prefab";

        private static readonly object[] FoodRewardPresentationCases =
        {
            new object[] { "act_food_gold", "reward_badge_gold", true },
            new object[] { "act_food_fragment", "reward_badge_table_cell", false },
            new object[] { "act_food_passive", "reward_badge_passive_item", false },
            new object[] { "act_food_active_strengthen", "reward_badge_active_strengthen", false },
            new object[] { "act_food_active_adjust", "reward_badge_active_adjust", false },
            new object[] { "act_food_hard_gold", "reward_badge_gold_large", true },
            new object[] { "act_food_hard_fragment", "reward_badge_table_cell_large", false },
            new object[] { "act_food_hard_passive", "reward_badge_passive_item_4", false },
            new object[] { "act_food_hard_active_strengthen", "reward_badge_active_strengthen_4", false },
            new object[] { "act_food_hard_active_adjust", "reward_badge_active_adjust_4", false },
        };

        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void UnclaimedOffer_ShowsOneGoldAndTwoChoicePackRows()
        {
            RewardOffer offer = CreateOffer(
                baseGoldClaimed: false,
                fixedClaimedIndices: null,
                specificClaimedIndices: null);

            WithRenderedOffer(offer, rows =>
            {
                Assert.That(rows, Has.Count.EqualTo(3));
                Assert.That(
                    rows.FindAll(RowButtonIsInteractable),
                    Has.Count.EqualTo(3));
                Assert.That(
                    rows.FindAll(RowShowsClaimedState),
                    Is.Empty);
                Assert.That(
                    rows.FindAll(RowShowsDishPreview),
                    Has.Count.EqualTo(1));
                Assert.That(
                    rows.FindAll(RowShowsGenericIcon),
                    Has.Count.EqualTo(2));
                Assert.That(RowIconSpriteName(rows[0]), Is.EqualTo("reward_badge_gold"));
                Assert.That(RowDescription(rows[0]), Is.EqualTo("点击领取"));
                Assert.That(rows[0].GetComponent<TipHoverTrigger>(), Is.Null);
            });
        }

        [TestCaseSource(nameof(FoodRewardPresentationCases))]
        public void FoodBattleOffer_UsesFixedAndSpecificActionBadges(
            string actionId,
            string expectedSpecificSprite,
            bool expectDirectSpecificRow)
        {
            GameRun run = CreateRun();
            RewardOffer offer = GenerateFoodOffer(run, actionId);

            Assert.That(offer, Is.Not.Null, actionId);
            Assert.That(offer.FixedGroups, Has.Count.GreaterThanOrEqualTo(1), actionId);
            Assert.That(offer.SpecificGroup.HasChoices, Is.True, actionId);
            Assert.That(
                offer.SpecificGroup.Choices.Count == 1,
                Is.EqualTo(expectDirectSpecificRow),
                $"{actionId} 未覆盖预期的 {(expectDirectSpecificRow ? "direct" : "pack")} 渲染路径");

            WithRenderedOffer(run, offer, rows =>
            {
                Assert.That(rows, Has.Count.EqualTo(3), actionId);
                Assert.That(RowIconSpriteName(rows[0]), Is.EqualTo("reward_badge_gold"), actionId);
                Assert.That(RowIconSpriteName(rows[1]), Is.EqualTo("reward_badge_base_dish"), actionId);
                Assert.That(RowShowsDishPreview(rows[1]), Is.False, actionId);
                Assert.That(RowShowsGenericIcon(rows[1]), Is.True, actionId);
                Assert.That(RowIconSpriteName(rows[2]), Is.EqualTo(expectedSpecificSprite), actionId);
                Assert.That(RowShowsDishPreview(rows[2]), Is.False, actionId);
                Assert.That(RowShowsGenericIcon(rows[2]), Is.True, actionId);
            });
        }

        [Test]
        public void FoodBattleOffer_PresentationSourceSurvivesPendingOfferRoundTrip()
        {
            const string rewardKey = "reward-form-source-roundtrip";
            GameRun run = CreateRun();
            RewardOffer offer = GenerateFoodOffer(run, "act_food_hard_passive");
            run.SetPendingRewardOffer(rewardKey, offer);

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            RewardOffer restoredOffer = restored.GetPendingRewardOffer(rewardKey);

            Assert.That(restoredOffer, Is.Not.Null);
            WithRenderedOffer(restored, restoredOffer, rows =>
            {
                Assert.That(rows, Has.Count.EqualTo(3));
                Assert.That(RowIconSpriteName(rows[0]), Is.EqualTo("reward_badge_gold"));
                Assert.That(RowIconSpriteName(rows[1]), Is.EqualTo("reward_badge_base_dish"));
                Assert.That(RowShowsDishPreview(rows[1]), Is.False);
                Assert.That(RowIconSpriteName(rows[2]), Is.EqualTo("reward_badge_passive_item_4"));
            });
        }

        [Test]
        public void MissingFoodContext_FallsBackToLegacyChoicePresentation()
        {
            RewardOffer offer = CreateOffer(
                baseGoldClaimed: false,
                fixedClaimedIndices: null,
                specificClaimedIndices: null);

            WithRenderedOffer(offer, rows =>
            {
                Assert.That(rows, Has.Count.EqualTo(3));
                Assert.That(RowIconSpriteName(rows[0]), Is.EqualTo("reward_badge_gold"));
                Assert.That(RowShowsDishPreview(rows[1]), Is.True);
                Assert.That(RowShowsGenericIcon(rows[1]), Is.False);
                Assert.That(RowIconSpriteName(rows[2]), Is.Not.EqualTo("reward_badge_active_strengthen"));
                Assert.That(RowIconSpriteName(rows[2]), Is.Not.EqualTo("reward_badge_active_strengthen_4"));
            });
        }

        [Test]
        public void LegacyFoodOfferWithoutSourceSlot_UsesStructuralBaseDishFallback()
        {
            GameRun run = CreateRun();
            run.SetLastActionContext(new ActionExecutionContext(
                _tables.TbAction.Get("act_food_active_strengthen")));
            RewardOffer offer = CreateOffer(
                baseGoldClaimed: false,
                fixedClaimedIndices: null,
                specificClaimedIndices: null);

            WithRenderedOffer(run, offer, rows =>
            {
                Assert.That(rows, Has.Count.EqualTo(3));
                Assert.That(RowIconSpriteName(rows[1]), Is.EqualTo("reward_badge_base_dish"));
                Assert.That(RowShowsDishPreview(rows[1]), Is.False);
                Assert.That(RowIconSpriteName(rows[2]), Is.EqualTo("reward_badge_active_strengthen"));
            });
        }

        [Test]
        public void BossReward_KeepsLegacyChoicePresentation()
        {
            GameRun run = CreateRun();
            run.SetLastActionContext(new ActionExecutionContext(
                _tables.TbAction.Get("act_boss")));
            RewardOffer offer = CreateOffer(
                baseGoldClaimed: false,
                fixedClaimedIndices: null,
                specificClaimedIndices: null);

            WithRenderedOffer(run, offer, rows =>
            {
                Assert.That(rows, Has.Count.EqualTo(3));
                Assert.That(RowShowsDishPreview(rows[1]), Is.True);
                Assert.That(RowShowsGenericIcon(rows[1]), Is.False);
                Assert.That(RowIconSpriteName(rows[2]), Is.Not.EqualTo("reward_badge_active_strengthen"));
            });
        }

        [Test]
        public void GenericConfiguredOffer_IsNotMisidentifiedAsFoodBattleReward()
        {
            GameRun run = CreateRun();
            RewardOffer offer = RewardGranter.BuildConfigOffer(
                run,
                new Xoshiro256SS(19UL),
                "specific_passive");

            Assert.That(offer, Is.Not.Null);
            WithRenderedOffer(run, offer, rows =>
            {
                Assert.That(rows, Has.Count.EqualTo(1));
                Assert.That(RowShowsGenericIcon(rows[0]), Is.True);
                Assert.That(RowIconSpriteName(rows[0]), Does.Not.StartWith("reward_badge_"));
            });
        }

        [Test]
        public void SlotConfiguredOffer_IsNotMisidentifiedAsFoodBattleReward()
        {
            GameRun run = CreateRun();
            cfg.GameAction slotAction = _tables.TbAction.Get("act_slot");
            cfg.RewardSlot slot = _tables.TbRewardSlot.Get("slot_machine_passive");
            var actionContext = new ActionExecutionContext(slotAction);
            run.SetLastActionContext(actionContext);
            RewardOffer offer = RewardGranter.BuildConfigOffer(
                run,
                new Xoshiro256SS(23UL),
                slot,
                actionContext);

            Assert.That(slotAction, Is.Not.Null);
            Assert.That(slot, Is.Not.Null);
            Assert.That(offer, Is.Not.Null);
            WithRenderedOffer(run, offer, rows =>
            {
                Assert.That(rows, Has.Count.EqualTo(1));
                Assert.That(RowShowsGenericIcon(rows[0]), Is.True);
                Assert.That(RowIconSpriteName(rows[0]), Does.Not.StartWith("reward_badge_"));
            });
        }

        [Test]
        public void FullyClaimedOffer_HidesAllRewardRows()
        {
            RewardOffer offer = CreateOffer(
                baseGoldClaimed: true,
                fixedClaimedIndices: new[] { 1 },
                specificClaimedIndices: new[] { 2 });

            WithRenderedOffer(offer, rows =>
            {
                Assert.That(rows, Is.Empty);
            });
        }

        [Test]
        public void PartiallyClaimedMultiPick_HidesClaimedRowsAndKeepsRemainingPacks()
        {
            RewardOffer offer = CreateOffer(
                baseGoldClaimed: true,
                fixedClaimedIndices: new[] { 1 },
                specificClaimedIndices: null,
                fixedRequiredChoiceCount: 2);

            WithRenderedOffer(offer, rows =>
            {
                Assert.That(rows, Has.Count.EqualTo(2));
                Assert.That(
                    rows.FindAll(RowShowsClaimedState),
                    Is.Empty);
                Assert.That(
                    rows.FindAll(RowButtonIsInteractable),
                    Has.Count.EqualTo(2));
            });
        }

        [Test]
        public void SingleDish_ShowsActualRewardWithSlotDescriptionAndFoodTips()
        {
            var group = new RewardChoiceGroup(
                "随机食物",
                new[] { Choice(cfg.RewardKind.DishChoice, "cake_slice", "蛋糕切角") },
                description: "随机食物配置描述",
                ruleText: "随机获得 1 个食物。",
                sourceSlotId: "dish_grant_1");
            RewardOffer offer = new RewardOffer(0, new[] { group }, null, baseGoldClaimed: true);

            WithRenderedOffer(offer, rows =>
            {
                Assert.That(rows, Has.Count.EqualTo(1));
                Assert.That(RowTitle(rows[0]), Is.EqualTo("蛋糕切角"));
                Assert.That(RowDescription(rows[0]), Is.EqualTo("随机食物配置描述"));
                Assert.That(rows[0].GetComponent<TipHoverTrigger>(), Is.Not.Null);
                Assert.That(RowTipsPreferVertical(rows[0]), Is.True);
                Assert.That(rows[0].SelectionFlySource, Is.Not.Null);
                Assert.That(RowHasDishFlyTexture(rows[0]), Is.True);
            });
        }

        [Test]
        public void FoodBattleDirectDishBadge_KeepsHiddenDishTextureForFlyAnimation()
        {
            GameRun run = CreateRun();
            run.SetLastActionContext(new ActionExecutionContext(
                _tables.TbAction.Get("act_food_gold")));
            var group = new RewardChoiceGroup(
                "基础食物",
                new[] { Choice(cfg.RewardKind.DishChoice, "cake_slice", "蛋糕切角") },
                sourceSlotId: "slot_base_dish");
            RewardOffer offer = new RewardOffer(0, new[] { group }, null, baseGoldClaimed: true);

            WithRenderedOffer(run, offer, rows =>
            {
                Assert.That(rows, Has.Count.EqualTo(1));
                Assert.That(RowShowsDishPreview(rows[0]), Is.False);
                Assert.That(RowShowsGenericIcon(rows[0]), Is.True);
                Assert.That(RowHasDishFlyTexture(rows[0]), Is.True);
                Assert.That(rows[0].SelectionFlySource, Is.Not.Null);
            });
        }

        [Test]
        public void SinglePassive_ShowsRolledItemDescriptionAndItemTips()
        {
            GameRun run = CreateRun();
            RewardChoiceGroup group = RewardGranter.BuildConfigChoiceGroup(
                run,
                new Xoshiro256SS(7UL),
                "passive_choice_1",
                "调用方标题");
            Assert.That(group, Is.Not.Null);

            RewardOffer offer = new RewardOffer(0, new[] { group }, null, baseGoldClaimed: true);
            WithRenderedOffer(offer, rows =>
            {
                Assert.That(rows, Has.Count.EqualTo(1));
                Assert.That(RowTitle(rows[0]), Is.EqualTo(group.Choices[0].Name));
                Assert.That(RowDescription(rows[0]), Does.StartWith(group.Choices[0].Description));
                Assert.That(rows[0].GetComponent<TipHoverTrigger>(), Is.Not.Null);
                Assert.That(RowTipsPreferVertical(rows[0]), Is.True);
                Assert.That(rows[0].SelectionFlySource, Is.Not.Null);
                Assert.That(rows[0].SelectionFlySprite, Is.Not.Null);
            });
        }

        [Test]
        public void SingleActive_ShowsRolledItemDescriptionAndItemTips()
        {
            GameRun run = CreateRun();
            RewardChoiceGroup group = RewardGranter.BuildConfigChoiceGroup(
                run,
                new Xoshiro256SS(11UL),
                "active_grant_1",
                "调用方标题");
            Assert.That(group, Is.Not.Null);

            RewardOffer offer = new RewardOffer(0, new[] { group }, null, baseGoldClaimed: true);
            WithRenderedOffer(offer, rows =>
            {
                Assert.That(rows, Has.Count.EqualTo(1));
                Assert.That(RowTitle(rows[0]), Is.EqualTo(group.Choices[0].Name));
                Assert.That(RowDescription(rows[0]), Does.StartWith(group.Choices[0].Description));
                Assert.That(rows[0].GetComponent<TipHoverTrigger>(), Is.Not.Null);
                Assert.That(RowTipsPreferVertical(rows[0]), Is.True);
                Assert.That(rows[0].SelectionFlySource, Is.Not.Null);
                Assert.That(rows[0].SelectionFlySprite, Is.Not.Null);
            });
        }

        [Test]
        public void FoodBattleDirectItemBadge_UsesActualItemSpriteForFlyAnimation()
        {
            GameRun run = CreateRun();
            run.SetLastActionContext(new ActionExecutionContext(
                _tables.TbAction.Get("act_food_passive")));
            RewardChoiceGroup group = RewardGranter.BuildConfigChoiceGroup(
                run,
                new Xoshiro256SS(31UL),
                "passive_choice_1",
                "装饰品奖励");
            Assert.That(group, Is.Not.Null);
            RewardOffer offer = new RewardOffer(0, null, group, baseGoldClaimed: true);

            WithRenderedOffer(run, offer, rows =>
            {
                Assert.That(rows, Has.Count.EqualTo(1));
                Assert.That(RowIconSpriteName(rows[0]), Is.EqualTo("reward_badge_passive_item"));
                Assert.That(rows[0].SelectionFlySprite, Is.Not.Null);
                Assert.That(rows[0].SelectionFlySprite.name, Is.Not.EqualTo(RowIconSpriteName(rows[0])));
            });
        }

        [Test]
        public void MultiChoicePack_ShowsOnlyConfiguredRuleText()
        {
            var group = new RewardChoiceGroup(
                "配置选择名称",
                new[]
                {
                    Choice(cfg.RewardKind.PassiveItemChoice, "item_a", "装饰品和消耗品 A"),
                    Choice(cfg.RewardKind.PassiveItemChoice, "item_b", "装饰品和消耗品 B"),
                },
                description: "配置用途描述",
                ruleText: "从 2 个装饰品中选择 1 个。",
                sourceSlotId: "slot_configured");
            RewardOffer offer = new RewardOffer(0, new[] { group }, null, baseGoldClaimed: true);

            WithRenderedOffer(offer, rows =>
            {
                Assert.That(rows, Has.Count.EqualTo(1));
                Assert.That(RowTitle(rows[0]), Is.EqualTo("配置选择名称"));
                Assert.That(
                    RowDescription(rows[0]),
                    Is.EqualTo("从 2 个装饰品中选择 1 个。"));
            });
        }

        [Test]
        public void SingleFallbackGold_ShowsActualConvertedReward()
        {
            RewardChoice fallback = RewardChoice.Gold(20, "折算金币", isFallback: true);
            var group = new RewardChoiceGroup(
                "原装饰品和消耗品选择",
                new[] { fallback },
                description: "候选不足时折算",
                ruleText: "随机获得 1 个金币。",
                sourceSlotId: "slot_empty_pool");
            RewardOffer offer = new RewardOffer(0, new[] { group }, null, baseGoldClaimed: true);

            WithRenderedOffer(offer, rows =>
            {
                Assert.That(rows, Has.Count.EqualTo(1));
                Assert.That(RowTitle(rows[0]), Is.EqualTo("折算金币"));
                Assert.That(RowDescription(rows[0]), Is.EqualTo("领取后获得金币 +20。"));
                Assert.That(rows[0].GetComponent<TipHoverTrigger>(), Is.Null);
            });
        }

        [Test]
        public void SingleFragment_RemainsAConfiguredChoicePack()
        {
            var group = new RewardChoiceGroup(
                "餐桌格选择",
                new[] { Choice(cfg.RewardKind.FragmentChoice, "fragment_square", "方形碎片") },
                description: "碎片包描述",
                ruleText: "从 1 个餐桌格中选择 1 个。",
                sourceSlotId: "fragment_test");
            RewardOffer offer = new RewardOffer(0, new[] { group }, null, baseGoldClaimed: true);

            WithRenderedOffer(offer, rows =>
            {
                Assert.That(rows, Has.Count.EqualTo(1));
                Assert.That(RowTitle(rows[0]), Is.EqualTo("餐桌格选择"));
                Assert.That(RowDescription(rows[0]), Is.EqualTo("从 1 个餐桌格中选择 1 个。"));
            });
        }

        private void WithRenderedOffer(
            RewardOffer offer,
            Action<List<RewardChoiceRowView>> assertion)
        {
            WithRenderedOffer(CreateRun(), offer, assertion);
        }

        private void WithRenderedOffer(
            GameRun run,
            RewardOffer offer,
            Action<List<RewardChoiceRowView>> assertion)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    RewardFormPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                MonoBehaviour form = Array.Find(
                    instance.GetComponents<MonoBehaviour>(),
                    component =>
                        component != null &&
                        component.GetType().FullName ==
                        "GourmetProject.Game.UI.Meta.RewardForm");
                Assert.That(form, Is.Not.Null);
                SetField(form, "_run", run);
                SetField(form, "_offer", offer);
                Invoke(form, "RefreshOffer");

                var rows = GetField<List<RewardChoiceRowView>>(
                    form,
                    "_spawnedRows");
                assertion(rows);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private RewardOffer GenerateFoodOffer(GameRun run, string actionId)
        {
            cfg.GameAction action = _tables.TbAction.Get(actionId);
            Assert.That(action, Is.Not.Null, actionId);
            Assert.That(action.Behavior, Is.EqualTo(cfg.ActionBehavior.Food), actionId);
            cfg.Food food = FoodService.Resolve(_tables, action);
            Assert.That(food, Is.Not.Null, actionId);
            Assert.That(
                WeekEventCardView.RewardIconSpriteName(food.ActionKind, food.RewardKind),
                Is.Not.Empty,
                actionId);

            var actionContext = new ActionExecutionContext(action);
            run.SetLastActionContext(actionContext);

            var fixedGroup = new RewardChoiceGroup(
                "基础食物",
                new[]
                {
                    Choice(cfg.RewardKind.DishChoice, "cake_slice", "蛋糕切角"),
                    Choice(cfg.RewardKind.DishChoice, "eclair", "闪电泡芙"),
                    Choice(cfg.RewardKind.DishChoice, "cheesecake", "芝士蛋糕"),
                },
                sourceSlotId: "slot_base_dish");

            int specificChoiceCount = food.RewardKind == cfg.RewardKind.Gold ? 1 : 3;
            var specificChoices = new List<RewardChoice>(specificChoiceCount);
            for (int i = 0; i < specificChoiceCount; i++)
            {
                specificChoices.Add(Choice(
                    food.RewardKind,
                    $"presentation_{actionId}_{i}",
                    $"特定奖励 {i + 1}"));
            }

            var specificGroup = new RewardChoiceGroup(
                "特定奖励",
                specificChoices);
            return new RewardOffer(28, new[] { fixedGroup }, specificGroup);
        }

        private RewardOffer CreateOffer(
            bool baseGoldClaimed,
            IReadOnlyList<int> fixedClaimedIndices,
            IReadOnlyList<int> specificClaimedIndices,
            int fixedRequiredChoiceCount = 1)
        {
            var dishChoices = new List<RewardChoice>
            {
                Choice(cfg.RewardKind.DishChoice, "cake_slice", "蛋糕切角"),
                Choice(cfg.RewardKind.DishChoice, "eclair", "闪电泡芙"),
                Choice(cfg.RewardKind.DishChoice, "cheesecake", "芝士蛋糕"),
            };
            var itemChoices = new List<RewardChoice>
            {
                Choice(
                    cfg.RewardKind.ActiveItemStrengthen,
                    "item_active_lay_silver",
                    "银采购单"),
                Choice(
                    cfg.RewardKind.ActiveItemStrengthen,
                    "item_active_lay_cherry",
                    "樱桃木采购单"),
                Choice(
                    cfg.RewardKind.ActiveItemStrengthen,
                    "item_active_lay_obsidian",
                    "黑曜石采购单"),
            };

            var fixedGroup = new RewardChoiceGroup(
                "基础食物",
                dishChoices,
                fixedRequiredChoiceCount,
                fixedClaimedIndices);
            var specificGroup = new RewardChoiceGroup(
                "特定奖励",
                itemChoices,
                1,
                specificClaimedIndices);
            return new RewardOffer(
                28,
                new[] { fixedGroup },
                specificGroup,
                baseGoldClaimed);
        }

        private GameRun CreateRun()
        {
            string characterId = _tables.TbCharacter.DataList[0].Id;
            return new GameRun(
                _tables,
                _database,
                characterId,
                "reward-form-display-tests");
        }

        private static RewardChoice Choice(
            cfg.RewardKind kind,
            string id,
            string name)
        {
            return new RewardChoice(kind, id, name, string.Empty);
        }

        private static bool RowShowsClaimedState(
            RewardChoiceRowView row)
        {
            TMP_Text state = GetField<TMP_Text>(row, "_stateText");
            return state != null &&
                   state.gameObject.activeSelf &&
                   state.text == "已领取";
        }

        private static bool RowButtonIsInteractable(
            RewardChoiceRowView row)
        {
            Button button = GetField<Button>(row, "_button");
            return button != null && button.interactable;
        }

        private static bool RowShowsDishPreview(RewardChoiceRowView row)
        {
            DishIconRenderTexturePreview preview =
                GetField<DishIconRenderTexturePreview>(row, "_dishPreview");
            return preview != null
                && preview.gameObject.activeSelf
                && preview.CurrentTexture != null;
        }

        private static bool RowHasDishFlyTexture(RewardChoiceRowView row)
        {
            DishIconRenderTexturePreview preview =
                GetField<DishIconRenderTexturePreview>(row, "_dishPreview");
            return preview != null && preview.CurrentTexture != null;
        }

        private static bool RowShowsGenericIcon(RewardChoiceRowView row)
        {
            Image icon = GetField<Image>(row, "_icon");
            return icon != null && icon.enabled && icon.sprite != null;
        }

        private static string RowIconSpriteName(RewardChoiceRowView row)
        {
            Image icon = GetField<Image>(row, "_icon");
            return icon != null && icon.enabled && icon.sprite != null
                ? icon.sprite.name
                : string.Empty;
        }

        private static bool RowTipsPreferVertical(RewardChoiceRowView row)
        {
            TipHoverTrigger trigger = row.GetComponent<TipHoverTrigger>();
            return trigger != null && GetField<bool>(trigger, "_preferVerticalPlacement");
        }

        private static string RowTitle(RewardChoiceRowView row)
        {
            return GetField<TMP_Text>(row, "_titleText")?.text ?? string.Empty;
        }

        private static string RowDescription(RewardChoiceRowView row)
        {
            return GetField<TMP_Text>(row, "_descriptionText")?.text ?? string.Empty;
        }

        private static T GetField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return (T)field.GetValue(target);
        }

        private static void SetField(
            object target,
            string name,
            object value)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }

        private static void Invoke(object target, string name)
        {
            MethodInfo method = target.GetType().GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name);
            method.Invoke(target, null);
        }
    }
}
