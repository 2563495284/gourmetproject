using System;
using System.Collections.Generic;
using DG.Tweening;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.Run;
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
        private const string ViewTableLabel = "查看餐桌：";
        private const string StomachBackLabel = "返回";
        private const float ScoreTitleDefaultY = 190f;
        private const float ScoreTitleBossY = 102f;
        private const float BossStatTransitionDuration = 0.24f;

        [SerializeField] private Text _weekText;
        [SerializeField] private Text _goldText;
        [SerializeField] private RectTransform _heartContainer;
        [SerializeField] private Image _heartItemPrefab;
        [SerializeField] private Sprite _heartActiveSprite;
        [SerializeField] private Sprite _heartEmptySprite;
        [SerializeField] private Text _scoreCurrentText;
        [SerializeField] private Text _scoreRequiredText;
        [SerializeField] private Button _viewRecipeButton;
        [SerializeField] private Text _viewRecipeCountText;
        [SerializeField] private Button _viewTableButton;
        [SerializeField] private Text _viewTableLabelText;
        [SerializeField] private Text _viewTableCountText;
        [SerializeField] private Text _discardCountText;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private SettlementScoreFireView _scoreFire;
        [SerializeField] private RectTransform _scoreTitlePanel;
        [SerializeField] private GameObject _bossStat;
        [SerializeField] private Text _bossTitleText;
        [SerializeField] private Text _bossSkillText;

        private readonly List<Image> _heartItems = new List<Image>();
        private int? _battleScoreOverride;
        private RectTransform _bossStatRect;
        private bool _bossStatPresented;
        private Sequence _bossStatTransition;

        public SettlementScoreFireView ScoreFire => _scoreFire;
        public RectTransform ViewRecipeButtonRect =>
            _viewRecipeButton != null
                ? _viewRecipeButton.transform as RectTransform
                : null;

        private void Awake()
        {
            EnsureBossStatRect();
            ResetBossStatPresentation();
        }

        private void OnDisable()
        {
            ResetBossStatPresentation();
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
                _viewTableButton.onClick.RemoveAllListeners();
                _viewTableButton.onClick.AddListener(() => onViewTable?.Invoke());
            }

            if (_viewRecipeButton != null)
            {
                _viewRecipeButton.onClick.RemoveAllListeners();
                _viewRecipeButton.onClick.AddListener(() => onViewRecipe?.Invoke());
            }
        }

        /// <summary>结算动画逐步写入当前显示分；为空时按 session 的稳定状态刷新。</summary>
        public void SetBattleScoreOverride(int? score)
        {
            _battleScoreOverride = score;
        }

        /// <summary>刷新左栏常驻信息：周/金币（局外）与分数要求（局内为真值，非战斗态占位）。</summary>
        public void Refresh(GameRun run, BattleSession session, GameplayView current, BattleWorldController world)
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

                if (_viewTableCountText != null)
                {
                    _viewTableCountText.gameObject.SetActive(!stomachView);
                    if (!stomachView)
                    {
                        _viewTableCountText.text = ResolveTableCellCount(run, session, world).ToString();
                    }
                }
            }

            if (_weekText != null)
            {
                _weekText.text = run.IsEndless
                    ? $"无尽第{run.WeekIndex - run.TotalWeeks}关"
                    : $"第{FormatChineseNumber(run.WeekIndex)}周";
            }

            if (_goldText != null)
            {
                _goldText.text = run.Gold.ToString();
            }

            RefreshHearts(run.HeartsRemaining, run.HeartCapacity);

            if (_viewRecipeCountText != null)
            {
                _viewRecipeCountText.text = run.RecipeEntries.Count.ToString();
            }

            bool showScore = current == GameplayView.Food
                && session != null
                && (_battleScoreOverride.HasValue || !session.IsSettled);
            if (_scoreCurrentText != null)
            {
                _scoreCurrentText.text = showScore ? (_battleScoreOverride ?? 0).ToString() : "-";
            }

            if (_scoreRequiredText != null)
            {
                _scoreRequiredText.text = showScore ? session.RequiredScore.ToString() : "-";
            }

            if (_discardCountText != null)
            {
                _discardCountText.text = current == GameplayView.Food && session != null
                    ? session.FoodDiscardsRemaining.ToString("D2")
                    : "--";
            }
        }

        /// <summary>
        /// 显式切换 Boss 战展示生命周期。页面刷新不会改变该状态；只有战斗开始、奖励完成等
        /// 生命周期边界调用此方法，避免临时页面切换重复播放动画。
        /// </summary>
        public void SetBossBattlePresentation(cfg.BossDebuff bossDebuff, bool active, bool animate)
        {
            EnsureBossStatRect();
            bool shouldPresent = active && bossDebuff != null && _bossStat != null;
            if (!shouldPresent)
            {
                SetBossStatVisible(false, animate);
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

            SetBossStatVisible(true, animate);
        }

        public void ResetTableLabel()
        {
            SetTableLabel(ViewTableLabel);
        }

        private void SetTableLabel(string text)
        {
            if (_viewTableLabelText != null)
            {
                _viewTableLabelText.text = text;
            }
        }

        private void RefreshHearts(int remaining, int capacity)
        {
            if (_heartContainer == null || _heartItemPrefab == null)
            {
                return;
            }

            capacity = Mathf.Max(1, capacity);
            remaining = Mathf.Clamp(remaining, 0, capacity);

            while (_heartItems.Count < capacity)
            {
                Image item = Instantiate(_heartItemPrefab, _heartContainer);
                item.name = $"HeartItem{_heartItems.Count + 1}";
                item.gameObject.SetActive(true);
                _heartItems.Add(item);
            }

            for (int i = 0; i < _heartItems.Count; i++)
            {
                Image item = _heartItems[i];
                bool visible = i < capacity;
                item.gameObject.SetActive(visible);
                if (visible)
                {
                    item.sprite = i < remaining ? _heartActiveSprite : _heartEmptySprite;
                    item.preserveAspect = true;
                }
            }
        }

        private static int ResolveTableCellCount(GameRun run, BattleSession session, BattleWorldController world)
        {
            if (world != null && world.ActiveTable != null)
            {
                return world.ActiveTable.CellCapacity;
            }

            if (session?.DiningTable != null)
            {
                return session.DiningTable.CellCapacity;
            }

            return run.BuildTablePreviewFromFragments()?.CellCapacity ?? 0;
        }

        private static string FormatChineseNumber(int value)
        {
            if (value <= 0 || value >= 100)
            {
                return value.ToString();
            }

            string[] digits = { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九" };
            if (value < 10)
            {
                return digits[value];
            }

            int tens = value / 10;
            int ones = value % 10;
            string prefix = tens == 1 ? string.Empty : digits[tens];
            return ones == 0
                ? $"{prefix}十"
                : $"{prefix}十{digits[ones]}";
        }

        private void SetBossStatVisible(bool visible, bool animate)
        {
            if (_bossStat == null)
            {
                return;
            }

            if (_bossStatPresented == visible)
            {
                if (!animate)
                {
                    if (visible)
                    {
                        KillBossStatTransition();
                        _bossStat.SetActive(true);
                        SetScoreTitleY(ScoreTitleBossY);
                        SetBossStatScale(Vector3.one);
                    }
                    else
                    {
                        ResetBossStatPresentation();
                    }
                }

                return;
            }

            _bossStatPresented = visible;
            KillBossStatTransition();

            if (visible)
            {
                _bossStat.SetActive(true);

                if (!animate)
                {
                    SetScoreTitleY(ScoreTitleBossY);
                    SetBossStatScale(Vector3.one);
                    return;
                }

                SetScoreTitleY(ScoreTitleDefaultY);
                SetBossStatScale(Vector3.zero);

                _bossStatTransition = DOTween.Sequence()
                    .SetUpdate(true)
                    .SetLink(gameObject);
                if (_scoreTitlePanel != null)
                {
                    _bossStatTransition.Join(
                        _scoreTitlePanel.DOAnchorPosY(ScoreTitleBossY, BossStatTransitionDuration)
                            .SetEase(Ease.OutCubic));
                }

                if (_bossStatRect != null)
                {
                    _bossStatTransition.Join(
                        _bossStatRect.DOScale(1f, BossStatTransitionDuration)
                            .SetEase(Ease.OutBack));
                }

                return;
            }

            if (!animate)
            {
                ResetBossStatPresentation();
                return;
            }

            _bossStatTransition = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(gameObject);
            if (_scoreTitlePanel != null)
            {
                _bossStatTransition.Join(
                    _scoreTitlePanel.DOAnchorPosY(ScoreTitleDefaultY, BossStatTransitionDuration)
                        .SetEase(Ease.InCubic));
            }

            if (_bossStatRect != null)
            {
                _bossStatTransition.Join(
                    _bossStatRect.DOScale(0f, BossStatTransitionDuration)
                        .SetEase(Ease.InCubic));
            }

            _bossStatTransition.OnComplete(() =>
            {
                if (!_bossStatPresented)
                {
                    _bossStat.SetActive(false);
                }
            });
        }

        private void ResetBossStatPresentation()
        {
            EnsureBossStatRect();
            KillBossStatTransition();
            _bossStatPresented = false;
            SetScoreTitleY(ScoreTitleDefaultY);
            SetBossStatScale(Vector3.zero);
            if (_bossStat != null)
            {
                _bossStat.SetActive(false);
            }
        }

        private void KillBossStatTransition()
        {
            _bossStatTransition?.Kill();
            _bossStatTransition = null;
            _scoreTitlePanel?.DOKill();
            _bossStatRect?.DOKill();
        }

        private void EnsureBossStatRect()
        {
            if (_bossStatRect == null && _bossStat != null)
            {
                _bossStatRect = _bossStat.transform as RectTransform;
            }
        }

        private void SetScoreTitleY(float y)
        {
            if (_scoreTitlePanel == null)
            {
                return;
            }

            Vector2 position = _scoreTitlePanel.anchoredPosition;
            position.y = y;
            _scoreTitlePanel.anchoredPosition = position;
        }

        private void SetBossStatScale(Vector3 scale)
        {
            if (_bossStatRect != null)
            {
                _bossStatRect.localScale = scale;
            }
        }
    }
}
