using System;
using System.Collections.Generic;
using System.Reflection;
using GourmetProject.Config;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Meta;
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
