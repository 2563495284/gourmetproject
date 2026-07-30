using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BattleServeCookiePityTests
    {
        [Test]
        public void AfterConfiguredCookieStreak_NextPreparePrefersFittingNonCookie()
        {
            DishDef cookie = Dish("cookie", "X");
            DishDef other = Dish("other", "XXX");
            var rng = new RecordingRandomStream(forcedWeightedIndex: 0);
            BattleSession session = CreateSession(
                new DiningTable(3, 1),
                new[] { cookie, other },
                new[] { "cookie", "cookie", "cookie", "cookie", "cookie", "other" },
                rng);
            session.ConfigureCookieServePity(4, new[] { "cookie" });
            session.ConfigureFoodDiscardLimit(6);

            for (int i = 0; i < 4; i++)
            {
                ServePrepareResult prepared = session.PrepareServe(0);
                Assert.That(prepared.PreparedDish.Definition.Id, Is.EqualTo("cookie"));
                Assert.That(session.TryDiscardPreparedServe(), Is.True);
            }

            ServePrepareResult pityPrepare = session.PrepareServe(0);

            Assert.That(pityPrepare.Success, Is.True);
            Assert.That(pityPrepare.PreparedDish.Definition.Id, Is.EqualTo("other"));
            Assert.That(session.ConsecutiveCookiePrepares, Is.Zero);
        }

        [Test]
        public void PityWithoutFittingNonCookie_FallsBackToFittingCookie()
        {
            DishDef cookie = Dish("cookie", "X");
            DishDef tooLarge = Dish("other", "XX");
            BattleSession session = CreateSession(
                new DiningTable(1, 1),
                new[] { cookie, tooLarge },
                new[] { "cookie", "cookie", "cookie", "cookie", "cookie", "other" },
                new RecordingRandomStream(forcedWeightedIndex: 0));
            session.ConfigureCookieServePity(4, new[] { "cookie" });
            session.ConfigureFoodDiscardLimit(6);

            for (int i = 0; i < 5; i++)
            {
                ServePrepareResult prepared = session.PrepareServe(0);
                Assert.That(prepared.Success, Is.True);
                Assert.That(prepared.PreparedDish.Definition.Id, Is.EqualTo("cookie"));
                Assert.That(session.TryDiscardPreparedServe(), Is.True);
            }

            Assert.That(session.ConsecutiveCookiePrepares, Is.EqualTo(5));
        }

        [Test]
        public void FittingCandidates_AreWeightedByOccupiedCellCount()
        {
            DishDef small = Dish("small", "X");
            DishDef large = Dish("large", "XXX");
            var rng = new RecordingRandomStream(forcedWeightedIndex: 1);
            BattleSession session = CreateSession(
                new DiningTable(3, 1),
                new[] { small, large },
                new[] { "small", "large" },
                rng);

            ServePrepareResult prepared = session.PrepareServe(0);

            Assert.That(prepared.Success, Is.True);
            Assert.That(prepared.PreparedDish.Definition.Id, Is.EqualTo("large"));
            Assert.That(rng.LastWeights, Is.EqualTo(new[] { 1f, 3f }));
        }

        [Test]
        public void CookieList_MatchesConfiguredBaseDishId()
        {
            DishDef flavoredCookie = Dish("cookie_sweet", "X", baseId: "cookie_base");
            BattleSession session = CreateSession(
                new DiningTable(1, 1),
                new[] { flavoredCookie },
                new[] { flavoredCookie.Id },
                new RecordingRandomStream(forcedWeightedIndex: 0));
            session.ConfigureCookieServePity(4, new[] { "cookie_base" });

            Assert.That(session.PrepareServe(0).Success, Is.True);
            Assert.That(session.ConsecutiveCookiePrepares, Is.EqualTo(1));
        }

        [Test]
        public void FailedPrepare_DoesNotChangeCookieStreak()
        {
            DishDef cookie = Dish("cookie", "X");
            BattleSession session = CreateSession(
                new DiningTable(1, 1),
                new[] { cookie },
                new[] { "cookie", "cookie" },
                new RecordingRandomStream(forcedWeightedIndex: 0));
            session.ConfigureCookieServePity(4, new[] { "cookie" });
            session.ConfigureFoodDiscardLimit(1);
            Assert.That(session.PrepareServe(0).Success, Is.True);
            Assert.That(session.TryDiscardPreparedServe(), Is.True);
            session.MaxServes = 0;

            ServePrepareResult failed = session.PrepareServe(0);

            Assert.That(failed.Outcome, Is.EqualTo(ServePrepareOutcome.LimitReached));
            Assert.That(session.ConsecutiveCookiePrepares, Is.EqualTo(1));
        }

        [Test]
        public void GameBaseCookieIds_AllResolveToConfiguredDishes()
        {
            var config = new ConfigService();
            config.LoadAll();
            GameplayDatabase database = GameplayContentBuilder.BuildDatabase(config.Tables);

            Assert.That(config.Tables.TbGameBase.ServeCookiePityCount, Is.GreaterThanOrEqualTo(0));
            Assert.That(config.Tables.TbGameBase.ServeCookieDishIds, Is.Not.Null);
            foreach (string dishId in config.Tables.TbGameBase.ServeCookieDishIds)
            {
                Assert.That(
                    database.AllDishes.Any(dish => dish.Id == dishId || dish.BaseId == dishId),
                    Is.True,
                    $"game_base 中配置的饼干食物 '{dishId}' 不存在。");
            }
        }

        private static BattleSession CreateSession(
            DiningTable table,
            IReadOnlyList<DishDef> dishes,
            IReadOnlyList<string> entries,
            IRandomStream rng)
        {
            var database = new GameplayDatabase(
                dishes,
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            return new BattleSession(
                table,
                database,
                rng,
                new[] { new RecipeSlot("test", entries) },
                requiredScore: 0);
        }

        private static DishDef Dish(string id, string shapeRow, string baseId = null)
        {
            return new DishDef(
                id,
                id,
                1,
                DishShape.FromRows(new[] { shapeRow }),
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                allowRotate: false,
                baseId: baseId);
        }

        private sealed class RecordingRandomStream : IRandomStream
        {
            private readonly int _forcedWeightedIndex;

            public RecordingRandomStream(int forcedWeightedIndex)
            {
                _forcedWeightedIndex = forcedWeightedIndex;
            }

            public IReadOnlyList<float> LastWeights { get; private set; } = Array.Empty<float>();

            public RngState State { get; set; }

            public uint NextUInt() => 0;

            public ulong NextULong() => 0;

            public int Range(int minInclusive, int maxExclusive) => minInclusive;

            public float Range(float minInclusive, float maxExclusive) => minInclusive;

            public float NextFloat() => 0f;

            public double NextDouble() => 0.0;

            public bool NextBool(double probability = 0.5) => probability > 0.0;

            public void Shuffle<T>(IList<T> list)
            {
            }

            public T Pick<T>(IReadOnlyList<T> list) => list[0];

            public int WeightedPickIndex(IReadOnlyList<float> weights)
            {
                LastWeights = weights.ToArray();
                return Math.Max(0, Math.Min(_forcedWeightedIndex, weights.Count - 1));
            }
        }
    }
}
