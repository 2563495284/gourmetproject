using System;
using GourmetProject.Game.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 周地图「三选一」事件卡视图。固定结构在 WeekEventCardView.prefab，
    /// 文案与点击回调通过 <see cref="Bind"/> 数据驱动填充。
    /// </summary>
    public sealed class WeekEventCardView : MonoBehaviour
    {
        [SerializeField] private Text _nameText;
        [SerializeField] private Text _descText;
        [SerializeField] private Text _timeText;
        [SerializeField] private Button _pickButton;

        public void Bind(cfg.GameEvent ev, Action onPick)
        {
            Bind(ev.Name, ev.Desc, ev.TimeCost, onPick);
        }

        /// <summary>行动轴「三选一行动」绑定。</summary>
        public void Bind(cfg.GameAction action, Action onPick)
        {
            Bind(action.Name, action.Desc, action.CostDays, onPick);
        }

        /// <summary>v2 行动组序列绑定：使用本次展开后的耗时。</summary>
        public void Bind(ScheduledActionChoice choice, Action onPick)
        {
            cfg.GameAction action = choice?.Action;
            if (action == null)
            {
                Bind("休息", "没有可执行行动。", 1, onPick);
                return;
            }

            Bind(action.Name, action.Desc, choice.CostDays, onPick);
        }

        public void Bind(string name, string desc, int costDays, Action onPick)
        {
            _nameText.text = name;
            _descText.text = desc;
            _timeText.text = $"耗时 {costDays} 天";

            _pickButton.onClick.RemoveAllListeners();
            _pickButton.onClick.AddListener(() => onPick?.Invoke());
        }
    }
}
