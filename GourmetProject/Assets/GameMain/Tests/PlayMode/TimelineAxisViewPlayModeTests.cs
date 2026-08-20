using System.Collections;
using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Hud;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class TimelineAxisViewPlayModeTests
    {
        [UnityTest]
        public IEnumerator Render_FormatsCurrentDayWithOneDecimalPlace()
        {
            TimelineAxisView prefab = Resources.Load<TimelineAxisView>("Prefabs/UI/Hud/TimelineAxisView");
            Assert.That(prefab, Is.Not.Null);
            TimelineAxisView view = Object.Instantiate(prefab);
            try
            {
                TMP_Text currentDayText = null;
                foreach (TMP_Text text in view.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.name == "CurrentDay")
                    {
                        currentDayText = text;
                        break;
                    }
                }

                Assert.That(currentDayText, Is.Not.Null);
                var cases = new[]
                {
                    (Day: 0.9f, Expected: "第0.9天"),
                    (Day: 1f, Expected: "第1.0天"),
                    (Day: 1.2f, Expected: "第1.2天"),
                };
                foreach ((float day, string expected) in cases)
                {
                    TimelineAxisViewState state = State();
                    state.CurrentDay = day;
                    view.Render(state);
                    Assert.That(currentDayText.text, Is.EqualTo(expected));
                }
            }
            finally
            {
                Object.Destroy(view.gameObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator RenderPresentationAndSelections_ConvergeWithoutDuplicateCallbacks()
        {
            TimelineAxisView prefab = Resources.Load<TimelineAxisView>("Prefabs/UI/Hud/TimelineAxisView");
            Assert.That(prefab, Is.Not.Null);
            TimelineAxisView view = Object.Instantiate(prefab);
            try
            {
                TimelineAxisViewState state = State();
                view.Render(state);
                Assert.That(view.TryGetNodeBubble("event", out TimelineNodeBubbleView bubble), Is.True);
                Assert.That(bubble, Is.Not.Null);
                TimelineNodeTailGraphic tail = bubble.GetComponentInChildren<TimelineNodeTailGraphic>(true);
                Assert.That(tail, Is.Not.Null);
                bubble.RefreshTail();
                Assert.That(
                    tail.Tip.y,
                    Is.LessThan(tail.rectTransform.rect.yMin - 1f),
                    "节点尾箭头尖端应落在气泡底边下方，而不是藏在 Shell 内。");
                Assert.That(view.GetDayGroup(3).NodeCount, Is.EqualTo(2));

                TimelineAxisViewState advanced = state.Clone();
                advanced.CurrentDay = 2f;
                int completed = 0;
                view.Play(
                    TimelineAxisPresentationPlan.Single(
                        TimelinePresentationCue.Advance(0f, 2f, null, advanced)),
                    () => completed++);
                view.CompletePresentation();
                view.CompletePresentation();
                Assert.That(completed, Is.EqualTo(1));
                Assert.That(view.DisplayedDay, Is.EqualTo(2f).Within(0.001f));

                int cancelledCallback = 0;
                view.Play(
                    TimelineAxisPresentationPlan.Single(
                        TimelinePresentationCue.Advance(2f, 5f, null, advanced)),
                    () => cancelledCallback++);
                view.CancelPresentation();
                Assert.That(cancelledCallback, Is.Zero);

                var preview = new TimelineAxisNodeState
                {
                    Id = TimelineAxisSelectionController.PreviewId,
                    ActionId = "preview",
                    Kind = ActionDisplayKind.Reward,
                    IconKey = TimelineAxisIconKeys.ForKind(ActionDisplayKind.Reward),
                };
                Assert.That(view.BeginSelection(TimelineAxisSelectionRequest.AddDay(
                    preview,
                    new[] { 2 },
                    _ => { },
                    null)), Is.True);
                TimelineDayPointView day = null;
                foreach (TimelineDayPointView point in view.GetComponentsInChildren<TimelineDayPointView>(true))
                {
                    if (point.Day == 2 && point.gameObject.activeInHierarchy)
                    {
                        day = point;
                        break;
                    }
                }

                Assert.That(day, Is.Not.Null);
                day.OnPointerEnter(null);
                Assert.That(view.HasPreview, Is.True);
                Assert.That(view.PromotePreview("added"), Is.True);
                Assert.That(view.TryGetNodeBubble("added", out _), Is.True);

                Assert.That(view.BeginSelection(TimelineAxisSelectionRequest.Nodes(
                    TimelineAxisSelectionMode.DeleteNode,
                    new[] { "event" },
                    _ => { },
                    null)), Is.True);
                view.EndSelection();
                Assert.That(view.BeginSelection(TimelineAxisSelectionRequest.Nodes(
                    TimelineAxisSelectionMode.ExecuteNode,
                    new[] { "boss" },
                    _ => { },
                    null)), Is.True);
                view.CancelSelection();
                Assert.That(view.SelectionMode, Is.EqualTo(TimelineAxisSelectionMode.None));
            }
            finally
            {
                Object.Destroy(view.gameObject);
            }

            yield return null;
        }

        private static TimelineAxisViewState State()
        {
            var state = new TimelineAxisViewState
            {
                LengthDays = 7f,
                CurrentDay = 0f,
            };
            state.Nodes.Add(Node("event", 3, ActionDisplayKind.Event));
            state.Nodes.Add(Node("reward", 3, ActionDisplayKind.Reward));
            state.Nodes.Add(Node("boss", 7, ActionDisplayKind.Boss));
            return state;
        }

        private static TimelineAxisNodeState Node(string id, int day, ActionDisplayKind kind)
        {
            return new TimelineAxisNodeState
            {
                Id = id,
                Day = day,
                ActionId = "action_" + id,
                Kind = kind,
                IconKey = TimelineAxisIconKeys.ForKind(kind),
            };
        }
    }
}
