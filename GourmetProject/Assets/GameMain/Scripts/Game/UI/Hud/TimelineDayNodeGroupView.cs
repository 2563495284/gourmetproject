using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 同一整数日期的节点容器。统一计算重叠扇形，并在节点目标模式中负责重叠节点命中。
    /// </summary>
    public sealed class TimelineDayNodeGroupView : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerMoveHandler,
        IPointerClickHandler
    {
        private sealed class Entry
        {
            public string Id;
            public TimelineNodeBubbleView Bubble;
            public bool Preview;
            public TimelineNodeFanPose Pose;
        }

        private const float MaximumSpan = 124f;
        private const string PreviewId = "__preview__";

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly HashSet<string> _targetableIds = new HashSet<string>();

        private RectTransform _rect;
        private Image _hitArea;
        private bool _nodeTargetMode;
        private bool _destructiveTarget;
        private string _hoveredId;
        private Action<string> _onTarget;
        private Tween _axisTween;

        public int Day { get; private set; }
        public int NodeCount => _entries.Count - (_entries.Exists(entry => entry.Preview) ? 1 : 0);
        public bool IsAnimating => _axisTween?.IsActive() ?? false;

        public void Initialize(int day, float axisX)
        {
            _rect = transform as RectTransform;
            Day = day;
            axisX = Mathf.Clamp01(axisX);

            _rect.anchorMin = new Vector2(axisX, 0.27f);
            _rect.anchorMax = new Vector2(axisX, 0.27f);
            _rect.pivot = new Vector2(0.5f, 0f);
            _rect.sizeDelta = new Vector2(MaximumSpan + 52f, 96f);
            _rect.anchoredPosition = Vector2.zero;
            _rect.localScale = Vector3.one;

            _hitArea = GetComponent<Image>();
            _hitArea.color = new Color(1f, 1f, 1f, 0.001f);
            _hitArea.raycastTarget = false;
        }

        public void SetAxisPosition(float axisX, bool animate, float speed = 1f)
        {
            if (_rect == null)
            {
                return;
            }

            axisX = Mathf.Clamp01(axisX);
            _axisTween?.Kill();
            if (!animate || !gameObject.activeInHierarchy)
            {
                _rect.anchorMin = new Vector2(axisX, _rect.anchorMin.y);
                _rect.anchorMax = new Vector2(axisX, _rect.anchorMax.y);
                return;
            }

            float start = _rect.anchorMin.x;
            _axisTween = DOTween.To(
                    () => start,
                    value =>
                    {
                        _rect.anchorMin = new Vector2(value, _rect.anchorMin.y);
                        _rect.anchorMax = new Vector2(value, _rect.anchorMax.y);
                    },
                    axisX,
                    0.20f / Mathf.Max(0.05f, speed))
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .SetTarget(_rect);
        }

        public void Add(
            string id,
            TimelineNodeBubbleView bubble,
            bool preview,
            bool animate,
            bool preserveWorldPosition = false,
            float speed = 1f)
        {
            if (bubble == null || string.IsNullOrEmpty(id))
            {
                return;
            }

            Remove(id, animate: false);
            bubble.transform.SetParent(_rect, preserveWorldPosition);
            bubble.gameObject.name = preview ? "NodeBubble_Preview" : $"NodeBubble_{id}";
            bubble.SetRaycastEnabled(!_nodeTargetMode && !preview);
            _entries.Add(new Entry
            {
                Id = id,
                Bubble = bubble,
                Preview = preview,
            });
            Layout(animate, preserveWorldPosition ? id : null, speed);
            if (animate && !preserveWorldPosition)
            {
                bubble.PlayEnter(speed);
            }
        }

        public bool Extract(
            string id,
            bool animate,
            out TimelineNodeBubbleView bubble,
            float speed = 1f)
        {
            int index = _entries.FindIndex(entry => entry.Id == id);
            if (index < 0)
            {
                bubble = null;
                return false;
            }

            bubble = _entries[index].Bubble;
            _entries.RemoveAt(index);
            Layout(animate, speed: speed);
            return bubble != null;
        }

        public void SetOrder(
            IReadOnlyList<string> orderedNodeIds,
            bool animate,
            float speed = 1f)
        {
            if (orderedNodeIds == null)
            {
                return;
            }

            var order = new Dictionary<string, int>(orderedNodeIds.Count);
            for (int i = 0; i < orderedNodeIds.Count; i++)
            {
                order[orderedNodeIds[i]] = i;
            }

            _entries.Sort((a, b) =>
            {
                if (a.Preview != b.Preview)
                {
                    return a.Preview ? 1 : -1;
                }

                int ai = order.TryGetValue(a.Id, out int av) ? av : int.MaxValue;
                int bi = order.TryGetValue(b.Id, out int bv) ? bv : int.MaxValue;
                return ai != bi ? ai.CompareTo(bi) : string.CompareOrdinal(a.Id, b.Id);
            });
            Layout(animate, speed: speed);
        }

        public bool Remove(string id, bool animate)
        {
            int index = _entries.FindIndex(entry => entry.Id == id);
            if (index < 0)
            {
                return false;
            }

            TimelineNodeBubbleView bubble = _entries[index].Bubble;
            _entries.RemoveAt(index);
            Layout(animate);
            if (bubble != null)
            {
                if (animate && bubble.gameObject.activeInHierarchy)
                {
                    Action destroy = () =>
                    {
                        if (bubble != null)
                        {
                            Destroy(bubble.gameObject);
                        }
                    };
                    if (id == PreviewId)
                    {
                        bubble.PlayExit(destroy);
                    }
                    else
                    {
                        bubble.PlayRemove(destroy);
                    }
                }
                else
                {
                    Destroy(bubble.gameObject);
                }
            }

            return true;
        }

        public void RemovePreview(bool animate)
        {
            Remove(PreviewId, animate);
        }

        public void AddPreview(TimelineNodeBubbleView bubble)
        {
            RemovePreview(animate: false);
            Add(PreviewId, bubble, preview: true, animate: true);
        }

        public void ConfigureNodeTargetMode(
            IEnumerable<string> targetableIds,
            Action<string> onTarget,
            bool destructive)
        {
            _nodeTargetMode = true;
            _destructiveTarget = destructive;
            _onTarget = onTarget;
            _hoveredId = string.Empty;
            _targetableIds.Clear();
            if (targetableIds != null)
            {
                foreach (string id in targetableIds)
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        _targetableIds.Add(id);
                    }
                }
            }

            _hitArea.raycastTarget = _targetableIds.Count > 0;
            foreach (Entry entry in _entries)
            {
                entry.Bubble?.SetRaycastEnabled(false);
                entry.Bubble?.SetNodeTargetState(
                    _targetableIds.Contains(entry.Id),
                    emphasized: false,
                    destructive: destructive);
            }
        }

        public void EndNodeTargetMode()
        {
            _nodeTargetMode = false;
            _destructiveTarget = false;
            _onTarget = null;
            _hoveredId = string.Empty;
            _targetableIds.Clear();
            if (_hitArea != null)
            {
                _hitArea.raycastTarget = false;
            }

            foreach (Entry entry in _entries)
            {
                if (entry.Bubble != null)
                {
                    entry.Bubble.SetRaycastEnabled(!entry.Preview);
                    entry.Bubble.ClearNodeTargetState();
                }
            }

            RestoreSiblingOrder();
        }

        public void Layout(
            bool animate,
            string arcingNodeId = null,
            float speed = 1f)
        {
            int count = _entries.Count;
            for (int i = 0; i < count; i++)
            {
                TimelineNodeBubbleView bubble = _entries[i].Bubble;
                if (bubble == null)
                {
                    continue;
                }

                TimelineNodeFanPose pose = TimelineNodeFanLayout.Calculate(i, count);
                _entries[i].Pose = pose;
                bubble.SetLayout(
                    _rect,
                    pose.Position,
                    pose.Angle,
                    animate,
                    _entries[i].Id == arcingNodeId,
                    speed);
                bubble.transform.SetSiblingIndex(i);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            ResolveHover(eventData);
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            ResolveHover(eventData);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            SetHovered(string.Empty);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!_nodeTargetMode || eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            ResolveHover(eventData);
            if (!string.IsNullOrEmpty(_hoveredId))
            {
                _onTarget?.Invoke(_hoveredId);
            }
        }

        private void ResolveHover(PointerEventData eventData)
        {
            if (!_nodeTargetMode || _targetableIds.Count == 0)
            {
                return;
            }

            Camera camera = eventData.pressEventCamera ?? eventData.enterEventCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rect,
                    eventData.position,
                    camera,
                    out Vector2 local))
            {
                SetHovered(string.Empty);
                return;
            }

            string nearest = string.Empty;
            float nearestDistance = float.MaxValue;
            foreach (Entry entry in _entries)
            {
                if (entry.Preview || !_targetableIds.Contains(entry.Id))
                {
                    continue;
                }

                float distance = (entry.Pose.Position - local).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = entry.Id;
                }
            }

            SetHovered(nearest);
        }

        private void SetHovered(string nodeId)
        {
            if (_hoveredId == nodeId)
            {
                return;
            }

            _hoveredId = nodeId ?? string.Empty;
            foreach (Entry entry in _entries)
            {
                bool eligible = _targetableIds.Contains(entry.Id);
                bool hovered = eligible && entry.Id == _hoveredId;
                entry.Bubble?.SetNodeTargetState(
                    eligible,
                    emphasized: hovered,
                    destructive: _destructiveTarget);
                if (hovered)
                {
                    entry.Bubble.transform.SetAsLastSibling();
                }
            }

            if (string.IsNullOrEmpty(_hoveredId))
            {
                RestoreSiblingOrder();
            }
        }

        private void RestoreSiblingOrder()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Bubble != null)
                {
                    _entries[i].Bubble.transform.SetSiblingIndex(i);
                }
            }
        }

        private void OnDestroy()
        {
            _axisTween?.Kill();
        }
    }
}
