using DG.Tweening;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// STS2 风格世界目标箭头：在屏幕空间计算 19 段二次贝塞尔曲线，再投影到经营挑战世界。
    /// 箭头末端沿曲线切线朝向，悬停有效目标时提供颜色与弹性缩放反馈。
    /// </summary>
    public sealed class WorldTargetArrow : MonoBehaviour
    {
        private const int SegmentCount = 19;
        private const float SegmentScaleStart = 0.28f;
        private const float SegmentScaleEnd = 0.42f;

        private static readonly Color DefaultColor = new Color(0.15f, 0.15f, 0.15f, 0.95f);
        private static readonly Color HighlightColor = new Color(0.18f, 0.78f, 0.38f, 1f);

        private LineRenderer _line;
        private SpriteRenderer _head;
        private Camera _camera;
        private Vector2 _startScreen;
        private Vector3 _headBaseScale;
        private float _headGapPixels = 28f;
        private Tween _headTween;
        private bool _highlighted;

        public bool TargetHighlighted => _highlighted;

        public static WorldTargetArrow Create(
            WorldTargetArrow prefab,
            Transform parent,
            float cellSize,
            Camera camera,
            Vector2 startScreen)
        {
            if (prefab == null || camera == null)
            {
                Debug.LogError($"{nameof(WorldTargetArrow)} 缺少 prefab 或 Camera。");
                return null;
            }

            WorldTargetArrow arrow = Instantiate(prefab, parent);
            arrow.gameObject.name = "WorldTargetArrow";
            arrow.Build(cellSize, camera, startScreen);
            return arrow;
        }

        private void Build(float cellSize, Camera camera, Vector2 startScreen)
        {
            _camera = camera;
            _startScreen = startScreen;
            _line = GetComponentInChildren<LineRenderer>(true);
            _head = GetComponentInChildren<SpriteRenderer>(true);
            if (_line == null || _head == null)
            {
                Debug.LogError($"{nameof(WorldTargetArrow)} prefab 缺少 LineRenderer 或箭头 SpriteRenderer。", this);
                return;
            }

            float lineWidth = Mathf.Max(0.06f, cellSize * 0.16f);
            _headGapPixels = Mathf.Max(18f, cellSize * 42f);
            _line.useWorldSpace = true;
            _line.positionCount = SegmentCount;
            _line.numCapVertices = 4;
            _line.textureMode = LineTextureMode.Stretch;
            _line.widthMultiplier = lineWidth;
            _line.widthCurve = new AnimationCurve(
                new Keyframe(0f, SegmentScaleStart / SegmentScaleEnd),
                new Keyframe(1f, 1f));
            if (_line.sharedMaterial == null)
            {
                _line.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            }

            _line.startColor = DefaultColor;
            _line.endColor = DefaultColor;
            BattleSorting.Apply(_line, BattleSorting.WorldUi, BattleSorting.OrderTargetArrowLine);

            if (_head.sprite == null)
            {
                _head.sprite = Resources.Load<Sprite>("Sprites/UI/ArrowHead");
            }

            _head.color = DefaultColor;
            SpriteRenderStyle.ApplyUnlitMaterial(_head);
            BattleSorting.Apply(_head, BattleSorting.WorldUi, BattleSorting.OrderTargetArrowHead);

            float headSize = Mathf.Max(0.2f, cellSize * 0.55f);
            Sprite headSprite = _head.sprite;
            if (headSprite != null)
            {
                Vector2 bounds = headSprite.bounds.size;
                float sx = bounds.x > 0f ? headSize / bounds.x : headSize;
                float sy = bounds.y > 0f ? headSize / bounds.y : headSize;
                _headBaseScale = new Vector3(sx, sy, 1f) * 0.95f;
            }
            else
            {
                _headBaseScale = Vector3.one * headSize * 0.95f;
            }

            _head.transform.localScale = _headBaseScale;
        }

        public void UpdateTo(Vector2 endScreen)
        {
            if (_line == null || _head == null || _camera == null)
            {
                return;
            }

            Vector2 control = new Vector2(
                _startScreen.x - (endScreen.x - _startScreen.x) * 0.25f,
                _startScreen.y > Screen.height * 0.5f
                    ? endScreen.y + (endScreen.y - _startScreen.y) * 0.5f
                    : endScreen.y * 0.75f + _startScreen.y * 0.25f);

            Vector2 tangent = endScreen - control;
            Vector2 direction = tangent.sqrMagnitude > 0.001f ? tangent.normalized : Vector2.right;
            Vector2 lineEnd = endScreen - direction * _headGapPixels;

            for (int i = 0; i < SegmentCount; i++)
            {
                float t = i / (float)(SegmentCount - 1);
                Vector2 screenPoint = QuadraticBezier(_startScreen, control, lineEnd, t);
                _line.SetPosition(i, ScreenToWorld(screenPoint, -0.02f));
            }

            _head.transform.position = ScreenToWorld(endScreen, -0.03f);
            _head.transform.rotation = Quaternion.Euler(
                0f,
                0f,
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        }

        public void SetTargetHighlighted(bool highlighted)
        {
            if (_highlighted == highlighted || _head == null || _line == null)
            {
                return;
            }

            _highlighted = highlighted;
            Color color = highlighted ? HighlightColor : DefaultColor;
            _line.startColor = color;
            _line.endColor = color;
            _head.color = color;

            _headTween?.Kill();
            Vector3 scale = highlighted ? _headBaseScale * (1.05f / 0.95f) : _headBaseScale;
            _headTween = _head.transform
                .DOScale(scale, highlighted ? 0.45f : 0.12f)
                .SetEase(highlighted ? Ease.OutElastic : Ease.OutQuad);
        }

        public void Destroy()
        {
            _headTween?.Kill();
            if (this != null)
            {
                Object.Destroy(gameObject);
            }
        }

        private Vector3 ScreenToWorld(Vector2 screenPoint, float z)
        {
            float depth = Mathf.Abs(_camera.transform.position.z);
            Vector3 world = _camera.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, depth));
            world.z = z;
            return world;
        }

        private static Vector2 QuadraticBezier(Vector2 from, Vector2 control, Vector2 to, float t)
        {
            float oneMinusT = 1f - t;
            return oneMinusT * oneMinusT * from
                + 2f * oneMinusT * t * control
                + t * t * to;
        }
    }
}
