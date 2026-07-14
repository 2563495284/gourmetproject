using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    public sealed class RewardItemChoicePanel : MonoBehaviour
    {
        private readonly List<GameObject> _spawned = new();
        [SerializeField] private RectTransform _cardsRoot;
        [SerializeField] private Text _titleText;
        [SerializeField] private Button _skipButton;
        [SerializeField] private RewardItemChoiceCardView _cardPrefab;
        private bool _resolved;

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
                RewardItemChoiceCardView card = CreateCard(choice, kind, () =>
                {
                    if (_resolved)
                    {
                        return;
                    }

                    _resolved = true;
                    onPick?.Invoke(index);
                });
                if (card != null)
                {
                    _spawned.Add(card.gameObject);
                }
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

        private RewardItemChoiceCardView CreateCard(RewardChoice choice, cfg.ItemKind kind, Action onClick)
        {
            if (_cardsRoot == null || _cardPrefab == null)
            {
                Debug.LogError($"{nameof(RewardItemChoicePanel)} 缺少 Cards 容器或卡片 prefab。", this);
                return null;
            }

            RewardItemChoiceCardView card = Instantiate(_cardPrefab, _cardsRoot);
            card.gameObject.name = $"ItemChoice_{choice?.Id}";
            card.gameObject.SetActive(true);
            card.Bind(choice, kind, onClick);
            return card;
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

    }
}
