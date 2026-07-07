using System;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 常驻壳左栏信息组件：周/金币/分数要求/食物调整文本，以及「查看胃」「设置」按钮。
    /// 数据刷新与「查看胃」按钮文案/可点态集中在此，点击通过 <see cref="Bind"/> 回调壳。
    /// </summary>
    public sealed class BattleInfoColumn : MonoBehaviour
    {
        private const string ViewStomachLabel = "查看胃";
        private const string StomachBackLabel = "返回";

        [SerializeField] private Text _weekText;
        [SerializeField] private Text _goldText;
        [SerializeField] private Text _scoreReqText;
        [SerializeField] private Text _foodAdjustText;
        [SerializeField] private Button _viewStomachButton;
        [SerializeField] private Button _settingsButton;

        private Text _viewStomachButtonText;

        /// <summary>接线按钮回调（由壳在 OnInit 调用一次）。</summary>
        public void Bind(Action onSettings, Action onViewStomach)
        {
            if (_settingsButton != null)
            {
                _settingsButton.onClick.RemoveAllListeners();
                _settingsButton.onClick.AddListener(() => onSettings?.Invoke());
            }

            if (_viewStomachButton != null)
            {
                _viewStomachButtonText = _viewStomachButton.GetComponentInChildren<Text>(true);
                _viewStomachButton.onClick.RemoveAllListeners();
                _viewStomachButton.onClick.AddListener(() => onViewStomach?.Invoke());
            }
        }

        /// <summary>刷新左栏常驻信息：周/金币（局外），分数要求/食物调整（局内为真值，非战斗态占位）。</summary>
        public void Refresh(GameRun run, BattleSession session, GameplayView current, BattleWorldController world)
        {
            if (run == null)
            {
                return;
            }

            if (_viewStomachButton != null)
            {
                bool stomachView = current == GameplayView.StomachView;
                _viewStomachButton.interactable = stomachView
                    || (world != null && current != GameplayView.None && world.CanEnterStomachView);
                SetStomachLabel(stomachView ? StomachBackLabel : ViewStomachLabel);
            }

            if (_weekText != null)
            {
                _weekText.text = run.IsEndless
                    ? $"无尽 第 {run.WeekIndex - run.TotalWeeks} 关"
                    : $"第 {run.WeekIndex}/{run.TotalWeeks} 周";
            }

            if (_goldText != null)
            {
                _goldText.text = run.Gold.ToString();
            }

            if (_scoreReqText != null)
            {
                if (session != null)
                {
                    int score = session.IsSettled && session.LastResult != null
                        ? session.LastResult.Total
                        : session.PreviewScore().Total;
                    _scoreReqText.text = $"<size=28>分数要求</size>\n\n{score}\n/\n{session.RequiredScore}";
                }
                else
                {
                    _scoreReqText.text = "<size=28>分数要求</size>\n\n-\n/\n-";
                }
            }

            if (_foodAdjustText != null)
            {
                _foodAdjustText.text = "<size=28>食物调整</size>\n\n-";
            }
        }

        public void ResetStomachLabel()
        {
            SetStomachLabel(ViewStomachLabel);
        }

        private void SetStomachLabel(string text)
        {
            if (_viewStomachButtonText == null && _viewStomachButton != null)
            {
                _viewStomachButtonText = _viewStomachButton.GetComponentInChildren<Text>(true);
            }

            if (_viewStomachButtonText != null)
            {
                _viewStomachButtonText.text = text;
            }
        }
    }
}
