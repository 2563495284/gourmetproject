using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class CandidateItemIntegration103To111Tests
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

        [TestCase("item_skip_reward_dish_luck", HiddenScorePurpose.Dish, HiddenScorePurpose.PassiveItem)]
        [TestCase("item_skip_reward_passive_luck", HiddenScorePurpose.PassiveItem, HiddenScorePurpose.Dish)]
        public void RewardAbandonLuck_ItemRuntimeAccumulatesOnlyForConfiguredPurpose(
            string itemId,
            HiddenScorePurpose expectedPurpose,
            HiddenScorePurpose unrelatedPurpose)
        {
            GameRun run = CreateRun();
            BindCandidate(run, itemId, 10f);
            var runtime = new ItemRuntime(run);

            runtime.NotifyRewardAbandoned();
            Assert.That(runtime.HiddenScoreOffset(expectedPurpose), Is.EqualTo(10f));
            Assert.That(runtime.HiddenScoreOffset(unrelatedPurpose), Is.Zero);

            runtime.NotifyRewardAbandoned();
            Assert.That(runtime.HiddenScoreOffset(expectedPurpose), Is.EqualTo(20f));
            Assert.That(runtime.HiddenScoreOffset(unrelatedPurpose), Is.Zero);
        }

        [TestCase("act_food_hard_gold", true, 1)]
        [TestCase("act_food_gold", true, 0)]
        [TestCase("act_food_hard_gold", false, 0)]
        public void SuperMaterialSpread_ItemRuntimeAddsExactlyOneAdjacentMaterialOnlyOnSurvivedSuper(
            string actionId,
            bool survived,
            int expectedAddedCount)
        {
            GameRun run = CreateRun();
            BindCandidate(run, "item_super_material_spread", 1f);
            DiningTable preview = run.BuildTablePreviewFromFragments();
            GridPos source = FindCellWithNeighbor(preview);
            Assert.That(run.AddCellMaterial(source, "m_cherry"), Is.True);
            int beforeCount = run.CellMaterialOverrides.Count;

            IReadOnlyList<FoodSettlementReward> rewards = new ItemRuntime(run).OnFoodBattleSettled(
                BuildActionContext(actionId),
                survived,
                new Xoshiro256SS(107UL));

            Assert.That(rewards, Is.Empty);
            CellMaterialOverride[] added = run.CellMaterialOverrides.Skip(beforeCount).ToArray();
            Assert.That(added, Has.Length.EqualTo(expectedAddedCount));
            if (expectedAddedCount == 1)
            {
                Assert.That(added[0].MaterialId, Is.EqualTo("m_cherry"));
                Assert.That(ManhattanDistance(source, added[0].Pos), Is.EqualTo(1));
                Assert.That(preview.Exists(added[0].Pos), Is.True);
            }
        }

        [TestCase("act_food_hard_gold", true, 1)]
        [TestCase("act_food_gold", true, 0)]
        [TestCase("act_food_hard_gold", false, 0)]
        public void SuperFragmentReward_ItemRuntimeCreatesOneConfiguredOfferOnlyOnSurvivedSuper(
            string actionId,
            bool survived,
            int expectedRewardCount)
        {
            GameRun run = CreateRun();
            BindCandidate(run, "item_super_fragment_reward", 1f, "fragment_choice_3");

            IReadOnlyList<FoodSettlementReward> rewards = new ItemRuntime(run).OnFoodBattleSettled(
                BuildActionContext(actionId),
                survived,
                new Xoshiro256SS(109UL));

            Assert.That(rewards, Has.Count.EqualTo(expectedRewardCount));
            if (expectedRewardCount == 1)
            {
                FoodSettlementReward reward = rewards.Single();
                Assert.That(reward.SourceItemId, Is.EqualTo("item_super_fragment_reward"));
                Assert.That(reward.Offer.FixedGroups, Has.Count.EqualTo(1));
                RewardChoiceGroup group = reward.Offer.FixedGroups.Single();
                Assert.That(group.SourceSlotId, Is.EqualTo("fragment_choice_3"));
                Assert.That(group.RequiredChoiceCount, Is.EqualTo(1));
                Assert.That(group.Choices, Has.Count.EqualTo(3));
                Assert.That(group.Choices.All(choice => choice.Kind == cfg.RewardKind.FragmentChoice), Is.True);
            }
        }

        private GameRun CreateRun()
            => new GameRun(_tables, _database, "glutton_dog", "candidate-103-111-test", 1);

        private ActionExecutionContext BuildActionContext(string actionId)
        {
            cfg.GameAction action = _tables.TbAction.GetOrDefault(actionId);
            Assert.That(action, Is.Not.Null, actionId);
            return new ActionExecutionContext(action);
        }

        private static GridPos FindCellWithNeighbor(DiningTable table)
        {
            foreach (GridPos cell in table.ExistingCells())
            {
                if (table.Exists(cell.Offset(1, 0))
                    || table.Exists(cell.Offset(-1, 0))
                    || table.Exists(cell.Offset(0, 1))
                    || table.Exists(cell.Offset(0, -1)))
                {
                    return cell;
                }
            }

            Assert.Fail("测试餐桌必须至少包含一对相邻格。");
            return default;
        }

        private static int ManhattanDistance(GridPos left, GridPos right)
            => Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y);

        private static PassiveItemModel BindCandidate(
            GameRun run,
            string itemId,
            float value,
            string param = "")
        {
            var state = new RunItemState(itemId, 1);
            PassiveItemModel model = PassiveItemModelRegistry.Create(itemId);
            ItemDefinition definition = BuildDefinition(itemId, value, param);
            model.Bind(run, definition, state);
            state.Model = model;

            FieldInfo itemsField = typeof(GameRun).GetField(
                "_items",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(itemsField, Is.Not.Null);
            var items = (List<RunItemState>)itemsField.GetValue(run);
            items.Add(state);
            return model;
        }

        private static ItemDefinition BuildDefinition(string itemId, float value, string param)
        {
            string json = "{"
                + $"\"id\":\"{itemId}\","
                + "\"name\":\"候选装饰品\","
                + "\"desc\":\"测试\","
                + "\"quality\":0,"
                + "\"specialTags\":0,"
                + $"\"effectValue\":{value.ToString(CultureInfo.InvariantCulture)},"
                + $"\"effectParam\":\"{param}\","
                + "\"baseWeight\":100,"
                + "\"hiddenRange\":{\"min\":10,\"max\":80},"
                + "\"targetScoreHiddenOffset\":0,"
                + "\"dishHiddenOffset\":0,"
                + "\"passiveItemHiddenOffset\":0,"
                + "\"fragmentHiddenOffset\":0,"
                + "\"termId\":\"\","
                + "\"price\":40}";
            return ItemDefinition.From(new cfg.PassiveItem(JSON.Parse(json)));
        }
    }
}
