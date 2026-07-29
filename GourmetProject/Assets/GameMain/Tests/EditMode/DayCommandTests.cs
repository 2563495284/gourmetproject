using System.Linq;
using GourmetProject.Config;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.DevConsole;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class DayCommandTests
    {
        private static cfg.Tables _tables;
        private static GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [SetUp]
        public void CreateRun()
        {
            string characterId = _tables.TbCharacter.DataList.First().Id;
            var run = new GameRun(_tables, _database, characterId, "day-command-tests");
            run.BeginTimeline("test", 7f);
            GameRunContext.Set(run);
        }

        [TearDown]
        public void ClearRun()
        {
            GameRunContext.Clear();
        }

        [Test]
        public void DayCommand_SetsAndQuantizesCurrentDay()
        {
            var console = new DevConsole();

            CmdResult result = console.ProcessCommand("day 2.34");

            Assert.That(result.Success, Is.True);
            Assert.That(GameRunContext.Current.CurrentDay, Is.EqualTo(2.3f));
        }

        [TestCase("-0.1")]
        [TestCase("-0.01")]
        [TestCase("7.1")]
        [TestCase("7.01")]
        [TestCase("NaN")]
        public void DayCommand_RejectsInvalidDaysWithoutChangingProgress(string value)
        {
            GameRunContext.Current.CurrentDay = 1.5f;
            var console = new DevConsole();

            CmdResult result = console.ProcessCommand($"day {value}");

            Assert.That(result.Success, Is.False);
            Assert.That(GameRunContext.Current.CurrentDay, Is.EqualTo(1.5f));
        }
    }
}
