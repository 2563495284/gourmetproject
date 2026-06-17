using System;
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
            _nameText.text = ev.Name;
            _descText.text = ev.Desc;
            _timeText.text = $"耗时 {ev.TimeCost}";

            _pickButton.onClick.RemoveAllListeners();
            _pickButton.onClick.AddListener(() => onPick?.Invoke());
        }
    }
}
