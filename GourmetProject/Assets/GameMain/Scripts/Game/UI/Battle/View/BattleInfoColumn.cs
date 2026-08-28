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
    internal readonly struct SettlementScoreBeatAggregate
    {
        public SettlementScoreBeatAggregate(
            BigDouble beforeScore,
            BigDouble afterScore,
            BigDouble delta,
            bool reachedTarget,
            float speed)
        {
            BeforeScore = beforeScore;
            AfterScore = afterScore;
            Delta = delta;
            ReachedTarget = reachedTarget;
            Speed = speed;
        }

        public BigDouble BeforeScore { get; }
        public BigDouble AfterScore { get; }
        public BigDouble Delta { get; }
        public bool ReachedTarget { get; }
        public float Speed { get; }
    }

    internal struct SettlementScoreBeatAccumulator
    {
        private BigDouble _beforeScore;
        private BigDouble _afterScore;
        private BigDouble _delta;
        private bool _reachedTarget;
        private float _speed;

        public bool HasPending { get; private set; }

        public void Add(SettlementBeatSignal signal)
        {
            if (!HasPending)
            {
                HasPending = true;
                _beforeScore = signal.BeforeScore;
                _afterScore = signal.AfterScore;
                _delta = signal.ScoreDelta;
                _reachedTarget = signal.ReachedTarget;
                _speed = Mathf.Max(0.0001f, signal.Speed);
                return;
            }

            _afterScore = signal.AfterScore;
            _delta += signal.ScoreDelta;
            _reachedTarget |= signal.ReachedTarget;
            _speed = Mathf.Max(0.0001f, signal.Speed);
        }

        public SettlementScoreBeatAggregate Consume()
        {
            var aggregate = new SettlementScoreBeatAggregate(
                _beforeScore,
                _afterScore,
                _delta,
                _reachedTarget,
                Mathf.Max(0.0001f, _speed));
            this = default;
            return aggregate;
        }

        public void Reset()
        {
            this = default;
        }
    }

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
        private const float GoldDeltaHorizontalGap = 6f;

        [SerializeField] private StarProgressView _starProgress;
        [SerializeField] private TMP_Text _goldText;
        [SerializeField] private TMP_Text _goldDeltaTemplate;
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
        private int? _recipeCountPresentationOverride;
        private RectTransform _scoreSection;
        private RectTransform _scoreMeter;
        private TMP_Text _settlementDeltaText;
        private Sequence _settlementScoreBeatSequence;
        private SettlementScoreBeatAccumulator _settlementScoreBeatAccumulator;
        private Vector2 _settlementDeltaBasePosition;
        private Vector2 _scoreCurrentBasePosition;
        private bool _scoreCurrentBasePositionCaptured;
        private Material _settlementDeltaMaterial;
        private Tween _goldRollTween;
        private Tween _goldPulseTween;
        private int _goldPresentationValue;
        private int _goldPresentationTarget;
        private bool _goldPresentationInitialized;
        private Vector3 _goldTextBaseScale = Vector3.one;

        public SettlementScoreFireView ScoreFire => _scoreFire;
        public RectTransform ViewRecipeButtonRect =>
            _viewRecipeButton != null
                ? _viewRecipeButton.transform as RectTransform
                : null;
        public RectTransform ViewTableButtonRect =>
            _viewTableButton != null
                ? _viewTableButton.transform as RectTransform
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
            _scoreFire?.BindToScore(
                _scoreCurrentText != null ? _scoreCurrentText.rectTransform : null,
                transform.parent as RectTransform);
            ResetBossStatPresentation();
            if (_goldText != null)
            {
                _goldTextBaseScale = _goldText.rectTransform.localScale;
            }

            _goldDeltaTemplate = _goldDeltaTemplate != null
                ? _goldDeltaTemplate
                : transform.Find("GoldDeltaTemplate")?.GetComponent<TMP_Text>();
            if (_goldDeltaTemplate != null)
            {
                if (_goldText != null)
                {
                    _goldDeltaTemplate.font = _goldText.font;
                    _goldDeltaTemplate.fontSharedMaterial = _goldText.fontSharedMaterial;
                    _goldDeltaTemplate.alignment = TextAlignmentOptions.Center;
                }

                _goldDeltaTemplate.gameObject.SetActive(false);
            }
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
            if (_settlementScoreBeatAccumulator.HasPending)
            {
                FlushSettlementScoreBeat();
            }
        }

        private void OnDisable()
        {
            CompleteGoldPresentation();
            CancelWeekIndexChange(complete: true);
            EndSettlementScorePresentation();
            ResetBossStatPresentation();
        }

        private void OnDestroy()
        {
            if (_settlementDeltaMaterial == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_settlementDeltaMaterial);
            }
            else
            {
                DestroyImmediate(_settlementDeltaMaterial);
            }

            _settlementDeltaMaterial = null;
        }

        internal static int ResolveDisplayedGold(int runGold, float pendingGold, bool includePending)
        {
            int pending = includePending
                ? (int)Math.Round(pendingGold, MidpointRounding.AwayFromZero)
                : 0;
            return Mathf.Max(0, runGold + pending);
        }

        internal static Vector2 ResolveGoldDeltaStart(
            Vector2 goldAnchoredPosition,
            float goldWidth,
            float deltaWidth)
        {
            float x = goldAnchoredPosition.x
                + Mathf.Max(0f, goldWidth) * 0.5f
                + Mathf.Max(0f, deltaWidth) * 0.5f
                + GoldDeltaHorizontalGap;
            return new Vector2(x, goldAnchoredPosition.y);
        }

        internal void PresentGoldChange(int before, int after)
        {
            if (_goldText == null || before == after)
            {
                return;
            }

            if (!_goldPresentationInitialized)
            {
                SetGoldImmediate(before);
            }

            _goldRollTween?.Kill(false);
            _goldPresentationTarget = after;
            int rollFrom = _goldPresentationValue;
            _goldRollTween = DOVirtual.Float(rollFrom, after, 0.45f, value =>
                {
                    _goldPresentationValue = (int)Math.Round(value, MidpointRounding.AwayFromZero);
                    _goldText.text = _goldPresentationValue.ToString();
                })
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    _goldPresentationValue = _goldPresentationTarget;
                    _goldText.text = _goldPresentationTarget.ToString();
                    _goldRollTween = null;
                });

            _goldPulseTween?.Kill(false);
            _goldText.rectTransform.localScale = _goldTextBaseScale;
            _goldPulseTween = _goldText.rectTransform
                .DOPunchScale(Vector3.one * 0.12f, 0.34f, 7, 0.65f)
                .SetUpdate(true)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    if (_goldText != null)
                    {
                        _goldText.rectTransform.localScale = _goldTextBaseScale;
                    }

                    _goldPulseTween = null;
                });

            SpawnGoldDelta(after - before);
        }

        internal void PresentGoldTarget(int after)
        {
            int before = _goldPresentationInitialized ? _goldPresentationTarget : after;
            PresentGoldChange(before, after);
        }

        internal int ResolveGoldPresentationTarget(int fallback)
            => _goldPresentationInitialized ? _goldPresentationTarget : fallback;

        private void SyncGold(int value)
        {
            if (!_goldPresentationInitialized)
            {
                SetGoldImmediate(value);
                return;
            }

            if (_goldRollTween != null && _goldRollTween.IsActive() && value == _goldPresentationTarget)
            {
                return;
            }

            if (value != _goldPresentationTarget)
            {
                SetGoldImmediate(value);
            }
        }

        private void SetGoldImmediate(int value)
        {
            _goldRollTween?.Kill(false);
            _goldRollTween = null;
            _goldPresentationInitialized = true;
            _goldPresentationValue = value;
            _goldPresentationTarget = value;
            if (_goldText != null)
            {
                _goldText.text = value.ToString();
            }
        }

        private void CompleteGoldPresentation()
        {
            _goldRollTween?.Kill(false);
            _goldRollTween = null;
            _goldPulseTween?.Kill(false);
            _goldPulseTween = null;
            if (_goldText != null)
            {
                _goldText.text = _goldPresentationTarget.ToString();
                _goldText.rectTransform.localScale = _goldTextBaseScale;
            }

            _goldPresentationValue = _goldPresentationTarget;
        }

        private void SpawnGoldDelta(int delta)
        {
            if (_goldDeltaTemplate == null || delta == 0)
            {
                return;
            }

            TMP_Text label = Instantiate(
                _goldDeltaTemplate,
                _goldDeltaTemplate.rectTransform.parent,
                worldPositionStays: false);
            label.name = "GoldDelta";
            label.text = delta > 0 ? $"+{delta}" : delta.ToString();
            label.color = delta > 0
                ? new Color32(87, 182, 95, 255)
                : new Color32(217, 87, 79, 255);
            label.gameObject.SetActive(true);

            RectTransform rect = label.rectTransform;
            RectTransform goldRect = _goldText.rectTransform;
            Vector2 start = goldRect.parent == rect.parent
                ? ResolveGoldDeltaStart(
                    goldRect.anchoredPosition,
                    goldRect.rect.width,
                    rect.rect.width)
                : _goldDeltaTemplate.rectTransform.anchoredPosition;
            rect.anchoredPosition = start;
            rect.localScale = Vector3.one * 0.78f;

            CanvasGroup group = label.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = label.gameObject.AddComponent<CanvasGroup>();
            }

            group.alpha = 1f;
            DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(label.gameObject)
                .Append(rect.DOScale(1.08f, 0.12f).SetEase(Ease.OutBack))
                .Join(rect.DOAnchorPosY(start.y + 40f, 0.78f).SetEase(Ease.OutCubic))
                .Insert(0.44f, group.DOFade(0f, 0.34f))
                .OnComplete(() =>
                {
                    if (label != null)
                    {
                        Destroy(label.gameObject);
                    }
                });
        }

        internal void CancelWeekIndexChange(bool complete)
        {
        }

        internal void PlayWeekIndexChange(
            GameRun run,
            int beforeWeekIndex,
            int afterWeekIndex,
            Action onComplete)
        {
            _starProgress?.Bind(run?.RatingStarsEarned ?? 0);
            onComplete?.Invoke();
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

        /// <summary>
        /// 结算演出热路径：同步记录并直接呈现当前分数，不刷新其余常驻 HUD。
        /// 分数补间会逐帧调用；餐桌格数、心数等稳定信息不应在这里重算。
        /// </summary>
        internal void SetSettlementScorePresentation(BigDouble score)
        {
            _battleScoreOverride = score;
            if (_scoreCurrentText != null)
            {
                _scoreCurrentText.text = ScoreNumberFormatter.Format(score);
            }
        }

        public void BeginSettlementScorePresentation()
        {
            EnsureSettlementDeltaText();
            EndSettlementScorePresentation();
            if (_scoreCurrentText != null)
            {
                _scoreCurrentBasePosition = _scoreCurrentText.rectTransform.anchoredPosition;
                _scoreCurrentBasePositionCaptured = true;
                _scoreCurrentText.rectTransform.localScale = Vector3.one;
            }

            if (_scoreTitlePanel != null)
            {
                _scoreTitlePanel.localScale = Vector3.one;
            }
        }

        public void QueueSettlementScoreBeat(SettlementBeatSignal signal)
        {
            if (!ShouldQueueSettlementScoreBeat(signal))
            {
                return;
            }

            _settlementScoreBeatAccumulator.Add(signal);
        }

        internal static bool ShouldQueueSettlementScoreBeat(SettlementBeatSignal signal)
        {
            return signal.Kind == SettlementBeatKind.ResultApplied
                && signal.HasScoreChange
                && HasVisibleSettlementScoreDelta(signal.ScoreDelta);
        }

        internal static bool HasVisibleSettlementScoreDelta(BigDouble delta) =>
            delta != BigDouble.Zero;

        public void EndSettlementScorePresentation()
        {
            _settlementScoreBeatAccumulator.Reset();
            _settlementScoreBeatSequence?.Kill();
            _settlementScoreBeatSequence = null;

            if (_scoreCurrentText != null)
            {
                _scoreCurrentText.rectTransform.DOKill();
                _scoreCurrentText.rectTransform.localScale = Vector3.one;
                if (_scoreCurrentBasePositionCaptured)
                {
                    _scoreCurrentText.rectTransform.anchoredPosition = _scoreCurrentBasePosition;
                }
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
            if (_settlementDeltaText != null)
            {
                EnsureSettlementDeltaAboveBossStat();
                return;
            }

            if (_scoreCurrentText == null)
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
            Material sourceMaterial = _settlementDeltaText.fontSharedMaterial;
            if (sourceMaterial != null)
            {
                _settlementDeltaMaterial = new Material(sourceMaterial)
                {
                    name = "Settlement Score Delta Runtime Material",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                _settlementDeltaMaterial.EnableKeyword(ShaderUtilities.Keyword_Outline);
                _settlementDeltaText.fontSharedMaterial = _settlementDeltaMaterial;
            }
            RectTransform deltaRect = _settlementDeltaText.rectTransform;
            deltaRect.anchoredPosition = _scoreCurrentText.rectTransform.anchoredPosition
                + new Vector2(0f, 64f);
            deltaRect.sizeDelta = new Vector2(190f, 54f);
            EnsureSettlementDeltaAboveBossStat();
            _settlementDeltaBasePosition = deltaRect.anchoredPosition;
            _settlementDeltaText.gameObject.SetActive(false);
        }

        private void EnsureSettlementDeltaAboveBossStat()
        {
            if (_settlementDeltaText == null)
            {
                return;
            }

            PlaceSettlementScoreDeltaAboveBossStat(
                _settlementDeltaText.rectTransform,
                _bossStat != null ? _bossStat.transform : null,
                transform);
        }

        internal static void PlaceSettlementScoreDeltaAboveBossStat(
            RectTransform deltaRect,
            Transform bossStat,
            Transform fallbackParent)
        {
            if (deltaRect == null)
            {
                return;
            }

            Transform overlayParent = bossStat != null && bossStat.parent != null
                ? bossStat.parent
                : fallbackParent;
            if (overlayParent == null)
            {
                return;
            }

            if (deltaRect.parent != overlayParent)
            {
                // 保持当前世界坐标，避免从 ScoreMeter 提升到 BossStat 同级时发生跳位。
                deltaRect.SetParent(overlayParent, worldPositionStays: true);
            }

            // uGUI 同一 Canvas 下后绘制的同级节点在上方，确保加减分永远盖住 BossStat。
            deltaRect.SetAsLastSibling();
        }

        private void FlushSettlementScoreBeat()
        {
            SettlementScoreBeatAggregate aggregate = _settlementScoreBeatAccumulator.Consume();
            EnsureSettlementDeltaText();
            if (_scoreCurrentText == null || _settlementDeltaText == null)
            {
                return;
            }

            BigDouble before = aggregate.BeforeScore;
            BigDouble after = aggregate.AfterScore;
            BigDouble delta = aggregate.Delta;
            bool reachedTarget = aggregate.ReachedTarget;
            float presentationSpeed = aggregate.Speed;

            // 同一帧内的多条变化可能正负抵消；最终净变化为 0 时也不显示 +0。
            if (!HasVisibleSettlementScoreDelta(delta))
            {
                _scoreCurrentText.text = ScoreNumberFormatter.Format(after);
                return;
            }

            SettlementScoreFeedbackProfile feedback =
                SettlementScoreFeedbackResolver.Resolve(delta);
            if (!feedback.Visible)
            {
                _scoreCurrentText.text = ScoreNumberFormatter.Format(after);
                return;
            }

            _settlementScoreBeatSequence?.Kill();
            RectTransform scoreRect = _scoreCurrentText.rectTransform;
            RectTransform deltaRect = _settlementDeltaText.rectTransform;
            scoreRect.DOKill();
            deltaRect.DOKill();
            _settlementDeltaText.DOKill();
            scoreRect.localScale = Vector3.one;
            if (!_scoreCurrentBasePositionCaptured)
            {
                _scoreCurrentBasePosition = scoreRect.anchoredPosition;
                _scoreCurrentBasePositionCaptured = true;
            }
            scoreRect.anchoredPosition = _scoreCurrentBasePosition;
            deltaRect.localScale = Vector3.one * feedback.ImpactScale;
            deltaRect.anchoredPosition = _settlementDeltaBasePosition;

            _settlementDeltaText.color = feedback.TextColor;
            ApplySettlementScoreDeltaOutline(
                _settlementDeltaText,
                feedback.OutlineColor,
                feedback.OutlineWidth);
            _settlementDeltaText.text = FormatSignedScore(delta);
            _settlementDeltaText.gameObject.SetActive(true);
            _scoreCurrentText.text = ScoreNumberFormatter.Format(before);
            PlaySettlementScoreFire(feedback);

            float rollDuration = feedback.RollDuration / presentationSpeed;
            float feedbackDuration = (feedback.RollDuration + 0.10f) / presentationSpeed;

            Sequence sequence = DOTween.Sequence()
                .Append(DOVirtual.Float(0f, 1f, rollDuration, progress =>
                    {
                        BigDouble displayed = BigDouble.Round(
                            before + (after - before) * progress,
                            MidpointRounding.AwayFromZero);
                        _scoreCurrentText.text = ScoreNumberFormatter.Format(displayed);
                    })
                    .SetEase(Ease.OutCubic))
                .Join(deltaRect.DOScale(
                    feedback.SettleScale,
                    Mathf.Min(
                        rollDuration,
                        feedback.SettleDuration / presentationSpeed))
                    .SetEase(Ease.OutCubic));

            if (feedback.IsPositive)
            {
                sequence.Join(scoreRect.DOPunchScale(
                    Vector3.one * feedback.ScorePunch,
                    feedbackDuration,
                    vibrato: 7,
                    elasticity: 0.68f));
            }
            else
            {
                sequence
                    .Join(scoreRect.DOPunchScale(
                        -Vector3.one * feedback.ScorePunch,
                        feedbackDuration,
                        vibrato: 7,
                        elasticity: 0.48f))
                    .Join(scoreRect.DOPunchAnchorPos(
                        new Vector2(feedback.ShakeStrength, 0f),
                        feedbackDuration,
                        vibrato: Mathf.RoundToInt(Mathf.Lerp(5f, 10f, feedback.Intensity)),
                        elasticity: 0.24f,
                        snapping: false));
            }

            if (_scoreTitlePanel != null && feedback.PanelPunch > 0f)
            {
                Vector3 panelPunch = Vector3.one * feedback.PanelPunch;
                sequence.Join(_scoreTitlePanel.DOPunchScale(
                    feedback.IsPositive ? panelPunch : -panelPunch,
                    feedbackDuration,
                    vibrato: 7,
                    elasticity: feedback.IsPositive ? 0.65f : 0.40f));
            }

            sequence.Append(_settlementDeltaText.DOFade(
                0f,
                feedback.FadeDuration / presentationSpeed));

            if (reachedTarget)
            {
                sequence
                    .AppendCallback(() =>
                    {
                        _settlementDeltaText.gameObject.SetActive(true);
                        _settlementDeltaText.text = "达标!";
                        _settlementDeltaText.color = SettlementColorPalette.FinalScore;
                        ApplySettlementScoreDeltaOutline(
                            _settlementDeltaText,
                            new Color32(140, 63, 0, 255),
                            0.22f);
                        deltaRect.anchoredPosition = _settlementDeltaBasePosition;
                        deltaRect.localScale = Vector3.one * 1.55f;
                    })
                    .Append(deltaRect.DOScale(
                        1f,
                        0.18f / presentationSpeed).SetEase(Ease.OutCubic))
                    .Join(_scoreTitlePanel != null
                        ? _scoreTitlePanel.DOPunchScale(
                            Vector3.one * 0.18f,
                            0.28f / presentationSpeed,
                            vibrato: 8,
                            elasticity: 0.72f)
                        : DOVirtual.DelayedCall(0.01f, () => { }))
                    .AppendInterval(0.08f / presentationSpeed)
                    .Append(_settlementDeltaText.DOFade(0f, 0.16f / presentationSpeed));
            }

            _settlementScoreBeatSequence = sequence.OnComplete(() =>
            {
                if (_scoreCurrentText != null)
                {
                    _scoreCurrentText.text = ScoreNumberFormatter.Format(after);
                    scoreRect.localScale = Vector3.one;
                    scoreRect.anchoredPosition = _scoreCurrentBasePosition;
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

        internal static void ApplySettlementScoreDeltaOutline(
            TMP_Text text,
            Color outlineColor,
            float outlineWidth)
        {
            if (text == null)
            {
                return;
            }

            float clampedWidth = Mathf.Clamp01(outlineWidth);
            text.outlineColor = outlineColor;
            text.outlineWidth = clampedWidth;

            Material material = text.fontSharedMaterial;
            if (material != null)
            {
                // TMP 的运行时材质不会仅因写入 OutlineWidth 自动开启 shader 变体。
                material.EnableKeyword(ShaderUtilities.Keyword_Outline);
                material.SetColor(ShaderUtilities.ID_OutlineColor, outlineColor);
                material.SetFloat(ShaderUtilities.ID_OutlineWidth, clampedWidth);
            }

            text.UpdateMeshPadding();
            text.SetMaterialDirty();
        }

        private void PlaySettlementScoreFire(SettlementScoreFeedbackProfile feedback)
        {
            if (_scoreFire == null)
            {
                return;
            }

            switch (SettlementScoreFeedbackResolver.ResolveFireFeedback(
                        feedback,
                        _scoreFire.Phase))
            {
                case SettlementScoreFireFeedback.TransientSpark:
                    _scoreFire.BurstTransient(feedback.FireStrength);
                    break;
                case SettlementScoreFireFeedback.IgnitedBurst:
                    _scoreFire.Burst(feedback.FireStrength);
                    break;
            }
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

            _starProgress?.Bind(run.RatingStarsEarned);

            bool includePendingGold = inspection == BattleInspectionView.None
                && current == GameplayView.Food
                && session != null
                && !session.IsSettled;
            SyncGold(ResolveDisplayedGold(
                run.Gold,
                session != null ? session.PendingGold : 0f,
                includePendingGold));

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
                _discardCountText.text = discardCount.ToString("D1");
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
