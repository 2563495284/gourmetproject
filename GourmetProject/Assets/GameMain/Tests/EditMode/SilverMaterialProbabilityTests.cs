using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SilverMaterialProbabilityTests
    {
        [Test]
        public void MaterialDef_ItemRollProbabilityUsesConfiguredValueAndLegacyFallback()
        {
            Assert.That(Silver("configured", new[] { 0.2f }).ItemRollProbability,
                Is.EqualTo(0.2f).Within(0.000001f));
            Assert.That(Silver("missing", Array.Empty<float>()).ItemRollProbability,
                Is.EqualTo(0.5f).Within(0.000001f));
            Assert.That(Silver("negative", new[] { -0.1f }).ItemRollProbability, Is.Zero);
            Assert.That(Silver("overflow", new[] { 1.1f }).ItemRollProbability, Is.EqualTo(1f));
            Assert.That(Silver("invalid", new[] { float.NaN }).ItemRollProbability,
                Is.EqualTo(0.5f).Within(0.000001f));
        }

        [Test]
        public void ScoreResult_LegacySilverCountExpandsToHalfProbabilityRequests()
        {
            var legacy = new ScoreResult(
                Array.Empty<DishScore>(),
                0f,
                0f,
                1f,
                silverItemRollRequests: 2);

            Assert.That(legacy.SilverItemRollRequests, Is.EqualTo(2));
            Assert.That(legacy.SilverItemRolls.Count, Is.EqualTo(2));
            Assert.That(legacy.SilverItemRolls[0].Probability, Is.EqualTo(0.5f));
            Assert.That(legacy.SilverItemRolls[1].Probability, Is.EqualTo(0.5f));

            var configured = new ScoreResult(
                Array.Empty<DishScore>(),
                0f,
                0f,
                1f,
                silverItemRollRequests: 3,
                silverItemRolls: new[] { new SilverItemRollRequest(0.2f) });

            Assert.That(configured.SilverItemRollRequests, Is.EqualTo(1),
                "显式逐条请求应覆盖旧整数参数，避免重复掷骰");
            Assert.That(configured.SilverItemRolls[0].Probability,
                Is.EqualTo(0.2f).Within(0.000001f));
        }

        [Test]
        public void ScoreContext_ParameterlessSilverApisKeepLegacyProbability()
        {
            var context = new ScoreContext(new ScoreSnapshot(
                new DiningTable(1, 1),
                Database(Array.Empty<DishDef>())));

            context.RequestSilverItemRoll();
            new RequestSilverItemRollCommand().Execute(context);
            ScoreResult result = context.ToResult();

            Assert.That(result.SilverItemRollRequests, Is.EqualTo(2));
            Assert.That(result.SilverItemRolls[0].Probability, Is.EqualTo(0.5f));
            Assert.That(result.SilverItemRolls[1].Probability, Is.EqualTo(0.5f));
        }

        [Test]
        public void ScoreCalculator_SilverMaterialCarriesProbabilityAndSourceOncePerDish()
        {
            MaterialDef silver = Silver("silver", new[] { 0.2f });
            DishShape shape = DishShape.FromRows(new[] { "XX" });
            DishInstance dish = Dish(7, "two_cell", shape, 0);
            var materials = new Dictionary<GridPos, IReadOnlyList<string>>
            {
                [new GridPos(0, 0)] = new[] { silver.Id },
                [new GridPos(1, 0)] = new[] { silver.Id },
            };
            var table = new DiningTable(2, 1, null, materials);
            table.Place(dish);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                Database(new[] { dish.Def }, new[] { silver }));

            Assert.That(result.SilverItemRollRequests, Is.EqualTo(1));
            Assert.That(result.SilverItemRolls.Count, Is.EqualTo(1));
            Assert.That(result.SilverItemRolls[0].Probability,
                Is.EqualTo(0.2f).Within(0.000001f));
            Assert.That(result.SilverItemRolls[0].DishInstanceId, Is.EqualTo(dish.Id));
            Assert.That(result.SilverItemRolls[0].MaterialId, Is.EqualTo(silver.Id));
        }

        [TestCase(0, 0f)]
        [TestCase(1, 6f)]
        [TestCase(2, 6f)]
        public void ScoreCalculator_GoldThresholdOneGrantsSixOncePerDish(
            int goldCellCount,
            float expectedGold)
        {
            MaterialDef gold = Gold("gold", 6f);
            DishShape shape = DishShape.FromRows(new[] { "XX" });
            DishInstance dish = Dish(8, "gold_dish", shape, 0);
            var materials = new Dictionary<GridPos, IReadOnlyList<string>>();
            for (int x = 0; x < goldCellCount; x++)
            {
                materials[new GridPos(x, 0)] = new[] { gold.Id };
            }

            var table = new DiningTable(2, 1, null, materials);
            table.Place(dish);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                Database(new[] { dish.Def }, new[] { gold }));

            Assert.That(result.GoldDelta, Is.EqualTo(expectedGold).Within(0.000001f));
        }

        [Test]
        public void BattleSession_PreviewDoesNotRollSilverProbability()
        {
            MaterialDef silver = Silver("silver", new[] { 0.2f });
            DishInstance dish = Dish(1, "dish", DishShape.FromRows(new[] { "X" }), 0);
            var materials = new Dictionary<GridPos, IReadOnlyList<string>>
            {
                [new GridPos(0, 0)] = new[] { silver.Id },
            };
            var table = new DiningTable(1, 1, null, materials);
            table.Place(dish);
            var random = new RecordingRandomStream();
            var session = new BattleSession(
                table,
                Database(new[] { dish.Def }, new[] { silver }),
                random,
                Array.Empty<RecipeSlot>(),
                requiredScore: 0);

            ScoreResult preview = session.PreviewScore();

            Assert.That(preview.SilverItemRollRequests, Is.EqualTo(1));
            Assert.That(preview.SilverItemRolls[0].Probability,
                Is.EqualTo(0.2f).Within(0.000001f));
            Assert.That(random.Probabilities, Is.Empty);
            Assert.That(session.PendingActiveItemGrants, Is.Zero);
        }

        [Test]
        public void BattleSession_SettleRollsEachConfiguredProbabilityInOrder()
        {
            MaterialDef low = Silver("silver_low", new[] { 0.2f });
            MaterialDef high = Silver("silver_high", new[] { 0.7f });
            DishShape shape = DishShape.FromRows(new[] { "X" });
            DishInstance left = Dish(1, "left", shape, 0);
            DishInstance right = Dish(2, "right", shape, 1);
            var materials = new Dictionary<GridPos, IReadOnlyList<string>>
            {
                [new GridPos(0, 0)] = new[] { low.Id },
                [new GridPos(1, 0)] = new[] { high.Id },
            };
            var table = new DiningTable(2, 1, null, materials);
            table.Place(left);
            table.Place(right);
            var random = new RecordingRandomStream();
            var session = new BattleSession(
                table,
                Database(new[] { left.Def, right.Def }, new[] { low, high }),
                random,
                Array.Empty<RecipeSlot>(),
                requiredScore: 0);

            ScoreResult result = session.Settle();

            Assert.That(result.SilverItemRollRequests, Is.EqualTo(2));
            Assert.That(random.Probabilities, Has.Count.EqualTo(2));
            Assert.That(random.Probabilities[0], Is.EqualTo(0.2d).Within(0.000001d));
            Assert.That(random.Probabilities[1], Is.EqualTo(0.7d).Within(0.000001d));
            Assert.That(session.PendingActiveItemGrants, Is.EqualTo(1));
        }

        private static MaterialDef Silver(string id, IReadOnlyList<float> values)
            => new MaterialDef(
                id,
                id,
                string.Empty,
                MaterialEffectType.GrantItemRollIfCellCount,
                values,
                new[] { "1" },
                string.Empty);

        private static MaterialDef Gold(string id, float value)
            => new MaterialDef(
                id,
                id,
                string.Empty,
                MaterialEffectType.GrantGoldIfCellCount,
                new[] { value },
                new[] { "1" },
                string.Empty);

        private static DishInstance Dish(int instanceId, string dishId, DishShape shape, int x)
        {
            var def = new DishDef(
                dishId,
                dishId,
                10,
                shape,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty);
            return new DishInstance(
                instanceId,
                def,
                new Placement(shape, 0, new GridPos(x, 0)),
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        private static GameplayDatabase Database(
            IReadOnlyList<DishDef> dishes,
            IReadOnlyList<MaterialDef> materials = null)
            => new GameplayDatabase(
                dishes,
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                materials ?? Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());

        private sealed class RecordingRandomStream : IRandomStream
        {
            private readonly Xoshiro256SS _inner = new Xoshiro256SS(991UL);

            public List<double> Probabilities { get; } = new List<double>();

            public RngState State
            {
                get => _inner.State;
                set => _inner.State = value;
            }

            public uint NextUInt() => _inner.NextUInt();

            public ulong NextULong() => _inner.NextULong();

            public int Range(int minInclusive, int maxExclusive)
                => _inner.Range(minInclusive, maxExclusive);

            public float Range(float minInclusive, float maxExclusive)
                => _inner.Range(minInclusive, maxExclusive);

            public float NextFloat() => _inner.NextFloat();

            public double NextDouble() => _inner.NextDouble();

            public bool NextBool(double probability = 0.5)
            {
                Probabilities.Add(probability);
                return probability >= 0.5;
            }

            public void Shuffle<T>(IList<T> list) => _inner.Shuffle(list);

            public T Pick<T>(IReadOnlyList<T> list) => _inner.Pick(list);

            public int WeightedPickIndex(IReadOnlyList<float> weights)
                => _inner.WeightedPickIndex(weights);
        }
    }
}
