using System;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Common;
using GourmetProject.Gameplay.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 常驻壳左栏信息组件：周/金币/分数要求，以及「查看菜谱」「查看餐桌」「设置」按钮。
    /// 数据刷新与「查看餐桌」按钮文案/可点态集中在此，点击通过 <see cref="Bind"/> 回调壳。
    /// </summary>
    public sealed class BattleInfoColumn : MonoBehaviour
    {
        private const string ViewTableLabel = "查看餐桌";
        private const string StomachBackLabel = "返回";

        [SerializeField] private Text _weekText;
        [SerializeField] private Text _goldText;
        [SerializeField] private Text _heartText;
        [SerializeField] private Text _scoreReqText;
        [SerializeField] private Button _viewRecipeButton;
        [SerializeField] private Button _viewTableButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private SettlementScoreFireView _scoreFire;
        [SerializeField] private GameObject _bossStat;
        [SerializeField] private Text _bossTitleText;
        [SerializeField] private Text _bossSkillText;

        private Text _viewTableButtonText;
        private int? _battleScoreOverride;

        public SettlementScoreFireView ScoreFire => _scoreFire;
        public RectTransform ViewRecipeButtonRect =>
            ResolveViewRecipeButton() != null
                ? _viewRecipeButton.transform as RectTransform
                : null;

        private void Awake()
        {
            SetBossStatVisible(false);
        }

        /// <summary>接线按钮回调（由壳在 OnInit 调用一次）。</summary>
        public void Bind(Action onSettings, Action onViewTable, Action onViewRecipe)
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

            Button viewRecipeButton = ResolveViewRecipeButton();
            if (viewRecipeButton != null)
            {
                viewRecipeButton.onClick.RemoveAllListeners();
                viewRecipeButton.onClick.AddListener(() => onViewRecipe?.Invoke());
            }
        }

        /// <summary>结算动画逐步写入当前显示分；为空时按 session 的稳定状态刷新。</summary>
        public void SetBattleScoreOverride(int? score)
        {
            _battleScoreOverride = score;
        }

        /// <summary>刷新左栏常驻信息：周/金币（局外）与分数要求（局内为真值，非战斗态占位）。</summary>
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

            if (_heartText != null)
            {
                _heartText.text = HeartDisplayText.Build(run.HeartsRemaining, run.HeartCapacity);
            }

            if (_scoreReqText != null)
            {
                bool showScore = session != null && (_battleScoreOverride.HasValue || !session.IsSettled);
                if (showScore)
                {
                    int score = _battleScoreOverride ?? 0;
                    _scoreReqText.text = $"<size=28>美味值要求</size>\n\n{score}\n/\n{session.RequiredScore}";
                }
                else
                {
                    _scoreReqText.text = "<size=28>美味值要求</size>\n\n-\n/\n-";
                }
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

        private Button ResolveViewRecipeButton()
        {
            if (_viewRecipeButton == null)
            {
                Transform child = transform.Find("ViewRecipe");
                _viewRecipeButton = child != null
                    ? child.GetComponent<Button>()
                    : null;
            }

            return _viewRecipeButton;
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
