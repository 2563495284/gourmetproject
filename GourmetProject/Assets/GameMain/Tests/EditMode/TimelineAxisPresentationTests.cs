using System.Linq;
using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Hud;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class TimelineAxisPresentationTests
    {
        [Test]
        public void BuildMutation_EmitsStableDifferenceOrder()
        {
            TimelineAxisViewState before = State(7f,
                Node("remove", 1, "old"),
                Node("move", 2, "move"),
                Node("replace", 3, "before"));
            TimelineAxisViewState after = State(8f,
                Node("move", 4, "move"),
                Node("replace", 3, "after"),
                Node("add", 5, "new"));

            TimelinePresentationCue[] cues = TimelineAxisPresentationPlanner.BuildMutation(
                    before,
                    after,
                    skipped: false,
                    targetNodeId: "remove")
                .ToArray();

            CollectionAssert.AreEqual(
                new[]
                {
                    TimelinePresentationCueKind.Resize,
                    TimelinePresentationCueKind.Remove,
                    TimelinePresentationCueKind.Move,
                    TimelinePresentationCueKind.Replace,
                    TimelinePresentationCueKind.Add,
                },
                cues.Select(cue => cue.Kind).ToArray());
            CollectionAssert.AreEqual(
                new[] { string.Empty, "remove", "move", "replace", "add" },
                cues.Select(cue => cue.NodeId).ToArray());
        }

        [Test]
        public void BuildMutation_ExplicitSkipDoesNotBecomeRemove()
        {
            TimelineAxisViewState before = State(7f, Node("skip-me", 2, "event"));
            TimelineAxisViewState after = State(7f);

            TimelinePresentationCue cue = TimelineAxisPresentationPlanner.BuildMutation(
                before,
                after,
                skipped: true,
                targetNodeId: "skip-me").Single();

            Assert.That(cue.Kind, Is.EqualTo(TimelinePresentationCueKind.Skip));
            Assert.That(cue.NodeId, Is.EqualTo("skip-me"));
        }

        [Test]
        public void DueStops_SortsByDayAndKeepsSameDayDefinitionOrder()
        {
            TimelineAxisViewState state = State(7f,
                Node("late", 5, "late"),
                Node("same-a", 3, "a"),
                Node("done", 2, "done", completed: true),
                Node("same-b", 3, "b"),
                Node("early", 1, "early"));

            string[] ids = TimelineAxisPresentationPlanner.DueStops(state, 0f, 5f)
                .Select(node => node.Id)
                .ToArray();

            CollectionAssert.AreEqual(new[] { "early", "same-a", "same-b", "late" }, ids);
        }

        [Test]
        public void DueStops_NoDueNodeReturnsDirectAdvanceSet()
        {
            TimelineAxisViewState state = State(7f, Node("future", 6, "future"));

            Assert.That(TimelineAxisPresentationPlanner.DueStops(state, 1f, 3f), Is.Empty);
        }

        private static TimelineAxisViewState State(
            float length,
            params TimelineAxisNodeState[] nodes)
        {
            var state = new TimelineAxisViewState { LengthDays = length };
            state.Nodes.AddRange(nodes);
            return state;
        }

        private static TimelineAxisNodeState Node(
            string id,
            int day,
            string actionId,
            bool completed = false)
        {
            return new TimelineAxisNodeState
            {
                Id = id,
                Day = day,
                ActionId = actionId,
                Kind = ActionDisplayKind.Event,
                Completed = completed,
            };
        }
    }
}
