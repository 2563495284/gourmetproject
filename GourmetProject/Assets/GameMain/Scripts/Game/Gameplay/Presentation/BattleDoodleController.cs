using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.Gameplay.Presentation
{
    /// <summary>
    /// 战斗内玩家自由涂鸦层（仿杀戮尖塔2画笔）：右键拖动按距离阈值采样，逐笔生成 <see cref="LineRenderer"/> 矢量笔迹，
    /// 归入最顶 Sorting Layer（<see cref="BattleSorting.Doodle"/>）压在所有战斗内容之上。纯表现，不参与玩法结算。
    /// 笔迹挂在子物体 <c>_strokesRoot</c> 下，便于一键清空与整体显隐。
    /// </summary>
    public sealed class BattleDoodleController : MonoBehaviour
    {
        // —— 固定画笔参数 ——
        private static readonly Color BrushColor = new Color(0.15f, 0.1f, 0.08f, 1f);
        private const float BrushWidth = 0.08f;
        private const float MinSampleDistance = 0.05f;
        private const int CapVertices = 6;
        private const int CornerVertices = 6;

        [Tooltip("用于屏幕→世界坐标换算的战斗相机；为空时回退 Camera.main。")]
        [SerializeField] private Camera _camera;

        private static Material _sharedMaterial;

        private Transform _strokesRoot;
        private LineRenderer _currentStroke;
        private readonly List<Vector3> _points = new List<Vector3>();
        private bool _visible = true;

        /// <summary>当前涂鸦层是否可见。</summary>
        public bool IsVisible => _visible;

        private void Awake()
        {
            EnsureStrokesRoot();
        }

        private void Update()
        {
            if (!_visible)
            {
                return;
            }

            Camera cam = _camera != null ? _camera : Camera.main;
            if (cam == null)
            {
                return;
            }

            if (WorldInput.SecondaryPressedThisFrame)
            {
                BeginStroke(WorldInput.MouseWorld(cam));
            }
            else if (_currentStroke != null && WorldInput.SecondaryHeld)
            {
                ExtendStroke(WorldInput.MouseWorld(cam));
            }

            if (WorldInput.SecondaryReleasedThisFrame)
            {
                EndStroke();
            }
        }

        /// <summary>清空所有已绘制的笔迹。</summary>
        public void Clear()
        {
            _currentStroke = null;
            _points.Clear();
            EnsureStrokesRoot();
            for (int i = _strokesRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(_strokesRoot.GetChild(i).gameObject);
            }
        }

        /// <summary>整体显隐涂鸦层；隐藏时同时中断并禁止新建笔迹。</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            EnsureStrokesRoot();
            _strokesRoot.gameObject.SetActive(visible);
            if (!visible)
            {
                _currentStroke = null;
                _points.Clear();
            }
        }

        private void BeginStroke(Vector3 worldPoint)
        {
            EnsureStrokesRoot();
            worldPoint.z = 0f;

            var go = new GameObject("Stroke");
            go.transform.SetParent(_strokesRoot, false);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = CapVertices;
            line.numCornerVertices = CornerVertices;
            line.startWidth = BrushWidth;
            line.endWidth = BrushWidth;
            line.startColor = BrushColor;
            line.endColor = BrushColor;
            line.sharedMaterial = GetSharedMaterial();
            BattleSorting.Apply(line, BattleSorting.Doodle);

            _points.Clear();
            _points.Add(worldPoint);
            line.positionCount = 1;
            line.SetPosition(0, worldPoint);
            _currentStroke = line;
        }

        private void ExtendStroke(Vector3 worldPoint)
        {
            worldPoint.z = 0f;
            if (_points.Count > 0 &&
                Vector3.Distance(_points[_points.Count - 1], worldPoint) < MinSampleDistance)
            {
                return;
            }

            _points.Add(worldPoint);
            _currentStroke.positionCount = _points.Count;
            _currentStroke.SetPosition(_points.Count - 1, worldPoint);
        }

        private void EndStroke()
        {
            // 单击未拖动：补一个极小偏移点，让圆角线帽渲染成一个圆点。
            if (_currentStroke != null && _points.Count == 1)
            {
                Vector3 dot = _points[0] + new Vector3(0.001f, 0f, 0f);
                _currentStroke.positionCount = 2;
                _currentStroke.SetPosition(1, dot);
            }

            _currentStroke = null;
            _points.Clear();
        }

        private void EnsureStrokesRoot()
        {
            if (_strokesRoot != null)
            {
                return;
            }

            Transform existing = transform.Find("Strokes");
            if (existing != null)
            {
                _strokesRoot = existing;
                return;
            }

            var go = new GameObject("Strokes");
            go.transform.SetParent(transform, false);
            _strokesRoot = go.transform;
        }

        private static Material GetSharedMaterial()
        {
            if (_sharedMaterial == null)
            {
                _sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            }

            return _sharedMaterial;
        }
    }
}
