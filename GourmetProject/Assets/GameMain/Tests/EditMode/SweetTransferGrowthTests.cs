using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BreakInfinity;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SweetTransferGrowthTests
    {
        private static readonly MethodInfo ApplyTransferRequestsMethod = typeof(BattleSession).GetMethod(
            "ApplyTransferRequests",
            BindingFlags.Instance | BindingFlags.NonPublic);

        [Test]
        public void TargetGrowth_IsAdditiveAndPreviewDoesNotMutate()
        {
            Fixture fixture = CreateFixture(targetCount: 1, transferCount: 1);
            fixture.Session.SweetTransferTargetMultiplier = 0.4f;

            ScoreResult preview = fixture.Session.PreviewScore();

            Assert.That(preview.SkillTransfers, Has.Count.EqualTo(1));
            Assert.That(Value(fixture.Targets[0].PermanentMultBonus), Is.EqualTo(2d).Within(1e-9));
            Assert.That(fixture.Session.LastRecipeScoreMultiplierDeltas, Is.Empty);

            fixture.Session.Settle();

            Assert.That(Value(fixture.Targets[0].PermanentMultBonus), Is.EqualTo(2.4d).Within(1e-9));
            Assert.That(fixture.Session.LastRecipeScoreMultiplierDeltas, Has.Count.EqualTo(1));
            Assert.That(
                Value(fixture.Session.LastRecipeScoreMultiplierDeltas[0].Multiplier),
                Is.EqualTo(1.2d).Within(1e-9));
        }

        [Test]
        public void SourceGrowth_PerSuccessfulTargetTelescopesWithoutCompounding()
        {
            Fixture fixture = CreateFixture(targetCount: 2, transferCount: 2);
            fixture.Session.SweetTransferSourceMultiplier = 0.5f;

            fixture.Session.Settle();

            Assert.That(Value(fixture.Source.PermanentMultBonus), Is.EqualTo(3d).Within(1e-9));
            Assert.That(fixture.Session.LastRecipeScoreMultiplierDeltas, Has.Count.EqualTo(2));
            double writtenBack = fixture.Session.LastRecipeScoreMultiplierDeltas
                .Aggregate(2d, (value, delta) => value * Value(delta.Multiplier));
            Assert.That(writtenBack, Is.EqualTo(3d).Within(1e-9));
        }

        [Test]
        public void FailedTransferAndZeroLegalTargets_DoNotCreateGrowth()
        {
            Fixture fixture = CreateFixture(targetCount: 0, transferCount: 1);
            fixture.Session.SweetTransferSourceMultiplier = 0.5f;

            fixture.Session.Settle();

            Assert.That(Value(fixture.Source.PermanentMultBonus), Is.EqualTo(2d).Within(1e-9));
            Assert.That(fixture.Session.LastRecipeScoreMultiplierDeltas, Is.Empty);
        }

        [Test]
        public void DishWithoutRecipeSource_GrowsOnlyItsRuntimeInstance()
        {
            Fixture fixture = CreateFixture(targetCount: 1, transferCount: 1, targetsHaveRecipeSource: false);
            fixture.Session.SweetTransferTargetMultiplier = 0.4f;

            fixture.Session.Settle();

            Assert.That(Value(fixture.Targets[0].PermanentMultBonus), Is.EqualTo(2.4d).Within(1e-9));
            Assert.That(fixture.Session.LastRecipeScoreMultiplierDeltas, Is.Empty);
        }

        [Test]
        public void OnServeGrowth_IsPreservedByFormalSettlement()
        {
            Fixture fixture = CreateFixture(targetCount: 1, transferCount: 0);
            fixture.Session.SweetTransferTargetMultiplier = 0.4f;
            var request = new SkillTransferRequest(
                fixture.Source.Id,
                fixture.Source.Def.Name,
                new[] { fixture.Targets[0].Id },
                new[] { CreateNoOpEffect() },
                count: 1);

            InvokeApplyTransferRequests(fixture.Session, new[] { request });
            Assert.That(fixture.Session.LastRecipeScoreMultiplierDeltas, Has.Count.EqualTo(1));

            fixture.Session.Settle();

            Assert.That(Value(fixture.Targets[0].PermanentMultBonus), Is.EqualTo(2.4d).Within(1e-9));
            Assert.That(fixture.Session.LastRecipeScoreMultiplierDeltas, Has.Count.EqualTo(1));
            Assert.That(
                Value(fixture.Session.LastRecipeScoreMultiplierDeltas[0].Multiplier),
                Is.EqualTo(1.2d).Within(1e-9));
        }

        [Test]
        public void RecipeGrowthMarker_IsIdempotent()
        {
            Fixture fixture = CreateFixture(targetCount: 1, transferCount: 1);
            fixture.Session.SweetTransferTargetMultiplier = 0.4f;
            fixture.Session.Settle();

            Assert.That(fixture.Session.TryMarkRunRecipeGrowthApplied(), Is.True);
            Assert.That(fixture.Session.TryMarkRunRecipeGrowthApplied(), Is.False);
            Assert.That(fixture.Session.LastRecipeScoreMultiplierDeltas, Has.Count.EqualTo(1));
        }

        private static Fixture CreateFixture(
            int targetCount,
            int transferCount,
            bool targetsHaveRecipeSource = true)
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            SkillDef transferSkill = CreateTransferSkill(transferCount);
            var defs = new List<DishDef>
            {
                CreateDish("source", "来源", shape, new[] { transferSkill.Id }),
            };
            for (int i = 0; i < targetCount; i++)
            {
                defs.Add(CreateDish($"target_{i}", $"目标{i}", shape, Array.Empty<string>()));
            }

            var database = new GameplayDatabase(
                defs,
                new[] { transferSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var board = new DiningTable(Math.Max(1, targetCount + 1), 1);
            DishInstance source = CreateInstance(1, defs[0], shape, x: 0, sourceDishIndex: 0);
            source.MultiplyPermanentMult(2d);
            board.Place(source);

            var targets = new List<DishInstance>();
            for (int i = 0; i < targetCount; i++)
            {
                int sourceDishIndex = targetsHaveRecipeSource ? i + 1 : -1;
                DishInstance target = CreateInstance(i + 2, defs[i + 1], shape, i + 1, sourceDishIndex);
                target.MultiplyPermanentMult(2d);
                board.Place(target);
                targets.Add(target);
            }

            var session = new BattleSession(
                board,
                database,
                new Xoshiro256SS(20260825UL),
                Array.Empty<RecipeSlot>(),
                requiredScore: 0);
            return new Fixture(session, source, targets);
        }

        private static DishDef CreateDish(
            string id,
            string name,
            DishShape shape,
            IReadOnlyList<string> skillIds)
            => new DishDef(
                id,
                name,
                deliciousness: 1,
                shape,
                hiddenMin: 0,
                hiddenMax: 0,
                baseWeight: 1f,
                skillIds,
                flavorId: string.Empty);

        private static DishInstance CreateInstance(
            int id,
            DishDef def,
            DishShape shape,
            int x,
            int sourceDishIndex)
        {
            var instance = new DishInstance(
                id,
                def,
                new Placement(shape, rotationIndex: 0, new GridPos(x, 0)),
                def.SkillIds,
                Array.Empty<string>());
            if (sourceDishIndex >= 0)
            {
                instance.SetSourceRecipeIndex(slotIndex: 0, sourceDishIndex);
            }

            return instance;
        }

        private static SkillDef CreateTransferSkill(int transferCount)
        {
            const string skillId = "test_sweet_transfer";
            SkillRuleDef transferableRule = CreateRule(
                "test_transfer_payload",
                skillId,
                order: 0,
                SkillActionType.None,
                actionCount: 0);
            SkillRuleDef transferRule = CreateRule(
                "test_transfer",
                skillId,
                order: 1,
                SkillActionType.TransferSkills,
                transferCount);
            return new SkillDef(
                skillId,
                "测试甜蜜传递",
                string.Empty,
                Array.Empty<string>(),
                new[] { transferableRule, transferRule },
                new[] { "测试效果", "甜蜜传递" });
        }

        private static SkillEffect CreateNoOpEffect()
            => new SkillEffect(
                CreateRule(
                    "test_on_serve_payload",
                    "test_on_serve_skill",
                    order: 0,
                    SkillActionType.None,
                    actionCount: 0,
                    SkillTrigger.OnServe),
                "测试效果");

        private static SkillRuleDef CreateRule(
            string id,
            string skillId,
            int order,
            SkillActionType actionType,
            int actionCount,
            SkillTrigger trigger = SkillTrigger.OnSettle)
            => new SkillRuleDef(
                id,
                skillId,
                order,
                trigger,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                actionType,
                SkillScope.All,
                actionCount,
                Array.Empty<float>(),
                Array.Empty<string>());

        private static void InvokeApplyTransferRequests(
            BattleSession session,
            IReadOnlyList<SkillTransferRequest> requests)
        {
            Assert.That(ApplyTransferRequestsMethod, Is.Not.Null);
            ApplyTransferRequestsMethod.Invoke(session, new object[] { requests });
        }

        private static double Value(BigDouble value) => value.ToDouble();

        private sealed class Fixture
        {
            public Fixture(BattleSession session, DishInstance source, IReadOnlyList<DishInstance> targets)
            {
                Session = session;
                Source = source;
                Targets = targets;
            }

            public BattleSession Session { get; }

            public DishInstance Source { get; }

            public IReadOnlyList<DishInstance> Targets { get; }
        }
    }
}
