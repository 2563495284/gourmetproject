using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BreakInfinity;
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

namespace GourmetProject.Tests.EditMode
{
    public sealed class CandidateItemIntegration112To120Tests
    {
        [Test]
        public void RemoveArrowCookies_TwoAndAll_KeepCompleteBeforeAfterSnapshots()
        {
            AssertArrowCookieRemoval(
                maxCount: 2,
                expectedRemovedIndices: new[] { 0, 2 },
                expectedRemainingIds: new[] { "normal_a", "arrow_cookie_gamma", "normal_b" });
            AssertArrowCookieRemoval(
                maxCount: int.MaxValue,
                expectedRemovedIndices: new[] { 0, 2, 3 },
                expectedRemainingIds: new[] { "normal_a", "normal_b" });
        }

        [Test]
        public void RandomizeAllRecipeDishes_ReplacesEveryDishAndPreservesSlotState()
        {
            GameplayDatabase database = Database(
                Dish("dish_a"),
                Dish("dish_b"),
                Dish("dish_c"),
                Dish("dish_d"));
            RecipeBookSlot[] slots =
            {
                StatefulSlot("dish_a", "flavor_a", "skill_a", 11d, 1.25d),
                StatefulSlot("dish_b", "flavor_b", "skill_b", 22d, 1.5d),
                StatefulSlot("dish_c", "flavor_c", "skill_c", 33d, 2d),
            };
            string[] originalIds = slots.Select(slot => slot.DishId).ToArray();
            var run = CreateRun(database, slots);

            RecipeMutationResult result = PassiveRecipeMutationService.RandomizeAllRecipeDishes(
                run,
                "万花筒菜单板",
                new Xoshiro256SS(114UL));

            Assert.That(result.Entries, Has.Count.EqualTo(slots.Length));
            for (int i = 0; i < slots.Length; i++)
            {
                RecipeBookSlot slot = run.RecipeEntries[i];
                RecipeMutationEntry mutation = result.Entries.Single(entry => entry.DishIndex == i);

                Assert.That(slot, Is.SameAs(slots[i]), $"slot {i} identity");
                Assert.That(slot.DishId, Is.Not.EqualTo(originalIds[i]), $"slot {i} dish");
                Assert.That(slot.ExtraFlavorIds, Is.EqualTo(new[] { $"flavor_{(char)('a' + i)}" }));
                Assert.That(slot.ExtraSkillIds, Is.EqualTo(new[] { $"skill_{(char)('a' + i)}" }));
                Assert.That(slot.ScoreFlatBonus.ToDouble(), Is.EqualTo(11d * (i + 1)));
                Assert.That(slot.ScoreMultiplier.ToDouble(),
                    Is.EqualTo(new[] { 1.25d, 1.5d, 2d }[i]).Within(0.0001d));

                Assert.That(mutation.Before.DishId, Is.EqualTo(originalIds[i]));
                Assert.That(mutation.After.DishId, Is.EqualTo(slot.DishId));
                Assert.That(mutation.After.FlavorIds, Is.EqualTo(slot.ExtraFlavorIds));
                Assert.That(mutation.After.SkillIds, Is.EqualTo(slot.ExtraSkillIds));
                Assert.That(mutation.After.ScoreFlatBonus.ToDouble(),
                    Is.EqualTo(slot.ScoreFlatBonus.ToDouble()));
                Assert.That(mutation.After.ScoreMultiplier.ToDouble(),
                    Is.EqualTo(slot.ScoreMultiplier.ToDouble()).Within(0.0001d));
            }
        }

        [Test]
        public void DiscardDishFlat_AllThreeSuccessfulBattlePaths_BoostOnlySourceRecipeSlot()
        {
            GameplayDatabase database = Database(Dish("dish_a"), Dish("dish_b"), Dish("dish_c"));
            RecipeBookSlot[] recipe =
            {
                new RecipeBookSlot("dish_a"),
                new RecipeBookSlot("dish_b"),
                new RecipeBookSlot("dish_c"),
            };
            var run = CreateRun(database, recipe);
            DiscardDishPermanentFlatModel model = BindModel<DiscardDishPermanentFlatModel>(
                run,
                "item_discard_dish_flat",
                15f);

            ExecuteDiscardPath(
                run,
                database,
                model,
                sourceDishIndex: 0,
                path: DiscardPath.PreparedServe);
            AssertRecipeFlatBonuses(run, 15d, 0d, 0d);

            ExecuteDiscardPath(
                run,
                database,
                model,
                sourceDishIndex: 1,
                path: DiscardPath.PlacedDish);
            AssertRecipeFlatBonuses(run, 15d, 15d, 0d);

            ExecuteDiscardPath(
                run,
                database,
                model,
                sourceDishIndex: 2,
                path: DiscardPath.TemporaryArea);
            AssertRecipeFlatBonuses(run, 15d, 15d, 15d);
        }

