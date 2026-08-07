using GourmetProject.Core.Rng;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta.Passives
{
    public static class PassiveTimelineMutationService
    {
        public static TimelineMutationResult DecreaseWeek(GameRun run, string title, int amount)
        {
            var result = Begin(run, title);
            result.Changed = run != null && run.DecreaseWeek(amount);
            End(run, result);
            return result;
        }

        public static TimelineMutationResult AddNode(GameRun run, string title, string actionId, IRandomStream rng)
        {
            var result = Begin(run, title);
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
            var result = Begin(run, title);
            result.Changed = run != null
                && !string.IsNullOrEmpty(run.AddWeekEndAnchoredTimelineNode(actionId, sourceItemId));
            End(run, result);
            return result;
        }

        public static TimelineMutationResult Randomize(GameRun run, string title, IRandomStream rng)
        {
            var result = Begin(run, title);
            result.Changed = run != null && run.RandomizeFutureTimelineActions(rng);
            End(run, result);
            return result;
        }

        public static TimelineMutationResult DelayBoss(GameRun run, string title, int days)
        {
            var result = Begin(run, title);
            result.Changed = run != null && run.DelayFutureBossNodes(days);
            End(run, result);
            return result;
        }

        private static TimelineMutationResult Begin(GameRun run, string title)
        {
            var result = new TimelineMutationResult { Title = title };
            Copy(run, result.Before);
            return result;
        }

        private static void End(GameRun run, TimelineMutationResult result)
        {
            Copy(run, result.After);
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
