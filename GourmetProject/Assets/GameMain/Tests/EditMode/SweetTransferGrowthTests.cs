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
        private const double FloatTolerance = 1e-6;

        private static readonly MethodInfo ApplyTransferRequestsMethod = typeof(BattleSession).GetMethod(
            "ApplyTransferRequests",
            BindingFlags.Instance | BindingFlags.NonPublic);

        [Test]
        public void TargetMultiplier_IsTemporaryAndIncludedInPreviewAndSettlement()
        {
            Fixture fixture = CreateFixture(targetCount: 1, transferCount: 1);
            fixture.Session.SweetTransferTargetMultiplier = 0.4f;

            ScoreResult preview = fixture.Session.PreviewScore();
            DishScore previewTarget = ScoreFor(preview, fixture.Targets[0]);

            Assert.That(preview.SkillTransfers, Has.Count.EqualTo(1));
            Assert.That(Value(previewTarget.Multiplier), Is.EqualTo(2.4d).Within(FloatTolerance));
            Assert.That(Value(fixture.Targets[0].PermanentMultBonus), Is.EqualTo(2d).Within(1e-9));
            Assert.That(fixture.Session.LastRecipeScoreMultiplierDeltas, Is.Empty);

            ScoreResult settled = fixture.Session.Settle();
            DishScore settledTarget = ScoreFor(settled, fixture.Targets[0]);

            Assert.That(Value(settledTarget.Multiplier), Is.EqualTo(2.4d).Within(FloatTolerance));
            Assert.That(Value(fixture.Targets[0].PermanentMultBonus), Is.EqualTo(2d).Within(1e-9));
            Assert.That(fixture.Session.LastRecipeScoreMultiplierDeltas, Is.Empty);
        }

        [Test]
        public void SourceMultiplier_AddsOncePerSuccessfulTargetWithoutPersisting()
        {
            Fixture fixture = CreateFixture(targetCount: 2, transferCount: 2);
            fixture.Session.SweetTransferSourceMultiplier = 0.5f;

            ScoreResult settled = fixture.Session.Settle();
            DishScore sourceScore = ScoreFor(settled, fixture.Source);

            Assert.That(Value(sourceScore.Multiplier), Is.EqualTo(3d).Within(1e-9));
            Assert.That(Value(fixture.Source.PermanentMultBonus), Is.EqualTo(2d).Within(1e-9));
            Assert.That(fixture.Session.LastRecipeScoreMultiplierDeltas, Is.Empty);
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
        public void DishWithoutRecipeSource_StillReceivesTemporaryMultiplier()
        {
            Fixture fixture = CreateFixture(targetCount: 1, transferCount: 1, targetsHaveRecipeSource: false);
            fixture.Session.SweetTransferTargetMultiplier = 0.4f;

            ScoreResult settled = fixture.Session.Settle();

            Assert.That(Value(ScoreFor(settled, fixture.Targets[0]).Multiplier), Is.EqualTo(2.4d).Within(FloatTolerance));
            Assert.That(Value(fixture.Targets[0].PermanentMultBonus), Is.EqualTo(2d).Within(1e-9));
            Assert.That(fixture.Session.LastRecipeScoreMultiplierDeltas, Is.Empty);
        }

        [Test]
        public void OnServeTransfer_UsesRuntimeMultiplierWithoutRecipeGrowth()
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
            Assert.That(Value(fixture.Targets[0].ServeMultiplierFlatBonus), Is.EqualTo(0.4d).Within(FloatTolerance));
            Assert.That(Value(fixture.Targets[0].PermanentMultBonus), Is.EqualTo(2d).Within(1e-9));
            Assert.That(fixture.Session.LastRecipeScoreMultiplierDeltas, Is.Empty);

            ScoreResult settled = fixture.Session.Settle();

            // ActionCount=0 的结算技能会再向全部合法目标传递一次，因此本次结算再临时 +0.4。
            Assert.That(Value(ScoreFor(settled, fixture.Targets[0]).Multiplier), Is.EqualTo(2.8d).Within(FloatTolerance));
            Assert.That(Value(fixture.Targets[0].PermanentMultBonus), Is.EqualTo(2d).Within(1e-9));
            Assert.That(fixture.Session.LastRecipeScoreMultiplierDeltas, Is.Empty);
        }

        [Test]
        public void PermanentFlatItems_AffectPreviewImmediatelyWithoutMutatingInstances()
        {
            Fixture fixture = CreateFixture(
                targetCount: 2,
                transferCount: 2,
                itemSpecs: PermanentFlatSpecs());

            ScoreResult preview = fixture.Session.PreviewScore();

            Assert.That(Value(ScoreFor(preview, fixture.Source).FlatBonus), Is.EqualTo(10d).Within(FloatTolerance));
            Assert.That(Value(ScoreFor(preview, fixture.Targets[0]).FlatBonus), Is.EqualTo(3d).Within(FloatTolerance));
            Assert.That(Value(ScoreFor(preview, fixture.Targets[1]).FlatBonus), Is.EqualTo(3d).Within(FloatTolerance));
            Assert.That(Value(preview.PermanentFlatDeltas[fixture.Source.Id]), Is.EqualTo(10d).Within(FloatTolerance));
            Assert.That(Value(preview.PermanentFlatDeltas[fixture.Targets[0].Id]), Is.EqualTo(3d).Within(FloatTolerance));
            Assert.That(Value(preview.PermanentFlatDeltas[fixture.Targets[1].Id]), Is.EqualTo(3d).Within(FloatTolerance));
            Assert.That(Value(fixture.Source.PermanentFlatBonus), Is.EqualTo(0d).Within(FloatTolerance));
            Assert.That(
                fixture.Targets.All(target => Math.Abs(Value(target.PermanentFlatBonus)) <= FloatTolerance),
                Is.True);
            Assert.That(fixture.Session.LastRecipeScoreFlatDeltas, Is.Empty);
        }

        [Test]
        public void PermanentFlatItems_FormalSettlementPersistsExactlyOncePerSuccessfulTarget()
        {
            Fixture fixture = CreateFixture(
                targetCount: 2,
                transferCount: 2,
                itemSpecs: PermanentFlatSpecs());

            ScoreResult settled = fixture.Session.Settle();

            Assert.That(Value(ScoreFor(settled, fixture.Source).FlatBonus), Is.EqualTo(10d).Within(FloatTolerance));
            Assert.That(Value(fixture.Source.PermanentFlatBonus), Is.EqualTo(10d).Within(FloatTolerance));
            Assert.That(Value(fixture.Targets[0].PermanentFlatBonus), Is.EqualTo(3d).Within(FloatTolerance));
            Assert.That(Value(fixture.Targets[1].PermanentFlatBonus), Is.EqualTo(3d).Within(FloatTolerance));
            Assert.That(RecipeFlatDeltaFor(fixture.Session, dishIndex: 0), Is.EqualTo(10d).Within(FloatTolerance));
            Assert.That(RecipeFlatDeltaFor(fixture.Session, dishIndex: 1), Is.EqualTo(3d).Within(FloatTolerance));
            Assert.That(RecipeFlatDeltaFor(fixture.Session, dishIndex: 2), Is.EqualTo(3d).Within(FloatTolerance));
        }

        [Test]
        public void PermanentFlatItems_PlayAfterSweetTransferResultsWithRelicAttribution()
        {
            Fixture fixture = CreateFixture(
                targetCount: 1,
                transferCount: 1,
                itemSpecs: PermanentFlatSpecs(),
                payloadAddsScore: true);

            ScoreResult result = fixture.Session.PreviewScore();
            List<ScoreLine> lines = result.ScoreLines.ToList();
            int transferResultIndex = lines.FindIndex(line =>
                line.Kind == ScoreLineKind.DishFlat
                && line.Trace?.Kind == SkillExecutionKind.SweetTransfer);
            int firstPermanentIndex = lines.FindIndex(line => line.Kind == ScoreLineKind.DishPermanentFlat);
            ScoreLine targetLine = lines.Single(line =>
                line.Kind == ScoreLineKind.DishPermanentFlat
                && line.Source?.Id == "item_transfer_target_flat");
            ScoreLine sourceLine = lines.Single(line =>
                line.Kind == ScoreLineKind.DishPermanentFlat
                && line.Source?.Id == "item_transfer_source_flat");

            Assert.That(transferResultIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(firstPermanentIndex, Is.GreaterThan(transferResultIndex));
            Assert.That(targetLine.Source.Type, Is.EqualTo(ScoreSourceType.Relic));
            Assert.That(targetLine.DishInstanceId, Is.EqualTo(fixture.Targets[0].Id));
            Assert.That(sourceLine.Source.Type, Is.EqualTo(ScoreSourceType.Relic));
            Assert.That(sourceLine.DishInstanceId, Is.EqualTo(fixture.Source.Id));
        }

        [Test]
        public void PermanentFlatItems_FailedTransferCreatesNoGrowth()
        {
            Fixture fixture = CreateFixture(
                targetCount: 0,
                transferCount: 1,
                itemSpecs: PermanentFlatSpecs());

            ScoreResult result = fixture.Session.Settle();

            Assert.That(result.PermanentFlatDeltas, Is.Empty);
            Assert.That(result.ScoreLines.Any(line => line.Kind == ScoreLineKind.DishPermanentFlat), Is.False);
            Assert.That(Value(fixture.Source.PermanentFlatBonus), Is.EqualTo(0d).Within(FloatTolerance));
            Assert.That(fixture.Session.LastRecipeScoreFlatDeltas, Is.Empty);
        }

        [Test]
        public void OnServePermanentFlatGrowth_RemainsImmediateAndOnSettleDoesNotDoubleCommit()
        {
            Fixture fixture = CreateFixture(
                targetCount: 1,
                transferCount: 1,
                itemSpecs: PermanentFlatSpecs());
            fixture.Session.SweetTransferTargetFlat = 3f;
            fixture.Session.SweetTransferSourceFlat = 5f;
            var request = new SkillTransferRequest(
                fixture.Source.Id,
                fixture.Source.Def.Name,
                new[] { fixture.Targets[0].Id },
                new[] { CreateNoOpEffect() },
                count: 1);

            InvokeApplyTransferRequests(fixture.Session, new[] { request });

            Assert.That(Value(fixture.Source.PermanentFlatBonus), Is.EqualTo(5d).Within(FloatTolerance));
            Assert.That(Value(fixture.Targets[0].PermanentFlatBonus), Is.EqualTo(3d).Within(FloatTolerance));

            fixture.Session.Settle();

            // OnServe 和 OnSettle 各发生一次真实传递；OnSettle 的持久化只提交一次。
            Assert.That(Value(fixture.Source.PermanentFlatBonus), Is.EqualTo(10d).Within(FloatTolerance));
            Assert.That(Value(fixture.Targets[0].PermanentFlatBonus), Is.EqualTo(6d).Within(FloatTolerance));
            Assert.That(RecipeFlatDeltaFor(fixture.Session, dishIndex: 0), Is.EqualTo(10d).Within(FloatTolerance));
            Assert.That(RecipeFlatDeltaFor(fixture.Session, dishIndex: 1), Is.EqualTo(6d).Within(FloatTolerance));
        }

        private static Fixture CreateFixture(
            int targetCount,
            int transferCount,
            bool targetsHaveRecipeSource = true,
            IReadOnlyList<ItemScoreSpec> itemSpecs = null,
            bool payloadAddsScore = false)
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            SkillDef transferSkill = CreateTransferSkill(transferCount, payloadAddsScore);
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

            ScoreCalculator calculator = itemSpecs != null
                ? new ScoreCalculator(effectSources: new[] { new ItemScoreEffectSource(itemSpecs) })
                : null;
            var session = new BattleSession(
                board,
                database,
                new Xoshiro256SS(20260825UL),
                Array.Empty<RecipeSlot>(),
                requiredScore: 0,
                calculator: calculator);
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

        private static SkillDef CreateTransferSkill(int transferCount, bool payloadAddsScore = false)
        {
            const string skillId = "test_sweet_transfer";
            SkillRuleDef transferableRule = CreateRule(
                "test_transfer_payload",
                skillId,
                order: 0,
                payloadAddsScore ? SkillActionType.AddFlat : SkillActionType.None,
                actionCount: 0,
                actionValues: payloadAddsScore ? new[] { 1f } : null);
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
            SkillTrigger trigger = SkillTrigger.OnSettle,
            IReadOnlyList<float> actionValues = null)
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
                actionValues ?? Array.Empty<float>(),
                Array.Empty<string>());

        private static IReadOnlyList<ItemScoreSpec> PermanentFlatSpecs()
            => new[]
            {
                new ItemScoreSpec(
                    ItemScoreEffectType.SweetTransferTargetPermanentFlat,
                    3f,
                    string.Empty,
                    "item_transfer_target_flat",
                    "传糖果签"),
                new ItemScoreSpec(
                    ItemScoreEffectType.SweetTransferSourcePermanentFlat,
                    5f,
                    string.Empty,
                    "item_transfer_source_flat",
                    "传菜糖罐"),
            };

        private static void InvokeApplyTransferRequests(
            BattleSession session,
            IReadOnlyList<SkillTransferRequest> requests)
        {
            Assert.That(ApplyTransferRequestsMethod, Is.Not.Null);
            ApplyTransferRequestsMethod.Invoke(session, new object[] { requests });
        }

        private static double Value(BigDouble value) => value.ToDouble();

        private static double RecipeFlatDeltaFor(BattleSession session, int dishIndex)
            => session.LastRecipeScoreFlatDeltas
                .Where(delta => delta.DishIndex == dishIndex)
                .Sum(delta => Value(delta.Delta));

        private static DishScore ScoreFor(ScoreResult result, DishInstance dish)
            => result.DishScores.Single(score => score.DishInstanceId == dish.Id);

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