        [TestCase("item_restore_heart", 1f, 1, 2)]
        [TestCase("item_restore_hearts", 2f, 0, 2)]
        [TestCase("item_restore_hearts", 2f, 2, 3)]
        public void RestoreHeartItems_OnAcquire_RestoreExactAmountAndClampAtCapacity(
            string itemId,
            float effectValue,
            int heartsBefore,
            int expectedHearts)
        {
            var run = CreateRun(Database());
            SetPrivateField(run, "_heartCapacity", 3);
            SetPrivateField(run, "_heartsRemaining", heartsBefore);
            RestoreHeartOnAcquireModel model = BindModel<RestoreHeartOnAcquireModel>(
                run,
                itemId,
                effectValue);

            model.OnAcquired();

            Assert.That(run.HeartCapacity, Is.EqualTo(3));
            Assert.That(run.HeartsRemaining, Is.EqualTo(expectedHearts));
            Assert.That(model.IsIconUsed, Is.True);
        }

        private static void AssertArrowCookieRemoval(
            int maxCount,
            int[] expectedRemovedIndices,
            string[] expectedRemainingIds)
        {
            GameplayDatabase database = Database(
                Dish("arrow_cookie_alpha"),
                Dish("normal_a"),
                Dish("arrow_cookie_beta"),
                Dish("arrow_cookie_gamma"),
                Dish("normal_b"));
            RecipeBookSlot[] slots =
            {
                StatefulSlot("arrow_cookie_alpha", "flavor_a", "skill_a", 10d, 1.1d),
                StatefulSlot("normal_a", "flavor_b", "skill_b", 20d, 1.2d),
                StatefulSlot("arrow_cookie_beta", "flavor_c", "skill_c", 30d, 1.3d),
                StatefulSlot("arrow_cookie_gamma", "flavor_d", "skill_d", 40d, 1.4d),
                StatefulSlot("normal_b", "flavor_e", "skill_e", 50d, 1.5d),
            };
            string[] originalIds = slots.Select(slot => slot.DishId).ToArray();
            var run = CreateRun(database, slots);

            RecipeMutationResult result = PassiveRecipeMutationService.RemoveArrowCookies(
                run,
                "饼干回收",
                maxCount);

            Assert.That(result.BeforeRecipe.Select(snapshot => snapshot.DishId), Is.EqualTo(originalIds));
            Assert.That(result.AfterRecipe.Select(snapshot => snapshot.DishId), Is.EqualTo(expectedRemainingIds));
            Assert.That(run.RecipeEntries.Select(slot => slot.DishId), Is.EqualTo(expectedRemainingIds));
            Assert.That(result.Entries.Select(entry => entry.DishIndex), Is.EqualTo(expectedRemovedIndices));
            Assert.That(
                result.Entries.Select(entry => entry.Before.DishId),
                Is.EqualTo(expectedRemovedIndices.Select(index => originalIds[index])));
            Assert.That(result.Entries.All(entry => string.IsNullOrEmpty(entry.After.DishId)), Is.True);

            for (int i = 0; i < result.BeforeRecipe.Count; i++)
            {
                Assert.That(result.BeforeRecipe[i].FlavorIds,
                    Is.EqualTo(new[] { $"flavor_{(char)('a' + i)}" }));
                Assert.That(result.BeforeRecipe[i].SkillIds,
                    Is.EqualTo(new[] { $"skill_{(char)('a' + i)}" }));
                Assert.That(result.BeforeRecipe[i].ScoreFlatBonus.ToDouble(),
                    Is.EqualTo(10d * (i + 1)));
            }

            for (int i = 0; i < result.AfterRecipe.Count; i++)
            {
                RecipeDishSnapshot snapshot = result.AfterRecipe[i];
                RecipeBookSlot slot = run.RecipeEntries[i];
                Assert.That(snapshot.FlavorIds, Is.EqualTo(slot.ExtraFlavorIds));
                Assert.That(snapshot.SkillIds, Is.EqualTo(slot.ExtraSkillIds));
                Assert.That(snapshot.ScoreFlatBonus.ToDouble(),
                    Is.EqualTo(slot.ScoreFlatBonus.ToDouble()));
                Assert.That(snapshot.ScoreMultiplier.ToDouble(),
                    Is.EqualTo(slot.ScoreMultiplier.ToDouble()).Within(0.0001d));
            }
        }

