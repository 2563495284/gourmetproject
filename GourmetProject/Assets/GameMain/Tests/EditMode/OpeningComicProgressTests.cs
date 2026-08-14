using GourmetProject.Game.Save;
using GourmetProject.Game.UI.Common;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class OpeningComicProgressTests
    {
        [Test]
        public void VersionOneSave_ReplaysVersionTwoOnceThenStaysCompleted()
        {
            var progress = new GuideProgressSaveData
            {
                OpeningComicCompletedVersion = 1,
            };

            Assert.That(OpeningComicProgress.CurrentVersion, Is.EqualTo(2));
            Assert.That(OpeningComicProgress.ShouldPlay(progress), Is.True);

            OpeningComicProgress.MarkCompleted(progress);

            Assert.That(progress.OpeningComicCompletedVersion, Is.EqualTo(2));
            Assert.That(OpeningComicProgress.ShouldPlay(progress), Is.False);
        }

        [Test]
        public void CurrentVersionSave_DoesNotReplay()
        {
            var progress = new GuideProgressSaveData
            {
                OpeningComicCompletedVersion = OpeningComicProgress.CurrentVersion,
            };

            Assert.That(OpeningComicProgress.ShouldPlay(progress), Is.False);
        }

        [Test]
        public void NullProgress_PlaysAndCanBeMarkedSafely()
        {
            Assert.That(OpeningComicProgress.ShouldPlay(null), Is.True);
            Assert.DoesNotThrow(() => OpeningComicProgress.MarkCompleted(null));
        }
    }
}
