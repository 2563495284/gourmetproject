using System;
using System.Collections.Generic;
using System.Reflection;
using GourmetProject.Game.UI.Meta;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class EventPagePanelOptionTests
    {
        private GameObject _root;
        private EventPagePanel _panel;
        private RectTransform _optionsRoot;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("EventPagePanelTest", typeof(RectTransform));
            _panel = _root.AddComponent<EventPagePanel>();

            var options = new GameObject("Options", typeof(RectTransform));
            options.transform.SetParent(_root.transform, false);
            _optionsRoot = options.GetComponent<RectTransform>();

            Button template = CreateButtonTemplate(_root.transform);
            template.gameObject.SetActive(false);
            SetPrivateField("_optionsRoot", _optionsRoot);
            SetPrivateField("_optionButtonTemplate", template);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }
        }

        [Test]
        public void Open_UsesConfiguredConditionTextWithoutRewritingIt()
        {
            int picked = -1;
            _panel.Open(
                "事件",
                "描述",
                string.Empty,
                string.Empty,
                new[] { "有条件选项", "无条件选项" },
                new[] { "策划填写的条件原文", string.Empty },
                new[] { false, true },
                index => picked = index,
                onEnd: null);

            Button conditional = FindButton("EventOption_1");
            Button unconditional = FindButton("EventOption_2");
            TMP_Text requirement = FindChildText(conditional, "RequirementText");

            Assert.That(conditional.interactable, Is.False);
            Assert.That(requirement.text, Is.EqualTo("策划填写的条件原文"));
            Assert.That(FindChildText(unconditional, "RequirementText"), Is.Null);

            conditional.onClick.Invoke();
            Assert.That(picked, Is.EqualTo(-1));
            unconditional.onClick.Invoke();
            Assert.That(picked, Is.EqualTo(1));
        }

        [Test]
        public void Open_AllDisabledOptionsCanBeShownWithEnabledLeaveButton()
        {
            bool left = false;
            _panel.Open(
                "事件",
                "描述",
                "离开",
                string.Empty,
                new[] { "选项一", "选项二" },
                new[] { "条件一", "条件二" },
                new[] { false, false },
                onPick: null,
                onEnd: () => left = true);

            Assert.That(FindButton("EventOption_1").interactable, Is.False);
            Assert.That(FindButton("EventOption_2").interactable, Is.False);

            Button leave = FindButton("EventResult");
            Assert.That(leave.interactable, Is.True);
            Assert.That(FindChildText(leave, "RequirementText"), Is.Null);
            leave.onClick.Invoke();
            Assert.That(left, Is.True);
        }

        [Test]
        public void Open_LoadsConfiguredEventIllustrationIntoDedicatedArea()
        {
            _panel.Open(
                "午夜食堂",
                "描述",
                string.Empty,
                "Sprites/UI/Events/event_midnight_tasting",
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<bool>(),
                onPick: null,
                onEnd: null);

            Image illustration = GetPrivateField<Image>("_illustrationImage");
            Assert.That(illustration, Is.Not.Null);
            Assert.That(illustration.gameObject.name, Is.EqualTo("Illustration"));
            Assert.That(illustration.gameObject.activeSelf, Is.True);
            Assert.That(illustration.sprite, Is.Not.Null);
            Assert.That(illustration.preserveAspect, Is.True);
        }

        [Test]
        public void Open_HidesIllustrationWhenTablePathIsEmpty()
        {
            _panel.Open(
                "事件",
                "描述",
                string.Empty,
                string.Empty,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<bool>(),
                onPick: null,
                onEnd: null);

            Image illustration = GetPrivateField<Image>("_illustrationImage");
            Assert.That(illustration, Is.Not.Null);
            Assert.That(illustration.gameObject.activeSelf, Is.False);
            Assert.That(illustration.sprite, Is.Null);
        }

        private static Button CreateButtonTemplate(Transform parent)
        {
            var buttonObject = new GameObject(
                "Template",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();

            var textObject = new GameObject(
                "Text",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(buttonObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            return button;
        }

        private Button FindButton(string name)
        {
            foreach (Button button in _optionsRoot.GetComponentsInChildren<Button>(true))
            {
                if (button.gameObject.name == name)
                {
                    return button;
                }
            }

            throw new AssertionException($"未找到按钮 {name}");
        }

        private static TMP_Text FindChildText(Button button, string name)
        {
            foreach (TMP_Text text in button.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.gameObject.name == name)
                {
                    return text;
                }
            }

            return null;
        }

        private void SetPrivateField(string name, object value)
        {
            FieldInfo field = typeof(EventPagePanel).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"缺少字段 {name}");
            field.SetValue(_panel, value);
        }

        private T GetPrivateField<T>(string name) where T : class
        {
            FieldInfo field = typeof(EventPagePanel).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"缺少字段 {name}");
            return field.GetValue(_panel) as T;
        }
    }
}
