using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Battle.Pages;
using GourmetProject.Game.UI.Battle.States;
using GourmetProject.Game.UI.Meta;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class EventPageRefreshTests
    {
        [Test]
        public void Show_WhenEventPageIsAlreadyVisible_RefreshesWithoutPageTransition()
        {
            GameObject root = CreatePanel(out EventPagePanel panel, out TMP_Text title);
            try
            {
                var host = new EventPageHost(GameplayView.Event, panel);
                var coordinator = new EventPageCoordinator(host);

                coordinator.Show(CreateRequest("下一页"));

                Assert.That(host.SwitchCount, Is.Zero);
                Assert.That(title.text, Is.EqualTo("下一页"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Show_WhenEnteringEventPage_StillUsesPageTransition()
        {
            GameObject root = CreatePanel(out EventPagePanel panel, out TMP_Text title);
            try
            {
                var host = new EventPageHost(GameplayView.ActionSelect, panel);
                var coordinator = new EventPageCoordinator(host);

                coordinator.Show(CreateRequest("事件页"));

                Assert.That(host.SwitchCount, Is.EqualTo(1));
                Assert.That(title.text, Is.EqualTo("事件页"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OptionClick_DisablesOldButtonsWithoutClearingPanelContent()
        {
            GameObject root = CreatePanel(out EventPagePanel panel, out _);
            try
            {
                var serialized = new SerializedObject(panel);
                RectTransform optionsRoot = CreateChild("Options", root.transform).GetComponent<RectTransform>();
                Button template = CreateButtonTemplate(optionsRoot);
                serialized.FindProperty("_optionsRoot").objectReferenceValue = optionsRoot;
                serialized.FindProperty("_optionButtonTemplate").objectReferenceValue = template;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                int picked = -1;
                panel.Open(
                    "事件",
                    "描述",
                    string.Empty,
                    string.Empty,
                    new[] { "继续" },
                    Array.Empty<string>(),
                    new[] { true },
                    index => picked = index,
                    null);

                Button option = optionsRoot.Find("EventOption_1").GetComponent<Button>();
                option.onClick.Invoke();

                Assert.That(picked, Is.Zero);
                Assert.That(option.gameObject.activeSelf, Is.True);
                Assert.That(option.interactable, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static EventPageRequest CreateRequest(string title)
        {
            return new EventPageRequest(
                title,
                "描述",
                string.Empty,
                string.Empty,
                new[] { "继续" },
                Array.Empty<string>(),
                new[] { true },
                _ => { },
                null);
        }

        private static GameObject CreatePanel(out EventPagePanel panel, out TMP_Text title)
        {
            var root = new GameObject("EventPagePanel", typeof(RectTransform), typeof(EventPagePanel));
            panel = root.GetComponent<EventPagePanel>();
            title = CreateChild("Title", root.transform, typeof(CanvasRenderer), typeof(TextMeshProUGUI))
                .GetComponent<TextMeshProUGUI>();

            var serialized = new SerializedObject(panel);
            serialized.FindProperty("_titleText").objectReferenceValue = title;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        private static GameObject CreateChild(string name, Transform parent, params Type[] components)
        {
            var types = new List<Type> { typeof(RectTransform) };
            types.AddRange(components);
            var child = new GameObject(name, types.ToArray());
            child.transform.SetParent(parent, false);
            return child;
        }

        private static Button CreateButtonTemplate(Transform parent)
        {
            GameObject buttonObject = CreateChild(
                "Template",
                parent,
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            CreateChild("Label", buttonObject.transform, typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            return buttonObject.GetComponent<Button>();
        }

        private sealed class EventPageHost : IEventPageHost
        {
            public EventPageHost(GameplayView currentView, EventPagePanel eventPagePanel)
            {
                CurrentView = currentView;
                EventPagePanel = eventPagePanel;
            }

            public int SwitchCount { get; private set; }
            public GameplayView CurrentView { get; }
            public EventPagePanel EventPagePanel { get; }

            public void SwitchTo(GameplayView view, Action buildCenter = null, Action onShown = null)
            {
                SwitchCount++;
                buildCenter?.Invoke();
                onShown?.Invoke();
            }

            public void OpenEventRecipeDishDelete(
                GameRun run,
                string title,
                Action onCancel,
                Action<ActiveTarget> onTargetConfirmed,
                Action onChanged)
            {
            }
        }
    }
}
