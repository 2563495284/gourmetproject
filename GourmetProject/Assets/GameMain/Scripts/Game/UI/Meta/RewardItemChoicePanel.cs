using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Tooltips;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Meta
{
    public sealed class RewardItemChoicePanel : MonoBehaviour
    {
        private readonly List<GameObject> _spawned = new();
        private readonly List<RewardItemChoiceCardView> _cards = new();
        private readonly HashSet<int> _claimedOnPage = new();
        [SerializeField] private RectTransform _cardsRoot;
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private Button _skipButton;
        [SerializeField] private RewardItemChoiceCardView _cardPrefab;
        private bool _resolved;
        private GameRun _run;
        private ItemTipView _itemTip;
        private Action<RewardItemChoiceCardView, int> _onPick;
        private Action _onFinish;
        private string _baseTitle = string.Empty;
        private int _requiredPicks;
        private int _claimedBeforeOpen;
        private RectTransform _actionButtonRect;
        private Vector2 _actionButtonAnchorMin;
        private Vector2 _actionButtonAnchorMax;
        private Vector2 _actionButtonPivot;
        private Vector2 _actionButtonPosition;

        private void Awake()
        {
            CaptureActionButtonLayout();
        }

        public void Open(
            RewardChoiceGroup group,
            IReadOnlyList<RewardChoice> choices,
            cfg.ItemKind kind,
            Action<RewardItemChoiceCardView, int> onPick,
            Action onFinish,
            GameRun run = null,
            ItemTipView itemTip = null)
        {
            _itemTip?.Hide();
            ClearCards();
            _resolved = false;
            _run = run;
            _itemTip = itemTip;
            _onPick = onPick;
            _onFinish = onFinish;
            _claimedOnPage.Clear();
            gameObject.SetActive(true);

            int count = choices?.Count ?? 0;
            if (count == 0)
            {
                _resolved = true;
                Close();
                onFinish?.Invoke();
                return;
            }

            _claimedBeforeOpen = group?.ClaimedIndices.Count ?? 0;
            int remainingRequired = (group?.RequiredChoiceCount ?? 1) - _claimedBeforeOpen;
            _requiredPicks = Mathf.Clamp(remainingRequired, 1, count);
            _baseTitle = BuildGroupText(group);

            for (int i = 0; i < count; i++)
            {
                int index = i;
                RewardChoice choice = choices[i];
                RewardItemChoiceCardView card = null;
                card = CreateCard(choice, kind, itemTip, () => OnCardClicked(index));
                if (card != null)
                {
                    _spawned.Add(card.gameObject);
                    _cards.Add(card);
                }
            }

            if (_skipButton != null)
            {
                _skipButton.onClick.RemoveAllListeners();
                _skipButton.onClick.AddListener(OnActionButtonClicked);
            }

            ConfigureActionButton(group?.RequiredChoiceCount > 1);
            RefreshPresentation();
        }

        public void Close()
        {
            _itemTip?.Hide();
            _itemTip = null;
            ClearCards();
            _run = null;
            _onPick = null;
            _onFinish = null;
            _baseTitle = string.Empty;
            _requiredPicks = 0;
            _claimedBeforeOpen = 0;
            _claimedOnPage.Clear();
            _resolved = false;
            if (_skipButton != null)
            {
                _skipButton.onClick.RemoveAllListeners();
            }

            RestoreActionButtonLayout();
            gameObject.SetActive(false);
        }

        private void OnCardClicked(int index)
        {
            if (_resolved
                || index < 0
                || index >= _cards.Count
                || _claimedOnPage.Contains(index))
            {
                return;
            }

            RewardItemChoiceCardView card = _cards[index];
            _onPick?.Invoke(card, index);
            _claimedOnPage.Add(index);
            card?.SetResolved(true);
            RefreshPresentation();

            if (_claimedOnPage.Count >= _requiredPicks)
            {
                Finish();
            }
        }

        private void OnActionButtonClicked()
        {
            if (_resolved)
            {
                return;
            }

            Finish();
        }

        private void Finish()
        {
            if (_resolved)
            {
                return;
            }

            _resolved = true;
            _onFinish?.Invoke();
        }

        private void RefreshPresentation()
        {
            if (_titleText != null)
            {
                int claimed = _claimedBeforeOpen + _claimedOnPage.Count;
                int required = _claimedBeforeOpen + _requiredPicks;
                _titleText.text = required > 1
                    ? $"{_baseTitle}\n已领 {claimed}/{required}"
                    : _baseTitle;
            }

            if (_skipButton != null)
            {
                _skipButton.interactable = true;
                TMP_Text label = _skipButton.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.text = _claimedBeforeOpen + _claimedOnPage.Count > 0
                        ? "结束"
                        : "跳过";
                }
            }
        }

        private void ConfigureActionButton(bool useBottomRight)
        {
            CaptureActionButtonLayout();
            if (_actionButtonRect == null)
            {
                return;
            }

            if (!useBottomRight)
            {
                RestoreActionButtonLayout();
                return;
            }

            _actionButtonRect.anchorMin = new Vector2(0.92f, _actionButtonAnchorMin.y);
            _actionButtonRect.anchorMax = new Vector2(0.92f, _actionButtonAnchorMax.y);
            _actionButtonRect.pivot = new Vector2(1f, _actionButtonPivot.y);
            _actionButtonRect.anchoredPosition = new Vector2(0f, _actionButtonPosition.y);
        }

        private void CaptureActionButtonLayout()
        {
            if (_actionButtonRect != null || _skipButton == null)
            {
                return;
            }

            _actionButtonRect = _skipButton.transform as RectTransform;
            if (_actionButtonRect == null)
            {
                return;
            }

            _actionButtonAnchorMin = _actionButtonRect.anchorMin;
            _actionButtonAnchorMax = _actionButtonRect.anchorMax;
            _actionButtonPivot = _actionButtonRect.pivot;
            _actionButtonPosition = _actionButtonRect.anchoredPosition;
        }

        private void RestoreActionButtonLayout()
        {
            if (_actionButtonRect == null)
            {
                return;
            }

            _actionButtonRect.anchorMin = _actionButtonAnchorMin;
            _actionButtonRect.anchorMax = _actionButtonAnchorMax;
            _actionButtonRect.pivot = _actionButtonPivot;
            _actionButtonRect.anchoredPosition = _actionButtonPosition;
        }

        private static string BuildGroupText(RewardChoiceGroup group)
        {
            if (group == null)
            {
                return "选择 1 件装饰品或消耗品";
            }

            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(group.Title))
            {
                lines.Add(group.Title);
            }
            if (!string.IsNullOrWhiteSpace(group.Description))
            {
                lines.Add(group.Description);
            }
            if (!string.IsNullOrWhiteSpace(group.RuleText))
            {
                lines.Add(group.RuleText);
            }
            return lines.Count > 0 ? string.Join("\n", lines) : "选择 1 件装饰品或消耗品";
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
                : "消耗品槽已满，选择后会折算金币";
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
            _cards.Clear();
        }

    }
}
