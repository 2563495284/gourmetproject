using System;
using System.Collections.Generic;
using System.IO;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class PassiveTrashItemTests
    {
        [TestCase("item_trash_upgrade", 1)]
        [TestCase("item_trash_expand", 1)]
        [TestCase("item_trash_evolve", 2)]
        [TestCase("item_trash_mutate", 2)]
        public void TrashPassivesRegisterConfiguredDiscardBonus(string itemId, int expectedBonus)
        {
            cfg.PassiveItem definition = PassiveTable().Get(itemId);
            PassiveItemModel model = PassiveItemModelRegistry.Create(itemId);
            model.Bind(null, ItemDefinition.From(definition), new RunItemState(itemId, 1));

            Assert.That(model, Is.TypeOf<FoodDiscardLimitBonusModel>());
            Assert.That(model.FoodDiscardLimitBonus(), Is.EqualTo(expectedBonus));
        }

        [Test]
        public void RhythmMasterAddsConfiguredFlatMultiplierToDishAfterThreeServes()
        {
            const string itemId = "item_every3_next_mult";
            cfg.PassiveItem definition = PassiveTable().Get(itemId);
            PassiveItemModel model = PassiveItemModelRegistry.Create(itemId);
            model.Bind(null, ItemDefinition.From(definition), new RunItemState(itemId, 1));

            DishShape shape = DishShape.FromRows(new[] { "X" });
            DishDef dish = Dish("dish", shape);
            var session = new BattleSession(
                new DiningTable(4, 1),
                Database(dish),
                new FirstRandomStream(),
                new[] { new RecipeSlot("recipe", new[] { dish.Id, dish.Id, dish.Id, dish.Id }) },
                0);
            model.ApplyToBattle(session);

            ServeResult first = ServeNext(session);
            ServeResult second = ServeNext(session);
            ServeResult third = ServeNext(session);
            ServeResult fourth = ServeNext(session);

            Assert.That(definition.EffectValue, Is.EqualTo(1f));
            Assert.That(first.Dish.ServeMultiplierFlatBonus, Is.Zero);
            Assert.That(second.Dish.ServeMultiplierFlatBonus, Is.Zero);
            Assert.That(third.Dish.ServeMultiplierFlatBonus, Is.Zero);
            Assert.That(fourth.Dish.ServeMultiplierFlatBonus, Is.EqualTo(1f));
        }

        private static cfg.TbPassiveItem PassiveTable()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Config", "tbpassiveitem.json");
            return new cfg.TbPassiveItem(JSON.Parse(File.ReadAllText(path)));
        }

        private static ServeResult ServeNext(BattleSession session)
        {
            PreparedServeDish prepared = session.PrepareServe(0).PreparedDish;
            return session.CommitPreparedServe(prepared.Placements[0]);
        }

        private static DishDef Dish(string id, DishShape shape)
        {
            return new DishDef(
                id,
                id,
                10,
                shape,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                false);
        }

        private static GameplayDatabase Database(params DishDef[] dishes)
        {
            return new GameplayDatabase(
                dishes,
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
        }

        private sealed class FirstRandomStream : IRandomStream
        {
            public RngState State { get; set; }

            public uint NextUInt() => 0;

            public ulong NextULong() => 0;

            public int Range(int minInclusive, int maxExclusive) => minInclusive;

            public float Range(float minInclusive, float maxExclusive) => minInclusive;

            public float NextFloat() => 0f;

            public double NextDouble() => 0d;

            public bool NextBool(double probability = 0.5) => probability > 0d;

            public void Shuffle<T>(IList<T> list)
            {
            }

            public T Pick<T>(IReadOnlyList<T> list) => list[0];

            public int WeightedPickIndex(IReadOnlyList<float> weights) => 0;
        }
    }
}