        private static void ExecuteDiscardPath(
            GameRun run,
            GameplayDatabase database,
            DiscardDishPermanentFlatModel model,
            int sourceDishIndex,
            DiscardPath path)
        {
            string dishId = run.RecipeEntries[sourceDishIndex].DishId;
            var entry = new RecipeSlotEntry(
                dishId,
                null,
                null,
                BigDouble.One,
                sourceBookIndex: 0,
                sourceDishIndex: sourceDishIndex);
            var session = new BattleSession(
                new DiningTable(2, 2),
                database,
                new Xoshiro256SS((ulong)(115 + sourceDishIndex)),
                new[] { new RecipeSlot("recipe", new[] { entry }) },
                requiredScore: 0);
            session.ConfigureFoodDiscardLimit(1);
            model.ApplyToBattle(session);

            ServePrepareResult prepared = session.PrepareServe(0);
            Assert.That(prepared.Success, Is.True, path.ToString());

            switch (path)
            {
                case DiscardPath.PreparedServe:
                    Assert.That(session.TryDiscardPreparedServe(), Is.True);
                    break;

                case DiscardPath.PlacedDish:
                {
                    ServeResult served = session.CommitPreparedServe(prepared.PreparedDish.Placements[0]);
                    Assert.That(served.Success, Is.True);
                    Assert.That(session.TryDiscardPlacedDish(served.Dish), Is.True);
                    break;
                }

                case DiscardPath.TemporaryArea:
                {
                    ServeResult served = session.CommitPreparedServe(prepared.PreparedDish.Placements[0]);
                    Assert.That(served.Success, Is.True);
                    Assert.That(session.MoveDishToTemporaryAreaAfterRotationDelta(served.Dish.Id, 1), Is.True);
                    Assert.That(session.TryDiscardTemporaryAreaDish(served.Dish.Id), Is.True);
                    break;
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(path), path, null);
            }

            Assert.That(session.FoodDiscardsUsed, Is.EqualTo(1));
            Assert.That(session.FoodDiscardsRemaining, Is.Zero);
        }

        private static void AssertRecipeFlatBonuses(GameRun run, params double[] expected)
        {
            Assert.That(run.RecipeEntries, Has.Count.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(run.RecipeEntries[i].ScoreFlatBonus.ToDouble(),
                    Is.EqualTo(expected[i]),
                    $"recipe slot {i}");
            }
        }

        private static RecipeBookSlot StatefulSlot(
            string dishId,
            string flavorId,
            string skillId,
            double scoreFlat,
            double scoreMultiplier)
        {
            var slot = new RecipeBookSlot(dishId);
            slot.AddFlavor(flavorId);
            slot.AddExtraSkill(skillId);
            slot.AddScoreFlat(scoreFlat);
            slot.MultiplyScore(scoreMultiplier);
            return slot;
        }

        private static GameRun CreateRun(GameplayDatabase database, params RecipeBookSlot[] recipe)
        {
#pragma warning disable SYSLIB0050
            var run = (GameRun)FormatterServices.GetUninitializedObject(typeof(GameRun));
#pragma warning restore SYSLIB0050
            SetPrivateField(run, "<Database>k__BackingField", database);
            SetPrivateField(run, "_recipe", new List<RecipeBookSlot>(recipe));
            SetPrivateField(run, "_bonusDishIds", recipe.Select(slot => slot.DishId).ToList());
            SetPrivateField(run, "_items", new List<RunItemState>());
            return run;
        }

        private static T BindModel<T>(GameRun run, string itemId, float value)
            where T : PassiveItemModel, new()
        {
            var state = new RunItemState(itemId, 1);
            ((List<RunItemState>)PrivateField(run, "_items")).Add(state);
            string json = "{"
                + $"\"id\":\"{itemId}\","
                + "\"name\":\"候选装饰品\","
                + "\"desc\":\"测试\","
                + "\"quality\":0,"
                + "\"specialTags\":0,"
                + $"\"effectValue\":{value.ToString(System.Globalization.CultureInfo.InvariantCulture)},"
                + "\"effectParam\":\"\","
                + "\"baseWeight\":100,"
                + "\"hiddenRange\":{\"min\":10,\"max\":80},"
                + "\"targetScoreHiddenOffset\":0,"
                + "\"dishHiddenOffset\":0,"
                + "\"passiveItemHiddenOffset\":0,"
                + "\"fragmentHiddenOffset\":0,"
                + "\"termId\":\"\","
                + "\"price\":40}";
            ItemDefinition definition = ItemDefinition.From(new cfg.PassiveItem(JSON.Parse(json)));
            var model = new T();
            state.Model = model;
            model.Bind(run, definition, state);
            return model;
        }

        private static DishDef Dish(string id)
        {
            return new DishDef(
                id,
                id,
                10,
                DishShape.FromRows(new[] { "X" }),
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                allowRotate: false);
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

        private static object PrivateField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            return field.GetValue(target);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        private enum DiscardPath
        {
            PreparedServe,
            PlacedDish,
            TemporaryArea,
        }
    }
}
