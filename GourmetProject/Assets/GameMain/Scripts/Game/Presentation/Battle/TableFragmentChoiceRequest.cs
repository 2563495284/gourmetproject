using System;
using System.Collections.Generic;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Model;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    public sealed class TableFragmentChoiceRequest
    {
        private static int _nextSessionVersion;

        public TableFragmentChoiceRequest(
            GameRun run,
            IReadOnlyList<string> candidateIds,
            Action<bool> completed,
            Action<TableFragmentEditActionState> editActionStateChanged)
        {
            SessionVersion = System.Threading.Interlocked.Increment(ref _nextSessionVersion);
            Run = run;
            var snapshot = new List<string>(candidateIds?.Count ?? 0);
            if (candidateIds != null)
            {
                foreach (string id in candidateIds)
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        snapshot.Add(id);
                    }
                }
            }

            CandidateIds = snapshot;
            Completed = completed;
            EditActionStateChanged = editActionStateChanged;
        }

        public GameRun Run { get; }

        public int SessionVersion { get; }

        public IReadOnlyList<string> CandidateIds { get; }

        public Action<bool> Completed { get; }

        public Action<TableFragmentEditActionState> EditActionStateChanged { get; }
    }

    public readonly struct TableFragmentHoverInfo
    {
        public TableFragmentHoverInfo(
            int sessionVersion,
            int candidateIndex,
            TableFragmentDef definition,
            Bounds worldBounds)
        {
            SessionVersion = sessionVersion;
            CandidateIndex = candidateIndex;
            Definition = definition;
            WorldBounds = worldBounds;
        }

        public int SessionVersion { get; }

        public int CandidateIndex { get; }

        public TableFragmentDef Definition { get; }

        public Bounds WorldBounds { get; }
    }

    public readonly struct TableFragmentEditActionState
    {
        public TableFragmentEditActionState(bool canConfirm, bool interactable)
        {
            CanConfirm = canConfirm;
            Interactable = interactable;
        }

        public bool CanConfirm { get; }

        public bool Interactable { get; }
    }
}
