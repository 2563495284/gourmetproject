using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using GourmetProject.Game.Meta.Passives;
using Luban.SimpleJSON;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class CandidateItemScoringTests
    {
        [Test]
        public void AllCandidateItemIds_HavePassiveModels()
        {
            string[] ids =
            {
                "item_transfer_extra_targets", "item_flavored_flat", "item_flavored_count_as",
                "item_active_count_flat_all", "item_empty_active_slot_mult_all", "item_edge_flat",
                "item_non_edge_mult", "item_gold_per5_flat_all", "item_empty_cell_flat_all",
                "item_unused_discard_mult_all", "item_unused_discard_flat_all",
                "item_skip_reward_dish_luck", "item_skip_reward_passive_luck",
                "item_super_material_spread", "item_count_as_cake", "item_super_fragment_reward",
                "item_settle_permanent_flat_all", "item_same_base_mult_all",
                "item_remove_arrow_cookie_2", "item_remove_arrow_cookie_all",
                "item_randomize_recipe_dishes", "item_discard_dish_flat",
                "item_heart_capacity", "item_heart_capacity_deluxe", "item_restore_heart",
                "item_restore_hearts", "item_slot_cost_discount",
            };

            foreach (string id in ids)
            {
                Assert.That(PassiveItemModelRegistry.HasModel(id), Is.True, id);
            }
        }

        [Test]
        public void FlavoredAndPositionEffects_ApplyAtSettlementStart()
        {
            var table = new DiningTable(3, 3);
            table.Place(CreateDish(1, "edge", "edge", 10, 0, 0, new[] { "flavor_sweet" }));
            table.Place(CreateDish(2, "center", "center", 10, 1, 1));
            var specs = new[]
            {
                Spec(ItemScoreEffectType.TagBonus, 30f, "flavored"),
                Spec(ItemScoreEffectType.TagCountAsBonus, 1f, "flavored"),
                Spec(ItemScoreEffectType.TagBonus, 50f, "position:edge"),
                Spec(ItemScoreEffectType.TagMultFlat, 0.5f, "position:non-edge"),
            };

            ScoreResult result = Calculate(table, specs);
            DishScore edge = result.DishScores.Single(score => score.DishId == "edge");
            DishScore center = result.DishScores.Single(score => score.DishId == "center");

            Assert.That(edge.FlatBonus.ToDouble(), Is.EqualTo(80d));
            Assert.That(edge.Multiplier.ToDouble(), Is.EqualTo(1d));
            Assert.That(edge.EffectiveCountAs, Is.EqualTo(2));
            Assert.That(center.FlatBonus.ToDouble(), Is.EqualTo(0d));
            Assert.That(center.Multiplier.ToDouble(), Is.EqualTo(1.5d));
            Assert.That(center.EffectiveCountAs, Is.EqualTo(1));
        }

        [Test]
        public void EmptyCellsAndUnusedDiscards_AreReadFromSnapshot()
        {
            var table = new DiningTable(2, 2);
            table.Place(CreateDish(1, "dish", "dish", 10, 0, 0));
            var specs = new[]
            {
                Spec(ItemScoreEffectType.AllDishFlatPerEmptyCell, 10f, string.Empty),
                Spec(ItemScoreEffectType.AllDishFlatPerUnusedDiscard, 10f, string.Empty),
                Spec(ItemScoreEffectType.AllDishMultPerUnusedDiscard, 0.1f, string.Empty),
            };

            ScoreResult result = Calculate(table, specs, remainingFoodDiscards: 2);
            DishScore dish = result.DishScores.Single();

            Assert.That(dish.FlatBonus.ToDouble(), Is.EqualTo(50d));
            Assert.That(dish.Multiplier.ToDouble(), Is.EqualTo(1.2d).Within(0.0001d));
        }

        [Test]
        public void SameBaseAndPerDishPermanent_IgnoreExcludedDish()
        {
            var table = new DiningTable(3, 1);
            table.Place(CreateDish(1, "variant_a", "shared", 10, 0, 0));
            table.Place(CreateDish(2, "variant_b", "shared", 10, 1, 0));
            DishInstance excluded = CreateDish(3, "variant_c", "other", 10, 2, 0);
            excluded.ExcludeFromScore();
            table.Place(excluded);
            var specs = new[]
            {
                Spec(ItemScoreEffectType.SameBaseDishMultFlat, 0.5f, "key:baseId;min:2"),
                Spec(ItemScoreEffectType.PerDishPermanentFlat, 3f, string.Empty),
            };

            ScoreResult result = Calculate(table, specs);

            Assert.That(result.DishScores, Has.Count.EqualTo(2));
            Assert.That(result.DishScores.All(score => Math.Abs(score.Multiplier.ToDouble() - 1.5d) < 0.0001d), Is.True);
            Assert.That(result.PermanentFlatDeltas.Keys, Is.EquivalentTo(new[] { 1, 2 }));
            Assert.That(result.PermanentFlatDeltas.Values.All(value => value.ToDouble() == 3d), Is.True);
        }

        [Test]
        public void CountThreshold_AddsToEveryDishMultiplier_NotFinalMultiplier()
        {
            var table = new DiningTable(2, 1);
            table.Place(CreateDish(1, "a", "a", 10, 0, 0));
            table.Place(CreateDish(2, "b", "b", 10, 1, 0));

            ScoreResult result = Calculate(
                table,
                new[] { Spec(ItemScoreEffectType.CountThresholdFinalMult, 0.5f, "gte:2") });

            Assert.That(result.DishScores.All(score => Math.Abs(score.Multiplier.ToDouble() - 1.5d) < 0.0001d), Is.True);
            Assert.That(result.FinalMultiplier.ToDouble(), Is.EqualTo(1d));
        }

        [Test]
        public void AllDishTemporaryCategory_IsLiveOnlyAndFeedsLaterItemMatchers()
        {
            var table = new DiningTable(1, 1);
            DishInstance dish = CreateDish(1, "dish", "dish", 10, 0, 0);
            table.Place(dish);
            var specs = new[]
            {
                Spec(ItemScoreEffectType.TagBonus, 7f, "category:cake"),
                Spec(ItemScoreEffectType.AllDishTemporaryCategory, 0f, "category:cake"),
            };

            ScoreResult result = Calculate(table, specs);

            Assert.That(result.DishScores.Single().FlatBonus.ToDouble(), Is.EqualTo(7d));
            Assert.That(dish.IsCategory("cake"), Is.False);
            Assert.That(result.TemporaryCategories, Is.Empty);
        }

        [Test]
        public void AllDishTemporaryCategory_FeedsRowCategorySkillsAndTheirTrace()
        {
            var rule = new SkillRuleDef(
                "rule_cake_row",
                "skill_cake_row",
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.AddFlat,
                SkillScope.RowAndSelf,
                0,
                new[] { 7f },
                new[] { "cat:cake" });
            var skill = new SkillDef(
                "skill_cake_row",
                "蛋糕同行加分",
                string.Empty,
                Array.Empty<string>(),
                new[] { rule },
                new[] { string.Empty });
            var table = new DiningTable(2, 1);
            table.Place(CreateDish(1, "source", "source", 10, 0, 0, skills: new[] { skill.Id }));
            table.Place(CreateDish(2, "target", "target", 10, 1, 0));
            var db = new GameplayDatabase(
                Array.Empty<DishDef>(),
                new[] { skill },
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var calculator = new ScoreCalculator(effectSources: new[]
            {
                new ItemScoreEffectSource(new[]
                {
                    Spec(ItemScoreEffectType.AllDishTemporaryCategory, 0f, "category:cake"),
                }),
            });

            ScoreResult result = calculator.Calculate(table, db);

            Assert.That(result.DishScores.All(score => score.FlatBonus.ToDouble() == 7d), Is.True);
            ScoreLine targetLine = result.ScoreLines.Single(line =>
                line.Kind == ScoreLineKind.DishFlat && line.DishInstanceId == 2);
            Assert.That(targetLine.Trace, Is.Not.Null);
            Assert.That(targetLine.Trace.VisualTargetDishInstanceIds, Is.EquivalentTo(new[] { 1, 2 }));
        }

        [Test]
        public void RewardAbandonLuck_IsPurposeScopedAndRoundTripsState()
        {
            (GameRun run, RewardAbandonDishLuckModel model) = BindModel<RewardAbandonDishLuckModel>(
                "item_skip_reward_dish_luck",
                10f);

            model.OnRewardAbandoned();
            model.OnRewardAbandoned();

            Assert.That(model.HiddenScoreOffset(HiddenScorePurpose.Dish), Is.EqualTo(20f));
            Assert.That(model.HiddenScoreOffset(HiddenScorePurpose.PassiveItem), Is.Zero);
            string state = model.CaptureState();

            (_, RewardAbandonDishLuckModel restored) = BindModel<RewardAbandonDishLuckModel>(
                "item_skip_reward_dish_luck",
                10f);
            restored.RestoreState(state);
            Assert.That(restored.InfoText, Is.EqualTo("2"));
            Assert.That(restored.HiddenScoreOffset(HiddenScorePurpose.Dish), Is.EqualTo(20f));
            Assert.That(run, Is.Not.Null);
        }

        [Test]
        public void HeartCapacityRemoval_ClampsCurrentHeartsWithoutRestoringThemLater()
        {
            (GameRun run, _) = BindModel<HeartCapacityBonusModel>("item_heart_capacity", 1f);
            SetPrivateField(run, "_heartCapacity", 3);
            SetPrivateField(run, "_heartsRemaining", 4);

            Assert.That(run.HeartCapacity, Is.EqualTo(4));
            Assert.That(run.HeartsRemaining, Is.EqualTo(4));
            Assert.That(run.RemoveItem("item_heart_capacity"), Is.True);
            Assert.That(run.HeartCapacity, Is.EqualTo(3));
            Assert.That(run.HeartsRemaining, Is.EqualTo(3));
        }

        [Test]
        public void SlotCostDiscount_IsUsedForIntegerCostAndKeepsPositiveMinimum()
        {
            (GameRun run, _) = BindModel<SlotCostDiscountModel>("item_slot_cost_discount", 0.25f);
            var runtime = new ItemRuntime(run);

            Assert.That(runtime.ModifySlotSpinCost(100), Is.EqualTo(75));
            Assert.That(runtime.ModifySlotSpinCost(10), Is.EqualTo(7));
            Assert.That(runtime.ModifySlotSpinCost(1), Is.EqualTo(1));
            Assert.That(runtime.ModifySlotSpinCost(0), Is.Zero);
        }

        private static ItemScoreSpec Spec(ItemScoreEffectType type, float value, string param)
            => new ItemScoreSpec(type, value, param, "candidate", "候选装饰品");

        private static ScoreResult Calculate(
            DiningTable table,
            ItemScoreSpec[] specs,
            int remainingFoodDiscards = 0)
        {
            var calculator = new ScoreCalculator(
                effectSources: new[] { new ItemScoreEffectSource(specs) });
            return calculator.Calculate(
                table,
                EmptyDatabase(),
                remainingFoodDiscards: remainingFoodDiscards);
        }

        private static DishInstance CreateDish(
            int instanceId,
            string id,
            string baseId,
            int deliciousness,
            int x,
            int y,
            string[] flavors = null,
            string[] skills = null)
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            var def = new DishDef(
                id,
                id,
                deliciousness,
                shape,
                0,
                0,
                1f,
                skills ?? Array.Empty<string>(),
                string.Empty,
                baseId: baseId);
            return new DishInstance(
                instanceId,
                def,
                new Placement(shape, 0, new GridPos(x, y)),
                skills ?? Array.Empty<string>(),
                flavors ?? Array.Empty<string>());
        }

        private static (GameRun Run, T Model) BindModel<T>(string itemId, float value)
            where T : PassiveItemModel, new()
        {
#pragma warning disable SYSLIB0050
            var run = (GameRun)FormatterServices.GetUninitializedObject(typeof(GameRun));
#pragma warning restore SYSLIB0050
            var state = new RunItemState(itemId, 1);
            SetPrivateField(run, "_items", new List<RunItemState> { state });
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
            return (run, model);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        private static GameplayDatabase EmptyDatabase()
            => new GameplayDatabase(
                Array.Empty<DishDef>(),
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
    }
}
