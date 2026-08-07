using System.Collections.Generic;
using GourmetProject.Core.Save;
using GourmetProject.Game.Meta;
using GourmetProject.Game.DevConsole.Commands;
using GourmetProject.Game.Run;
using GourmetProject.Game.Save;
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
            Assert.That(OpeningComicProgress.ShouldPlay(new GuideProgressSaveData()), Is.True);
        }

        [Test]
        public void LegacyJsonWithoutOpeningField_ShouldPlayOpeningComic()
        {
            MetaProgressSaveData progress = JsonUtility.FromJson<MetaProgressSaveData>("{\"Version\":1}");

            Assert.That(progress.OpeningComicCompletedVersion, Is.Zero);
            var root = new GameSaveData { MetaProgress = progress };
            root.Normalize();
            Assert.That(OpeningComicProgress.ShouldPlay(root.GuideProgress), Is.True);
        }

        [Test]
        public void CompletedCurrentVersion_ShouldNotPlayOpeningComic()
        {
            var progress = new GuideProgressSaveData
            {
                OpeningComicCompletedVersion = OpeningComicProgress.CurrentVersion,
            };

            Assert.That(OpeningComicProgress.ShouldPlay(progress), Is.False);
        }

        [Test]
        public void MarkCompleted_WritesCurrentOpeningVersionOnlyWhenCalled()
        {
            var progress = new GuideProgressSaveData();
            Assert.That(progress.OpeningComicCompletedVersion, Is.Zero);

            OpeningComicProgress.MarkCompleted(progress);

            Assert.That(progress.OpeningComicCompletedVersion, Is.EqualTo(OpeningComicProgress.CurrentVersion));
        }

        [Test]
        public void LegacyCompletedVersion_MigratesIntoGuideProgress()
        {
            var legacy = new MetaProgressSaveData { OpeningComicCompletedVersion = 1 };
            var root = new GameSaveData { MetaProgress = legacy };
            root.Normalize();

            Assert.That(root.GuideProgress.OpeningComicCompletedVersion, Is.EqualTo(1));
            Assert.That(root.MetaProgress.OpeningComicCompletedVersion, Is.Zero);
        }

        [Test]
        public void SaveClearCommand_DeletesRunSaveWithoutTouchingGuideProgress()
        {
            bool runSaveDeleted = false;
            var save = new MemorySaveService();
            var root = new GameSaveData
            {
                Run = new RunSaveData { ActionRandomRuleVersion = RunPersistence.CurrentActionRandomRuleVersion },
            };
            OpeningComicProgress.MarkCompleted(root.GuideProgress);
            GameSavePersistence.Save(save, root);
            var command = new SaveCommand(
                () =>
                {
                    RunPersistence.Delete(save);
                    runSaveDeleted = true;
                },
                () => GameSavePersistence.DeleteAll(save));

            var result = command.Execute(new[] { "clear" });

            GameSaveData completed = GameSavePersistence.Load(save);
            Assert.That(result.Success, Is.True);
            Assert.That(runSaveDeleted, Is.True);
            Assert.That(completed.Run, Is.Null);
            Assert.That(OpeningComicProgress.ShouldPlay(completed.GuideProgress), Is.False);
        }

        [Test]
        public void SaveClearAllCommand_DeletesEntireGameSave()
        {
            var save = new MemorySaveService();
            var root = new GameSaveData
            {
                Run = new RunSaveData { ActionRandomRuleVersion = RunPersistence.CurrentActionRandomRuleVersion },
            };
            OpeningComicProgress.MarkCompleted(root.GuideProgress);
            GameSavePersistence.Save(save, root);
            var command = new SaveCommand(
                () => RunPersistence.Delete(save),
                () => GameSavePersistence.DeleteAll(save));

            var result = command.Execute(new[] { "clear-all" });

            Assert.That(result.Success, Is.True);
            Assert.That(save.Has(GameSavePersistence.Slot), Is.False);
            Assert.That(
                OpeningComicProgress.ShouldPlay(GameSavePersistence.Load(save).GuideProgress),
                Is.True);
        }

        [Test]
        public void LegacySplitSlots_MigrateIntoAggregateGameSave()
        {
            var save = new MemorySaveService();
            var legacyRun = new RunSaveData
            {
                ActionRandomRuleVersion = RunPersistence.CurrentActionRandomRuleVersion,
            };
            var legacyMeta = new MetaProgressSaveData
            {
                OpeningComicCompletedVersion = OpeningComicProgress.CurrentVersion,
                CompletedRunCount = 4,
            };
            save.Save(GameSavePersistence.Slot, legacyRun);
            save.Save("meta_progress", legacyMeta);

            GameSaveData migrated = GameSavePersistence.Load(save);

            Assert.That(migrated.Run, Is.SameAs(legacyRun));
            Assert.That(migrated.MetaProgress.CompletedRunCount, Is.EqualTo(4));
            Assert.That(
                migrated.GuideProgress.OpeningComicCompletedVersion,
                Is.EqualTo(OpeningComicProgress.CurrentVersion));
            Assert.That(save.TryLoad(GameSavePersistence.Slot, out GameSaveData persisted), Is.True);
            Assert.That(persisted.Format, Is.EqualTo("gourmet_project_game_save"));
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

        private sealed class MemorySaveService : ISaveService
        {
            private readonly Dictionary<string, object> _slots = new();

            public void Save<T>(string slot, T data)
            {
                _slots[slot] = data;
            }

            public bool TryLoad<T>(string slot, out T data)
            {
                if (_slots.TryGetValue(slot, out object value) && value is T typed)
                {
                    data = typed;
                    return true;
                }

                data = default;
                return false;
            }

            public bool Has(string slot) => _slots.ContainsKey(slot);

            public void Delete(string slot) => _slots.Remove(slot);

            public IEnumerable<string> ListSlots() => _slots.Keys;
        }
    }
}
