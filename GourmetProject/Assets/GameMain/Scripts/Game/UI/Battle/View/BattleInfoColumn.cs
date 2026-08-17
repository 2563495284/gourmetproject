using System;
using BreakInfinity;
using System.Collections.Generic;
using DG.Tweening;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Common;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Scoring;
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
        private BigDouble? _battleScoreOverride;
        private RectTransform _bossStatRect;
        private bool _bossStatPresented;
        private string _presentedBossDebuffId = string.Empty;
        private Sequence _bossStatTransition;
        private bool _inspectionNavigationBlocked;
        private bool _inspectionAvailabilityInitialized;
        private bool _recipeInspectionAvailable;
        private bool _tableInspectionAvailable;
        private Sequence _weekChangeSequence;
        private int? _recipeCountPresentationOverride;
        private RectTransform _scoreSection;
        private RectTransform _scoreMeter;
        private TMP_Text _settlementDeltaText;
        private Sequence _settlementScoreBeatSequence;
        private bool _settlementScoreBeatPending;
        private BigDouble _pendingScoreBefore;
        private BigDouble _pendingScoreAfter;
        private BigDouble _pendingScoreDelta;
        private SettlementImpactTier _pendingImpactTier;
        private ScoreLineKind _pendingLineKind;
        private bool _pendingReachedTarget;
        private float _pendingSettlementSpeed = 1f;
        private Vector2 _settlementDeltaBasePosition;

        public SettlementScoreFireView ScoreFire => _scoreFire;
        public RectTransform ViewRecipeButtonRect =>
            _viewRecipeButton != null
                ? _viewRecipeButton.transform as RectTransform
                : null;
        public RectTransform ScoreRect => _scoreTitlePanel != null ? _scoreTitlePanel : transform as RectTransform;
        public RectTransform ScoreSectionRect
        {
            get
            {
                EnsureTutorialScoreRects();
                return _scoreSection;
            }
        }
        public RectTransform ScoreTitleRect => _scoreTitlePanel;
        public RectTransform ScoreMeterRect
        {
            get
            {
                EnsureTutorialScoreRects();
                return _scoreMeter;
            }
        }
        public RectTransform HeartsRect => _heartContainer;
        public RectTransform BossRuleRect
        {
            get
            {
                EnsureBossStatRect();
                return _bossStatRect != null ? _bossStatRect : transform as RectTransform;
            }
        }

        public void SetRecipeCountPresentationOverride(int? count)
        {
            _recipeCountPresentationOverride = count.HasValue
                ? Mathf.Max(0, count.Value)
                : null;
        }

        private void Awake()
        {
            EnsureTutorialScoreRects();
            EnsureBossStatRect();
            EnsureSettlementDeltaText();
            _scoreFire?.BindToScore(_scoreCurrentText != null ? _scoreCurrentText.rectTransform : null);
            ResetBossStatPresentation();
        }

        private void EnsureTutorialScoreRects()
        {
            _scoreSection = _scoreSection != null
                ? _scoreSection
                : transform.Find("ScoreSection") as RectTransform;
            _scoreMeter = _scoreMeter != null
                ? _scoreMeter
                : transform.Find("ScoreMeter") as RectTransform;
        }

        private void LateUpdate()
        {
            if (_settlementScoreBeatPending)
            {
                FlushSettlementScoreBeat();
            }
        }

        private void OnDisable()
        {
            _weekChangeSequence?.Kill();
            _weekChangeSequence = null;
            EndSettlementScorePresentation();
            ResetBossStatPresentation();
        }

        internal void PlayWeekIndexChange(
            GameRun run,
            int beforeWeekIndex,
            int afterWeekIndex,
            Action onComplete)
        {
            if (_weekText == null || run == null)
            {
                onComplete?.Invoke();
                return;
            }

            _weekChangeSequence?.Kill();
            RectTransform rect = _weekText.rectTransform;
            Vector3 originalScale = rect.localScale;
            Color originalColor = _weekText.color;
            _weekText.text = WeekText(run, beforeWeekIndex);
            _weekChangeSequence = DOTween.Sequence()
                .SetUpdate(true)
                .AppendInterval(0.10f)
                .AppendCallback(() => _weekText.text = WeekText(run, afterWeekIndex))
                .Append(rect.DOPunchScale(Vector3.one * 0.28f, 0.34f, 8, 0.62f))
                .Join(_weekText.DOColor(new Color(1f, 0.58f, 0.12f, 1f), 0.12f))
                .Append(_weekText.DOColor(originalColor, 0.16f))
                .OnComplete(() =>
                {
                    rect.localScale = originalScale;
                    _weekText.color = originalColor;
                    _weekText.text = WeekText(run, afterWeekIndex);
                    _weekChangeSequence = null;
                    onComplete?.Invoke();
                });
        }

        private static string WeekText(GameRun run, int weekIndex)
        {
            return weekIndex > run.TotalWeeks
                ? $"无尽第{weekIndex - run.TotalWeeks}关"
                : $"{weekIndex}/{run.TotalWeeks}周";
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
        /// 临时领奖编辑期间保留常驻栏视觉。菜谱可作为只读覆盖层打开；
        /// 餐桌查看仍会替换当前世界状态，因此保持禁用。
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
        public void SetBattleScoreOverride(BigDouble? score)
        {
            _battleScoreOverride = score;
        }

        public void BeginSettlementScorePresentation()
        {
            EnsureSettlementDeltaText();
            EndSettlementScorePresentation();
            if (_scoreCurrentText != null)
            {
                _scoreCurrentText.rectTransform.localScale = Vector3.one;
            }

            if (_scoreTitlePanel != null)
            {
                _scoreTitlePanel.localScale = Vector3.one;
            }
        }

        public void QueueSettlementScoreBeat(SettlementBeatSignal signal)
        {
            if (signal.Kind != SettlementBeatKind.ResultApplied || !signal.HasScoreChange)
            {
                return;
            }

            if (!_settlementScoreBeatPending)
            {
                _settlementScoreBeatPending = true;
                _pendingScoreBefore = signal.BeforeScore;
                _pendingScoreAfter = signal.AfterScore;
                _pendingScoreDelta = signal.ScoreDelta;
                _pendingImpactTier = signal.ImpactTier;
                _pendingLineKind = signal.LineKind;
                _pendingReachedTarget = signal.ReachedTarget;
                _pendingSettlementSpeed = Mathf.Max(0.0001f, signal.Speed);
                return;
            }

            _pendingScoreAfter = signal.AfterScore;
            _pendingScoreDelta += signal.ScoreDelta;
            _pendingReachedTarget |= signal.ReachedTarget;
            _pendingSettlementSpeed = Mathf.Max(0.0001f, signal.Speed);
            if (signal.ImpactTier > _pendingImpactTier)
            {
                _pendingImpactTier = signal.ImpactTier;
                _pendingLineKind = signal.LineKind;
            }
        }

        public void EndSettlementScorePresentation()
        {
            _settlementScoreBeatPending = false;
            _pendingScoreBefore = BigDouble.Zero;
            _pendingScoreAfter = BigDouble.Zero;
            _pendingScoreDelta = BigDouble.Zero;
            _pendingImpactTier = SettlementImpactTier.Base;
            _pendingReachedTarget = false;
            _pendingSettlementSpeed = 1f;
            _settlementScoreBeatSequence?.Kill();
            _settlementScoreBeatSequence = null;

            if (_scoreCurrentText != null)
            {
                _scoreCurrentText.rectTransform.DOKill();
                _scoreCurrentText.rectTransform.localScale = Vector3.one;
            }

            if (_scoreTitlePanel != null)
            {
                _scoreTitlePanel.DOKill();
                _scoreTitlePanel.localScale = Vector3.one;
            }

            if (_settlementDeltaText != null)
            {
                _settlementDeltaText.DOKill();
                _settlementDeltaText.rectTransform.DOKill();
                _settlementDeltaText.gameObject.SetActive(false);
                _settlementDeltaText.rectTransform.anchoredPosition = _settlementDeltaBasePosition;
                _settlementDeltaText.rectTransform.localScale = Vector3.one;
            }
        }

        private void EnsureSettlementDeltaText()
        {
            if (_settlementDeltaText != null || _scoreCurrentText == null)
            {
                return;
            }

            _settlementDeltaText = Instantiate(
                _scoreCurrentText,
                _scoreCurrentText.transform.parent);
            _settlementDeltaText.name = "SettlementScoreDeltaText";
            _settlementDeltaText.text = string.Empty;
            _settlementDeltaText.raycastTarget = false;
            _settlementDeltaText.enableAutoSizing = true;
            _settlementDeltaText.fontSizeMin = 24f;
            _settlementDeltaText.fontSizeMax = 42f;
            _settlementDeltaText.alignment = TextAlignmentOptions.Center;
            RectTransform deltaRect = _settlementDeltaText.rectTransform;
            deltaRect.anchoredPosition = _scoreCurrentText.rectTransform.anchoredPosition
                + new Vector2(0f, 64f);
            deltaRect.sizeDelta = new Vector2(190f, 54f);
            _settlementDeltaBasePosition = deltaRect.anchoredPosition;
            _settlementDeltaText.gameObject.SetActive(false);
            deltaRect.SetAsLastSibling();
        }

        private void FlushSettlementScoreBeat()
        {
            _settlementScoreBeatPending = false;
            EnsureSettlementDeltaText();
            if (_scoreCurrentText == null || _settlementDeltaText == null)
            {
                return;
            }

            BigDouble before = _pendingScoreBefore;
            BigDouble after = _pendingScoreAfter;
            BigDouble delta = _pendingScoreDelta;
            SettlementImpactTier impact = _pendingImpactTier;
            ScoreLineKind lineKind = _pendingLineKind;
            bool reachedTarget = _pendingReachedTarget;
            float presentationSpeed = Mathf.Max(0.0001f, _pendingSettlementSpeed);
            _pendingScoreDelta = BigDouble.Zero;
            _pendingReachedTarget = false;
            _pendingSettlementSpeed = 1f;

            _settlementScoreBeatSequence?.Kill();
            RectTransform scoreRect = _scoreCurrentText.rectTransform;
            RectTransform deltaRect = _settlementDeltaText.rectTransform;
            scoreRect.DOKill();
            deltaRect.DOKill();
            _settlementDeltaText.DOKill();
            scoreRect.localScale = Vector3.one;
            deltaRect.localScale = Vector3.one * 0.82f;
            deltaRect.anchoredPosition = _settlementDeltaBasePosition;

            Color semantic = SettlementColorPalette.For(lineKind);
            Color deltaColor = SettlementColorPalette.TextFor(semantic);
            _settlementDeltaText.color = deltaColor;
            _settlementDeltaText.text = FormatSignedScore(delta);
            _settlementDeltaText.gameObject.SetActive(true);
            _scoreCurrentText.text = ScoreNumberFormatter.Format(before);

            float rollDuration = (impact switch
            {
                SettlementImpactTier.Base => 0.12f,
                SettlementImpactTier.Normal => 0.14f,
                SettlementImpactTier.Strong => 0.17f,
                _ => 0.20f,
            }) / presentationSpeed;
            float punch = impact switch
            {
                SettlementImpactTier.Base => 0.06f,
                SettlementImpactTier.Normal => 0.10f,
                SettlementImpactTier.Strong => 0.15f,
                _ => 0.20f,
            };

            Sequence sequence = DOTween.Sequence()
                .Append(DOVirtual.Float(0f, 1f, rollDuration, progress =>
                    {
                        BigDouble displayed = BigDouble.Round(
                            before + (after - before) * progress,
                            MidpointRounding.AwayFromZero);
                        _scoreCurrentText.text = ScoreNumberFormatter.Format(displayed);
                    })
                    .SetEase(Ease.OutCubic))
                .Join(scoreRect.DOPunchScale(
                    Vector3.one * punch,
                    rollDuration + 0.08f / presentationSpeed,
                    vibrato: 7,
                    elasticity: 0.68f))
                .Join(deltaRect.DOScale(
                    1.08f + punch * 0.5f,
                    0.09f / presentationSpeed).SetEase(Ease.OutBack))
                .Join(deltaRect.DOAnchorPosY(
                    _settlementDeltaBasePosition.y + 18f,
                    rollDuration + 0.10f / presentationSpeed).SetEase(Ease.OutCubic))
                .Append(_settlementDeltaText.DOFade(0f, 0.15f / presentationSpeed));

            if (reachedTarget)
            {
                sequence
                    .AppendCallback(() =>
                    {
                        _settlementDeltaText.gameObject.SetActive(true);
                        _settlementDeltaText.text = "达标!";
                        _settlementDeltaText.color = SettlementColorPalette.FinalScore;
                        deltaRect.anchoredPosition = _settlementDeltaBasePosition;
                        deltaRect.localScale = Vector3.one * 0.78f;
                    })
                    .AppendInterval(0.07f / presentationSpeed)
                    .Append(deltaRect.DOScale(
                        1.22f,
                        0.12f / presentationSpeed).SetEase(Ease.OutBack))
                    .Join(deltaRect.DOAnchorPosY(
                        _settlementDeltaBasePosition.y + 24f,
                        0.20f / presentationSpeed).SetEase(Ease.OutCubic))
                    .Join(_scoreTitlePanel != null
                        ? _scoreTitlePanel.DOPunchScale(
                            Vector3.one * 0.18f,
                            0.28f / presentationSpeed,
                            vibrato: 8,
                            elasticity: 0.72f)
                        : DOVirtual.DelayedCall(0.01f, () => { }))
                    .Append(_settlementDeltaText.DOFade(0f, 0.16f / presentationSpeed));
            }

            _settlementScoreBeatSequence = sequence.OnComplete(() =>
            {
                if (_scoreCurrentText != null)
                {
                    _scoreCurrentText.text = ScoreNumberFormatter.Format(after);
                    scoreRect.localScale = Vector3.one;
                }

                if (_scoreTitlePanel != null)
                {
                    _scoreTitlePanel.localScale = Vector3.one;
                }

                if (_settlementDeltaText != null)
                {
                    _settlementDeltaText.gameObject.SetActive(false);
                    deltaRect.anchoredPosition = _settlementDeltaBasePosition;
                    deltaRect.localScale = Vector3.one;
                }

                _settlementScoreBeatSequence = null;
            });
        }

        private static string FormatSignedScore(BigDouble value)
        {
            return $"{(value >= 0 ? "+" : string.Empty)}{ScoreNumberFormatter.Format(value)}";
        }

        /// <summary>刷新左栏常驻信息：周/金币（局外）与分数要求（局内为真值，非经营挑战态占位）。</summary>
        internal void Refresh(
            GameRun run,
            BattleSession session,
            GameplayView current,
            BattleInspectionView inspection,
            bool tableFragmentEditActive,
            BattleWorldController world,
            bool rewardNavigationAvailable)
        {
            if (run == null)
            {
                return;
            }

            bool canOpenInspection = rewardNavigationAvailable
                || (current != GameplayView.None
                    && current != GameplayView.RecipeSelection);

            _recipeInspectionAvailable = CanOpenRecipeInspection(current, rewardNavigationAvailable)
                && inspection != BattleInspectionView.Recipe;
            _tableInspectionAvailable = canOpenInspection
                && inspection != BattleInspectionView.Table
                && world != null
                && world.CanEnterTableInspectionView;
            _inspectionAvailabilityInitialized = true;

            ApplyInspectionAvailability();

            if (_viewTableButton != null)
            {
                SetTableLabel(ViewTableLabel);

                if (_viewTableCountText != null)
                {
                    _viewTableCountText.gameObject.SetActive(true);
                    _viewTableCountText.text = ResolveTableCellCount(
                        run,
                        session,
                        inspection,
                        tableFragmentEditActive,
                        world).ToString();
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
                _viewRecipeCountText.text =
                    (_recipeCountPresentationOverride ?? run.RecipeEntries.Count).ToString();
            }

            bool showScore = inspection == BattleInspectionView.None
                && current == GameplayView.Food
                && session != null
                && (_battleScoreOverride.HasValue || !session.IsSettled);
            if (_scoreCurrentText != null)
            {
                _scoreCurrentText.text = showScore
                    ? ScoreNumberFormatter.Format(_battleScoreOverride ?? BigDouble.Zero)
                    : "-";
            }

            if (_scoreRequiredText != null)
            {
                _scoreRequiredText.text = showScore
                    ? ScoreNumberFormatter.Format(session.RequiredScore)
                    : "-";
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
                _viewRecipeButton.interactable = _recipeInspectionAvailable;
            }

            if (_viewTableButton != null)
            {
                _viewTableButton.interactable = !_inspectionNavigationBlocked
                    && _tableInspectionAvailable;
            }
        }

        internal static bool CanOpenRecipeInspection(
            GameplayView current,
            bool rewardNavigationAvailable = false)
        {
            return rewardNavigationAvailable
                || (current != GameplayView.None
                    && current != GameplayView.RecipeSelection);
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
                SemanticDescriptionFormatter.Set(_bossSkillText, bossDebuff.Desc);
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
            BattleInspectionView inspection,
            bool tableFragmentEditActive,
            BattleWorldController world)
        {
            // 餐桌查看/编辑页以当前世界表现为准；它可能包含尚未提交的编辑预览。
            if ((tableFragmentEditActive || inspection == BattleInspectionView.Table)
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
