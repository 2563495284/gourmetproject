using System.Runtime.Serialization;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Gameplay.Battle;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class GoldPresentationTests
    {
        [Test]
        public void PendingGoldChanged_ReportsBeforeAndAfter_AndIgnoresZeroChange()
        {
#pragma warning disable SYSLIB0050
            var session = (BattleSession)FormatterServices.GetUninitializedObject(typeof(BattleSession));
#pragma warning restore SYSLIB0050
            int calls = 0;
            float observedBefore = 0f;
            float observedAfter = 0f;
            session.PendingGoldChanged += (before, after) =>
            {
                calls++;
                observedBefore = before;
                observedAfter = after;
            };

            session.AddPendingGold(12.5f);
            session.AddPendingGold(0f);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(observedBefore, Is.EqualTo(0f));
            Assert.That(observedAfter, Is.EqualTo(12.5f));
        }

        [TestCase(100, 0.5f, true, 101)]
        [TestCase(100, -0.5f, true, 99)]
        [TestCase(100, 25.4f, true, 125)]
        [TestCase(100, 25.4f, false, 100)]
        [TestCase(3, -20f, true, 0)]
        public void ResolveDisplayedGold_UsesAwayFromZeroAndClamps(
            int runGold,
            float pendingGold,
            bool includePending,
            int expected)
        {
            Assert.That(
                BattleInfoColumn.ResolveDisplayedGold(runGold, pendingGold, includePending),
                Is.EqualTo(expected));
        }
    }
}
