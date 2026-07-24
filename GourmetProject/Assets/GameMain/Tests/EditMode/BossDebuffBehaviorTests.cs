using System;
using System.Reflection;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BossDebuffBehaviorTests
    {
        private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

        [Test]
        public void SplitBoardModifiers_ChangeOnlyTheirConfiguredAxis()
        {
            DiningTable indulgent = LShapedTable();
            ApplyShapeModifier(indulgent, BossDebuffModifiers.Indulgent);
            Assert.That(indulgent.Exists(new GridPos(0, 2)), Is.True);
            Assert.That(indulgent.Exists(new GridPos(2, 0)), Is.False);

            DiningTable binge = LShapedTable();
            ApplyShapeModifier(binge, BossDebuffModifiers.Binge);
            Assert.That(binge.Exists(new GridPos(0, 2)), Is.False);
            Assert.That(binge.Exists(new GridPos(2, 0)), Is.True);

            DiningTable kidsMeal = new DiningTable(3, 3);
            ApplyShapeModifier(kidsMeal, BossDebuffModifiers.KidsMeal);
            Assert.That(kidsMeal.Exists(new GridPos(0, 2)), Is.False);
            Assert.That(kidsMeal.Exists(new GridPos(2, 0)), Is.True);

            DiningTable weightLoss = new DiningTable(3, 3);
            ApplyShapeModifier(weightLoss, BossDebuffModifiers.WeightLoss);
            Assert.That(weightLoss.Exists(new GridPos(0, 2)), Is.True);
            Assert.That(weightLoss.Exists(new GridPos(2, 0)), Is.False);
        }

        [Test]
        public void SplitBoardModifiers_KeepTheirOriginalTargetScoreTiers()
        {
            Assert.That(ApplyRequiredScoreModifier(100, BossDebuffModifiers.Indulgent), Is.EqualTo(150));
            Assert.That(ApplyRequiredScoreModifier(100, BossDebuffModifiers.Binge), Is.EqualTo(150));
            Assert.That(ApplyRequiredScoreModifier(100, BossDebuffModifiers.KidsMeal), Is.EqualTo(80));
            Assert.That(ApplyRequiredScoreModifier(100, BossDebuffModifiers.WeightLoss), Is.EqualTo(80));
        }

        [Test]
        public void UpdatedSessionModifiers_MatchBossDebuffTable()
        {
            BattleSession session = EmptySession();
            session.ConfigureFoodDiscardLimit(3);

            ApplySessionModifier(session, BossDebuffModifiers.Omakase);
            ApplySessionModifier(session, BossDebuffModifiers.DineAndDash);
            ApplySessionModifier(session, BossDebuffModifiers.CarbMeal);
            ApplySessionModifier(session, BossDebuffModifiers.Appetizer);

            Assert.That(session.FoodDiscardLimit, Is.Zero);
            Assert.That(session.GoldCostPerBellServe, Is.EqualTo(5));
            Assert.That(session.BellServeMantouChance, Is.EqualTo(0.5f));
            Assert.That(session.RemoveFirstServedDishes, Is.True);
            Assert.That(session.FirstServedDishesToRemove, Is.EqualTo(2));
        }

        private static DiningTable LShapedTable()
        {
            return new DiningTable(
                3,
                3,
                new[]
                {
                    new GridPos(0, 0),
                    new GridPos(1, 0),
                    new GridPos(0, 1),
                },
                null);
        }

        private static BattleSession EmptySession()
        {
            return new BattleSession(
                new DiningTable(1, 1),
                new GameplayDatabase(
                    Array.Empty<DishDef>(),
                    Array.Empty<SkillDef>(),
                    Array.Empty<FlavorDef>(),
                    Array.Empty<MaterialDef>(),
                    Array.Empty<RecipeDef>()),
                new DeterministicRandomStream(),
                Array.Empty<RecipeSlot>(),
                0);
        }

        private static void ApplyShapeModifier(DiningTable table, string modifier)
        {
            MethodInfo method = typeof(BattleSessionFactory).GetMethod("ApplyShapeModifier", StaticPrivate);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { table, modifier });
        }

        private static int ApplyRequiredScoreModifier(int score, string modifier)
        {
            MethodInfo method = typeof(BattleSessionFactory).GetMethod("ApplyRequiredScoreModifier", StaticPrivate);
            Assert.That(method, Is.Not.Null);
            return (int)method.Invoke(null, new object[] { score, modifier });
        }

        private static void ApplySessionModifier(BattleSession session, string modifier)
        {
            MethodInfo method = typeof(BattleSessionFactory).GetMethod("ApplySessionModifiers", StaticPrivate);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { session, modifier });
        }

        private sealed class DeterministicRandomStream : IRandomStream
        {
            public RngState State { get; set; }

            public uint NextUInt() => 0;
            public ulong NextULong() => 0;
            public int Range(int minInclusive, int maxExclusive) => minInclusive;
            public float Range(float minInclusive, float maxExclusive) => minInclusive;
            public float NextFloat() => 0f;
            public double NextDouble() => 0d;
            public bool NextBool(double probability = 0.5) => probability > 0d;
            public void Shuffle<T>(System.Collections.Generic.IList<T> list) { }
            public T Pick<T>(System.Collections.Generic.IReadOnlyList<T> list) => list[0];
            public int WeightedPickIndex(System.Collections.Generic.IReadOnlyList<float> weights) => 0;
        }
    }
}
