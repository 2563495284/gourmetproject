using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Common;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class OpeningComicTests
    {
        [Test]
        public void NewProgress_ShouldPlayOpeningComic()
        {
            Assert.That(OpeningComicProgress.ShouldPlay(new MetaProgressSaveData()), Is.True);
        }

        [Test]
        public void LegacyJsonWithoutOpeningField_ShouldPlayOpeningComic()
        {
            MetaProgressSaveData progress = JsonUtility.FromJson<MetaProgressSaveData>("{\"Version\":1}");

            Assert.That(progress.OpeningComicCompletedVersion, Is.Zero);
            Assert.That(OpeningComicProgress.ShouldPlay(progress), Is.True);
        }

        [Test]
        public void CompletedCurrentVersion_ShouldNotPlayOpeningComic()
        {
            var progress = new MetaProgressSaveData
            {
                OpeningComicCompletedVersion = OpeningComicProgress.CurrentVersion,
            };

            Assert.That(OpeningComicProgress.ShouldPlay(progress), Is.False);
        }

        [Test]
        public void MarkCompleted_WritesCurrentOpeningVersionOnlyWhenCalled()
        {
            var progress = new MetaProgressSaveData();
            Assert.That(progress.OpeningComicCompletedVersion, Is.Zero);

            OpeningComicProgress.MarkCompleted(progress);

            Assert.That(progress.OpeningComicCompletedVersion, Is.EqualTo(OpeningComicProgress.CurrentVersion));
        }

        [Test]
        public void PlaybackState_IgnoresRapidAdvanceUntilTransitionCompletes()
        {
            var state = new OpeningComicPlaybackState(8);

            Assert.That(state.TryBeginAdvance(), Is.True);
            Assert.That(state.TryBeginAdvance(), Is.False);
            Assert.That(state.CurrentIndex, Is.Zero);

            state.CompleteAdvance();

            Assert.That(state.CurrentIndex, Is.EqualTo(1));
            Assert.That(state.TryBeginAdvance(), Is.True);
        }

        [Test]
        public void PlaybackState_AdvancesInOrderAndStopsAtEighthPanel()
        {
            var state = new OpeningComicPlaybackState(8);

            for (int expected = 1; expected < 8; expected++)
            {
                Assert.That(state.TryBeginAdvance(), Is.True);
                state.CompleteAdvance();
                Assert.That(state.CurrentIndex, Is.EqualTo(expected));
            }

            Assert.That(state.IsFinalPanel, Is.True);
            Assert.That(state.TryBeginAdvance(), Is.False);
            Assert.That(state.CurrentIndex, Is.EqualTo(7));
        }
    }
}
