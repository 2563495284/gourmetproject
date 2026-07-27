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
            Action<TableFragmentPlacementConfirmationRequest> requestPlacementConfirmation)
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
            RequestPlacementConfirmation = requestPlacementConfirmation;
        }

        public GameRun Run { get; }

        public int SessionVersion { get; }

        public IReadOnlyList<string> CandidateIds { get; }

        public Action<bool> Completed { get; }

        public Action<TableFragmentPlacementConfirmationRequest> RequestPlacementConfirmation { get; }
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

    public sealed class TableFragmentPlacementConfirmationRequest
    {
        public TableFragmentPlacementConfirmationRequest(
            int sessionVersion,
            int requestId,
            TableFragmentDef fragment,
            GridPos origin,
            Action confirm,
            Action cancel)
        {
            SessionVersion = sessionVersion;
            RequestId = requestId;
            Fragment = fragment;
            Origin = origin;
            Confirm = confirm;
            Cancel = cancel;
        }

        public int SessionVersion { get; }

        public int RequestId { get; }

        public TableFragmentDef Fragment { get; }

        public GridPos Origin { get; }

        public Action Confirm { get; }

        public Action Cancel { get; }
    }
}
