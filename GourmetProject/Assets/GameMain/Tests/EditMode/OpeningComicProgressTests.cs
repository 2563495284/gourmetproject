using System.IO;
using GourmetProject.Core.Save;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.Save;
using GourmetProject.Game.UI.Common;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class OpeningComicProgressTests
    {
        private string _directory;
        private JsonSaveService _save;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(
                Path.GetTempPath(),
                "GourmetProjectTests",
                TestContext.CurrentContext.Test.ID);
            Directory.CreateDirectory(_directory);
            _save = new JsonSaveService(_directory, new SaveServiceOptions
            {
                CurrentVersion = 1,
                EnableChecksum = true,
                Indented = false,
            });
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void CompletedComic_RemainsCompleted_AfterRunEndPersistence()
        {
            GameSaveData data = GameSavePersistence.Load(_save);
            Assert.That(OpeningComicProgress.ShouldPlay(data.GuideProgress), Is.True);

            OpeningComicProgress.MarkCompleted(data.GuideProgress);
            GameSavePersistence.Save(_save, data);

            MetaProgressPersistence.Save(_save, new MetaProgressSaveData
            {
                CompletedRunCount = 1,
                LostRunCount = 1,
            });
            RunPersistence.Delete(_save);

            GameSaveData reloaded = GameSavePersistence.Load(_save);
            Assert.That(OpeningComicProgress.ShouldPlay(reloaded.GuideProgress), Is.False);
            Assert.That(
                reloaded.GuideProgress.OpeningComicCompletedVersion,
                Is.EqualTo(OpeningComicProgress.CurrentVersion));
        }

        [Test]
        public void CorruptedPrimarySave_RecoversComicCompletionFromBackup()
        {
            GameSaveData data = GameSavePersistence.Load(_save);
            OpeningComicProgress.MarkCompleted(data.GuideProgress);
            GameSavePersistence.Save(_save, data);

            data.MetaProgress.CompletedRunCount = 1;
            GameSavePersistence.Save(_save, data);

            string primaryPath = Path.Combine(_directory, GameSavePersistence.Slot + ".sav");
            string json = File.ReadAllText(primaryPath);
            File.WriteAllText(
                primaryPath,
                json.Replace("\"CompletedRunCount\":1", "\"CompletedRunCount\":2"));

            GameSaveData recovered = GameSavePersistence.Load(_save);
            Assert.That(OpeningComicProgress.ShouldPlay(recovered.GuideProgress), Is.False);

            File.Delete(primaryPath + ".bak");
            GameSaveData restoredPrimary = GameSavePersistence.Load(_save);
            Assert.That(OpeningComicProgress.ShouldPlay(restoredPrimary.GuideProgress), Is.False);
        }
    }
}
