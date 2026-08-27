#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class DeferredActionItemAvailabilityTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            string directory = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name =>
                JSON.Parse(File.ReadAllText(Path.Combine(directory, name + ".json"))));
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [TestCase("item_active_reroll_action", ActiveUseContextKind.Battle)]
        [TestCase("item_active_reroll_action", ActiveUseContextKind.ActionSelect)]
        [TestCase("item_active_reroll_action", ActiveUseContextKind.Shop)]
        [TestCase("item_active_reroll_action", ActiveUseContextKind.Event)]
        [TestCase("item_active_reroll_action", ActiveUseContextKind.Reward)]
        [TestCase("item_active_half_next_action_cost", ActiveUseContextKind.Battle)]
        [TestCase("item_active_half_next_action_cost", ActiveUseContextKind.ActionSelect)]
        [TestCase("item_active_half_next_action_cost", ActiveUseContextKind.Shop)]
        [TestCase("item_active_half_next_action_cost", ActiveUseContextKind.Event)]
        [TestCase("item_active_half_next_action_cost", ActiveUseContextKind.Reward)]
        public void DeferredActionItem_IsUsableAndAppliesInEveryContext(
            string itemId,
            ActiveUseContextKind contextKind)
        {
            ItemDefinition item = ItemDefinition.Get(
                _tables,
                itemId,
                cfg.ItemKind.Active);
            Assert.That(item, Is.Not.Null, itemId);

            var run = new GameRun(
                _tables,
                _database,
                "glutton_dog",
                $"deferred-item-anytime-{itemId}-{contextKind}");
            run.AcquireItem(itemId, fallbackGold: 0);
            Assert.That(run.HasItem(itemId), Is.True);

            Assert.That(
                ItemActiveUsage.CanUse(run, item, contextKind, out string reason),
                Is.True,
                reason);

            ActiveItemUseResult result = ActiveItemEffectRegistry.Apply(
                CreateContext(run, contextKind),
                item,
                Array.Empty<ActiveTarget>());
            Assert.That(result.Success, Is.True, result.Message);

            if (item.EffectType == ItemEffectTypes.DoubleNextBusinessReward)
            {
                Assert.That(run.NextBusinessRewardDoubleStacks, Is.EqualTo(1));
            }
            else
            {
                Assert.That(run.NextDailyActionHalfCostStacks, Is.EqualTo(1));
            }
        }

        [Test]
        public void CakeLayerBuffHud_HasNoDeferredActionItemBindingApi()
        {
            string[] methodNames = typeof(CakeLayerBuffHud)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(method => method.Name)
                .ToArray();

            Assert.That(methodNames.Any(name => name.Contains("HalfDay")), Is.False);
            Assert.That(methodNames.Any(name => name.Contains("RewardDouble")), Is.False);
        }

        private static IActiveUseContext CreateContext(
            GameRun run,
            ActiveUseContextKind contextKind)
        {
            return contextKind switch
            {
                ActiveUseContextKind.Battle => new BattleUseContext(null, run),
                ActiveUseContextKind.ActionSelect => new ActionSelectUseContext(run, null),
                ActiveUseContextKind.Shop => new ShopUseContext(run),
                ActiveUseContextKind.Event =>
                    new ShopUseContext(run, contextKind: ActiveUseContextKind.Event),
                ActiveUseContextKind.Reward =>
                    new ShopUseContext(run, contextKind: ActiveUseContextKind.Reward),
                _ => throw new ArgumentOutOfRangeException(nameof(contextKind), contextKind, null),
            };
        }
    }
}
#endif
