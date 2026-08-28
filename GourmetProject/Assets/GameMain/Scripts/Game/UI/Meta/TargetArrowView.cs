using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 消耗品使用时的目标箭头。根节点铺满 Canvas，起点固定在消耗品图标，
    /// 19 个箭身段沿二次贝塞尔曲线排列，箭头跟随鼠标。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class TargetArrowView : MonoBehaviour
    {
        internal const int SegmentCount = 19;
        internal const float SegmentScaleStart = 0.28f;
        internal const float SegmentScaleEnd = 0.42f;
        internal const float ReferenceHeight = 1080f;
        internal const float HeadTargetOffset = 88f;
        internal const float SegmentEndOffset = 40f;
        internal const float HeadDefaultScale = 0.55f;
        internal const float HeadHoverScale = 0.61f;

        internal static readonly Color DefaultColor = new Color32(0x6F, 0xD8, 0xE8, 0xFF);
        internal static readonly Color HighlightColor = new Color32(0x36, 0xC7, 0x8A, 0xFF);

        [SerializeField] private Image _segmentTemplate;
        [SerializeField] private Image _head;

        private readonly Image[] _segments = new Image[SegmentCount];
        private readonly Vector2[] _segmentPositions = new Vector2[SegmentCount];
        private readonly float[] _segmentRotations = new float[SegmentCount];
        private readonly float[] _segmentScales = new float[SegmentCount];

        private RectTransform _rect;
        private Canvas _canvas;
        private Camera _eventCamera;
        private Vector2 _startScreenPoint;
        private float _headRotationDegrees;
        private Tween _headTween;
        private bool _highlighted;
        private bool _segmentsBuilt;

        internal IReadOnlyList<Image> Segments => _segments;
        internal Image Head => _head;
        internal Image SegmentTemplate => _segmentTemplate;
        internal bool TargetHighlighted => _highlighted;

        private RectTransform Rect
        {
            get
            {
                if (_rect == null)
                {
                    _rect = (RectTransform)transform;
                }

                return _rect;
            }
        }

        private void Awake()
        {
            EnsureRefs();
            BuildSegments();
            BindCanvas();
        }

        private void Update()
        {
            if (Mouse.current == null)
            {
                return;
            }

            SetEndScreenPoint(Mouse.current.position.ReadValue());
        }

        private void OnDestroy()
        {
            _headTween?.Kill();
            _headTween = null;
        }

        public void SetupArrow(Vector2 startScreenPoint)
        {
            EnsureRefs();
            BuildSegments();
            _startScreenPoint = startScreenPoint;
            _headRotationDegrees = 0f;
            BindCanvas();
            StretchToParent();
            ApplyHighlighting(highlighted: false, animate: false);
            SetEndScreenPoint(Mouse.current != null ? Mouse.current.position.ReadValue() : startScreenPoint);
        }

        public void SetEndScreenPoint(Vector2 endScreenPoint)
        {
            EnsureRefs();
            BuildSegments();
            if (_head == null
                || !TryScreenToLocal(_startScreenPoint, out Vector2 startLocal)
                || !TryScreenToLocal(endScreenPoint, out Vector2 endLocal))
            {
                return;
            }

            float referenceScale = ReferenceScale();
            bool fromBottomHalf = startLocal.y < Rect.rect.center.y;
            CalculateGeometry(
                startLocal,
                endLocal,
                _headRotationDegrees,
                referenceScale,
                fromBottomHalf,
                _segmentPositions,
                _segmentRotations,
                _segmentScales,
                out Vector2 headPosition,
                out float headRotation,
                out _,
                out _);

            for (int i = 0; i < SegmentCount; i++)
            {
                RectTransform segment = _segments[i].rectTransform;
                segment.anchoredPosition = _segmentPositions[i];
                segment.localRotation = Quaternion.Euler(0f, 0f, _segmentRotations[i]);
                segment.localScale = Vector3.one * _segmentScales[i];
            }

            RectTransform head = _head.rectTransform;
            head.anchoredPosition = headPosition;
            head.localRotation = Quaternion.Euler(0f, 0f, headRotation);
            _headRotationDegrees = headRotation;
        }

        public void SetTargetHighlighted(bool highlighted)
        {
            EnsureRefs();
            BuildSegments();
            if (_highlighted == highlighted)
            {
                return;
            }

            ApplyHighlighting(highlighted, animate: highlighted);
        }

        internal static void CalculateGeometry(
            Vector2 initialPosition,
            Vector2 targetPosition,
            float previousHeadRotationDegrees,
            float referenceScale,
            bool fromBottomHalf,
            Vector2[] segmentPositions,
            float[] segmentRotations,
            float[] segmentScales,
            out Vector2 headPosition,
            out float headRotationDegrees,
            out Vector2 controlPoint,
            out Vector2 finalPosition)
        {
            if (segmentPositions == null || segmentPositions.Length < SegmentCount)
            {
                throw new ArgumentException($"至少需要 {SegmentCount} 个箭身位置。", nameof(segmentPositions));
            }

            if (segmentRotations == null || segmentRotations.Length < SegmentCount)
            {
                throw new ArgumentException($"至少需要 {SegmentCount} 个箭身旋转值。", nameof(segmentRotations));
            }

            if (segmentScales == null || segmentScales.Length < SegmentCount)
            {
                throw new ArgumentException($"至少需要 {SegmentCount} 个箭身缩放值。", nameof(segmentScales));
            }

            float scale = Mathf.Max(0.0001f, referenceScale);
            headPosition = targetPosition
                + Rotate(Vector2.down * (HeadTargetOffset * scale), previousHeadRotationDegrees);
            finalPosition = targetPosition
                + Rotate(Vector2.down * (SegmentEndOffset * scale), previousHeadRotationDegrees);
            controlPoint = CalculateControlPoint(initialPosition, headPosition, fromBottomHalf);
            headRotationDegrees = DirectionToUpRotation(targetPosition - controlPoint);

            for (int i = 0; i < SegmentCount; i++)
            {
                float t = i / 20f;
                segmentPositions[i] = QuadraticBezier(initialPosition, finalPosition, controlPoint, t);
                segmentScales[i] = Mathf.LerpUnclamped(
                    SegmentScaleStart,
                    SegmentScaleEnd,
                    i * 2f / SegmentCount) * scale;
            }

            segmentRotations[0] = DirectionToUpRotation(segmentPositions[1] - segmentPositions[0]);
            for (int i = 1; i < SegmentCount; i++)
            {
                segmentRotations[i] = DirectionToUpRotation(
                    segmentPositions[i] - segmentPositions[i - 1]);
            }
        }

        internal static Vector2 CalculateControlPoint(
            Vector2 initialPosition,
            Vector2 headPosition,
            bool fromBottomHalf)
        {
            Vector2 control = Vector2.zero;
            control.x = initialPosition.x - (headPosition.x - initialPosition.x) * 0.25f;
            control.y = fromBottomHalf
                ? headPosition.y + (headPosition.y - initialPosition.y) * 0.5f
                : headPosition.y * 0.75f + initialPosition.y * 0.25f;
            return control;
        }

        internal static Vector2 QuadraticBezier(
            Vector2 initialPosition,
            Vector2 finalPosition,
            Vector2 controlPoint,
            float t)
        {
            float oneMinusT = 1f - t;
            return oneMinusT * oneMinusT * initialPosition
                + 2f * oneMinusT * t * controlPoint
                + t * t * finalPosition;
        }

        private void ApplyHighlighting(bool highlighted, bool animate)
        {
            _highlighted = highlighted;
            Color color = highlighted ? HighlightColor : DefaultColor;
            for (int i = 0; i < SegmentCount; i++)
            {
                if (_segments[i] != null)
                {
                    _segments[i].color = color;
                }
            }

            if (_head == null)
            {
                return;
            }

            _head.color = color;
            _headTween?.Kill();
            _headTween = null;

            float targetScale = ReferenceScale()
                * (highlighted ? HeadHoverScale : HeadDefaultScale);
            if (!animate)
            {
                _head.rectTransform.localScale = Vector3.one * targetScale;
                return;
            }

            _headTween = _head.rectTransform
                .DOScale(Vector3.one * targetScale, 1f)
                .SetEase(Ease.OutElastic)
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        private void BuildSegments()
        {
            if (_segmentsBuilt || _segmentTemplate == null || _head == null)
            {
                return;
            }

            _segmentTemplate.gameObject.SetActive(false);
            Transform parent = _segmentTemplate.transform.parent;
            int firstSiblingIndex = _segmentTemplate.transform.GetSiblingIndex() + 1;
            for (int i = 0; i < SegmentCount; i++)
            {
                Image segment = Instantiate(_segmentTemplate, parent);
                segment.gameObject.name = $"Segment{i:00}";
                segment.raycastTarget = false;
                segment.gameObject.SetActive(true);
                segment.transform.SetSiblingIndex(firstSiblingIndex + i);
                _segments[i] = segment;
            }

            _head.raycastTarget = false;
            _head.transform.SetAsLastSibling();
            _segmentsBuilt = true;
        }

        private static float DirectionToUpRotation(Vector2 direction)
        {
            return Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        }

        private static Vector2 Rotate(Vector2 vector, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            return new Vector2(
                vector.x * cos - vector.y * sin,
                vector.x * sin + vector.y * cos);
        }

        private float ReferenceScale()
        {
            float height = Rect.rect.height;
            return height > 0.001f ? height / ReferenceHeight : 1f;
        }

        private bool TryScreenToLocal(Vector2 screenPoint, out Vector2 localPoint)
        {
            BindCanvas();
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                Rect,
                screenPoint,
                _eventCamera,
                out localPoint);
        }

        private void BindCanvas()
        {
            if (_canvas == null)
            {
                _canvas = GetComponentInParent<Canvas>();
            }

            _eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;
        }

        private void StretchToParent()
        {
            RectTransform rect = Rect;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private void EnsureRefs()
        {
            if (_segmentTemplate == null)
            {
                Debug.LogError($"{nameof(TargetArrowView)} prefab 缺少箭身模板绑定。", this);
            }

            if (_head == null)
            {
                Debug.LogError($"{nameof(TargetArrowView)} prefab 缺少箭头绑定。", this);
            }
        }
    }
}
