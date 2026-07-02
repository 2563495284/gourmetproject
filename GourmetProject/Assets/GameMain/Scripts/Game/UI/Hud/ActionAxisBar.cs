using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 行动轴横条：按当前周行动轴长度铺 N 个天格，标注特殊节点图标，并把当前位置三角标定位到当前天。
    /// 数据来自 <see cref="TimelineService.GetNodes"/> 与 <see cref="GameRun.CurrentDay"/>/<see cref="GameRun.TimelineLengthDays"/>，
    /// 替代旧 WeekMapForm 里拼字符串的纯文本轴。天格用 ActionAxisCellView 数据驱动实例化到 <see cref="_container"/>。
    /// </summary>
    public sealed class ActionAxisBar : MonoBehaviour
    {
        [SerializeField] private RectTransform _container;
        [SerializeField] private ActionAxisCellView _cellPrefab;
        [SerializeField] private RectTransform _positionMarker;
        [SerializeField] private float _cellGap = 0.01f;

        private readonly List<ActionAxisCellView> _cells = new();

        /// <summary>按当前 run 的行动轴状态重建天格与当前位置标记。</summary>
        public void Build(GameRun run)
        {
            Clear();
            if (run == null || _container == null || _cellPrefab == null)
            {
                return;
            }

            int length = Mathf.Max(1, run.TimelineLengthDays);

            var nodeByDay = new Dictionary<int, cfg.TimelineNodeType>();
            foreach (cfg.TimelineNode node in TimelineService.GetNodes(run))
            {
                nodeByDay[node.Day] = node.NodeType;
            }

            float cellW = (1f - _cellGap * (length + 1)) / length;
            for (int i = 0; i < length; i++)
            {
                int day = i + 1;
                float minX = _cellGap + i * (cellW + _cellGap);
                float maxX = minX + cellW;

                ActionAxisCellView cell = Instantiate(_cellPrefab, _container);
                cell.gameObject.name = $"AxisCell_{day}";
                var rect = (RectTransform)cell.transform;
                rect.anchorMin = new Vector2(minX, 0f);
                rect.anchorMax = new Vector2(maxX, 1f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;

                string nodeLabel = nodeByDay.TryGetValue(day, out cfg.TimelineNodeType type) ? NodeLabel(type) : string.Empty;
                cell.Bind(day, day <= run.CurrentDay, nodeLabel);
                _cells.Add(cell);
            }

            PositionMarker(run, length, cellW);
        }

        private void PositionMarker(GameRun run, int length, float cellW)
        {
            if (_positionMarker == null)
            {
                return;
            }

            // 当前位置标记落在「下一步将执行」的天格中心：CurrentDay 是已过天数，指向第 CurrentDay+1 天。
            int markerIndex = Mathf.Clamp(run.CurrentDay, 0, length - 1);
            float minX = _cellGap + markerIndex * (cellW + _cellGap);
            float centerX = minX + cellW * 0.5f;

            _positionMarker.anchorMin = new Vector2(centerX, _positionMarker.anchorMin.y);
            _positionMarker.anchorMax = new Vector2(centerX, _positionMarker.anchorMax.y);
            _positionMarker.anchoredPosition = new Vector2(0f, _positionMarker.anchoredPosition.y);
        }

        private void Clear()
        {
            foreach (ActionAxisCellView cell in _cells)
            {
                if (cell != null)
                {
                    Destroy(cell.gameObject);
                }
            }

            _cells.Clear();
        }

        private static string NodeLabel(cfg.TimelineNodeType type)
        {
            switch (type)
            {
                case cfg.TimelineNodeType.Boss: return "BOSS";
                case cfg.TimelineNodeType.Interest: return "利息";
                case cfg.TimelineNodeType.Shop: return "商店";
                case cfg.TimelineNodeType.Event: return "事件";
                default: return string.Empty;
            }
        }
    }
}
