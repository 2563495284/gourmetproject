using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Tooltips;
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
        private GameRun _run;

        private void OnDisable()
        {
            ClearCards();
            _resolved = false;
            _run = null;
            if (_skipButton != null)
            {
                _skipButton.onClick.RemoveAllListeners();
            }
        }

        public void Open(
            string title,
            IReadOnlyList<RewardChoice> choices,
            cfg.ItemKind kind,
            Action<int> onPick,
            Action onSkip,
            GameRun run = null,
            ItemTipView itemTip = null)
        {
            ClearCards();
            _resolved = false;
            _run = run;
            gameObject.SetActive(true);

            if (_titleText != null)
            {
                _titleText.text = string.IsNullOrWhiteSpace(title) ? "选择一个道具" : title;
            }

            int count = choices?.Count ?? 0;
            if (count == 0)
            {
                _resolved = true;
                Close();
                onSkip?.Invoke();
                return;
            }

            for (int i = 0; i < count; i++)
            {
                int index = i;
                RewardChoice choice = choices[i];
                RewardItemChoiceCardView card = CreateCard(choice, kind, itemTip, () =>
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
            _run = null;
            _resolved = false;
            if (_skipButton != null)
            {
                _skipButton.onClick.RemoveAllListeners();
            }

            gameObject.SetActive(false);
        }

        private RewardItemChoiceCardView CreateCard(RewardChoice choice, cfg.ItemKind kind, ItemTipView itemTip, Action onClick)
        {
            if (_cardsRoot == null || _cardPrefab == null)
            {
                Debug.LogError($"{nameof(RewardItemChoicePanel)} 缺少 Cards 容器或卡片 prefab。", this);
                return null;
            }

            RewardItemChoiceCardView card = Instantiate(_cardPrefab, _cardsRoot);
            card.gameObject.name = $"ItemChoice_{choice?.Id}";
            card.gameObject.SetActive(true);
            string slotWarning = ActiveSlotWarning(choice, kind);
            card.Bind(
                choice,
                kind,
                onClick,
                itemTip,
                true,
                slotWarning);
            return card;
        }

        private string ActiveSlotWarning(RewardChoice choice, cfg.ItemKind kind)
        {
            if (choice == null ||
                (kind != cfg.ItemKind.Active &&
                 choice.Kind != cfg.RewardKind.ActiveItemGrant &&
                 choice.Kind != cfg.RewardKind.ActiveItemStrengthen &&
                 choice.Kind != cfg.RewardKind.ActiveItemAdjust))
            {
                return null;
            }

            return _run == null || _run.HasFreeActiveSlot
                ? null
                : "主动道具槽已满，选择后会折算金币";
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
