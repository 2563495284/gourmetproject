using GourmetProject.Game.Meta;
using GourmetProject.Game.DevConsole.Commands;
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
            Assert.That(OpeningComicProgress.ShouldPlay(new MemoryOpeningComicStore()), Is.True);
        }

        [Test]
        public void LegacyJsonWithoutOpeningField_ShouldPlayOpeningComic()
        {
            MetaProgressSaveData progress = JsonUtility.FromJson<MetaProgressSaveData>("{\"Version\":1}");

            Assert.That(progress.OpeningComicCompletedVersion, Is.Zero);
            var store = new MemoryOpeningComicStore();
            OpeningComicProgress.MigrateLegacy(store, progress);
            Assert.That(OpeningComicProgress.ShouldPlay(store), Is.True);
        }

        [Test]
        public void CompletedCurrentVersion_ShouldNotPlayOpeningComic()
        {
            var store = new MemoryOpeningComicStore(OpeningComicProgress.CurrentVersion);

            Assert.That(OpeningComicProgress.ShouldPlay(store), Is.False);
        }

        [Test]
        public void MarkCompleted_WritesCurrentOpeningVersionOnlyWhenCalled()
        {
            var store = new MemoryOpeningComicStore();
            Assert.That(store.LoadCompletedVersion(), Is.Zero);

            OpeningComicProgress.MarkCompleted(store);

            Assert.That(store.LoadCompletedVersion(), Is.EqualTo(OpeningComicProgress.CurrentVersion));
        }

        [Test]
        public void LegacyCompletedVersion_MigratesOnlyWhenApplicationPreferenceIsMissing()
        {
            var legacy = new MetaProgressSaveData { OpeningComicCompletedVersion = 1 };
            var missingPreference = new MemoryOpeningComicStore();
            var existingPreference = new MemoryOpeningComicStore(3);

            OpeningComicProgress.MigrateLegacy(missingPreference, legacy);
            OpeningComicProgress.MigrateLegacy(existingPreference, legacy);

            Assert.That(missingPreference.LoadCompletedVersion(), Is.EqualTo(1));
            Assert.That(existingPreference.LoadCompletedVersion(), Is.EqualTo(3));
        }

        [Test]
        public void SaveClearCommand_DeletesRunSaveWithoutTouchingOpeningPreference()
        {
            bool runSaveDeleted = false;
            var store = new MemoryOpeningComicStore();
            OpeningComicProgress.MarkCompleted(store);
            var command = new SaveCommand(() => runSaveDeleted = true);

            var result = command.Execute(new[] { "clear" });

            Assert.That(result.Success, Is.True);
            Assert.That(runSaveDeleted, Is.True);
            Assert.That(OpeningComicProgress.ShouldPlay(store), Is.False);
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

        private sealed class MemoryOpeningComicStore : IOpeningComicProgressStore
        {
            private int _completedVersion;

            internal MemoryOpeningComicStore(int? completedVersion = null)
            {
                HasCompletedVersion = completedVersion.HasValue;
                _completedVersion = completedVersion.GetValueOrDefault();
            }

            public bool HasCompletedVersion { get; private set; }

            public int LoadCompletedVersion() => _completedVersion;

            public void SaveCompletedVersion(int version)
            {
                _completedVersion = version;
                HasCompletedVersion = true;
            }
        }
    }
}
