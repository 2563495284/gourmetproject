using System.Linq;
using BreakInfinity;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SettlementCakeLayerPresentationTests
    {
        [Test]
        public void CakeLayerBuff_BatchesEachResultKindAcrossAllCakes()
        {
            ScoreSource source = ScoreSource.TableTag("cake_layer_buff", "欢乐蛋糕层数");
            var group = new SettlementEffectGroup(Line(source, ScoreLineKind.DishFlat, 101));
            group.Append(Line(source, ScoreLineKind.DishFlat, 102));
            group.Append(Line(source, ScoreLineKind.DishMultiplierAdd, 101));
            group.Append(Line(source, ScoreLineKind.DishMultiplierAdd, 102));
            group.Append(Line(source, ScoreLineKind.DishMultiplier, 101));
            group.Append(Line(source, ScoreLineKind.DishMultiplier, 102));

            var batches = SettlementSequencer.BuildResultLineBatches(group);

            Assert.That(batches.Count, Is.EqualTo(3));
            CollectionAssert.AreEqual(new[] { 0, 1 }, batches[0]);
            CollectionAssert.AreEqual(new[] { 2, 3 }, batches[1]);
            CollectionAssert.AreEqual(new[] { 4, 5 }, batches[2]);
            foreach (var batch in batches)
            {
                Assert.That(
                    batch.Select(index => group.Lines[index].DishInstanceId),
                    Is.EquivalentTo(new[] { 101, 102 }),
                    "同一层数效果批次中的多个蛋糕必须一起触发");
                Assert.That(
                    batch.Select(index => group.Lines[index].Kind).Distinct().Count(),
                    Is.EqualTo(1),
                    "同一蛋糕的不同反馈不能在同一帧互相取消");
            }
        }

        [Test]
        public void OrdinaryEffect_KeepsMixedResultsInOneBatch()
        {
            ScoreSource source = ScoreSource.TableTag("ordinary_buff", "普通效果");
            var group = new SettlementEffectGroup(Line(source, ScoreLineKind.DishFlat, 101));
            group.Append(Line(source, ScoreLineKind.DishMultiplier, 102));

            var batches = SettlementSequencer.BuildResultLineBatches(group);

            Assert.That(batches.Count, Is.EqualTo(1));
            CollectionAssert.AreEqual(new[] { 0, 1 }, batches[0]);
        }

        private static ScoreLine Line(
            ScoreSource source,
            ScoreLineKind kind,
            int dishInstanceId)
        {
            return new ScoreLine(
                ScorePhase.AfterAllDishes,
                kind,
                source,
                dishInstanceId,
                $"cake_{dishInstanceId}",
                null,
                BigDouble.One,
                BigDouble.One,
                new BigDouble(2),
                "test",
                executionGroupId: 7);
        }
    }
}
