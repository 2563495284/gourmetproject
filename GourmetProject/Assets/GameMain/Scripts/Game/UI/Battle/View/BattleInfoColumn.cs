using System;
using System.Collections.Generic;
using DG.Tweening;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 常驻壳左栏信息组件：周/金币/分数要求，以及「查看食谱」「查看餐桌」「设置」按钮。
    /// 数据刷新与「查看餐桌」按钮文案/可点态集中在此，点击通过 <see cref="Bind"/> 回调壳。
    /// </summary>
    public sealed class BattleInfoColumn : MonoBehaviour
    {
        private const string ViewTableLabel = "查看餐桌：";
        private const float ScoreTitleDefaultY = 190f;
        private const float ScoreTitleBossY = 102f;
        private const float BossStatTransitionDuration = 0.24f;

        [SerializeField] private TMP_Text _weekText;
        [SerializeField] private TMP_Text _goldText;
        [SerializeField] private RectTransform _heartContainer;
        [SerializeField] private Image _heartItemPrefab;
        [SerializeField] private Sprite _heartActiveSprite;
        [SerializeField] private Sprite _heartEmptySprite;
        [SerializeField] private TMP_Text _scoreCurrentText;
        [SerializeField] private TMP_Text _scoreRequiredText;
        [SerializeField] private Button _viewRecipeButton;
        [SerializeField] private TMP_Text _viewRecipeCountText;
        [SerializeField] private Button _viewTableButton;
        [SerializeField] private TMP_Text _viewTableLabelText;
        [SerializeField] private TMP_Text _viewTableCountText;
        [SerializeField] private TMP_Text _discardCountText;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private SettlementScoreFireView _scoreFire;
        [SerializeField] private RectTransform _scoreTitlePanel;
        [SerializeField] private GameObject _bossStat;
        [SerializeField] private TMP_Text _bossTitleText;
        [SerializeField] private TMP_Text _bossSkillText;

        private readonly List<Image> _heartItems = new List<Image>();
        private int? _battleScoreOverride;
        private RectTransform _bossStatRect;
        private bool _bossStatPresented;
        private string _presentedBossDebuffId = string.Empty;
        private Sequence _bossStatTransition;
        private bool _inspectionNavigationBlocked;
        private bool _inspectionAvailabilityInitialized;
        private bool _recipeInspectionAvailable;
        private bool _tableInspectionAvailable;

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

        /// <summary>
        /// 临时领奖编辑期间保留常驻栏视觉，但禁止进入会替换中部/世界状态的查看页。
        /// 解锁时恢复阻塞前或最近一次 Refresh 计算出的按钮状态。
        /// </summary>
        public void SetInspectionNavigationBlocked(bool blocked)
        {
            if (!_inspectionAvailabilityInitialized)
            {
                _recipeInspectionAvailable = _viewRecipeButton != null
                    && _viewRecipeButton.interactable;
                _tableInspectionAvailable = _viewTableButton != null
                    && _viewTableButton.interactable;
                _inspectionAvailabilityInitialized = true;
            }

            _inspectionNavigationBlocked = blocked;
            ApplyInspectionAvailability();
        }

        /// <summary>结算动画逐步写入当前显示分；为空时按 session 的稳定状态刷新。</summary>
        public void SetBattleScoreOverride(int? score)
        {
            _battleScoreOverride = score;
        }

        /// <summary>刷新左栏常驻信息：周/金币（局外）与分数要求（局内为真值，非经营挑战态占位）。</summary>
        internal void Refresh(
            GameRun run,
            BattleSession session,
            GameplayView current,
            BattleInspectionView inspection,
            BattleWorldController world)
        {
            if (run == null)
            {
                return;
            }

            bool canOpenInspection = current != GameplayView.None
                && current != GameplayView.TableEdit
                && current != GameplayView.RecipeSelection;

            _recipeInspectionAvailable = CanOpenRecipeInspection(current)
                && inspection != BattleInspectionView.Recipe;
            _tableInspectionAvailable = canOpenInspection
                && inspection != BattleInspectionView.Table
                && world != null
                && world.CanEnterTableView;
            _inspectionAvailabilityInitialized = true;

            ApplyInspectionAvailability();

            if (_viewTableButton != null)
            {
                SetTableLabel(ViewTableLabel);

                if (_viewTableCountText != null)
                {
                    _viewTableCountText.gameObject.SetActive(true);
                    _viewTableCountText.text = ResolveTableCellCount(run, session, current, inspection, world).ToString();
                }
            }

            if (_weekText != null)
            {
                _weekText.text = run.IsEndless
                    ? $"无尽第{run.WeekIndex - run.TotalWeeks}关"
                    : $"{run.WeekIndex}/{run.TotalWeeks}周";
            }

            if (_goldText != null)
            {
                int displayedGold = inspection == BattleInspectionView.None
                    && current == GameplayView.Food
                    && session != null
                    && !session.IsSettled
                    ? Mathf.Max(0, run.Gold + (int)Math.Round(
                        session.PendingGold,
                        MidpointRounding.AwayFromZero))
                    : run.Gold;
                _goldText.text = displayedGold.ToString();
            }

            RefreshHearts(run.HeartsRemaining, run.HeartCapacity);

            if (_viewRecipeCountText != null)
            {
                _viewRecipeCountText.text = run.RecipeEntries.Count.ToString();
            }

            bool showScore = inspection == BattleInspectionView.None
                && current == GameplayView.Food
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
                // 营业 / 评鉴进行中显示本场剩余；局外与已结算页显示
                // 当前持有装饰决定的每场最大值，不再用“--”隐藏。
                int discardCount = session != null && !session.IsSettled
                    ? session.FoodDiscardsRemaining
                    : new ItemRuntime(run).FoodDiscardCapacity();
                _discardCountText.text = discardCount.ToString("D2");
            }
        }

        private void ApplyInspectionAvailability()
        {
            if (_viewRecipeButton != null)
            {
                _viewRecipeButton.interactable = !_inspectionNavigationBlocked
                    && _recipeInspectionAvailable;
            }

            if (_viewTableButton != null)
            {
                _viewTableButton.interactable = !_inspectionNavigationBlocked
                    && _tableInspectionAvailable;
            }
        }

        internal static bool CanOpenRecipeInspection(GameplayView current)
        {
            return current != GameplayView.None
                && current != GameplayView.TableEdit
                && current != GameplayView.RecipeSelection;
        }

        /// <summary>
        /// 显式切换 Boss 战展示生命周期。页面刷新不会改变该状态；只有经营挑战开始、奖励完成等
        /// 生命周期边界调用此方法，避免临时页面切换重复播放动画。
        /// </summary>
        public void SetBossBattlePresentation(cfg.BossDebuff bossDebuff, bool active, bool animate)
        {
            EnsureBossStatRect();
            bool shouldPresent = active && bossDebuff != null && _bossStat != null;
            if (!shouldPresent)
            {
                _presentedBossDebuffId = string.Empty;
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

            _presentedBossDebuffId = bossDebuff.Id ?? string.Empty;

            SetBossStatVisible(true, animate);
        }

        public void PlayBossDebuffTrigger(string debuffId)
        {
            if (!_bossStatPresented
                || _bossStatRect == null
                || string.IsNullOrEmpty(debuffId)
                || !string.Equals(_presentedBossDebuffId, debuffId, StringComparison.Ordinal))
            {
                return;
            }

            _bossStatRect.DOKill(complete: true);
            _bossStatRect.localScale = Vector3.one;
            _bossStatRect
                .DOPunchScale(Vector3.one * 0.13f, 0.3f, vibrato: 7, elasticity: 0.62f)
                .SetUpdate(true)
                .SetLink(gameObject);
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

        private static int ResolveTableCellCount(
            GameRun run,
            BattleSession session,
            GameplayView current,
            BattleInspectionView inspection,
            BattleWorldController world)
        {
            // 餐桌查看/编辑页以当前世界表现为准；它可能包含尚未提交的编辑预览。
            if ((current == GameplayView.TableEdit || inspection == BattleInspectionView.Table)
                && world != null
                && world.ActiveTable != null)
            {
                return world.ActiveTable.CellCapacity;
            }

            // 经营尚未结算时使用本场餐桌快照。结算领奖后 GameRun 已加入新餐桌格，
            // 但 BattleSession 仍是本场开始时的旧快照，此时必须回退到运行态餐桌。
            if (session != null && !session.IsSettled && session.DiningTable != null)
            {
                return session.DiningTable.CellCapacity;
            }

            return run.BuildTablePreviewFromFragments()?.CellCapacity ?? 0;
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
            _presentedBossDebuffId = string.Empty;
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
