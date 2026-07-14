using System;
using System.Collections.Generic;
using System.Globalization;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 行动轴进度条：把当前周的行动轴渲染成一条连续进度条，绿色填充覆盖 [0, CurrentDay/长度]，
    /// 下方三角箭头指向当前进度，整天位置画刻度线与天序号，特殊节点图标（商店/利息/Boss/事件）
    /// 按 day/长度 比例摆在进度条上方。天数为 0.1 粒度的 float（见 <see cref="GameRun.CurrentDay"/>）。
    /// 所有子物体在 <see cref="Build"/> 时数据驱动重建到 <see cref="_container"/>，节点数据来自
    /// <see cref="TimelineService.GetNodes"/>。
    /// </summary>
    public sealed class ActionAxisBar : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private RectTransform _container;
        [SerializeField] private RectTransform _positionMarker;
        [SerializeField] private Text _remainingDaysText;
        [SerializeField] private Image _fillTemplate;
        [SerializeField] private Image _tickTemplate;
        [SerializeField] private Text _dayLabelTemplate;
        [SerializeField] private Image _nodeIconTemplate;
        [SerializeField] private Text _nodeLabelTemplate;

        [Header("节点图标")]
        [SerializeField] private Sprite _shopNodeSprite;
        [SerializeField] private Sprite _interestNodeSprite;
        [SerializeField] private Sprite _bossNodeSprite;
        [SerializeField] private Sprite _eventNodeSprite;

        [Header("样式")]
        [SerializeField] private Color _fillColor = new Color(0.55f, 0.85f, 0.45f, 0.85f);
        [SerializeField] private Color _tickColor = new Color(0.15f, 0.12f, 0.08f, 0.35f);
        [SerializeField] private Color _dayTextColor = new Color(0.12f, 0.09f, 0.06f, 1f);
        [SerializeField] private float _nodeIconHeight = 26f;

        private readonly List<GameObject> _spawned = new();
        private Font _cachedFont;

        /// <summary>按当前 run 的行动轴状态重建进度条填充、整天刻度、节点图标与当前位置箭头。</summary>
        public void Build(GameRun run, Action<cfg.TimelineNode, GameObject> onNodeCreated = null)
        {
            Clear();
            if (run == null || _container == null)
            {
                return;
            }

            float length = Mathf.Max(0.1f, run.TimelineLengthDays);
            int wholeDays = Mathf.Max(1, Mathf.RoundToInt(length));
            float ratio = Mathf.Clamp01(run.CurrentDay / length);

            var nodeByDay = new Dictionary<int, cfg.TimelineNode>();
            foreach (cfg.TimelineNode node in TimelineService.GetNodes(run))
            {
                nodeByDay[node.Day] = node;
            }

            BuildFill(ratio);
            BuildTicksAndLabels(wholeDays, length);
            BuildNodeIcons(run, nodeByDay, length, onNodeCreated);
            PositionMarker(ratio);
            RefreshRemainingDays(run, length);
        }

        /// <summary>绿色进度填充：用锚点宽度表示 [0, ratio]，置于最底层。</summary>
        private void BuildFill(float ratio)
        {
            Image image = SpawnTemplate(_fillTemplate, "AxisFill");
            if (image == null)
            {
                return;
            }

            var rect = (RectTransform)image.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(ratio, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            image.color = _fillColor;
            image.raycastTarget = false;
            image.transform.SetAsFirstSibling();
        }

        /// <summary>整天刻度线（1..N-1 分隔）与每格天序号。</summary>
        private void BuildTicksAndLabels(int wholeDays, float length)
        {
            for (int i = 1; i < wholeDays; i++)
            {
                float x = Mathf.Clamp01(i / length);
                Image image = SpawnTemplate(_tickTemplate, $"Tick_{i}");
                if (image == null)
                {
                    continue;
                }

                var rect = (RectTransform)image.transform;
                rect.anchorMin = new Vector2(x, 0.12f);
                rect.anchorMax = new Vector2(x, 0.88f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(2f, 0f);
                rect.anchoredPosition = Vector2.zero;
                image.color = _tickColor;
                image.raycastTarget = false;
            }

            for (int day = 1; day <= wholeDays; day++)
            {
                float minX = Mathf.Clamp01((day - 1) / length);
                float maxX = Mathf.Clamp01(day / length);
                Text text = SpawnTemplate(_dayLabelTemplate, $"Day_{day}");
                if (text == null)
                {
                    continue;
                }

                var rect = (RectTransform)text.transform;
                rect.anchorMin = new Vector2(minX, 0f);
                rect.anchorMax = new Vector2(maxX, 1f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                text.text = day.ToString();
                text.font = ResolveFont();
                text.color = _dayTextColor;
                text.alignment = TextAnchor.MiddleCenter;
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 8;
                text.resizeTextMaxSize = 22;
                text.raycastTarget = false;
            }
        }

        /// <summary>特殊节点图标：按 day/长度 比例摆在进度条上方。</summary>
        private void BuildNodeIcons(GameRun run, Dictionary<int, cfg.TimelineNode> nodeByDay, float length, Action<cfg.TimelineNode, GameObject> onNodeCreated)
        {
            foreach (KeyValuePair<int, cfg.TimelineNode> kv in nodeByDay)
            {
                cfg.TimelineNode node = kv.Value;
                if (node == null)
                {
                    continue;
                }

                ActionDisplayKind kind = ActionDisplay.KindOf(run.Tables, TimelineService.NodeAction(run, node));
                Sprite sprite = NodeSprite(kind);
                float x = Mathf.Clamp01(kv.Key / length);
                GameObject go;
                RectTransform rect;
                // 锚定到进度条顶边、图标底部贴着顶边向上突出。
                if (sprite != null)
                {
                    Image image = SpawnTemplate(_nodeIconTemplate, $"Node_{kv.Key}");
                    if (image == null)
                    {
                        continue;
                    }

                    go = image.gameObject;
                    rect = (RectTransform)image.transform;
                    rect.sizeDelta = new Vector2(_nodeIconHeight, _nodeIconHeight);
                    image.sprite = sprite;
                    image.preserveAspect = true;
                    image.raycastTarget = true;
                }
                else
                {
                    Text text = SpawnTemplate(_nodeLabelTemplate, $"Node_{kv.Key}");
                    if (text == null)
                    {
                        continue;
                    }

                    go = text.gameObject;
                    rect = (RectTransform)text.transform;
                    text.text = NodeLabel(kind);
                    text.font = ResolveFont();
                    text.color = _dayTextColor;
                    text.alignment = TextAnchor.LowerCenter;
                    text.resizeTextForBestFit = true;
                    text.resizeTextMinSize = 8;
                    text.resizeTextMaxSize = 18;
                    text.raycastTarget = true;
                    rect.sizeDelta = new Vector2(_nodeIconHeight * 2f, _nodeIconHeight);
                }

                rect.anchorMin = new Vector2(x, 1f);
                rect.anchorMax = new Vector2(x, 1f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, 2f);
                onNodeCreated?.Invoke(node, go);
            }
        }

        private void PositionMarker(float ratio)
        {
            if (_positionMarker == null)
            {
                return;
            }

            // 三角箭头指向当前进度点：把 [0,1] 的进度映射到进度条容器所占的水平区间
            // （容器相对父物体是内缩的，marker 与容器同父，需按容器锚点区间换算），
            // 并保留 marker 原有宽度与竖直位置。
            float mapped = ratio;
            if (_container != null && _positionMarker.parent == _container.parent)
            {
                mapped = _container.anchorMin.x + ratio * (_container.anchorMax.x - _container.anchorMin.x);
            }

            float halfWidth = (_positionMarker.anchorMax.x - _positionMarker.anchorMin.x) * 0.5f;
            _positionMarker.anchorMin = new Vector2(mapped - halfWidth, _positionMarker.anchorMin.y);
            _positionMarker.anchorMax = new Vector2(mapped + halfWidth, _positionMarker.anchorMax.y);
            _positionMarker.anchoredPosition = new Vector2(0f, _positionMarker.anchoredPosition.y);
        }

        private void RefreshRemainingDays(GameRun run, float length)
        {
            if (_remainingDaysText == null)
            {
                return;
            }

            float remaining = Mathf.Max(0f, length - run.CurrentDay);
            _remainingDaysText.text = $"{remaining.ToString("0.#", CultureInfo.InvariantCulture)}天";
        }

        private T SpawnTemplate<T>(T template, string childName) where T : Component
        {
            if (template == null)
            {
                Debug.LogError($"{nameof(ActionAxisBar)} 缺少 {childName} 对应的 UI template。", this);
                return null;
            }

            T instance = Instantiate(template, _container);
            instance.gameObject.name = childName;
            instance.gameObject.SetActive(true);
            var rect = (RectTransform)instance.transform;
            rect.SetParent(_container, false);
            rect.localScale = Vector3.one;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            _spawned.Add(instance.gameObject);
            return instance;
        }

        private Font ResolveFont()
        {
            if (_remainingDaysText != null && _remainingDaysText.font != null)
            {
                return _remainingDaysText.font;
            }

            if (_cachedFont == null)
            {
                _cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_cachedFont == null)
                {
                    _cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
            }

            return _cachedFont;
        }

        private void Clear()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }

            _spawned.Clear();
        }

        private static string NodeLabel(ActionDisplayKind kind)
        {
            switch (kind)
            {
                case ActionDisplayKind.Boss: return "BOSS";
                case ActionDisplayKind.Interest: return "利息";
                case ActionDisplayKind.Shop: return "商店";
                case ActionDisplayKind.Event: return "事件";
                default: return string.Empty;
            }
        }

        private Sprite NodeSprite(ActionDisplayKind kind)
        {
            switch (kind)
            {
                case ActionDisplayKind.Boss:
                    return _bossNodeSprite != null ? _bossNodeSprite : Resources.Load<Sprite>("Sprites/UI/icon_axis_boss");
                case ActionDisplayKind.Interest:
                    return _interestNodeSprite != null ? _interestNodeSprite : Resources.Load<Sprite>("Sprites/UI/icon_axis_interest");
                case ActionDisplayKind.Shop:
                    return _shopNodeSprite != null ? _shopNodeSprite : Resources.Load<Sprite>("Sprites/UI/icon_axis_shop");
                case ActionDisplayKind.Event:
                    return _eventNodeSprite != null ? _eventNodeSprite : Resources.Load<Sprite>("Sprites/UI/icon_axis_event");
                default:
                    return null;
            }
        }
    }
}
