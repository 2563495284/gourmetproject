using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Meta
{
    public sealed class RandomizedItemsPanel : MonoBehaviour
    {
        private readonly List<RectTransform> _cardRects = new();
        private readonly List<RandomizedItemCardView> _spawned = new();
        [SerializeField] private RectTransform _cardsRoot;
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private Button _continueButton;
        [SerializeField] private RandomizedItemCardView _cardTemplate;
        private bool _resolved;

        public void Open(string title, IReadOnlyList<RandomizedItemResult> results, Action onContinue)
        {
            ClearCards();
            _resolved = false;
            gameObject.SetActive(true);
            EnsureRefs();

            if (_titleText != null)
            {
                _titleText.text = string.IsNullOrWhiteSpace(title) ? "随机后的装饰品和消耗品" : title;
            }

            int count = results?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                CreateCard(i, results[i]);
            }

            if (_continueButton != null)
            {
                _continueButton.onClick.RemoveAllListeners();
                _continueButton.onClick.AddListener(() =>
                {
                    if (_resolved)
                    {
                        return;
                    }

                    _resolved = true;
                    onContinue?.Invoke();
                });
            }
        }

        public void Close()
        {
            ClearCards();
            gameObject.SetActive(false);
        }

        public bool TryGetCardRect(int index, RectTransform layer, out Vector2 center, out Vector2 size)
        {
            center = Vector2.zero;
            size = Vector2.zero;
            if (index < 0 || index >= _cardRects.Count || _cardRects[index] == null || layer == null)
            {
                return false;
            }

            RectTransform rect = _cardRects[index];
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector3 first = layer.InverseTransformPoint(corners[0]);
            float minX = first.x;
            float maxX = first.x;
            float minY = first.y;
            float maxY = first.y;

            for (int i = 1; i < corners.Length; i++)
            {
                Vector3 local = layer.InverseTransformPoint(corners[i]);
                minX = Mathf.Min(minX, local.x);
                maxX = Mathf.Max(maxX, local.x);
                minY = Mathf.Min(minY, local.y);
                maxY = Mathf.Max(maxY, local.y);
            }

            center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            size = new Vector2(Mathf.Max(1f, maxX - minX), Mathf.Max(1f, maxY - minY));
            return true;
        }

        private void CreateCard(int index, RandomizedItemResult result)
        {
            if (_cardsRoot == null || _cardTemplate == null)
            {
                return;
            }

            RandomizedItemCardView card = Instantiate(_cardTemplate, _cardsRoot);
            card.gameObject.name = $"RandomizedItem_{index + 1}";
            card.gameObject.SetActive(true);
            card.Bind(result);
            _spawned.Add(card);
            _cardRects.Add(card.Rect);
        }

        private void ClearCards()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Destroy(_spawned[i].gameObject);
                }
            }

            _spawned.Clear();
            _cardRects.Clear();
        }

        private void EnsureRefs()
        {
            if (_titleText == null)
            {
                Transform title = transform.Find("Title");
                _titleText = title != null ? title.GetComponent<TMP_Text>() : null;
            }

            if (_continueButton == null)
            {
                Transform button = transform.Find("ContinueButton");
                _continueButton = button != null ? button.GetComponent<Button>() : null;
            }

            if (_cardsRoot == null)
            {
                Transform cards = transform.Find("Cards");
                _cardsRoot = cards as RectTransform;
            }

            if (_cardTemplate == null && _cardsRoot != null)
            {
                Transform template = _cardsRoot.Find("CardTemplate");
                _cardTemplate = template != null ? template.GetComponent<RandomizedItemCardView>() : null;
            }
        }
    }
}
