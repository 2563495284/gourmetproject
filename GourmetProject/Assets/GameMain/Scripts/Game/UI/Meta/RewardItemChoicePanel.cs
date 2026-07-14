using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    public sealed class RewardItemChoicePanel : MonoBehaviour
    {
        private readonly List<GameObject> _spawned = new();
        private RectTransform _cardsRoot;
        private Text _titleText;
        private Button _skipButton;
        private bool _resolved;

        public static RewardItemChoicePanel Create(RectTransform parent)
        {
            var go = new GameObject("RewardItemChoicePanel", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var bg = go.GetComponent<Image>();
            bg.color = new Color(0.12f, 0.10f, 0.08f, 0.78f);

            RewardItemChoicePanel panel = go.AddComponent<RewardItemChoicePanel>();
            panel.BuildStatic();
            go.SetActive(false);
            return panel;
        }

        public void Open(string title, IReadOnlyList<RewardChoice> choices, cfg.ItemKind kind, Action<int> onPick, Action onSkip)
        {
            ClearCards();
            _resolved = false;
            gameObject.SetActive(true);

            if (_titleText != null)
            {
                _titleText.text = string.IsNullOrWhiteSpace(title) ? "选择一个道具" : title;
            }

            int count = choices?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                int index = i;
                RewardChoice choice = choices[i];
                _spawned.Add(CreateCard(choice, kind, () =>
                {
                    if (_resolved)
                    {
                        return;
                    }

                    _resolved = true;
                    onPick?.Invoke(index);
                }));
            }

            if (_skipButton != null)
            {
                _skipButton.onClick.RemoveAllListeners();
                _skipButton.onClick.AddListener(() =>
                {
                    if (_resolved)
                    {
                        return;
                    }

                    _resolved = true;
                    onSkip?.Invoke();
                });
            }
        }

        public void Close()
        {
            ClearCards();
            gameObject.SetActive(false);
        }

        private void BuildStatic()
        {
            _titleText = CreateText("Title", (RectTransform)transform, 30, TextAnchor.MiddleCenter);
            RectTransform titleRect = (RectTransform)_titleText.transform;
            titleRect.anchorMin = new Vector2(0.08f, 0.84f);
            titleRect.anchorMax = new Vector2(0.92f, 0.96f);
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;

            var cardsGo = new GameObject("Cards", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            _cardsRoot = (RectTransform)cardsGo.transform;
            _cardsRoot.SetParent(transform, false);
            _cardsRoot.anchorMin = new Vector2(0.08f, 0.24f);
            _cardsRoot.anchorMax = new Vector2(0.92f, 0.78f);
            _cardsRoot.offsetMin = Vector2.zero;
            _cardsRoot.offsetMax = Vector2.zero;

            HorizontalLayoutGroup layout = cardsGo.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.spacing = 18f;

            _skipButton = CreateButton("SkipButton", (RectTransform)transform, "跳过");
            RectTransform skipRect = (RectTransform)_skipButton.transform;
            skipRect.anchorMin = new Vector2(0.42f, 0.08f);
            skipRect.anchorMax = new Vector2(0.58f, 0.17f);
            skipRect.offsetMin = Vector2.zero;
            skipRect.offsetMax = Vector2.zero;
        }

        private GameObject CreateCard(RewardChoice choice, cfg.ItemKind kind, Action onClick)
        {
            var go = new GameObject($"ItemChoice_{choice?.Id}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_cardsRoot, false);

            LayoutElement layoutElement = go.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 180f;
            layoutElement.preferredHeight = 260f;
            layoutElement.flexibleWidth = 1f;

            Image bg = go.GetComponent<Image>();
            ItemDefinition item = ItemDefinition.Get(GameApp.Config.Tables, choice?.Id, kind);
            bg.color = item != null ? RunItemSlotView.QualityColor(item.Quality) : new Color(0.88f, 0.82f, 0.70f, 1f);

            Button button = go.GetComponent<Button>();
            button.onClick.AddListener(() => onClick?.Invoke());

            VerticalLayoutGroup vertical = go.GetComponent<VerticalLayoutGroup>();
            vertical.padding = new RectOffset(12, 12, 12, 12);
            vertical.spacing = 8f;
            vertical.childAlignment = TextAnchor.UpperCenter;
            vertical.childControlWidth = true;
            vertical.childControlHeight = false;

            Image icon = CreateImage("Icon", rect, RunItemSlotView.LoadIcon(item));
            icon.rectTransform.sizeDelta = new Vector2(72f, 72f);

            Text name = CreateText("Name", rect, 22, TextAnchor.MiddleCenter);
            name.text = choice?.Name ?? string.Empty;

            Text desc = CreateText("Description", rect, 16, TextAnchor.UpperCenter);
            desc.text = item != null ? item.Desc : choice?.Description ?? string.Empty;

            return go;
        }

        private void ClearCards()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Destroy(_spawned[i]);
                }
            }

            _spawned.Clear();
        }

        private static Button CreateButton(string name, RectTransform parent, string text)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.95f, 0.76f, 0.36f, 1f);

            Text label = CreateText("Label", rect, 20, TextAnchor.MiddleCenter);
            label.text = text;
            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            return go.GetComponent<Button>();
        }

        private static Image CreateImage(string name, RectTransform parent, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.color = sprite != null ? Color.white : new Color(0.75f, 0.68f, 0.56f, 1f);
            return image;
        }

        private static Text CreateText(string name, RectTransform parent, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Text text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
    }
}
