using System.Collections.Generic;
using System.Reflection;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SettlementSequencerLayoutTests
    {
        [TestCase(new[] { ".XX", "XXX" }, 0, 1, 2)]
        [TestCase(new[] { "XXX", "X.X" }, 0, 0, 2)]
        [TestCase(new[] { "X.X", "XXX" }, 0, 0, 0)]
        public void TopContinuousRunUsesTopmostLongestSegment(
            string[] rows,
            int expectedRow,
            int expectedStartX,
            int expectedEndX)
        {
            DishShape shape = DishShape.FromRows(rows);
            object run = InvokeFindTopContinuousRun(shape.Cells);

            Assert.That(ReadInt(run, "Row"), Is.EqualTo(expectedRow));
            Assert.That(ReadInt(run, "StartX"), Is.EqualTo(expectedStartX));
            Assert.That(ReadInt(run, "EndX"), Is.EqualTo(expectedEndX));
        }

        [Test]
        public void DishBaseCueUsesBadgeWithoutSettlementEffectLabel()
        {
            MethodInfo method = typeof(SettlementSequencer).GetMethod(
                "BuildDishBaseCue",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object cue = method.Invoke(null, new object[] { null, null, null });
            PropertyInfo showEffectLabel = cue?.GetType().GetProperty("ShowEffectLabel");
            PropertyInfo valueChange = cue?.GetType().GetProperty("ValueChange");
            Assert.That(showEffectLabel, Is.Not.Null);
            Assert.That(valueChange, Is.Not.Null);
            Assert.That((bool)showEffectLabel.GetValue(cue), Is.False);

            object change = valueChange.GetValue(cue);
            PropertyInfo kind = change?.GetType().GetProperty("Kind");
            Assert.That(kind, Is.Not.Null);
            Assert.That(kind.GetValue(change)?.ToString(), Is.EqualTo("Base"));
        }

        [TestCase(ItemScoreEffectType.NthServeMultFlat, "item_first_+2")]
        [TestCase(ItemScoreEffectType.NthServeMultFlat, "item_last_+2")]
        public void NthServePassiveRunsBeforeDishSkillsSoPresentationFollowsBaseBatch(
            ItemScoreEffectType effectType,
            string itemId)
        {
            var source = new ItemScoreEffectSource(new[]
            {
                new ItemScoreSpec(effectType, 2f, "index:1", itemId, itemId),
            });
            var collector = new ScoreEffectCollector();

            source.CollectEffects(null, collector);

            Assert.That(collector.Entries, Has.Count.EqualTo(1));
            Assert.That(collector.Entries[0].Phase, Is.EqualTo(ScorePhase.BeforeAll));
        }

        [Test]
        public void RelicScoreCueCarriesPassiveItemIdForSynchronizedPresentation()
        {
            const string itemId = "item_first_+2";
            var line = new ScoreLine(
                ScorePhase.BeforeAll,
                ScoreLineKind.DishMultiplierAdd,
                ScoreSource.Relic(itemId, "开门红"),
                1,
                "dish",
                null,
                2f,
                1f,
                3f,
                string.Empty);
            MethodInfo buildMethod = typeof(SettlementSequencer).GetMethod(
                "TryBuildCue",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo attachMethod = typeof(SettlementSequencer).GetMethod(
                "AttachPassiveSource",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(buildMethod, Is.Not.Null);
            Assert.That(attachMethod, Is.Not.Null);

            object[] args = { line, null };
            Assert.That((bool)buildMethod.Invoke(null, args), Is.True);
            object cue = args[1];
            attachMethod.Invoke(null, new[] { line, cue });

            PropertyInfo sourceItemId = cue?.GetType().GetProperty("SourceItemId");
            Assert.That(sourceItemId, Is.Not.Null);
            Assert.That(sourceItemId.GetValue(cue), Is.EqualTo(itemId));
        }

        [TestCase("Sprites/Items/first_+2")]
        [TestCase("Sprites/Items/last_+2")]
        public void NthServePassiveIconExistsAtLoaderConventionPath(string resourcePath)
        {
            Assert.That(Resources.LoadAll<Sprite>(resourcePath), Is.Not.Empty);
        }

        private static object InvokeFindTopContinuousRun(IReadOnlyList<GridPos> cells)
        {
            MethodInfo method = typeof(SettlementSequencer).GetMethod(
                "FindTopContinuousRun",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return method.Invoke(null, new object[] { cells });
        }

        private static int ReadInt(object target, string propertyName)
        {
            PropertyInfo property = target?.GetType().GetProperty(propertyName);
            Assert.That(property, Is.Not.Null);
            return (int)property.GetValue(target);
        }
    }
}
