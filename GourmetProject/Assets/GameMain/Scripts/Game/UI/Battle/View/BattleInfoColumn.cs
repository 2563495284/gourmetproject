using System;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 常驻壳左栏信息组件：周/金币/分数要求/食物调整文本，以及「查看餐桌」「设置」按钮。
    /// 数据刷新与「查看餐桌」按钮文案/可点态集中在此，点击通过 <see cref="Bind"/> 回调壳。
    /// </summary>
    public sealed class BattleInfoColumn : MonoBehaviour
    {
        private const string ViewTableLabel = "查看餐桌";
        private const string StomachBackLabel = "返回";

        private const string FoodAdjustBackLabel = "返回";

        [SerializeField] private Text _weekText;
        [SerializeField] private Text _goldText;
        [SerializeField] private Text _scoreReqText;
        [SerializeField] private Text _foodAdjustText;
        [SerializeField] private Button _foodAdjustButton;
        [SerializeField] private Button _viewTableButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private SettlementScoreFireView _scoreFire;
        [SerializeField] private GameObject _bossStat;
        [SerializeField] private Text _bossTitleText;
        [SerializeField] private Text _bossSkillText;

        private Text _viewTableButtonText;
        private bool _foodAdjustActive;
        private int? _battleScoreOverride;
        private Canvas _foodAdjustRaiseCanvas;

        public SettlementScoreFireView ScoreFire => _scoreFire;

        private void Awake()
        {
            SetBossStatVisible(false);
        }

        /// <summary>接线按钮回调（由壳在 OnInit 调用一次）。</summary>
        public void Bind(Action onSettings, Action onViewTable, Action onFoodAdjust)
        {
            if (_settingsButton != null)
            {
                _settingsButton.onClick.RemoveAllListeners();
                _settingsButton.onClick.AddListener(() => onSettings?.Invoke());
            }

            if (_viewTableButton != null)
            {
                _viewTableButtonText = _viewTableButton.GetComponentInChildren<Text>(true);
                _viewTableButton.onClick.RemoveAllListeners();
                _viewTableButton.onClick.AddListener(() => onViewTable?.Invoke());
            }

            if (_foodAdjustButton != null)
            {
                _foodAdjustButton.onClick.RemoveAllListeners();
                _foodAdjustButton.onClick.AddListener(() => onFoodAdjust?.Invoke());
            }
        }

        /// <summary>切换食物调整态：文案在「食物调整/次数」与「返回」间切换，激活时把按钮浮到遮罩之上。</summary>
        public void SetFoodAdjustActive(bool active, int count, bool free = false)
        {
            _foodAdjustActive = active;
            SetFoodAdjustRaised(active);
            if (_foodAdjustText != null)
            {
                _foodAdjustText.text = active
                    ? FoodAdjustBackLabel
                    : $"<size=28>食物调整</size>\n{FoodAdjustDisplayValue(count, free)}";
            }
        }

        /// <summary>结算动画逐步写入当前显示分；为空时按 session 的稳定状态刷新。</summary>
        public void SetBattleScoreOverride(int? score)
        {
            _battleScoreOverride = score;
        }

        private void SetFoodAdjustRaised(bool raised)
        {
            if (_foodAdjustButton == null)
            {
                return;
            }

            if (_foodAdjustRaiseCanvas == null)
            {
                _foodAdjustRaiseCanvas = _foodAdjustButton.GetComponent<Canvas>();
                if (_foodAdjustRaiseCanvas == null)
                {
                    _foodAdjustRaiseCanvas = _foodAdjustButton.gameObject.AddComponent<Canvas>();
                }

                if (_foodAdjustButton.GetComponent<GraphicRaycaster>() == null)
                {
                    _foodAdjustButton.gameObject.AddComponent<GraphicRaycaster>();
                }
            }

            _foodAdjustRaiseCanvas.overrideSorting = raised;
            if (raised)
            {
                _foodAdjustRaiseCanvas.sortingOrder = 600;
            }
        }

        /// <summary>刷新左栏常驻信息：周/金币（局外），分数要求/食物调整（局内为真值，非战斗态占位）。</summary>
        public void Refresh(GameRun run, BattleSession session, GameplayView current, BattleWorldController world, cfg.BossDebuff bossDebuff = null)
        {
            if (run == null)
            {
                return;
            }

            if (_viewTableButton != null)
            {
                bool stomachView = current == GameplayView.TableView;
                _viewTableButton.interactable = stomachView
                    || (world != null && current != GameplayView.None && world.CanEnterTableView);
                SetTableLabel(stomachView ? StomachBackLabel : ViewTableLabel);
            }

            if (_weekText != null)
            {
                _weekText.text = run.IsEndless
                    ? $"无尽 第 {run.WeekIndex - run.TotalWeeks} 关"
                    : $"第 {run.WeekIndex}/{run.TotalWeeks} 周";
            }

            if (_goldText != null)
            {
                _goldText.text =$"金币：{run.Gold}";
            }

            if (_scoreReqText != null)
            {
                bool showScore = session != null && (_battleScoreOverride.HasValue || !session.IsSettled);
                if (showScore)
                {
                    int score = _battleScoreOverride ?? 0;
                    _scoreReqText.text = $"<size=28>分数要求</size>\n\n{score}\n/\n{session.RequiredScore}";
                }
                else
                {
                    _scoreReqText.text = "<size=28>分数要求</size>\n\n-\n/\n-";
                }
            }

            bool foodView = current == GameplayView.Food && session != null && !session.IsSettled;

            if (_foodAdjustText != null)
            {
                _foodAdjustText.text = _foodAdjustActive
                    ? FoodAdjustBackLabel
                    : $"<size=28>食物调整</size>\n{FoodAdjustDisplayValue(run.FoodAdjustCount, run.FoodAdjustFreeAvailable)}";
            }

            if (_foodAdjustButton != null)
            {
                _foodAdjustButton.interactable = foodView || _foodAdjustActive;
            }

            RefreshBossStat(current, session, bossDebuff);
        }

        public void ResetTableLabel()
        {
            SetTableLabel(ViewTableLabel);
        }

        private void SetTableLabel(string text)
        {
            if (_viewTableButtonText == null && _viewTableButton != null)
            {
                _viewTableButtonText = _viewTableButton.GetComponentInChildren<Text>(true);
            }

            if (_viewTableButtonText != null)
            {
                _viewTableButtonText.text = text;
            }
        }

        private static string FoodAdjustDisplayValue(int count, bool free)
        {
            return free ? "免费" : count.ToString();
        }

        private void RefreshBossStat(GameplayView current, BattleSession session, cfg.BossDebuff bossDebuff)
        {
            bool visible = current == GameplayView.Food && session != null && bossDebuff != null;
            SetBossStatVisible(visible);
            if (!visible)
            {
                return;
            }

            if (_bossTitleText != null)
            {
                _bossTitleText.text = bossDebuff.Name ?? string.Empty;
            }

            if (_bossSkillText != null)
            {
                _bossSkillText.text = bossDebuff.Desc ?? string.Empty;
            }
        }

        private void SetBossStatVisible(bool visible)
        {
            if (_bossStat != null && _bossStat.activeSelf != visible)
            {
                _bossStat.SetActive(visible);
            }
        }
    }
}
