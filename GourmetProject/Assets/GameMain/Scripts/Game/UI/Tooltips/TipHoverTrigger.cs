using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Tooltips
{
    internal interface ITooltipPlacementAware
    {
        void OnPlacedAroundTarget(bool placedLeftOfTarget);
    }

    /// <summary>
    /// 通用「悬停显示 Tips」口子：挂在任意可悬停的 UI 元素上（行动轴节点格 / 道具槽等），
    /// 指针进入时 <see cref="ActionTipView.Show"/> 目标 Tips，离开时 <see cref="ActionTipView.Hide"/>。
    ///
    /// 只负责显隐 + 跟随定位，不关心 Tips 内容——内容由持有方在 <see cref="Show"/> 前
    /// 调用对应子类的 Bind 填好。<see cref="_tip"/> 可在 Inspector 预先指定，也可运行时
    /// 通过 <see cref="SetTip"/> 注入共享的 Tips 实例。
    /// </summary>
    public sealed class TipHoverTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("要显隐的 Tips 视图；留空则运行时用 SetTip 注入。")]
        [SerializeField] private ActionTipView _tip;

        [Tooltip("用于避让定位的目标 RectTransform；留空则使用当前物体。")]
        [SerializeField] private RectTransform _targetOverride;

        [Tooltip("是否让 Tips 跟随指针（在同一 Canvas 下按屏幕坐标定位）。")]
        [SerializeField] private bool _followPointer = true;

        [Tooltip("跟随指针时相对指针的像素偏移。")]
        [SerializeField] private Vector2 _pointerOffset = new Vector2(24f, -24f);

        [Tooltip("Tips 与悬浮目标之间的最小间距。")]
        [SerializeField] private float _targetGap = 12f;

        [Tooltip("Tips 与 Canvas 边缘之间的最小间距。")]
        [SerializeField] private float _screenPadding = 12f;

        private MonoBehaviour _tipView;
        private RectTransform _tipRect;
        private Canvas _canvas;
        private Action _showTip;
        private Action _hideTip;
        private Action _beforeShow;

        /// <summary>运行时注入 / 替换目标 Tips（多个触发器可共用一个 Tips 实例）。</summary>
        public void SetTip(ActionTipView tip)
        {
            SetTip(tip, null);
        }

        /// <summary>设置 Tips 避让的目标矩形；视觉节点不在当前物体根上时使用。</summary>
        public void SetTarget(RectTransform target)
        {
            _targetOverride = target;
        }

        /// <summary>设置是否由本触发器负责定位 Tips；全屏 overlay 类 Tips 可自行定位子模块。</summary>
        public void SetFollowPointer(bool followPointer)
        {
            _followPointer = followPointer;
        }

        /// <summary>运行时注入 Tips，并提供显示前刷新内容的回调。</summary>
        public void SetTip(ActionTipView tip, Action beforeShow)
        {
            _tip = tip;
            _tipView = tip;
            _tipRect = tip != null ? tip.transform as RectTransform : null;
            _canvas = null;
            _showTip = tip != null ? tip.Show : null;
            _hideTip = tip != null ? tip.Hide : null;
            _beforeShow = beforeShow;
        }

        /// <summary>运行时注入任意 MonoBehaviour Tips，由持有方提供显隐方法。</summary>
        public void SetTip(MonoBehaviour tip, Action showTip, Action hideTip, Action beforeShow = null)
        {
            _tip = tip as ActionTipView;
            _tipView = tip;
            _tipRect = tip != null ? tip.transform as RectTransform : null;
            _canvas = null;
            _showTip = showTip;
            _hideTip = hideTip;
            _beforeShow = beforeShow;
        }

        /// <summary>清空当前 Tips 绑定。</summary>
        public void ClearTip()
        {
            if (ActiveTip != null)
            {
                HideTip();
            }

            _tip = null;
            _tipView = null;
            _tipRect = null;
            _canvas = null;
            _showTip = null;
            _hideTip = null;
            _beforeShow = null;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            MonoBehaviour tip = ActiveTip;
            if (tip == null)
            {
                return;
            }

            ShowTip(tip);
            _beforeShow?.Invoke();
            tip.transform.SetAsLastSibling();
            Canvas.ForceUpdateCanvases();

            if (_followPointer)
            {
                PositionAt(eventData);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (ActiveTip != null)
            {
                HideTip();
            }
        }

        private void PositionAt(PointerEventData eventData)
        {
            MonoBehaviour tip = ActiveTip;
            if (tip == null)
            {
                return;
            }

            if (_tipRect == null)
            {
                _tipRect = tip.transform as RectTransform;
            }

            if (_tipRect == null)
            {
                return;
            }

            if (_canvas == null)
            {
                _canvas = tip.GetComponentInParent<Canvas>();
            }

            var parent = _tipRect.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            Camera cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            RectTransform target = _targetOverride != null ? _targetOverride : transform as RectTransform;
            if (target != null)
            {
                PositionAroundTarget(parent, target);
                return;
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, cam, out Vector2 local))
            {
                _tipRect.anchoredPosition = local + _pointerOffset;
            }
        }

        private MonoBehaviour ActiveTip => _tipView != null ? _tipView : _tip;

        private void ShowTip(MonoBehaviour tip)
        {
            if (_showTip != null)
            {
                _showTip.Invoke();
                return;
            }

            if (_tip != null)
            {
                _tip.Show();
                return;
            }

            tip.gameObject.SetActive(true);
        }

        private void HideTip()
        {
            if (_hideTip != null)
            {
                _hideTip.Invoke();
                return;
            }

            if (_tip != null)
            {
                _tip.Hide();
                return;
            }

            ActiveTip.gameObject.SetActive(false);
        }

        private void PositionAroundTarget(RectTransform parent, RectTransform target)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_tipRect);

            _tipRect.anchorMin = new Vector2(0.5f, 0.5f);
            _tipRect.anchorMax = new Vector2(0.5f, 0.5f);
            _tipRect.pivot = new Vector2(0.5f, 0.5f);

            Rect targetRect = VisualRect(parent, target);
            Vector2 tipSize = TipSize();
            Rect parentRect = parent.rect;

            Vector2[] candidates =
            {
                new Vector2(targetRect.xMax + _targetGap + tipSize.x * 0.5f, targetRect.center.y),
                new Vector2(targetRect.xMin - _targetGap - tipSize.x * 0.5f, targetRect.center.y),
                new Vector2(targetRect.center.x, targetRect.yMax + _targetGap + tipSize.y * 0.5f),
                new Vector2(targetRect.center.x, targetRect.yMin - _targetGap - tipSize.y * 0.5f),
            };

            Vector2 best = candidates[0];
            float bestScore = float.MaxValue;
            int bestIndex = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                Vector2 clamped = ClampCenter(candidates[i], tipSize, parentRect);
                float overflow = OverflowArea(candidates[i], tipSize, parentRect);
                float overlap = OverlapArea(clamped, tipSize, targetRect);
                float drift = (clamped - candidates[i]).sqrMagnitude;
                float score = overlap * 1000000f + overflow * 100000f + drift;
                if (score < bestScore)
                {
                    best = clamped;
                    bestScore = score;
                    bestIndex = i;
                }
            }

            _tipRect.anchoredPosition = best;
            if (ActiveTip is ITooltipPlacementAware placementAware)
            {
                placementAware.OnPlacedAroundTarget(bestIndex == 1 || best.x < targetRect.center.x);
            }
        }

        private Vector2 TipSize()
        {
            Vector2 size = _tipRect.rect.size;
            float preferredWidth = LayoutUtility.GetPreferredWidth(_tipRect);
            float preferredHeight = LayoutUtility.GetPreferredHeight(_tipRect);
            if (preferredWidth > size.x)
            {
                size.x = preferredWidth;
            }

            if (preferredHeight > size.y)
            {
                size.y = preferredHeight;
            }

            size.x = Mathf.Max(1f, size.x);
            size.y = Mathf.Max(1f, size.y);
            return size;
        }

        private Rect LocalRect(RectTransform parent, RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);

            Vector2 min = parent.InverseTransformPoint(corners[0]);
            Vector2 max = min;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector2 point = parent.InverseTransformPoint(corners[i]);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private Rect VisualRect(RectTransform parent, RectTransform target)
        {
            Graphic[] graphics = target.GetComponentsInChildren<Graphic>(true);
            bool hasGraphicRect = false;
            Vector2 min = Vector2.zero;
            Vector2 max = Vector2.zero;

            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null || !graphic.enabled || !graphic.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (graphic.transform is not RectTransform graphicRect)
                {
                    continue;
                }

                Rect rect = LocalRect(parent, graphicRect);
                if (rect.width <= 0.5f && rect.height <= 0.5f)
                {
                    continue;
                }

                if (!hasGraphicRect)
                {
                    min = rect.min;
                    max = rect.max;
                    hasGraphicRect = true;
                }
                else
                {
                    min = Vector2.Min(min, rect.min);
                    max = Vector2.Max(max, rect.max);
                }
            }

            return hasGraphicRect ? Rect.MinMaxRect(min.x, min.y, max.x, max.y) : LocalRect(parent, target);
        }

        private Vector2 ClampCenter(Vector2 center, Vector2 size, Rect bounds)
        {
            float halfW = size.x * 0.5f;
            float halfH = size.y * 0.5f;
            float minX = bounds.xMin + _screenPadding + halfW;
            float maxX = bounds.xMax - _screenPadding - halfW;
            float minY = bounds.yMin + _screenPadding + halfH;
            float maxY = bounds.yMax - _screenPadding - halfH;

            if (minX > maxX)
            {
                center.x = bounds.center.x;
            }
            else
            {
                center.x = Mathf.Clamp(center.x, minX, maxX);
            }

            if (minY > maxY)
            {
                center.y = bounds.center.y;
            }
            else
            {
                center.y = Mathf.Clamp(center.y, minY, maxY);
            }

            return center;
        }

        private float OverflowArea(Vector2 center, Vector2 size, Rect bounds)
        {
            float halfW = size.x * 0.5f;
            float halfH = size.y * 0.5f;
            float left = Mathf.Max(0f, bounds.xMin + _screenPadding - (center.x - halfW));
            float right = Mathf.Max(0f, center.x + halfW - (bounds.xMax - _screenPadding));
            float bottom = Mathf.Max(0f, bounds.yMin + _screenPadding - (center.y - halfH));
            float top = Mathf.Max(0f, center.y + halfH - (bounds.yMax - _screenPadding));
            return left + right + bottom + top;
        }

        private float OverlapArea(Vector2 center, Vector2 size, Rect target)
        {
            float halfW = size.x * 0.5f;
            float halfH = size.y * 0.5f;
            float minX = center.x - halfW;
            float maxX = center.x + halfW;
            float minY = center.y - halfH;
            float maxY = center.y + halfH;
            float overlapW = Mathf.Max(0f, Mathf.Min(maxX, target.xMax) - Mathf.Max(minX, target.xMin));
            float overlapH = Mathf.Max(0f, Mathf.Min(maxY, target.yMax) - Mathf.Max(minY, target.yMin));
            return overlapW * overlapH;
        }
    }
}
