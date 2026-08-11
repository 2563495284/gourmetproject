using System.Collections;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Hud;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class TimelineAxisPresentationPlayModeTests
    {
        private GameObject _canvasObject;
        private ActionAxisBar _axis;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _canvasObject = new GameObject("TimelineAxisTestCanvas", typeof(Canvas));
            _canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            ActionAxisBar prefab = Resources.Load<ActionAxisBar>("Prefabs/UI/Hud/TimelineAxisView");
            Assert.That(prefab, Is.Not.Null, "共享时间轴预制体必须可从 Resources 加载。");
            _axis = Object.Instantiate(prefab, _canvasObject.transform, false);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_canvasObject != null)
            {
                Object.Destroy(_canvasObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator QueuedCues_PlayStrictlyInOrderAndReachFinalDay()
        {
            TimelineAxisViewState initial = State(0f, Node("event", 2));
            TimelineAxisViewState arrived = State(2f, Node("event", 2, executing: true));
            TimelineAxisViewState completed = State(2f, Node("event", 2, completed: true));
            var order = new List<string>();
            _axis.BindState(initial, animate: false);

            _axis.PlayCue(
                TimelinePresentationCue.Advance(0f, 2f, "event", arrived),
                () => order.Add("advance"),
                speed: 20f);
            _axis.PlayCue(
                TimelinePresentationCue.Node(TimelinePresentationCueKind.TriggerStart, "event", arrived),
                () => order.Add("start"),
                speed: 20f);
            _axis.PlayCue(
                TimelinePresentationCue.Node(TimelinePresentationCueKind.TriggerComplete, "event", completed),
                () => order.Add("complete"),
                speed: 20f);

            float deadline = Time.realtimeSinceStartup + 4f;
            while (_axis.IsPresenting && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(_axis.IsPresenting, Is.False);
            Assert.That(_axis.HasActivePresentationTweens, Is.False);
            Assert.That(_axis.DisplayedDay, Is.EqualTo(2f).Within(0.001f));
            CollectionAssert.AreEqual(new[] { "advance", "start", "complete" }, order);
        }

        [UnityTest]
        public IEnumerator CompletePresentation_ConvergesAndCompletesCallbacksOnce()
        {
            TimelineAxisViewState initial = State(0f, Node("remove", 2));
            TimelineAxisViewState advanced = State(5f, Node("remove", 2));
            TimelineAxisViewState removed = State(5f);
            TimelineAxisViewState chained = State(6f);
            int callbackCount = 0;
            _axis.BindState(initial, animate: false);
            _axis.PlayCue(
                TimelinePresentationCue.Advance(0f, 5f, null, advanced),
                () =>
                {
                    callbackCount++;
                    _axis.PlayCue(
                        TimelinePresentationCue.Advance(5f, 6f, null, chained),
                        () => callbackCount++);
                },
                speed: 0.1f);
            _axis.PlayCue(
                TimelinePresentationCue.Node(TimelinePresentationCueKind.Remove, "remove", removed),
                () => callbackCount++);

            yield return null;
            _axis.CompletePresentation();
            _axis.CompletePresentation();
            yield return null;

            Assert.That(_axis.IsPresenting, Is.False);
            Assert.That(_axis.HasActivePresentationTweens, Is.False);
            Assert.That(_axis.DisplayedDay, Is.EqualTo(6f).Within(0.001f));
            Assert.That(callbackCount, Is.EqualTo(3));
            Assert.That(_axis.GetComponentsInChildren<TimelineNodeBubbleView>(true), Is.Empty);
        }

        [UnityTest]
        public IEnumerator Disable_ConvergesToQueuedAuthorityWithoutLeavingTweens()
        {
            TimelineAxisViewState initial = State(0f, Node("event", 2));
            TimelineAxisViewState arrived = State(2f, Node("event", 2, executing: true));
            TimelineAxisViewState final = State(4f, Node("event", 2, completed: true));
            _axis.BindState(initial, animate: false);
            _axis.PlayCue(
                TimelinePresentationCue.Advance(0f, 2f, "event", arrived),
                speed: 0.1f);
            _axis.PlayCue(
                TimelinePresentationCue.Advance(2f, 4f, null, final),
                speed: 0.1f);

            yield return null;
            _axis.gameObject.SetActive(false);
            yield return null;

            Assert.That(_axis.DisplayedDay, Is.EqualTo(4f).Within(0.001f));
            Assert.That(_axis.IsPresenting, Is.False);
            Assert.That(_axis.HasActivePresentationTweens, Is.False);
        }

        [UnityTest]
        public IEnumerator PromotePreview_ReusesBubbleWithoutDestroyingIt()
        {
            var groupObject = new GameObject(
                "PreviewPromotionGroup",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Image),
                typeof(TimelineDayNodeGroupView));
            groupObject.transform.SetParent(_canvasObject.transform, false);
            TimelineDayNodeGroupView group = groupObject.GetComponent<TimelineDayNodeGroupView>();
            group.Initialize(day: 3, axisX: 0.5f);

            TimelineNodeBubbleView bubblePrefab =
                Resources.Load<TimelineNodeBubbleView>("Prefabs/UI/Hud/TimelineNodeBubbleView");
            Assert.That(bubblePrefab, Is.Not.Null);
            TimelineNodeBubbleView preview =
                Object.Instantiate(bubblePrefab, groupObject.transform, false);
            group.AddPreview(preview);
            yield return null;

            bool promoted = group.PromotePreview("runtime-node", out TimelineNodeBubbleView normal);

            Assert.That(promoted, Is.True);
            Assert.That(normal, Is.SameAs(preview));
            Assert.That(group.NodeCount, Is.EqualTo(1));
            Assert.That(group.GetComponentsInChildren<TimelineNodeBubbleView>(true), Has.Length.EqualTo(1));
            Assert.That(normal.gameObject.name, Is.EqualTo("NodeBubble_runtime-node"));

            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(normal, Is.Not.Null, "提升后的预览节点不应被旧的退场回调销毁。");
        }

        private static TimelineAxisViewState State(
            float currentDay,
            params TimelineAxisNodeState[] nodes)
        {
            var state = new TimelineAxisViewState
            {
                LengthDays = 7f,
                CurrentDay = currentDay,
            };
            state.Nodes.AddRange(nodes);
            return state;
        }

        private static TimelineAxisNodeState Node(
            string id,
            int day,
            bool completed = false,
            bool executing = false)
        {
            return new TimelineAxisNodeState
            {
                Id = id,
                Day = day,
                ActionId = "act_event",
                Kind = ActionDisplayKind.Event,
                Completed = completed,
                Executing = executing,
            };
        }
    }
}
