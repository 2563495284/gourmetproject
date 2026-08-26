#if UNITY_EDITOR
using BreakInfinity;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SettlementRunningLedgerTests
    {
        [Test]
        public void ReorderedSweetTransferResponse_AppliesRecordedDeltaWithoutMinus352()
        {
            var score = new DishScore(1, "dish", 100f, 412f, 1f);
            var ledger = CreateLedger(score);
            ScoreLine laterResult = CreateDishLine(
                ScoreLineKind.DishFlat,
                before: 60f,
                after: 412f,
                "后续甜蜜传递结果");
            ScoreLine delayedResponse = CreateDishLine(
                ScoreLineKind.DishFlat,
                before: 0f,
                after: 60f,
                "跳跳糖响应");

            BigDouble beforeLaterResult = ledger.CurrentTotal;
            ledger.Apply(laterResult);
            BigDouble afterLaterResult = ledger.CurrentTotal;
            ledger.Apply(delayedResponse);
            BigDouble afterDelayedResponse = ledger.CurrentTotal;

            Assert.That(
                Value(afterLaterResult - beforeLaterResult),
                Is.EqualTo(352d).Within(1e-9));
            Assert.That(
                Value(afterDelayedResponse - afterLaterResult),
                Is.EqualTo(60d).Within(1e-9),
                "延后播放的正向响应不能用旧 After 快照制造 -352");
            Assert.That(
                Value(afterDelayedResponse),
                Is.EqualTo(Value(score.Contribution)).Within(1e-9));
        }

        [Test]
        public void ReorderedMultiplierAddAndMultiply_ConvergeToCalculatedMultiplier()
        {
            var score = new DishScore(1, "dish", 100f, 0f, 4f);
            var ledger = CreateLedger(score);
            ScoreLine multiply = CreateDishLine(
                ScoreLineKind.DishMultiplier,
                before: 2f,
                after: 4f,
                "倍率 x2");
            ScoreLine delayedAdd = CreateDishLine(
                ScoreLineKind.DishMultiplierAdd,
                before: 1f,
                after: 2f,
                "倍率 +1");

            BigDouble beforeMultiply = ledger.CurrentTotal;
            ledger.Apply(multiply);
            BigDouble afterMultiply = ledger.CurrentTotal;
            ledger.Apply(delayedAdd);
            BigDouble afterDelayedAdd = ledger.CurrentTotal;

            Assert.That(afterMultiply, Is.GreaterThan(beforeMultiply));
            Assert.That(afterDelayedAdd, Is.GreaterThan(afterMultiply));
            Assert.That(
                Value(afterDelayedAdd),
                Is.EqualTo(Value(score.Contribution)).Within(1e-9));
        }

        [Test]
        public void NegativePermanentFlat_RemainsAVisibleRealDeduction()
        {
            var score = new DishScore(1, "dish", 100f, -20f, 1f);
            var ledger = CreateLedger(score);
            ScoreLine penalty = CreateDishLine(
                ScoreLineKind.DishPermanentFlat,
                before: 0f,
                after: -20f,
                "永久分数 -20");
            BigDouble before = ledger.CurrentTotal;

            ledger.Apply(penalty);

            Assert.That(Value(ledger.CurrentTotal - before), Is.EqualTo(-20d).Within(1e-9));
            Assert.That(
                Value(ledger.CurrentTotal),
                Is.EqualTo(Value(score.Contribution)).Within(1e-9));
        }

        [Test]
        public void ReorderedFinalModifiers_UseRecordedTransitionsAndConverge()
        {
            var score = new DishScore(1, "dish", 100f, 0f, 1f);

            var flatLedger = CreateLedger(score);
            flatLedger.Apply(CreateFinalLine(ScoreLineKind.FinalFlat, 40f, 100f));
            flatLedger.Apply(CreateFinalLine(ScoreLineKind.FinalFlat, 0f, 40f));
            Assert.That(Value(flatLedger.CurrentTotal), Is.EqualTo(200d).Within(1e-9));

            var multiplierLedger = CreateLedger(score);
            multiplierLedger.Apply(CreateFinalLine(ScoreLineKind.FinalMultiplier, 2f, 6f));
            multiplierLedger.Apply(CreateFinalLine(ScoreLineKind.FinalMultiplier, 1f, 2f));
            Assert.That(Value(multiplierLedger.CurrentTotal), Is.EqualTo(600d).Within(1e-9));
        }

        private static SettlementRunningLedger CreateLedger(DishScore score)
        {
            var ledger = new SettlementRunningLedger(new[] { score }, baselineSnapshot: null);
            ledger.ApplyBase(score.DishInstanceId, score.BaseValue);
            return ledger;
        }

        private static ScoreLine CreateDishLine(
            ScoreLineKind kind,
            BigDouble before,
            BigDouble after,
            string message)
        {
            return new ScoreLine(
                ScorePhase.DishSkills,
                kind,
                ScoreSource.FinalModifier("test", "测试"),
                1,
                "dish",
                null,
                after - before,
                before,
                after,
                message);
        }

        private static ScoreLine CreateFinalLine(
            ScoreLineKind kind,
            BigDouble before,
            BigDouble after)
        {
            return new ScoreLine(
                ScorePhase.Final,
                kind,
                ScoreSource.FinalModifier("test", "测试"),
                0,
                string.Empty,
                null,
                after - before,
                before,
                after,
                string.Empty);
        }

        private static double Value(BigDouble value) => value.ToDouble();
    }
}
#endif
