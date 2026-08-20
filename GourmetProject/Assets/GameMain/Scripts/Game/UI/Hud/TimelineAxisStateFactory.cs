using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using UnityEngine;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>把运行时领域数据映射为不持有 GameRun 的时间轴视图快照。</summary>
    public static class TimelineAxisStateFactory
    {
        public static TimelineAxisViewState Create(
            GameRun run,
            IReadOnlyList<RuntimeTimelineNodeSnapshot> snapshots = null,
            float? lengthDays = null,
            float? currentDay = null,
            string executingNodeId = null)
        {
            float length = Mathf.Max(1f, lengthDays ?? run?.TimelineLengthDays ?? 1f);
            var state = new TimelineAxisViewState
            {
                LengthDays = length,
                CurrentDay = Mathf.Clamp(currentDay ?? run?.CurrentDay ?? 0f, 0f, length),
            };
            if (run == null)
            {
                return state;
            }

            if (snapshots != null)
            {
                foreach (RuntimeTimelineNodeSnapshot snapshot in snapshots)
                {
                    AddNode(
                        run,
                        state,
                        snapshot?.Id,
                        snapshot?.Day ?? 0,
                        snapshot?.ActionId,
                        executingNodeId);
                }

                return state;
            }

            foreach (cfg.TimelineNode node in TimelineService.GetNodes(run))
            {
                if (node != null)
                {
                    AddNode(run, state, node.Id, node.Day, node.ActionId, executingNodeId);
                }
            }

            return state;
        }

        public static TimelineAxisNodeState CreatePreviewNode(GameRun run, string actionId)
        {
            cfg.GameAction action = run?.Tables?.TbAction.GetOrDefault(actionId);
            ActionDisplayKind kind = ActionDisplay.KindOf(run?.Tables, action);
            return new TimelineAxisNodeState
            {
                Id = TimelineAxisSelectionController.PreviewId,
                ActionId = actionId ?? string.Empty,
                Kind = kind,
                IconKey = TimelineAxisIconKeys.ForKind(kind),
            };
        }

        private static void AddNode(
            GameRun run,
            TimelineAxisViewState state,
            string id,
            int day,
            string actionId,
            string executingNodeId)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            cfg.GameAction action = run.Tables?.TbAction.GetOrDefault(actionId);
            ActionDisplayKind kind = ActionDisplay.KindOf(run.Tables, action);
            string iconKey = TimelineAxisIconKeys.ForKind(kind);
            if (kind == ActionDisplayKind.Boss)
            {
                cfg.TimelineNode node = TimelineService.GetNode(run, id);
                cfg.BossDebuff debuff = node != null
                    ? BossService.PreviewBossDebuff(run, node)
                    : null;
                iconKey = TimelineAxisIconKeys.Boss(debuff?.Id);
            }

            state.Nodes.Add(new TimelineAxisNodeState
            {
                Id = id,
                Day = day,
                ActionId = actionId ?? string.Empty,
                Kind = kind,
                IconKey = iconKey,
                Completed = run.IsNodeTriggered(id),
                Executing = run.IsTimelineNodeExecutionInProgress(id)
                    || string.Equals(id, executingNodeId, StringComparison.Ordinal),
            });
        }
    }
}
