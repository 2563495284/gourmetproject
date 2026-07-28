using System.Collections;
using System.Linq;
using System.Reflection;
using GourmetProject.Config;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Gameplay.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class ActionAxisBarPlayModeTests
    {
        [UnityTest]
        public IEnumerator AddSelection_PreviewsLocksAndConfirmsOnDiscreteAxis()
        {
            var config = new ConfigService();
            config.LoadAll();
            cfg.Tables tables = config.Tables;
            GameplayDatabase database = GameplayContentBuilder.BuildDatabase(tables);
            string characterId = tables.TbCharacter.DataList.First().Id;
            cfg.GameAction action = tables.TbAction.DataList.First();
            var run = new GameRun(tables, database, characterId, "axis-playmode");
            run.BeginTimeline(
                "test",
                7f,
                new[]
                {
                    new RuntimeTimelineNode("same_a", "test", 4, action.Id),
                    new RuntimeTimelineNode("same_b", "test", 4, action.Id),
                });

            GameObject root = BuildAxis(out ActionAxisBar axis, out RectTransform container);
            axis.Build(run);
            yield return null;

            Assert.That(container.Find("NodeBubble_same_a"), Is.Not.Null);
            Assert.That(container.Find("NodeBubble_same_b"), Is.Not.Null);

            int confirmedDay = -1;
            Assert.That(
                axis.BeginAddDaySelection(run, action.Id, new[] { 3, 4, 5 }, day => confirmedDay = day, null),
                Is.True);
            yield return null;

            Transform dayHit = container.Find("DayHit_5");
            Assert.That(dayHit, Is.Not.Null);
            var pointer = dayHit.GetComponent<TimelineAxisPointerTarget>();
            pointer.OnPointerEnter(new PointerEventData(EventSystem.current));
            Assert.That(container.Find("NodeBubble_Preview"), Is.Not.Null);
            pointer.OnPointerExit(new PointerEventData(EventSystem.current));
            yield return null;
            Assert.That(container.Find("NodeBubble_Preview"), Is.Null);

            pointer.OnPointerClick(new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
            });
            Button confirm = container.Find("AxisSelectionConfirm/Confirm")?.GetComponent<Button>();
            Assert.That(confirm, Is.Not.Null);
            Assert.That(confirm.interactable, Is.True);
            confirm.onClick.Invoke();
            Assert.That(confirmedDay, Is.EqualTo(5));

            axis.EndSelection();
            string confirmedNode = null;
            Assert.That(
                axis.BeginDeleteNodeSelection(run, new[] { "same_a" }, nodeId => confirmedNode = nodeId, null),
                Is.True);
            yield return null;

            Transform eligibleBubble = container.Find("NodeBubble_same_a");
            Transform ineligibleBubble = container.Find("NodeBubble_same_b");
            Assert.That(eligibleBubble.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f));
            Assert.That(ineligibleBubble.GetComponent<CanvasGroup>().alpha, Is.LessThan(0.5f));
            eligibleBubble.GetComponent<TimelineAxisPointerTarget>().OnPointerClick(
                new PointerEventData(EventSystem.current)
                {
                    button = PointerEventData.InputButton.Left,
                });
            confirm = container.Find("AxisSelectionConfirm/Confirm")?.GetComponent<Button>();
            Assert.That(confirm, Is.Not.Null);
            Assert.That(confirm.interactable, Is.True);
            confirm.onClick.Invoke();
            Assert.That(confirmedNode, Is.EqualTo("same_a"));

            Object.Destroy(root);
            yield return null;
        }

        private static GameObject BuildAxis(out ActionAxisBar axis, out RectTransform container)
        {
            var root = new GameObject("AxisTestRoot", typeof(RectTransform), typeof(Canvas));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem));
            eventSystem.transform.SetParent(root.transform, false);
            var axisGo = new GameObject("ActionAxis", typeof(RectTransform), typeof(ActionAxisBar));
            axisGo.transform.SetParent(root.transform, false);
            RectTransform axisRect = (RectTransform)axisGo.transform;
            axisRect.sizeDelta = new Vector2(1000f, 140f);
            axis = axisGo.GetComponent<ActionAxisBar>();

            var containerGo = new GameObject("Container", typeof(RectTransform));
            containerGo.transform.SetParent(axisGo.transform, false);
            container = (RectTransform)containerGo.transform;
            container.anchorMin = new Vector2(0.06f, 0.30f);
            container.anchorMax = new Vector2(0.94f, 0.75f);
            container.offsetMin = Vector2.zero;
            container.offsetMax = Vector2.zero;

            var markerGo = new GameObject("Marker", typeof(RectTransform), typeof(Image));
            markerGo.transform.SetParent(axisGo.transform, false);
            RectTransform marker = (RectTransform)markerGo.transform;
            marker.anchorMin = new Vector2(0f, 0.15f);
            marker.anchorMax = new Vector2(0.03f, 0.30f);

            var remainingGo = new GameObject("Remaining", typeof(RectTransform), typeof(Text));
            remainingGo.transform.SetParent(axisGo.transform, false);
            Text remaining = remainingGo.GetComponent<Text>();
            remaining.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            SetPrivate(axis, "_container", container);
            SetPrivate(axis, "_positionMarker", marker);
            SetPrivate(axis, "_remainingDaysText", remaining);
            return root;
        }

        private static void SetPrivate(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {fieldName}");
            field.SetValue(target, value);
        }
    }
}
