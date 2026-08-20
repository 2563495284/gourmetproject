using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;

namespace GourmetProject.Game.UI.Hud
{
    public enum TimelinePresentationCueKind
    {
        Advance,
        TriggerStart,
        TriggerComplete,
        Add,
        Replace,
        Move,
        Remove,
        Skip,
        Resize,
    }

    /// <summary>纯 UI 时间轴节点快照；不持有 GameRun 或配置对象。</summary>
    public sealed class TimelineAxisNodeState
    {
        public string Id { get; set; } = string.Empty;

        public int Day { get; set; }

        public string ActionId { get; set; } = string.Empty;

        public ActionDisplayKind Kind { get; set; } = ActionDisplayKind.Event;

        public string IconKey { get; set; } = string.Empty;

        public bool Completed { get; set; }

        public bool Executing { get; set; }

        public TimelineAxisNodeState Clone()
        {
            return new TimelineAxisNodeState
            {
                Id = Id,
                Day = Day,
                ActionId = ActionId,
                Kind = Kind,
                IconKey = IconKey,
                Completed = Completed,
                Executing = Executing,
            };
        }
    }

    /// <summary>一次演出所需的不可变 Cue 序列及结束时必须收敛到的权威状态。</summary>
    public sealed class TimelineAxisPresentationPlan
    {
        private readonly TimelinePresentationCue[] _cues;
        private readonly IReadOnlyList<TimelinePresentationCue> _readOnlyCues;
        private readonly TimelineAxisViewState _finalState;

        public IReadOnlyList<TimelinePresentationCue> Cues => _readOnlyCues;
        public TimelineAxisViewState FinalState => _finalState.Clone();
        public bool IsEmpty => _cues.Length == 0;

        public TimelineAxisPresentationPlan(
            IEnumerable<TimelinePresentationCue> cues,
            TimelineAxisViewState finalState)
        {
            var copy = new List<TimelinePresentationCue>();
            if (cues != null)
            {
                foreach (TimelinePresentationCue cue in cues)
                {
                    if (cue != null)
                    {
                        copy.Add(cue);
                    }
                }
            }

            _cues = copy.ToArray();
            _readOnlyCues = Array.AsReadOnly(_cues);
            _finalState = finalState?.Clone() ?? ResolveFinalState(_cues);
        }

        public static TimelineAxisPresentationPlan Empty(TimelineAxisViewState finalState)
        {
            return new TimelineAxisPresentationPlan(Array.Empty<TimelinePresentationCue>(), finalState);
        }

        public static TimelineAxisPresentationPlan Single(TimelinePresentationCue cue)
        {
            return new TimelineAxisPresentationPlan(
                cue == null ? Array.Empty<TimelinePresentationCue>() : new[] { cue },
                cue?.TargetState);
        }

        private static TimelineAxisViewState ResolveFinalState(
            IReadOnlyList<TimelinePresentationCue> cues)
        {
            for (int i = (cues?.Count ?? 0) - 1; i >= 0; i--)
            {
                if (cues[i]?.TargetState != null)
                {
                    return cues[i].TargetState.Clone();
                }
            }

            return new TimelineAxisViewState();
        }
    }

    /// <summary>可被正式流程和开发演示页共同消费的时间轴视图状态。</summary>
    public sealed class TimelineAxisViewState
    {
        public float LengthDays { get; set; } = 7f;

        public float CurrentDay { get; set; }

        public List<TimelineAxisNodeState> Nodes { get; } = new List<TimelineAxisNodeState>();

        public TimelineAxisViewState Clone()
        {
            var clone = new TimelineAxisViewState
            {
                LengthDays = LengthDays,
                CurrentDay = CurrentDay,
            };
            foreach (TimelineAxisNodeState node in Nodes)
            {
                if (node != null)
                {
                    clone.Nodes.Add(node.Clone());
                }
            }

            return clone;
        }

        public TimelineAxisNodeState FindNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return null;
            }

