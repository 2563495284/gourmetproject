using System.Collections.Generic;
using System.Text;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Flow;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 行动轴「n 选一行动」界面：每次打开时读取整局行动组序列，并从当前行动组随机最多 3 个行动。
    /// 玩家选择一个行动后交回 <see cref="BattleForm"/> 执行（推进天数、触发节点、可能开战）。
    /// 固定壳（遮罩/面板/标题/按钮/卡片容器）复用 WeekMapForm.prefab，卡片用 WeekEventCardView 数据驱动。
    /// </summary>
    public sealed class WeekMapForm : UGuiForm
    {
        [SerializeField] private Text _titleText;
        [SerializeField] private Button _shopButton;
        [SerializeField] private Button _skipButton;
        [SerializeField] private RectTransform _cardsContainer;
        [SerializeField] private WeekEventCardView _cardPrefab;

        private readonly List<WeekEventCardView> _cards = new();
        private Text _axisText;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            // 商店改由行动/节点进入，隐藏旧的手动商店入口。
            _shopButton.gameObject.SetActive(false);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            GameRun run = GameRunContext.Current;
            if (run == null)
            {
                Close();
                return;
            }

            Build(run);
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            ClearCards();
            base.OnClose(isShutdown, userData);
        }

        private void Build(GameRun run)
        {
            ClearCards();

            string weekLabel = run.IsEndless ? $"无尽 第 {run.WeekIndex - run.TotalWeeks} 关" : $"第 {run.WeekIndex} 周";
            _titleText.text = $"{weekLabel} · 第 {run.CurrentDay}/{run.TimelineLengthDays} 天 · 本周第 {run.ActionStepIndex + 1} 次 · 整局第 {run.RunActionStepIndex + 1} 次";
            BuildAxisText(run);

            List<ActionChoice> choices = RollChoices(run);
            bool hasActions = choices.Count > 0;

            _cardsContainer.gameObject.SetActive(hasActions);
            _skipButton.gameObject.SetActive(!hasActions);
            _skipButton.onClick.RemoveAllListeners();
            _skipButton.onClick.AddListener(() => Choose(null));

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
                float maxX = minX + cardW;
                SpawnCard(choices[i], minX, maxX);
            }
        }

        private static List<ActionChoice> RollChoices(GameRun run)
        {
            if (run == null)
            {
                return new List<ActionChoice>();
            }

            var rng = GameApp.Random.Stream($"action_choices_r{run.RunActionStepIndex}_w{run.WeekIndex}_d{run.CurrentDay}_s{run.ActionStepIndex}");
            return ActionScheduleService.GenerateChoices(run, rng);
        }

        /// <summary>在标题下方动态生成行动轴进度文本（已过/当前/未来天 + 节点标注）。</summary>
        private void BuildAxisText(GameRun run)
        {
            if (_axisText == null)
            {
                var go = new GameObject("AxisText", typeof(RectTransform), typeof(Text));
                go.transform.SetParent(_titleText.transform.parent, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = new Vector2(0.05f, 0.84f);
                rect.anchorMax = new Vector2(0.95f, 0.9f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                _axisText = go.GetComponent<Text>();
                _axisText.font = _titleText.font;
                _axisText.fontSize = Mathf.Max(14, _titleText.fontSize - 6);
                _axisText.color = _titleText.color;
                _axisText.alignment = TextAnchor.MiddleCenter;
            }

            var nodeByDay = new Dictionary<int, cfg.TimelineNodeType>();
            foreach (cfg.TimelineNode node in TimelineService.GetNodes(run))
            {
                nodeByDay[node.Day] = node.NodeType;
            }

            var sb = new StringBuilder();
            for (int day = 1; day <= run.TimelineLengthDays; day++)
            {
                if (day > 1)
                {
                    sb.Append(' ');
                }

                string mark = day <= run.CurrentDay ? "●" : "○";
                if (nodeByDay.TryGetValue(day, out cfg.TimelineNodeType type))
                {
                    sb.Append($"{mark}{NodeShort(type)}");
                }
                else
                {
                    sb.Append(mark);
                }
            }

            _axisText.text = sb.ToString();
        }

        private static string NodeShort(cfg.TimelineNodeType type)
        {
            switch (type)
            {
                case cfg.TimelineNodeType.Boss: return "[Boss]";
                case cfg.TimelineNodeType.Interest: return "[利息]";
                case cfg.TimelineNodeType.Shop: return "[商店]";
                case cfg.TimelineNodeType.Event: return "[事件]";
                default: return string.Empty;
            }
        }

        private void SpawnCard(ActionChoice choice, float minX, float maxX)
        {
            WeekEventCardView card = Instantiate(_cardPrefab, _cardsContainer);
            var rect = (RectTransform)card.transform;
            rect.anchorMin = new Vector2(minX, 0.12f);
            rect.anchorMax = new Vector2(maxX, 0.82f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            ActionChoice captured = choice;
            card.Bind(choice, () => Choose(captured));
            _cards.Add(card);
        }

        private void ClearCards()
        {
            foreach (WeekEventCardView card in _cards)
            {
                if (card != null)
                {
                    Destroy(card.gameObject);
                }
            }

            _cards.Clear();
        }

        private void Choose(ActionChoice choice)
        {
            Close();
            BattleForm.Active?.OnActionPicked(choice);
        }

        private void Close()
        {
            GameApp.UI.CloseUIForm(UIForm);
        }
    }
}
