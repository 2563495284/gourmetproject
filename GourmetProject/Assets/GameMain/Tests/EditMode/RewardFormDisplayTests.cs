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

namespace GourmetProject.Tests.EditMode
{
    public sealed class RewardFormDisplayTests
    {
        private const string RewardFormPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/RewardForm.prefab";

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
                "随机菜品",
                new[] { Choice(cfg.RewardKind.DishChoice, "cake_slice", "蛋糕切角") },
                description: "随机菜品配置描述",
                ruleText: "随机获得 1 个菜品。",
                sourceSlotId: "dish_grant_1");
            RewardOffer offer = new RewardOffer(0, new[] { group }, null, baseGoldClaimed: true);

            WithRenderedOffer(offer, rows =>
            {
                Assert.That(rows, Has.Count.EqualTo(1));
                Assert.That(RowTitle(rows[0]), Is.EqualTo("蛋糕切角"));
                Assert.That(RowDescription(rows[0]), Is.EqualTo("随机菜品配置描述"));
                Assert.That(rows[0].GetComponent<TipHoverTrigger>(), Is.Not.Null);
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
            });
        }

        [Test]
        public void MultiChoicePack_UsesConfiguredGroupPresentation()
        {
            var group = new RewardChoiceGroup(
                "配置选择名称",
                new[]
                {
                    Choice(cfg.RewardKind.PassiveItemChoice, "item_a", "道具 A"),
                    Choice(cfg.RewardKind.PassiveItemChoice, "item_b", "道具 B"),
                },
                description: "配置用途描述",
                ruleText: "从 2 个被动道具中选择 1 个。",
                sourceSlotId: "slot_configured");
            RewardOffer offer = new RewardOffer(0, new[] { group }, null, baseGoldClaimed: true);

            WithRenderedOffer(offer, rows =>
            {
                Assert.That(rows, Has.Count.EqualTo(1));
                Assert.That(RowTitle(rows[0]), Is.EqualTo("配置选择名称"));
                Assert.That(
                    RowDescription(rows[0]),
                    Is.EqualTo("配置用途描述\n从 2 个被动道具中选择 1 个。"));
            });
        }

        [Test]
        public void SingleFallbackGold_ShowsActualConvertedReward()
        {
            RewardChoice fallback = RewardChoice.Gold(20, "折算金币", isFallback: true);
            var group = new RewardChoiceGroup(
                "原道具选择",
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
            });
        }

        [Test]
        public void SingleFragment_RemainsAConfiguredChoicePack()
        {
            var group = new RewardChoiceGroup(
                "餐桌碎片选择",
                new[] { Choice(cfg.RewardKind.FragmentChoice, "fragment_square", "方形碎片") },
                description: "碎片包描述",
                ruleText: "从 1 个餐桌碎片中选择 1 个。",
                sourceSlotId: "fragment_test");
            RewardOffer offer = new RewardOffer(0, new[] { group }, null, baseGoldClaimed: true);

            WithRenderedOffer(offer, rows =>
            {
                Assert.That(rows, Has.Count.EqualTo(1));
                Assert.That(RowTitle(rows[0]), Is.EqualTo("餐桌碎片选择"));
                Assert.That(RowDescription(rows[0]), Is.EqualTo("碎片包描述\n从 1 个餐桌碎片中选择 1 个。"));
            });
        }

        private void WithRenderedOffer(
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
                SetField(form, "_run", CreateRun());
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
                "基础菜品",
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
            Text state = GetField<Text>(row, "_stateText");
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

        private static bool RowShowsGenericIcon(RewardChoiceRowView row)
        {
            Image icon = GetField<Image>(row, "_icon");
            return icon != null && icon.enabled && icon.sprite != null;
        }

        private static string RowTitle(RewardChoiceRowView row)
        {
            return GetField<Text>(row, "_titleText")?.text ?? string.Empty;
        }

        private static string RowDescription(RewardChoiceRowView row)
        {
            return GetField<Text>(row, "_descriptionText")?.text ?? string.Empty;
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