            return Nodes.Find(node => node != null && node.Id == nodeId);
        }
    }

    public sealed class TimelinePresentationCue
    {
        public TimelinePresentationCueKind Kind { get; private set; }

        public string NodeId { get; private set; } = string.Empty;

        public float FromDay { get; private set; }

        public float ToDay { get; private set; }

        public TimelineAxisViewState TargetState { get; private set; }

        public static TimelinePresentationCue Node(
            TimelinePresentationCueKind kind,
            string nodeId,
            TimelineAxisViewState targetState = null)
        {
            return new TimelinePresentationCue
            {
                Kind = kind,
                NodeId = nodeId ?? string.Empty,
                TargetState = targetState?.Clone(),
            };
        }

        public static TimelinePresentationCue Advance(
            float fromDay,
            float toDay,
            string arrivingNodeId = null,
            TimelineAxisViewState targetState = null)
        {
            return new TimelinePresentationCue
            {
                Kind = TimelinePresentationCueKind.Advance,
                NodeId = arrivingNodeId ?? string.Empty,
                FromDay = fromDay,
                ToDay = toDay,
                TargetState = targetState?.Clone(),
            };
        }

        public static TimelinePresentationCue Resize(
            float fromLength,
            float toLength,
            TimelineAxisViewState targetState)
        {
            return new TimelinePresentationCue
            {
                Kind = TimelinePresentationCueKind.Resize,
                FromDay = fromLength,
                ToDay = toLength,
                TargetState = targetState?.Clone(),
            };
        }
    }

    public static class TimelineAxisPresentationPlanner
    {
        public static TimelineAxisPresentationPlan BuildMutation(
            TimelineAxisViewState before,
            TimelineAxisViewState after,
            bool skipped,
            string targetNodeId)
        {
            before ??= new TimelineAxisViewState();
            after ??= new TimelineAxisViewState();
            var cues = new List<TimelinePresentationCue>();
            TimelineAxisViewState working = before.Clone();
            if (Math.Abs(before.LengthDays - after.LengthDays) > 0.0001f)
            {
                working.LengthDays = after.LengthDays;
                working.CurrentDay = Math.Min(working.CurrentDay, working.LengthDays);
                cues.Add(TimelinePresentationCue.Resize(
                    before.LengthDays,
                    after.LengthDays,
                    working));
            }

            var beforeById = Index(before.Nodes);
            var afterById = Index(after.Nodes);
            foreach (TimelineAxisNodeState oldNode in before.Nodes)
            {
                if (oldNode == null || string.IsNullOrEmpty(oldNode.Id) || afterById.ContainsKey(oldNode.Id))
                {
                    continue;
                }

                bool isSkip = skipped && string.Equals(oldNode.Id, targetNodeId, StringComparison.Ordinal);
                working.Nodes.RemoveAll(node => node != null && node.Id == oldNode.Id);
                cues.Add(TimelinePresentationCue.Node(
                    isSkip ? TimelinePresentationCueKind.Skip : TimelinePresentationCueKind.Remove,
                    oldNode.Id,
                    working.Clone()));
            }

            foreach (TimelineAxisNodeState newNode in after.Nodes)
            {
                if (newNode == null || string.IsNullOrEmpty(newNode.Id))
                {
                    continue;
                }

                if (!beforeById.TryGetValue(newNode.Id, out TimelineAxisNodeState oldNode))
                {
                    working.Nodes.Add(newNode.Clone());
                    cues.Add(TimelinePresentationCue.Node(
                        TimelinePresentationCueKind.Add,
                        newNode.Id,
                        working.Clone()));
                    continue;
                }

                if (oldNode.Day != newNode.Day)
                {
                    TimelineAxisNodeState moving = working.FindNode(newNode.Id);
                    if (moving != null)
                    {
                        moving.Day = newNode.Day;
                    }

                    cues.Add(TimelinePresentationCue.Node(
                        TimelinePresentationCueKind.Move,
                        newNode.Id,
                        working.Clone()));
                }

                if (!string.Equals(oldNode.ActionId, newNode.ActionId, StringComparison.Ordinal)
                    || oldNode.Kind != newNode.Kind)
                {
                    TimelineAxisNodeState replacing = working.FindNode(newNode.Id);
                    if (replacing != null)
                    {
                        replacing.ActionId = newNode.ActionId;
                        replacing.Kind = newNode.Kind;
                    }

                    cues.Add(TimelinePresentationCue.Node(
                        TimelinePresentationCueKind.Replace,
                        newNode.Id,
                        working.Clone()));
                }
            }

            return new TimelineAxisPresentationPlan(cues, after);
        }

        public static IReadOnlyList<TimelineAxisNodeState> DueStops(
            TimelineAxisViewState state,
            float fromDay,
            float toDay)
        {
            var result = new List<TimelineAxisNodeState>();
            if (state == null || toDay <= fromDay)
            {
                return result;
            }

            foreach (TimelineAxisNodeState node in state.Nodes)
            {
                if (node == null
                    || node.Completed
                    || node.Day <= fromDay + 0.0001f
                    || node.Day > toDay + 0.0001f)
                {
                    continue;
                }

                result.Add(node);
            }

            result.Sort((a, b) =>
            {
                int day = a.Day.CompareTo(b.Day);
                if (day != 0)
                {
                    return day;
                }

                int ai = state.Nodes.IndexOf(a);
                int bi = state.Nodes.IndexOf(b);
                return ai.CompareTo(bi);
            });
            return result;
        }

        private static Dictionary<string, TimelineAxisNodeState> Index(
            IEnumerable<TimelineAxisNodeState> nodes)
        {
            var result = new Dictionary<string, TimelineAxisNodeState>(StringComparer.Ordinal);
            if (nodes == null)
            {
                return result;
            }

            foreach (TimelineAxisNodeState node in nodes)
            {
                if (node != null && !string.IsNullOrEmpty(node.Id))
                {
                    result[node.Id] = node;
                }
            }

            return result;
        }
    }
}
