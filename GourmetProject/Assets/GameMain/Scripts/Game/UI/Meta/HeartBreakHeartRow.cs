using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>在一行中展示红心状态，并将失去的红心逐颗熄灭。</summary>
    internal sealed class HeartBreakHeartRow : MonoBehaviour
    {
        [SerializeField] private Image _heartTemplate;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private Sprite _fullHeartSprite;
        [SerializeField] private Sprite _emptyHeartSprite;
        [SerializeField] private float _maxRowWidth = 520f;
        [SerializeField] private float _maxCellWidth = 100f;

        private readonly List<Image> _heartSlots = new List<Image>();
        private HeartBreakHeartRowState _state;

        internal HeartBreakHeartRowState State => _state;

        internal int SlotCount => _heartSlots.Count;

        internal string StatusText => _statusText != null ? _statusText.text : string.Empty;

        private void OnDisable()
        {
            StopAndReset();
        }

        private void OnDestroy()
        {
            KillTweens();
        }

        internal void Bind(int beforeHeartCount, int afterHeartCount, int capacity, bool isTerminal)
        {
            KillTweens();
            _state = HeartBreakHeartRowState.Normalize(
                beforeHeartCount,
                afterHeartCount,
                capacity,
                isTerminal);

            EnsureSlotCount(_state.Capacity);
            LayoutSlots(_state.Capacity);
            ApplyHeartCount(_state.BeforeHeartCount);
            SetInitialStatus();
        }

        internal void AppendLossAnimation(Sequence sequence)
        {
            if (sequence == null)
            {
                throw new ArgumentNullException(nameof(sequence));
            }

            for (int index = _state.BeforeHeartCount - 1; index >= _state.AfterHeartCount; index--)
            {
                int slotIndex = index;
                if (slotIndex < 0 || slotIndex >= _heartSlots.Count)
                {
                    continue;
                }

                Image heart = _heartSlots[slotIndex];
                RectTransform rect = heart.rectTransform;
                sequence.Append(rect.DOPunchScale(Vector3.one * 0.18f, 0.18f, 5, 0.55f));
                sequence.Join(rect.DOPunchRotation(new Vector3(0f, 0f, 10f), 0.18f, 6, 0.6f));
                sequence.Append(rect.DOScale(0.12f, 0.16f).SetEase(Ease.InBack));
                sequence.AppendCallback(() => SetHeartState(slotIndex, false));
                sequence.Append(rect.DOScale(1f, 0.22f).SetEase(Ease.OutBack));
                sequence.AppendInterval(0.06f);
            }

            sequence.AppendCallback(ApplyFinalState);
        }

        internal Sprite GetHeartSprite(int index)
        {
            return index >= 0 && index < _heartSlots.Count
                ? _heartSlots[index].sprite
                : null;
        }

        internal Vector3 GetHeartScale(int index)
        {
            return index >= 0 && index < _heartSlots.Count
                ? _heartSlots[index].rectTransform.localScale
                : Vector3.zero;
        }

        internal void StopAndReset()
        {
            KillTweens();
            ResetSlotTransforms();
        }

        private void EnsureSlotCount(int capacity)
        {
            if (_heartTemplate == null)
            {
                return;
            }

            if (_heartSlots.Count == 0)
            {
                _heartSlots.Add(_heartTemplate);
            }

            while (_heartSlots.Count < capacity)
            {
                Image heart = Instantiate(_heartTemplate, _heartTemplate.transform.parent);
                heart.name = $"HeartSlot{_heartSlots.Count + 1}";
                _heartSlots.Add(heart);
            }

            for (int index = 0; index < _heartSlots.Count; index++)
            {
                _heartSlots[index].gameObject.SetActive(index < capacity);
            }
        }

        private void LayoutSlots(int capacity)
        {
            if (capacity <= 0)
            {
                return;
            }

            var rowRect = transform as RectTransform;
            float availableWidth = rowRect != null && rowRect.rect.width > 0f
                ? Mathf.Min(_maxRowWidth, rowRect.rect.width)
                : _maxRowWidth;
            float cellWidth = Mathf.Min(_maxCellWidth, availableWidth / capacity);
            float rowHeight = rowRect != null && rowRect.rect.height > 0f
                ? rowRect.rect.height
                : 120f;

            for (int index = 0; index < capacity && index < _heartSlots.Count; index++)
            {
                Image heart = _heartSlots[index];
                RectTransform rect = heart.rectTransform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(cellWidth, rowHeight);
                rect.anchoredPosition = new Vector2((index - (capacity - 1) * 0.5f) * cellWidth, 0f);
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
                rect.SetSiblingIndex(index);

                heart.preserveAspect = true;
                heart.raycastTarget = false;
            }
        }

        private void ApplyHeartCount(int fullHeartCount)
        {
            ResetSlotTransforms();
            for (int index = 0; index < _state.Capacity && index < _heartSlots.Count; index++)
            {
                bool isFull = index < fullHeartCount;
                SetHeartState(index, isFull);
                _heartSlots[index].rectTransform.localScale = isFull
                    ? Vector3.one
                    : Vector3.one * 0.84f;
            }
        }

        private void SetHeartState(int index, bool isFull)
        {
            if (index < 0 || index >= _heartSlots.Count)
            {
                return;
            }

            Image heart = _heartSlots[index];
            heart.sprite = isFull ? _fullHeartSprite : _emptyHeartSprite;
            heart.color = Color.white;
            heart.rectTransform.localRotation = Quaternion.identity;
        }

        private void ApplyFinalState()
        {
            ApplyHeartCount(_state.AfterHeartCount);
            if (_statusText != null)
            {
                _statusText.text = $"剩余红心 {_state.AfterHeartCount} / {_state.Capacity}";
            }
        }

        private void SetInitialStatus()
        {
            if (_statusText != null)
            {
                _statusText.text = $"剩余红心 {_state.BeforeHeartCount} / {_state.Capacity}";
            }
        }

        private void ResetSlotTransforms()
        {
            foreach (Image heart in _heartSlots)
            {
                if (heart == null)
                {
                    continue;
                }

                heart.rectTransform.localRotation = Quaternion.identity;
                heart.rectTransform.localScale = Vector3.one;
            }
        }

        private void KillTweens()
        {
            foreach (Image heart in _heartSlots)
            {
                if (heart == null)
                {
                    continue;
                }

                DOTween.Kill(heart);
                DOTween.Kill(heart.rectTransform);
            }
        }

    }

    internal readonly struct HeartBreakHeartRowState
    {
        private HeartBreakHeartRowState(
            int beforeHeartCount,
            int afterHeartCount,
            int capacity,
            bool isTerminal)
        {
            BeforeHeartCount = beforeHeartCount;
            AfterHeartCount = afterHeartCount;
            Capacity = capacity;
            IsTerminal = isTerminal;
        }

        internal int BeforeHeartCount { get; }

        internal int AfterHeartCount { get; }

        internal int Capacity { get; }

        internal bool IsTerminal { get; }

        internal int LostHeartCount => BeforeHeartCount - AfterHeartCount;

        internal static HeartBreakHeartRowState Normalize(
            int beforeHeartCount,
            int afterHeartCount,
            int capacity,
            bool isTerminal)
        {
            capacity = Mathf.Max(1, capacity);
            int after = Mathf.Clamp(afterHeartCount, 0, capacity);
            int before = Mathf.Clamp(beforeHeartCount, 0, capacity);
            before = Mathf.Max(before, after);

            // 旧存档可能只留下终局 0 -> 0；视觉上重建最后一颗心，仍播放最终熄灭。
            if (isTerminal && after == 0 && before == 0)
            {
                before = 1;
            }

            return new HeartBreakHeartRowState(before, after, capacity, isTerminal);
        }
    }
}
