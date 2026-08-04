using System;
using System.Collections.Generic;
using DG.Tweening;
using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 中部行动卡组：行动 n 选一 / 事件 n 选一 / 行动轴节点单卡三种铺卡，含出场/退场 tween 与跳过按钮。
    /// 组件挂在 CenterContent（行动选择面板）上；「选择后做什么」由壳通过回调注入，卡组只管构建与动画。
    /// </summary>
    public sealed class ActionCardDeck : MonoBehaviour
    {
        [SerializeField] private RectTransform _cardsContainer;
        [SerializeField] private WeekEventCardView _cardPrefab;
        [SerializeField] private Button _rerollButton;

        private readonly List<WeekEventCardView> _cards = new List<WeekEventCardView>();
        private Tween _pendingCardShowTween;
        private Tween _cardsHideTween;
        private Func<bool> _canShowPredicate;
        private Func<ItemTipView> _rewardTip;

        public bool CardsActive => _cardsContainer != null && _cardsContainer.gameObject.activeSelf;

        public void SetRewardTip(Func<ItemTipView> rewardTip)
        {
            _rewardTip = rewardTip;
        }

        public void SetCardsActive(bool active)
        {
            if (_cardsContainer != null)
            {
                _cardsContainer.gameObject.SetActive(active);
            }
        }

        /// <summary>行动 n 选一：无行动可选时改显跳过按钮。卡片与跳过都回调 onPick（跳过传 null）。</summary>
        public void ShowActionChoices(IReadOnlyList<ActionChoice> choices, Action<ActionChoice> onPick)
        {
            ShowActionChoices(choices, onPick, null, 0);
        }

        public void ShowActionChoices(IReadOnlyList<ActionChoice> choices, Action<ActionChoice> onPick, Action onReroll, int rerollCount)
        {
            EnsureRefs();
            Clear();
            if (_cardsContainer == null || _cardPrefab == null)
            {
                return;
            }

            bool hasActions = choices != null && choices.Count > 0;
            _cardsContainer.gameObject.SetActive(hasActions);

            if (_rerollButton != null)
            {
                bool canReroll = hasActions && onReroll != null && rerollCount > 0;
                _rerollButton.gameObject.SetActive(canReroll);
                _rerollButton.onClick.RemoveAllListeners();
                if (canReroll)
                {
                    SetButtonText(_rerollButton, $"刷新({rerollCount})");
                    _rerollButton.onClick.AddListener(() => onReroll());
                }
            }

            if (!hasActions)
            {
                return;
            }

            int n = choices.Count;
            float gap = 0.03f;
            float cardW = (1f - gap * (n + 1)) / n;
            for (int i = 0; i < n; i++)
            {
                float minX = gap + i * (cardW + gap);
                ActionChoice captured = choices[i];
                SpawnCard(minX, minX + cardW, card => card.Bind(captured, () => onPick?.Invoke(captured)));
            }
        }

        /// <summary>事件 n 选一：每个选项一张卡，点击回调选项序号；无法构建时走 onEmpty 兜底。</summary>
        public void ShowEventOptions(IReadOnlyList<string> options, Action<int> onPick, Action onEmpty)
        {
            EnsureRefs();
            Clear();
            if (_cardsContainer == null || _cardPrefab == null)
            {
                onEmpty?.Invoke();
                return;
            }

            int n = options?.Count ?? 0;
            _cardsContainer.gameObject.SetActive(n > 0);
            if (_rerollButton != null)
            {
                _rerollButton.gameObject.SetActive(false);
            }

            if (n == 0)
            {
                onEmpty?.Invoke();
                return;
            }

            float gap = 0.03f;
            float cardW = (1f - gap * (n + 1)) / n;
            for (int i = 0; i < n; i++)
            {
                float minX = gap + i * (cardW + gap);
                int index = i;
                string text = options[i];
                SpawnCard(minX, minX + cardW, card => card.BindEventOption(text, () => onPick?.Invoke(index)));
            }
        }

        /// <summary>行动轴节点单卡：点击后回调 onPick；无法构建时走 onEmpty 兜底。</summary>
        public void ShowTimelineNode(cfg.TimelineNode node, int? interestMaxGain, Action onPick, Action onEmpty)
        {
            ShowTimelineNode(node, null, null, interestMaxGain, onPick, onEmpty);
        }

        public void ShowTimelineNode(cfg.TimelineNode node, int? interestThreshold, int? interestGoldPer, int? interestMaxGain, Action onPick, Action onEmpty)
        {
            EnsureRefs();
            Clear();
            if (_cardsContainer == null || _cardPrefab == null)
            {
                onEmpty?.Invoke();
                return;
            }

            _cardsContainer.gameObject.SetActive(true);
            if (_rerollButton != null)
            {
                _rerollButton.gameObject.SetActive(false);
            }

            SpawnCard(0.03f, 0.97f, card => card.Bind(node, interestThreshold, interestGoldPer, interestMaxGain, () => onPick?.Invoke()));
        }

        /// <summary>退场：卡片播放隐藏动画后销毁，全部完成再触发 onHidden。</summary>
        public void HideThenDestroy(Action onHidden)
        {
            KillPendingShow();
            if (_cardsHideTween != null && _cardsHideTween.IsActive())
            {
                return;
            }

            var cards = new List<WeekEventCardView>(_cards);
            _cards.Clear();
            float hideDelay = PickEffectHold(cards);

            Sequence seq = DOTween.Sequence().SetUpdate(true);
            bool hasTween = false;
            foreach (WeekEventCardView card in cards)
            {
                if (card == null)
                {
                    continue;
                }

                Tween tween = card.PlayHideThenDestroy(hideDelay);
                if (tween == null)
                {
                    continue;
                }

                seq.Join(tween);
                hasTween = true;
            }

            if (!hasTween)
            {
                seq.Kill();
                onHidden?.Invoke();
                return;
            }

            _cardsHideTween = seq.OnComplete(() =>
            {
                _cardsHideTween = null;
                onHidden?.Invoke();
            });
        }

        /// <summary>等待场景转场结束后播放卡片出场动画；canShow 每次重新判定（保证仍在行动选择态）。</summary>
        public void PlayShowWhenReady(Func<bool> canShow)
        {
            _canShowPredicate = canShow;
            PlayShowInternal();
        }

        public void Clear()
        {
            EnsureRefs();
            KillPendingShow();
            foreach (WeekEventCardView card in _cards)
            {
                if (card != null)
                {
                    card.PlayHideThenDestroy();
                }
            }

            _cards.Clear();
        }

        private void EnsureRefs()
        {
        }

        private static void SetButtonText(Button button, string text)
        {
            if (button == null)
            {
                return;
            }

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = text ?? string.Empty;
            }
        }

        public void KillPendingShow()
        {
            if (_pendingCardShowTween != null)
            {
                _pendingCardShowTween.Kill();
                _pendingCardShowTween = null;
            }
        }

        /// <summary>关闭清场：停掉待播出场与退场动画。</summary>
        public void KillAllTweens()
        {
            KillPendingShow();
            if (_cardsHideTween != null)
            {
                _cardsHideTween.Kill();
                _cardsHideTween = null;
            }
        }

        private void PlayShowInternal()
        {
            KillPendingShow();
            if ((_canShowPredicate != null && !_canShowPredicate()) || _cards.Count == 0)
            {
                return;
            }

            if (GameApp.UI.HasUIForm(UIForms.CartoonSceneTransition))
            {
                _pendingCardShowTween = DOVirtual.DelayedCall(0.03f, PlayShowInternal, true).SetUpdate(true);
                return;
            }

            foreach (WeekEventCardView card in _cards)
            {
                if (card != null && card.isActiveAndEnabled)
                {
                    card.PlayShow();
                }
            }
        }

        private void SpawnCard(float minX, float maxX, Action<WeekEventCardView> bind)
        {
            WeekEventCardView card = Instantiate(_cardPrefab, _cardsContainer);
            card.SetRewardTip(_rewardTip?.Invoke());
            var rect = (RectTransform)card.transform;
            Rect parentRect = _cardsContainer.rect;
            if (parentRect.width <= 1f || parentRect.height <= 1f)
            {
                Canvas.ForceUpdateCanvases();
                parentRect = _cardsContainer.rect;
            }

            Vector2 fallbackSize = rect.sizeDelta;
            float slotWidth = parentRect.width * (maxX - minX);
            float maxHeight = parentRect.height * 0.92f;
            float width = Mathf.Min(slotWidth, maxHeight * 0.67f);
            if (width <= 1f)
            {
                width = Mathf.Max(1f, fallbackSize.x);
            }

            float height = width / 0.67f;
            float centerX = parentRect.width * ((minX + maxX) * 0.5f - 0.5f);

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(centerX, 0f);
            rect.sizeDelta = new Vector2(width, height);

            bind?.Invoke(card);
            _cards.Add(card);
        }

        private static float PickEffectHold(IReadOnlyList<WeekEventCardView> cards)
        {
            float hold = 0f;
            if (cards == null)
            {
                return hold;
            }

            for (int i = 0; i < cards.Count; i++)
            {
                WeekEventCardView card = cards[i];
                if (card != null)
                {
                    hold = Mathf.Max(hold, card.PickEffectHold);
                }
            }

            return hold;
        }
    }
}
