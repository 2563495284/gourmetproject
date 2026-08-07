using GourmetProject.Core.Rng;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta.Passives
{
    public static class PassiveTimelineMutationService
    {
        public static TimelineMutationResult DecreaseWeek(GameRun run, string title, int amount)
        {
            var result = Begin(run, title, TimelineMutationCause.WeekChange);
            result.Changed = run != null && run.DecreaseWeek(amount);
            End(run, result);
            return result;
        }

        public static TimelineMutationResult EnsureLengthAtLeast(GameRun run, string title, int days)
        {
            var result = Begin(run, title, TimelineMutationCause.Resize);
            result.Changed = run != null && run.EnsureTimelineLengthAtLeast(days);
            End(run, result);
            return result;
        }

        public static TimelineMutationResult AddNode(GameRun run, string title, string actionId, IRandomStream rng)
        {
            var result = Begin(run, title, TimelineMutationCause.Add);
            result.Changed = run != null && !string.IsNullOrEmpty(run.AddRuntimeTimelineNode(actionId, rng));
            End(run, result);
            return result;
        }

        public static TimelineMutationResult AddWeekEndNode(
            GameRun run,
            string title,
            string actionId,
            string sourceItemId)
        {
            var result = Begin(run, title, TimelineMutationCause.Add);
            result.Changed = run != null
                && !string.IsNullOrEmpty(run.AddWeekEndAnchoredTimelineNode(actionId, sourceItemId));
            End(run, result);
            return result;
        }

        public static TimelineMutationResult Randomize(GameRun run, string title, IRandomStream rng)
        {
            var result = Begin(run, title, TimelineMutationCause.Replace);
            result.Changed = run != null && run.RandomizeFutureTimelineActions(rng);
            End(run, result);
            return result;
        }

        public static TimelineMutationResult DelayBoss(GameRun run, string title, int days)
        {
            var result = Begin(run, title, TimelineMutationCause.Move);
            result.Changed = run != null && run.DelayFutureBossNodes(days);
            End(run, result);
            return result;
        }

        public static TimelineMutationResult RemoveNode(GameRun run, string title, string nodeId)
        {
            var result = Begin(run, title, TimelineMutationCause.Remove, nodeId);
            result.Changed = run != null && run.RemoveRuntimeTimelineNode(nodeId);
            End(run, result);
            return result;
        }

        public static TimelineMutationResult SkipNode(GameRun run, string title, string nodeId)
        {
            var result = Begin(run, title, TimelineMutationCause.Skip, nodeId);
            result.Changed = run != null && run.RemoveRuntimeTimelineNode(nodeId);
            End(run, result);
            return result;
        }

        private static TimelineMutationResult Begin(
            GameRun run,
            string title,
            TimelineMutationCause cause,
            string targetNodeId = null)
        {
            var result = new TimelineMutationResult
            {
                Title = title,
                Cause = cause,
                TargetNodeId = targetNodeId ?? string.Empty,
                BeforeLengthDays = run?.TimelineLengthDays ?? 0f,
                BeforeWeekIndex = run?.WeekIndex ?? 0,
            };
            Copy(run, result.Before);
            return result;
        }

        private static void End(GameRun run, TimelineMutationResult result)
        {
            Copy(run, result.After);
            result.AfterLengthDays = run?.TimelineLengthDays ?? 0f;
            result.AfterWeekIndex = run?.WeekIndex ?? 0;
        }

        private static void Copy(GameRun run, System.Collections.Generic.List<RuntimeTimelineNodeSnapshot> output)
        {
            output.Clear();
            if (run == null)
            {
                return;
            }

            foreach (RuntimeTimelineNode node in run.RuntimeTimelineNodes)
            {
                output.Add(new RuntimeTimelineNodeSnapshot
                {
                    Id = node.Id,
                    Day = node.Day,
                    ActionId = node.ActionId,
                });
            }
        }
    }
}
