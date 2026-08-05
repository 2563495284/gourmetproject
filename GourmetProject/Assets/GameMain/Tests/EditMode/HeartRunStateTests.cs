using System.Linq;
using GourmetProject.Config;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Common;
using GourmetProject.Gameplay.Data;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class HeartRunStateTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void GameBase_InitialHeartCountIsThree()
        {
            Assert.That(_tables.TbGameBase.InitialHeartCount, Is.EqualTo(3));
        }

        [Test]
        public void GameBase_TotalWeeksMatchesConfiguredWeekRows()
        {
            Assert.That(_tables.TbGameBase.TotalWeeks, Is.EqualTo(4));
            Assert.That(_tables.TbGameBase.TotalWeeks, Is.GreaterThanOrEqualTo(1));
            Assert.That(_tables.TbGameBase.TotalWeeks, Is.LessThanOrEqualTo(_tables.TbWeek.DataList.Count));

            GameRun run = CreateRun();
            Assert.That(run.TotalWeeks, Is.EqualTo(4));
        }

        [Test]
        public void NewRun_InitializesHeartCurrentAndCapacity()
        {
            GameRun run = CreateRun();

            Assert.That(run.HeartCapacity, Is.EqualTo(3));
            Assert.That(run.HeartsRemaining, Is.EqualTo(3));
        }

        [Test]
        public void HeartMutation_ClampsCurrentAndCapacity()
        {
            GameRun run = CreateRun();

            Assert.That(run.TryLoseHeart(out int before, out int after), Is.True);
            Assert.That(before, Is.EqualTo(3));
            Assert.That(after, Is.EqualTo(2));
            Assert.That(run.RestoreHearts(20), Is.EqualTo(3));

            Assert.That(run.AdjustHeartCapacity(-2), Is.EqualTo(1));
            Assert.That(run.HeartsRemaining, Is.EqualTo(1));
            Assert.That(run.AdjustHeartCapacity(-20), Is.EqualTo(1));
            Assert.That(run.TryLoseHeart(out _, out _), Is.True);
            Assert.That(run.TryLoseHeart(out _, out int empty), Is.False);
            Assert.That(empty, Is.Zero);

            Assert.That(run.AdjustHeartCapacity(4), Is.EqualTo(5));
            Assert.That(run.HeartsRemaining, Is.Zero, "提高上限不应隐式恢复当前爱心。");
            Assert.That(run.RestoreHearts(2), Is.EqualTo(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HeartStateAndPendingBreak_RoundTrip(bool terminal)
        {
            GameRun run = CreateRun();
            run.AdjustHeartCapacity(2);
            run.TryLoseHeart(out _, out _);
            run.SetPendingHeartBreak(new PendingHeartBreakSaveData
            {
                BeforeHeartCount = terminal ? 1 : 5,
                AfterHeartCount = terminal ? 0 : 4,
                BattleTotal = 123,
                IsTerminal = terminal,
            });

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            PendingHeartBreakSaveData pending = restored.GetPendingHeartBreak();

            Assert.That(restored.HeartCapacity, Is.EqualTo(5));
            Assert.That(restored.HeartsRemaining, Is.EqualTo(2));
            Assert.That(pending, Is.Not.Null);
            Assert.That(pending.BeforeHeartCount, Is.EqualTo(terminal ? 1 : 5));
            Assert.That(pending.AfterHeartCount, Is.EqualTo(terminal ? 0 : 4));
            Assert.That(pending.BattleTotal, Is.EqualTo(123));
            Assert.That(pending.IsTerminal, Is.EqualTo(terminal));

            restored.ClearPendingHeartBreak();
            Assert.That(restored.HasPendingHeartBreak, Is.False);
        }

        [Test]
        public void HeartDisplay_RendersFullThenBrokenHearts()
        {
            string text = HeartDisplayText.Build(2, 3);

            Assert.That(text, Does.Contain("♥♥"));
            Assert.That(text, Does.EndWith("♥</color>"));
            Assert.That(text, Does.Contain(HeartDisplayText.FullColor));
            Assert.That(text, Does.Contain(HeartDisplayText.BrokenColor));
        }

        private GameRun CreateRun()
        {
            string characterId = _tables.TbCharacter.DataList.First().Id;
            return new GameRun(_tables, _database, characterId, "heart-run-state-tests");
        }
    }
}
