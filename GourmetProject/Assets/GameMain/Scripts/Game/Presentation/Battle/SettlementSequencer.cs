using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using BreakInfinity;
using DG.Tweening;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 背包乱斗式的结算演出：点「吃」后，按真实结算明细顺序播放来源 cue，
    /// 最后在分数汇总阶段滚到总分。消费结算结果与结算前基线，不改动任何计分逻辑。
    /// </summary>
    public sealed class SettlementSequencer : MonoBehaviour
    {
        private readonly Dictionary<int, DishValuePlaybackAccumulator> _retainedDishValues = new();
        private const float SourceCueRise = 0.12f;
        private const float SourceCueDuration = 0.62f;
        private const float SourceCueStackOffset = 0.12f;
        private const float DishValuePunchScale = 0.18f;
        private const float DishValuePunchDuration = 0.18f;
        private const float FinalCueInterval = 0.22f;
        private const float FinalScorePopupRise = 0.78f;
        private const float FinalScorePopupDuration = 1.1f;
        private const float FinalScorePopupHold = 0.28f;
        private const float BatchedCueHold = 0.24f;
        private const float SweetTransferParticleDuration = 0.34f;
        private const string InitialDishBaseBatchKey = "initial:dish-bases";

        [Header("结算常驻三阶段速度")]
        [SerializeField] private float _belowTargetSpeed = 1f;
        [SerializeField] private float _targetReachedSpeed = 1.4f;
        [SerializeField] private float _doubleTargetSpeed = 1.8f;

        [FormerlySerializedAs("_floatingTextPrefab")]
        [SerializeField] private FloatingTextView _settlementEffectLabelPrefab;
        [SerializeField] private SweetTransferParticleView _sweetTransferParticlePrefab;
        [SerializeField] private SettlementStageView _settlementStagePrefab;

        [Header("餐桌舞台节拍（统一速度下的秒数）")]
        [SerializeField] private float _baseDishDuration = 0.38f;
        [SerializeField] private float _sourceFocusDuration = 0.38f;
        [SerializeField] private float _scopeRevealDuration = 0.24f;
        [SerializeField] private float _resultBeatDuration = 0.52f;
        [SerializeField] private float _groupSettleDuration = 0.10f;
        [SerializeField] private float _finaleDuration = 0.95f;
        [SerializeField] private float _sweetTransferSourceDuration = 0.22f;
        [SerializeField] private float _sweetTransferTravelDuration = 0.38f;
        [SerializeField] private float _sweetTransferExecutorDuration = 0.30f;

        [Header("结算标签布局")]
        [Tooltip("结算效果标签相对常驻美味值的垂直偏移，负值表示显示在下方。")]
        [SerializeField] private float _dishFloatingVerticalOffset = -0.45f;

        private float _currentSettlementSpeed = 1f;
        private float _visualScale = 1f;
        private SettlementPacePhase _currentPacePhase;
        private bool _settlementDoubleSpeed;
        private SettlementStageView _stage;
        private SettlementCameraFeedback _cameraFeedback;
        private bool _externalPlaybackPaused;
        private bool _playbackHasSavedTimeScale;
        private float _playbackSavedTimeScale = 1f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [SerializeField, Tooltip("开发版结算调试：Space 暂停/继续时的当前状态。")]
        private bool _debugScorePaused;
        private bool _debugScoreControlsActive;
        private GUIStyle _debugScoreOverlayStyle;
#endif

        public bool IsPlaybackPaused => _externalPlaybackPaused
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            || _debugScorePaused
#endif
            ;

        /// <summary>暂停当前结算演出并保存进入暂停前的世界时间倍率。重复调用不会重复保存。</summary>
        public bool PausePlayback()
        {
            if (_externalPlaybackPaused)
            {
                return false;
            }

            _externalPlaybackPaused = true;
            ApplyPlaybackPauseState();
            return true;
        }

        /// <summary>恢复由 <see cref="PausePlayback"/> 发起的暂停；只在所有暂停来源都释放后恢复时间倍率。</summary>
        public bool ResumePlayback()
        {
            if (!_externalPlaybackPaused)
            {
                return false;
            }

            _externalPlaybackPaused = false;
            ApplyPlaybackPauseState();
            return true;
        }

        private enum SettlementCueKind
        {
            Source = 0,
            DishContribution = 1,
            FinalModifier = 2,
            SideEffect = 3,
            FinalScore = 4,
        }

        private enum TriggerSweetTransferCuePhase
        {
            None = 0,
            ActivatorStarted = 1,
            SourceStarted = 2,
            FinalSourceStarted = 3,
        }

        public void PlayFloatingText(
            Transform parent,
            Vector3 worldPos,
            string text,
            float? rise = null,
            float? duration = null,
            float visualScale = 1f)
        {
            FloatingTextView.Spawn(
                _settlementEffectLabelPrefab,
                parent != null ? parent : transform,
                worldPos,
                text,
                rise,
                duration,
                visualScale);
        }

        public void PlayFloatingEffect(
            Transform parent,
            Vector3 worldPos,
            string sourceName,
            string effectText,
            Color effectColor,
            float? rise = null,
            float? duration = null,
            float delay = 0f,
            float visualScale = 1f)
        {
            EnsureStage();
            _stage.PlayTransientEffect(
                parent != null ? parent : transform,
                worldPos,
                sourceName,
                effectText,
                effectColor,
                duration ?? 0.78f,
                delay,
                destroyCancellationToken,
                visualScale);
        }

        public async Awaitable PlayAsync(
            BattleSession session,
            ScoreResult result,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            DiningTableCoordinateMapper mapper,
            Transform fxRoot,
            Camera worldCamera,
            SettlementScoreFireView scoreFire,
            Action<BigDouble> renderScore,
            Action<SettlementRevealSignal> onReveal,
            Action<SettlementScopeSignal> onScope,
            Action<string> onPassiveTriggered,
            Action<SettlementBeatSignal> onBeat,
            SettlementBaselineSnapshot baselineSnapshot,
            float visualScale,
            CancellationToken cancellationToken)
        {
            if (result == null)
            {
                return;
            }

            if (!EnsureStage())
            {
                return;
            }

            renderScore?.Invoke(0);
            _visualScale = Mathf.Max(0.0001f, visualScale);
            _stage.Configure(
                dishViews,
                mapper,
                fxRoot,
                _visualScale);
            SettlementPresentationPlan plan = SettlementPresentationPlan.Build(result);
            var playback = new SettlementPlaybackState(plan.ResultBeatCount, scoreFire);
            var ledger = new SettlementRunningLedger(result.DishScores, baselineSnapshot);
            ClearRetainedDishValueBadges();
            ClearSweetTransferBuffMarkers(dishViews);
            var sweetTransferPlayback = new SweetTransferPlaybackState();
            bool completed = false;
            ResetPlaybackPauseState();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using CancellationTokenSource debugScorePauseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _debugScorePaused = false;
            _debugScoreControlsActive = true;
            ApplyPlaybackPauseState();
            _ = MonitorDebugScorePauseAsync(debugScorePauseCts.Token);
#endif
            BeginSettlementPace();
            scoreFire?.Show();
            scoreFire?.SetPhase(_currentPacePhase, _currentSettlementSpeed);
            _cameraFeedback ??= new SettlementCameraFeedback();
            _cameraFeedback.Begin(worldCamera, mapper.Center);
            _cameraFeedback.SetPhase(
                _currentPacePhase,
                _currentSettlementSpeed,
                playTransition: false);
            try
            {
                var baseTasks = new List<Awaitable>(plan.BaseBeats.Count);
                for (int i = 0; i < plan.BaseBeats.Count; i++)
                {
                    await WaitWhilePlaybackPausedAsync(cancellationToken);
                    SettlementBaseBeat beat = plan.BaseBeats[i];
                    AdvanceSettlementCue(playback, SettlementCueKind.DishContribution);
                    BigDouble beforeTotal = ledger.CurrentTotal;
                    BigDouble contribution = ledger.ApplyBase(beat.DishInstanceId, beat.BaseValue);
                    dishViews.TryGetValue(beat.DishInstanceId, out DishPieceView view);
                    if (view != null)
                    {
                        view.SetDishValueBadge(contribution);
                        view.PunchDishValueBadge(DishValuePunchScale, ScaleSettlementDuration(DishValuePunchDuration));
                    }

                    renderScore?.Invoke(ledger.CurrentTotal);
                    SettlementPacePhase nextPhase = ResolvePacePhase(
                        ledger.CurrentTotal,
                        session?.RequiredScore ?? 0,
                        _currentPacePhase);
                    bool reachedTarget = _currentPacePhase < SettlementPacePhase.TargetReached
                        && nextPhase >= SettlementPacePhase.TargetReached;

                    EmitScoreBeat(
                        onBeat,
                        view?.Instance?.Def?.Name ?? beat.DishId,
                        beat.DishInstanceId,
                        playback,
                        ScoreLineKind.DishBase,
                        beforeTotal,
                        ledger.CurrentTotal,
                        SettlementImpactTier.Base,
                        1,
                        reachedTarget);
                    baseTasks.Add(_stage.PlayBaseAsync(
                        view,
                        view?.Instance?.Def?.Name ?? beat.DishId,
                        contribution,
                        ScaleSettlementDuration(_baseDishDuration),
                        cancellationToken));
                    PromoteSettlementPace(playback, nextPhase);
                }

                // 账本按正式顺序更新，但所有基础贡献动画均已在同一帧启动。
                for (int i = 0; i < baseTasks.Count; i++)
                {
                    await baseTasks[i];
                }

                for (int groupIndex = 0; groupIndex < plan.Groups.Count; )
                {
                    await WaitWhilePlaybackPausedAsync(cancellationToken);
                    int waveLength = CountSweetTransferWaveLength(plan.Groups, groupIndex);
                    if (waveLength > 0)
                    {
                        await PlaySweetTransferWaveAsync(
                            session,
                            plan,
                            playback,
                            ledger,
                            dishViews,
                            mapper,
                            fxRoot,
                            scoreFire,
                            renderScore,
                            onReveal,
                            onScope,
                            onPassiveTriggered,
                            onBeat,
                            baselineSnapshot,
                            sweetTransferPlayback,
                            groupIndex,
                            waveLength,
                            cancellationToken);
                        groupIndex += waveLength;
                        continue;
                    }

                    SettlementEffectGroup group = plan.Groups[groupIndex];
                    EmitBeat(
                        onBeat,
                        SettlementBeatKind.SourceStarted,
                        group.SourceName,
                        group.ActorDishInstanceId,
                        playback);
                    DishPieceView cameraActor = TryGetDishView(group.ActorDishInstanceId, dishViews);
                    _cameraFeedback.FocusSource(
                        cameraActor != null ? cameraActor.WorldBounds.center : mapper.Center,
                        ScaleSettlementDuration(_sourceFocusDuration));
                    await _stage.FocusSourceAsync(
                        group,
                        ScaleSettlementDuration(_sourceFocusDuration),
                        cancellationToken);

                    SettlementScopeSignal scope = ScopeFor(group);
                    EmitScope(onScope, scope);
                    EmitBeat(
                        onBeat,
                        SettlementBeatKind.ScopeShown,
                        group.SourceName,
                        group.ActorDishInstanceId,
                        playback);
                    await _stage.ShowScopeAsync(
                        group,
                        ScaleSettlementDuration(_scopeRevealDuration),
                        cancellationToken);

                    int groupTargetCount = CountDistinctTargets(group, dishViews);
                    SettlementImpactTier groupImpact = StrongestImpact(group, groupTargetCount);
                    _cameraFeedback.PlayImpact(groupImpact, groupTargetCount);
                    if (groupImpact >= SettlementImpactTier.Strong)
                    {
                        scoreFire?.Burst(groupImpact >= SettlementImpactTier.Chain ? 0.72f : 0.46f);
                    }

                    // 普通技能组保持一次性并发；蛋糕层数会按加分、加倍率、乘倍率的真实顺序
                    // 分成连续批次。每一批里的多个蛋糕仍在同一帧启动，避免同一蛋糕的三种
                    // 反馈互相取消，也不会把多个蛋糕退化成逐个播放。
                    List<List<int>> resultLineBatches = BuildResultLineBatches(group);
                    for (int batchIndex = 0; batchIndex < resultLineBatches.Count; batchIndex++)
                    {
                        await PlayResultLineBatchAsync(
                            BuildResultLineRefs(group, resultLineBatches[batchIndex]),
                            groupTargetCount,
                            session,
                            playback,
                            ledger,
                            dishViews,
                            fxRoot,
                            renderScore,
                            onReveal,
                            onPassiveTriggered,
                            onBeat,
                            sweetTransferPlayback,
                            skipTransferTravel: false,
                            cancellationToken);
                    }

                    if (group.Lines.Count > 0)
                    {
                        int lastLineIndex = group.Lines.Count - 1;
                        CompleteSweetTransferStateIfNeeded(
                            sweetTransferPlayback,
                            BuildNextSweetTransferSteps(plan, groupIndex, lastLineIndex),
                            BuildNextSweetTransferPresentationContext(plan, groupIndex, lastLineIndex),
                            dishViews);
                    }

                    onScope?.Invoke(default);
                    _cameraFeedback.ReturnHome(ScaleSettlementDuration(_groupSettleDuration + 0.08f));
                    await _stage.EndGroupAsync(
                        ScaleSettlementDuration(_groupSettleDuration),
                        cancellationToken);
                    EmitBeat(
                        onBeat,
                        SettlementBeatKind.GroupCompleted,
                        group.SourceName,
                        group.ActorDishInstanceId,
                        playback);
                    groupIndex++;
                }

                await WaitWhilePlaybackPausedAsync(cancellationToken);
                ClearSweetTransferVisuals(sweetTransferPlayback, dishViews);
                onScope?.Invoke(default);
                AdvanceSettlementCue(playback, SettlementCueKind.FinalScore);
                renderScore?.Invoke(result.Total);
                PromoteSettlementPace(
                    playback,
                    ResolvePacePhase(
                        result.Total,
                        session?.RequiredScore ?? 0,
                        _currentPacePhase));
                if (_currentPacePhase == SettlementPacePhase.TargetReached)
                {
                    scoreFire?.Burst(0.72f);
                }
                else if (_currentPacePhase == SettlementPacePhase.DoubleTarget)
                {
                    scoreFire?.Burst(1f);
                }
                _cameraFeedback.PlayFinale(ScaleSettlementDuration(_finaleDuration));
                await _stage.PlayFinaleAsync(
                    result.Total,
                    ScaleSettlementDuration(_finaleDuration),
                    cancellationToken);
                EmitBeat(
                    onBeat,
                    SettlementBeatKind.FinaleConfirmed,
                    "本桌结算",
                    0,
                    playback);
                ApplyFinalDishValues(result.DishScores, dishViews);
                completed = true;
            }
            finally
            {
                ClearSweetTransferVisuals(sweetTransferPlayback, dishViews);
                ClearSweetTransferBuffMarkers(dishViews);
                onScope?.Invoke(default);
                _stage?.ClearImmediate();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                debugScorePauseCts.Cancel();
                ClearDebugScorePauseState();
#endif
                ResetPlaybackPauseState();
                RestoreSettlementPace();
                _cameraFeedback?.RestoreImmediate();
                scoreFire?.Hide();
                if (!completed)
                {
                    ClearRetainedDishValueBadges();
                }
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnGUI()
        {
            if (!_debugScoreControlsActive)
            {
                return;
            }

            Color previousColor = GUI.color;
            GUI.color = _debugScorePaused
                ? new Color(1f, 0.45f, 0.2f, 0.95f)
                : new Color(0.65f, 1f, 0.65f, 0.85f);

            string text = _debugScorePaused
                ? "结算演出：暂停中（Space 继续）"
                : "结算演出：运行中（Space 暂停）";
            GUI.Label(new Rect(16f, 16f, 360f, 30f), text, GetDebugScoreOverlayStyle());
            GUI.color = previousColor;
        }
#endif

        private void OnDisable()
        {
            ForceRestorePlaybackTimeScale();
            RestoreSettlementPace();
        }

        private void OnDestroy()
        {
            ForceRestorePlaybackTimeScale();
            RestoreSettlementPace();
            ClearRetainedDishValueBadges();
        }

        private bool EnsureStage()
        {
            if (_stage != null)
            {
                return true;
            }

            if (_settlementStagePrefab == null)
            {
                Debug.LogError($"{nameof(SettlementSequencer)} 缺少结算舞台 prefab。", this);
                return false;
            }

            _stage = Instantiate(_settlementStagePrefab, transform, false);
            _stage.name = "SettlementStage";
            return true;
        }

        private static SettlementScopeSignal ScopeFor(SettlementEffectGroup group)
        {
            if (group == null)
            {
                return default;
            }

            SkillExecutionTrace trace = group.Trace;
            if (trace != null)
            {
                return new SettlementScopeSignal(
                    trace.OwnerDishInstanceId,
                    trace.RuntimeSelfDishInstanceId,
                    trace);
            }

            return new SettlementScopeSignal(
                group.ActorDishInstanceId,
                group.ActorDishInstanceId,
                null);
        }

        private static IReadOnlyList<SettlementPlaybackStep> BuildSweetTransferSteps(
            ScoreLine line,
            SettlementCue cue)
        {
            if (line == null || cue == null)
            {
                return Array.Empty<SettlementPlaybackStep>();
            }

            return new[]
            {
                new SettlementPlaybackStep(
                    line.DishInstanceId,
                    cue,
                    SettlementScopeSignal.FromScoreLine(line)),
            };
        }

        private static IReadOnlyList<SettlementPlaybackStep> BuildNextSweetTransferSteps(
            SettlementPresentationPlan plan,
            int groupIndex,
            int lineIndex)
        {
            if (plan == null || groupIndex < 0 || groupIndex >= plan.Groups.Count)
            {
                return Array.Empty<SettlementPlaybackStep>();
            }

            ScoreLine nextLine = NextScoreLine(plan, groupIndex, lineIndex);

            return nextLine != null && TryBuildCue(nextLine, out SettlementCue nextCue)
                ? BuildSweetTransferSteps(nextLine, nextCue)
                : Array.Empty<SettlementPlaybackStep>();
        }

        private static SettlementSweetTransferPresentationContext BuildNextSweetTransferPresentationContext(
            SettlementPresentationPlan plan,
            int groupIndex,
            int lineIndex)
        {
            return SettlementSweetTransferPresentationContext.FromLine(
                NextScoreLine(plan, groupIndex, lineIndex));
        }

        private static ScoreLine NextScoreLine(
            SettlementPresentationPlan plan,
            int groupIndex,
            int lineIndex)
        {
            if (plan == null || groupIndex < 0 || groupIndex >= plan.Groups.Count)
            {
                return null;
            }

            SettlementEffectGroup group = plan.Groups[groupIndex];
            if (lineIndex + 1 < group.Lines.Count)
            {
                return group.Lines[lineIndex + 1];
            }

            return groupIndex + 1 < plan.Groups.Count
                && plan.Groups[groupIndex + 1].Lines.Count > 0
                    ? plan.Groups[groupIndex + 1].Lines[0]
                    : null;
        }

        internal static bool IsSweetTransferRelatedGroup(SettlementEffectGroup group)
        {
            if (group == null)
            {
                return false;
            }

            if (group.Trace?.Kind == SkillExecutionKind.SweetTransfer)
            {
                return true;
            }

            for (int i = 0; i < group.Lines.Count; i++)
            {
                switch (group.Lines[i].Kind)
                {
                    case ScoreLineKind.TriggerSweetTransfer:
                    case ScoreLineKind.TriggeredSweetTransferSource:
                    case ScoreLineKind.SweetTransferBuffTriggered:
                    case ScoreLineKind.SweetTransferFailed:
                        return true;
                }
            }

            return false;
        }

        internal static int CountSweetTransferWaveLength(
            IReadOnlyList<SettlementEffectGroup> groups,
            int startIndex)
        {
            if (groups == null || startIndex < 0 || startIndex >= groups.Count)
            {
                return 0;
            }

            if (!IsSweetTransferRelatedGroup(groups[startIndex]))
            {
                return 0;
            }

            int count = 1;
            for (int i = startIndex + 1; i < groups.Count; i++)
            {
                if (!IsSweetTransferRelatedGroup(groups[i]))
                {
                    break;
                }

                count++;
            }

            return count;
        }

        internal static List<SettlementSweetTransferPresentationContext> CollectWaveHandoffs(
            IReadOnlyList<SettlementEffectGroup> groups,
            int startIndex,
            int length)
        {
            var result = new List<SettlementSweetTransferPresentationContext>();
            if (groups == null || length <= 0)
            {
                return result;
            }

            var seen = new HashSet<SettlementSweetTransferPresentationKey>();
            int end = Mathf.Min(groups.Count, startIndex + length);
            for (int groupIndex = Mathf.Max(0, startIndex); groupIndex < end; groupIndex++)
            {
                SettlementEffectGroup group = groups[groupIndex];
                if (group == null)
                {
                    continue;
                }

                for (int lineIndex = 0; lineIndex < group.Lines.Count; lineIndex++)
                {
                    SettlementSweetTransferPresentationContext context =
                        SettlementSweetTransferPresentationContext.FromLine(group.Lines[lineIndex]);
                    if (!context.IsValid || !seen.Add(context.Key))
                    {
                        continue;
                    }

                    result.Add(context);
                }
            }

            return result;
        }

        internal static List<SkillExecutionTrace> CollectWaveScopeTraces(
            IReadOnlyList<SettlementEffectGroup> groups,
            int startIndex,
            int length)
        {
            var result = new List<SkillExecutionTrace>();
            if (groups == null || length <= 0)
            {
                return result;
            }

            var seen = new HashSet<string>();
            int end = Mathf.Min(groups.Count, startIndex + length);
            for (int groupIndex = Mathf.Max(0, startIndex); groupIndex < end; groupIndex++)
            {
                SettlementEffectGroup group = groups[groupIndex];
                if (group == null)
                {
                    continue;
                }

                for (int lineIndex = 0; lineIndex < group.Lines.Count; lineIndex++)
                {
                    SkillExecutionTrace trace = group.Lines[lineIndex]?.Trace;
                    if (trace == null || !seen.Add(ScopeTraceKey(trace)))
                    {
                        continue;
                    }

                    result.Add(trace);
                }
            }

            return result;
        }

        internal static SettlementScopeSignal ScopeForWave(
            IReadOnlyList<SettlementEffectGroup> groups,
            int startIndex,
            int length)
        {
            IReadOnlyList<SkillExecutionTrace> traces = CollectWaveScopeTraces(groups, startIndex, length);
            if (traces.Count == 0)
            {
                return groups != null
                    && startIndex >= 0
                    && startIndex < groups.Count
                        ? ScopeFor(groups[startIndex])
                        : default;
            }

            SkillExecutionTrace first = traces[0];
            return SettlementScopeSignal.FromTraces(
                first.OwnerDishInstanceId,
                first.RuntimeSelfDishInstanceId,
                traces);
        }

        internal static SettlementScopeSignal ScopeForScoreLines(IReadOnlyList<ScoreLine> lines)
        {
            var traces = new List<SkillExecutionTrace>();
            if (lines == null)
            {
                return default;
            }

            var seen = new HashSet<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                SkillExecutionTrace trace = lines[i]?.Trace;
                if (trace == null || !seen.Add(ScopeTraceKey(trace)))
                {
                    continue;
                }

                traces.Add(trace);
            }

            if (traces.Count == 0)
            {
                return default;
            }

            SkillExecutionTrace first = traces[0];
            return SettlementScopeSignal.FromTraces(
                first.OwnerDishInstanceId,
                first.RuntimeSelfDishInstanceId,
                traces);
        }

        private static string ScopeTraceKey(SkillExecutionTrace trace)
        {
            IReadOnlyList<GridPos> cells = trace.ScopeRegionCells;
            if (cells == null || cells.Count == 0)
            {
                return string.Join(
                    "|",
                    trace.RuntimeSelfDishInstanceId,
                    trace.SkillId,
                    trace.RuleId,
                    (int)trace.ActionScope);
            }

            var parts = new string[cells.Count + 2];
            parts[0] = trace.RuntimeSelfDishInstanceId.ToString();
            parts[1] = ((int)trace.ActionScope).ToString();
            for (int i = 0; i < cells.Count; i++)
            {
                GridPos cell = cells[i];
                parts[i + 2] = $"{cell.X},{cell.Y}";
            }

            return string.Join("|", parts);
        }

        private static List<ResultLineRef> BuildResultLineRefs(
            SettlementEffectGroup group,
            IReadOnlyList<int> lineIndices)
        {
            var result = new List<ResultLineRef>();
            if (group == null || lineIndices == null)
            {
                return result;
            }

            for (int i = 0; i < lineIndices.Count; i++)
            {
                result.Add(new ResultLineRef(group, lineIndices[i]));
            }

            return result;
        }

        private static List<ResultLineRef> BuildWaveResultLineRefs(
            IReadOnlyList<SettlementEffectGroup> groups,
            int startIndex,
            int length)
        {
            var result = new List<ResultLineRef>();
            if (groups == null || length <= 0)
            {
                return result;
            }

            int end = Mathf.Min(groups.Count, startIndex + length);
            for (int groupIndex = Mathf.Max(0, startIndex); groupIndex < end; groupIndex++)
            {
                SettlementEffectGroup group = groups[groupIndex];
                if (group == null)
                {
                    continue;
                }

                for (int lineIndex = 0; lineIndex < group.Lines.Count; lineIndex++)
                {
                    result.Add(new ResultLineRef(group, lineIndex));
                }
            }

            return result;
        }

        internal static bool IsSweetTransferLaunchLine(ScoreLine line)
        {
            return line != null && line.Kind == ScoreLineKind.TriggerSweetTransfer;
        }

        /// <summary>
        /// 改变本次传递怎么飞的 Buff：棉花糖固定加目标、松露巧克力概率加目标。
        /// 目标数必须在粒子起飞前兑现，但表现上要等「触发甜蜜传递」播完后逐条展示。
        /// </summary>
        internal static bool IsExtraTargetBuffLine(ScoreLine line)
        {
            return line != null
                && line.Kind == ScoreLineKind.SweetTransferBuffTriggered
                && line.Trace != null
                && line.Trace.ActionType == SkillActionType.TriggerSweetTransfer;
        }

        internal static int ResultVisualDishInstanceId(ScoreLine line)
        {
            if (IsExtraTargetBuffLine(line)
                && line.Trace.RuntimeSelfDishInstanceId > 0)
            {
                // Header 仍取 Buff 所有者；提示位置跟随本次真正触发甜蜜传递的食物。
                return line.Trace.RuntimeSelfDishInstanceId;
            }

            return line?.DishInstanceId ?? 0;
        }

        internal static bool IsSweetTransferAnnounceLine(ScoreLine line)
        {
            return IsSweetTransferLaunchLine(line) || IsExtraTargetBuffLine(line);
        }

        internal static int CountSweetTransferAnnounceLines(
            IReadOnlyList<SettlementEffectGroup> groups,
            int startIndex,
            int length)
        {
            List<ResultLineRef> lines = BuildWaveResultLineRefs(groups, startIndex, length);
            int count = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                if (IsSweetTransferAnnounceLine(lines[i].Line))
                {
                    count++;
                }
            }

            return count;
        }

        internal static List<int> CollectWaveNativeLaunchSourceIds(
            IReadOnlyList<SettlementEffectGroup> groups,
            int startIndex,
            int length)
        {
            var result = new List<int>();
            if (groups == null || length <= 0)
            {
                return result;
            }

            List<ResultLineRef> lines = BuildWaveResultLineRefs(groups, startIndex, length);
            for (int i = 0; i < lines.Count; i++)
            {
                if (IsSweetTransferLaunchLine(lines[i].Line))
                {
                    return result;
                }
            }

            var seen = new HashSet<int>();
            List<SettlementSweetTransferPresentationContext> handoffs =
                CollectWaveHandoffs(groups, startIndex, length);
            for (int i = 0; i < handoffs.Count; i++)
            {
                int sourceId = handoffs[i].SourceDishInstanceId;
                if (sourceId > 0 && seen.Add(sourceId))
                {
                    result.Add(sourceId);
                }
            }

            return result;
        }

        /// <summary>
        /// 传递成功后的分数/倍率响应：跳跳糖在本列获得传递后加分，软糖在本列发动传递后加倍率。
        /// 被传食物的技能结果先播完，再播这一条汇总，不能同一拍弹出。
        /// </summary>
        internal static bool IsSweetTransferResponseLine(ScoreLine line)
        {
            return line != null
                && line.Kind == ScoreLineKind.SweetTransferBuffTriggered
                && !IsExtraTargetBuffLine(line);
        }

        /// <summary>
        /// 被传食物真正执行外来技能的明细。即使和跳跳糖写在同一执行组，也必须先作为传递结算播出。
        /// </summary>
        internal static bool IsSweetTransferExecutorResultLine(ScoreLine line)
        {
            return line?.Trace != null
                && line.Trace.Kind == SkillExecutionKind.SweetTransfer
                && line.Kind != ScoreLineKind.TriggeredSweetTransferSource;
        }

        internal static int CountSweetTransferResponseLines(
            IReadOnlyList<SettlementEffectGroup> groups,
            int startIndex,
            int length)
        {
            List<ResultLineRef> lines = BuildWaveResultLineRefs(groups, startIndex, length);
            int count = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                if (IsSweetTransferResponseLine(lines[i].Line))
                {
                    count++;
                }
            }

            return count;
        }

        private static void PartitionWaveResultLines(
            IReadOnlyList<ResultLineRef> lines,
            List<ResultLineRef> announceLines,
            List<ResultLineRef> settleLines,
            List<ResultLineRef> responseLines)
        {
            announceLines.Clear();
            settleLines.Clear();
            responseLines.Clear();
            if (lines == null)
            {
                return;
            }

            var responseGroups = new HashSet<SettlementEffectGroup>();
            for (int i = 0; i < lines.Count; i++)
            {
                ResultLineRef lineRef = lines[i];
                if (IsSweetTransferResponseLine(lineRef.Line))
                {
                    responseGroups.Add(lineRef.Group);
                }
            }

            for (int i = 0; i < lines.Count; i++)
            {
                ResultLineRef lineRef = lines[i];
                if (IsSweetTransferAnnounceLine(lineRef.Line))
                {
                    announceLines.Add(lineRef);
                }
                else if (IsSweetTransferExecutorResultLine(lineRef.Line))
                {
                    settleLines.Add(lineRef);
                }
                else if (IsSweetTransferResponseLine(lineRef.Line)
                    || responseGroups.Contains(lineRef.Group))
                {
                    responseLines.Add(lineRef);
                }
                else
                {
                    settleLines.Add(lineRef);
                }
            }
        }

        internal static void ClassifySweetTransferWaveLines(
            IReadOnlyList<SettlementEffectGroup> groups,
            int startIndex,
            int length,
            List<ScoreLine> announceLines,
            List<ScoreLine> settleLines,
            List<ScoreLine> responseLines)
        {
            announceLines?.Clear();
            settleLines?.Clear();
            responseLines?.Clear();
            List<ResultLineRef> waveLines = BuildWaveResultLineRefs(groups, startIndex, length);
            var announceRefs = new List<ResultLineRef>();
            var settleRefs = new List<ResultLineRef>();
            var responseRefs = new List<ResultLineRef>();
            PartitionWaveResultLines(waveLines, announceRefs, settleRefs, responseRefs);
            CopyPartitionedLines(announceRefs, announceLines);
            CopyPartitionedLines(settleRefs, settleLines);
            CopyPartitionedLines(responseRefs, responseLines);
        }

        private static void CopyPartitionedLines(
            IReadOnlyList<ResultLineRef> source,
            List<ScoreLine> destination)
        {
            if (destination == null || source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                destination.Add(source[i].Line);
            }
        }

        private async Awaitable PlaySweetTransferWaveAsync(
            BattleSession session,
            SettlementPresentationPlan plan,
            SettlementPlaybackState playback,
            SettlementRunningLedger ledger,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            DiningTableCoordinateMapper mapper,
            Transform fxRoot,
            SettlementScoreFireView scoreFire,
            Action<BigDouble> renderScore,
            Action<SettlementRevealSignal> onReveal,
            Action<SettlementScopeSignal> onScope,
            Action<string> onPassiveTriggered,
            Action<SettlementBeatSignal> onBeat,
            SettlementBaselineSnapshot baselineSnapshot,
            SweetTransferPlaybackState sweetTransferPlayback,
            int startIndex,
            int waveLength,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<SettlementEffectGroup> groups = plan.Groups;
            SettlementEffectGroup firstGroup = groups[startIndex];
            var sourceIds = new HashSet<int>();
            var executorIds = new HashSet<int>();
            var targetIds = new HashSet<int>();
            int endIndex = startIndex + waveLength;
            for (int groupIndex = startIndex; groupIndex < endIndex; groupIndex++)
            {
                SettlementEffectGroup group = groups[groupIndex];
                if (group == null)
                {
                    continue;
                }

                foreach (int targetId in group.TargetDishIds)
                {
                    if (targetId > 0)
                    {
                        targetIds.Add(targetId);
                    }
                }

                for (int lineIndex = 0; lineIndex < group.Lines.Count; lineIndex++)
                {
                    ScoreLine line = group.Lines[lineIndex];
                    TryBuildCue(line, out SettlementCue cue);
                    BeginTriggerSweetTransferStateIfNeeded(
                        sweetTransferPlayback,
                        BuildSweetTransferSteps(line, cue),
                        dishViews,
                        cancellationToken);
                    if ((IsSweetTransferLaunchLine(line) || IsExtraTargetBuffLine(line))
                        && line.DishInstanceId > 0)
                    {
                        sourceIds.Add(line.DishInstanceId);
                    }

                    SettlementSweetTransferPresentationContext context =
                        SettlementSweetTransferPresentationContext.FromLine(line);
                    if (!context.IsValid)
                    {
                        continue;
                    }

                    sourceIds.Add(context.SourceDishInstanceId);
                    executorIds.Add(context.ExecutorDishInstanceId);
                    DishPieceView sourceView = TryGetDishView(context.SourceDishInstanceId, dishViews);
                    sourceView?.BeginSweetTransferSourceFeedback();
                    DishPieceView executorView = TryGetDishView(context.ExecutorDishInstanceId, dishViews);
                    executorView?.BeginSweetTransferExecutorFeedback();
                }
            }

            List<SettlementSweetTransferPresentationContext> handoffContexts =
                CollectWaveHandoffs(groups, startIndex, waveLength);
            var handoffs = new List<SettlementStageView.SweetTransferHandoffVisual>(handoffContexts.Count);
            for (int i = 0; i < handoffContexts.Count; i++)
            {
                SettlementSweetTransferPresentationContext context = handoffContexts[i];
                DishPieceView sourceView = TryGetDishView(context.SourceDishInstanceId, dishViews);
                DishPieceView executorView = TryGetDishView(context.ExecutorDishInstanceId, dishViews);
                int transferredDelta = CountNewTransferredSkills(
                    context,
                    executorView,
                    baselineSnapshot);
                if (transferredDelta > 0)
                {
                    onReveal?.Invoke(SettlementRevealSignal.TransferredReveal(
                        context.ExecutorDishInstanceId,
                        transferredDelta));
                }

                handoffs.Add(new SettlementStageView.SweetTransferHandoffVisual(
                    context,
                    sourceView,
                    executorView));
                sweetTransferPlayback.PresentationKey = context.Key;
                sweetTransferPlayback.ReceiverDishInstanceId = context.ExecutorDishInstanceId;
                sweetTransferPlayback.ExecutorDishInstanceId = context.ExecutorDishInstanceId;
            }

            EmitBeat(
                onBeat,
                SettlementBeatKind.SourceStarted,
                firstGroup.SourceName,
                firstGroup.ActorDishInstanceId,
                playback);
            int uniqueActorCount = sourceIds.Count + executorIds.Count;
            Vector3 cameraFocus = mapper.Center;
            if (uniqueActorCount == 1)
            {
                int actorId = sourceIds.Count == 1
                    ? FirstId(sourceIds)
                    : FirstId(executorIds);
                DishPieceView cameraActor = TryGetDishView(actorId, dishViews);
                if (cameraActor != null)
                {
                    cameraFocus = cameraActor.WorldBounds.center;
                }
            }

            _cameraFeedback.FocusSource(
                cameraFocus,
                ScaleSettlementDuration(_sourceFocusDuration));
            _stage.ApplyWaveFocus(sourceIds, executorIds, targetIds);

            List<ResultLineRef> waveLines = BuildWaveResultLineRefs(groups, startIndex, waveLength);
            var announceLines = new List<ResultLineRef>();
            var settleLines = new List<ResultLineRef>();
            var responseLines = new List<ResultLineRef>();
            PartitionWaveResultLines(waveLines, announceLines, settleLines, responseLines);
            var launchLines = new List<ResultLineRef>();
            var extraTargetLines = new List<ResultLineRef>();
            for (int i = 0; i < announceLines.Count; i++)
            {
                ResultLineRef lineRef = announceLines[i];
                if (IsExtraTargetBuffLine(lineRef.Line))
                {
                    extraTargetLines.Add(lineRef);
                }
                else
                {
                    launchLines.Add(lineRef);
                }
            }

            List<int> nativeLaunchIds = CollectWaveNativeLaunchSourceIds(groups, startIndex, waveLength);
            var nativeLaunchViews = new List<DishPieceView>(nativeLaunchIds.Count);
            for (int i = 0; i < nativeLaunchIds.Count; i++)
            {
                DishPieceView launchView = TryGetDishView(nativeLaunchIds[i], dishViews);
                if (launchView != null)
                {
                    nativeLaunchViews.Add(launchView);
                }
            }

            float announceDuration = ScaleSettlementDuration(_sourceFocusDuration);
            var announceTasks = new List<Awaitable>();
            if (launchLines.Count > 0)
            {
                announceTasks.Add(PlayResultLineBatchAsync(
                    launchLines,
                    Mathf.Max(1, targetIds.Count),
                    session,
                    playback,
                    ledger,
                    dishViews,
                    fxRoot,
                    renderScore,
                    onReveal,
                    onPassiveTriggered,
                    onBeat,
                    sweetTransferPlayback,
                    skipTransferTravel: true,
                    cancellationToken,
                    resultDurationOverride: announceDuration));
            }

            if (nativeLaunchViews.Count > 0)
            {
                announceTasks.Add(_stage.ShowSweetTransferLaunchAsync(
                    nativeLaunchViews,
                    announceDuration,
                    cancellationToken,
                    holdUntilCleared: false));
            }

            if (announceTasks.Count == 0)
            {
                await Awaitable.WaitForSecondsAsync(
                    ScaleSettlementDuration(_sweetTransferSourceDuration),
                    cancellationToken);
            }
            else
            {
                for (int i = 0; i < announceTasks.Count; i++)
                {
                    await announceTasks[i];
                }
            }

            // 额外目标 Buff 必须在目标选择后、粒子起飞前兑现，但不再和触发文本同一拍。
            // 每条 Buff 单独播放，确保多个棉花糖/松露巧克力会逐条显示来源与增量。
            for (int i = 0; i < extraTargetLines.Count; i++)
            {
                await PlayResultLineBatchAsync(
                    new[] { extraTargetLines[i] },
                    Mathf.Max(1, targetIds.Count),
                    session,
                    playback,
                    ledger,
                    dishViews,
                    fxRoot,
                    renderScore,
                    onReveal,
                    onPassiveTriggered,
                    onBeat,
                    sweetTransferPlayback,
                    skipTransferTravel: true,
                    cancellationToken,
                    resultDurationOverride: announceDuration);
            }

            if (handoffs.Count > 0)
            {
                await _stage.PlaySweetTransferHandoffsAsync(
                    handoffs,
                    _sweetTransferParticlePrefab,
                    ScaleSettlementDuration(_sweetTransferTravelDuration),
                    ScaleSettlementDuration(_sweetTransferExecutorDuration),
                    cancellationToken);
            }

            int waveTargetCount = Mathf.Max(1, targetIds.Count);
            SettlementImpactTier waveImpact = SettlementImpactTier.Base;
            for (int groupIndex = startIndex; groupIndex < endIndex; groupIndex++)
            {
                SettlementImpactTier groupImpact = StrongestImpact(groups[groupIndex], waveTargetCount);
                if (groupImpact > waveImpact)
                {
                    waveImpact = groupImpact;
                }
            }

            _cameraFeedback.PlayImpact(waveImpact, waveTargetCount);
            if (waveImpact >= SettlementImpactTier.Strong)
            {
                scoreFire?.Burst(waveImpact >= SettlementImpactTier.Chain ? 0.72f : 0.46f);
            }

            await RevealScopeAndPlayResultsAsync(
                settleLines,
                firstGroup,
                waveTargetCount,
                session,
                playback,
                ledger,
                dishViews,
                fxRoot,
                renderScore,
                onReveal,
                onScope,
                onPassiveTriggered,
                onBeat,
                sweetTransferPlayback,
                cancellationToken);

            if (responseLines.Count > 0)
            {
                onScope?.Invoke(default);
                await _stage.EndGroupAsync(
                    ScaleSettlementDuration(_groupSettleDuration),
                    cancellationToken);
                await RevealScopeAndPlayResultsAsync(
                    responseLines,
                    firstGroup,
                    waveTargetCount,
                    session,
                    playback,
                    ledger,
                    dishViews,
                    fxRoot,
                    renderScore,
                    onReveal,
                    onScope,
                    onPassiveTriggered,
                    onBeat,
                    sweetTransferPlayback,
                    cancellationToken);
            }

            int lastGroupIndex = endIndex - 1;
            SettlementEffectGroup lastGroup = groups[lastGroupIndex];
            foreach (int sourceId in sourceIds)
            {
                EndSweetTransferSourceFeedback(sourceId, dishViews);
            }

            foreach (int executorId in executorIds)
            {
                EndSweetTransferExecutorFeedback(executorId, dishViews);
            }

            sweetTransferPlayback.SourceDishInstanceId = 0;
            sweetTransferPlayback.ExecutorDishInstanceId = 0;
            sweetTransferPlayback.ReceiverDishInstanceId = 0;
            sweetTransferPlayback.PresentationKey = default;
            CompleteSweetTransferStateIfNeeded(
                sweetTransferPlayback,
                BuildNextSweetTransferSteps(plan, lastGroupIndex, Mathf.Max(0, lastGroup.Lines.Count - 1)),
                BuildNextSweetTransferPresentationContext(plan, lastGroupIndex, Mathf.Max(0, lastGroup.Lines.Count - 1)),
                dishViews);

            onScope?.Invoke(default);
            _cameraFeedback.ReturnHome(ScaleSettlementDuration(_groupSettleDuration + 0.08f));
            await _stage.EndGroupAsync(
                ScaleSettlementDuration(_groupSettleDuration),
                cancellationToken);
            EmitBeat(
                onBeat,
                SettlementBeatKind.GroupCompleted,
                firstGroup.SourceName,
                firstGroup.ActorDishInstanceId,
                playback);
        }

        private static int FirstId(HashSet<int> ids)
        {
            foreach (int id in ids)
            {
                return id;
            }

            return 0;
        }

        private async Awaitable RevealScopeAndPlayResultsAsync(
            IReadOnlyList<ResultLineRef> lines,
            SettlementEffectGroup beatGroup,
            int targetCount,
            BattleSession session,
            SettlementPlaybackState playback,
            SettlementRunningLedger ledger,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            Transform fxRoot,
            Action<BigDouble> renderScore,
            Action<SettlementRevealSignal> onReveal,
            Action<SettlementScopeSignal> onScope,
            Action<string> onPassiveTriggered,
            Action<SettlementBeatSignal> onBeat,
            SweetTransferPlaybackState sweetTransferPlayback,
            CancellationToken cancellationToken)
        {
            if (lines == null || lines.Count == 0)
            {
                return;
            }

            var scoreLines = new List<ScoreLine>(lines.Count);
            var dishIds = new HashSet<int>();
            for (int i = 0; i < lines.Count; i++)
            {
                ScoreLine line = lines[i].Line;
                if (line == null)
                {
                    continue;
                }

                scoreLines.Add(line);
                if (line.DishInstanceId > 0)
                {
                    dishIds.Add(line.DishInstanceId);
                }

                IReadOnlyList<int> visualIds = line.Trace?.VisualTargetDishInstanceIds;
                if (visualIds == null)
                {
                    continue;
                }

                for (int targetIndex = 0; targetIndex < visualIds.Count; targetIndex++)
                {
                    int visualId = visualIds[targetIndex];
                    if (visualId > 0)
                    {
                        dishIds.Add(visualId);
                    }
                }
            }

            SettlementScopeSignal scope = ScopeForScoreLines(scoreLines);
            if (!scope.IsEmpty)
            {
                EmitScope(onScope, scope);
                EmitBeat(
                    onBeat,
                    SettlementBeatKind.ScopeShown,
                    beatGroup.SourceName,
                    beatGroup.ActorDishInstanceId,
                    playback);
                await _stage.ShowScopeAsync(
                    dishIds,
                    ScaleSettlementDuration(_scopeRevealDuration),
                    cancellationToken);
            }

            await PlayResultLineBatchAsync(
                lines,
                targetCount,
                session,
                playback,
                ledger,
                dishViews,
                fxRoot,
                renderScore,
                onReveal,
                onPassiveTriggered,
                onBeat,
                sweetTransferPlayback,
                skipTransferTravel: true,
                cancellationToken);
        }

        private async Awaitable PlayResultLineBatchAsync(
            IReadOnlyList<ResultLineRef> lines,
            int targetCount,
            BattleSession session,
            SettlementPlaybackState playback,
            SettlementRunningLedger ledger,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            Transform fxRoot,
            Action<BigDouble> renderScore,
            Action<SettlementRevealSignal> onReveal,
            Action<string> onPassiveTriggered,
            Action<SettlementBeatSignal> onBeat,
            SweetTransferPlaybackState sweetTransferPlayback,
            bool skipTransferTravel,
            CancellationToken cancellationToken,
            bool holdResultLabels = false,
            float resultDurationOverride = -1f)
        {
            if (lines == null || lines.Count == 0)
            {
                return;
            }

            float resultDuration = resultDurationOverride > 0f
                ? resultDurationOverride
                : ScaleSettlementDuration(_resultBeatDuration);
            var resultTasks = new List<Awaitable>(lines.Count);
            Dictionary<int, int> primaryFeedbackLines = BuildPrimaryFeedbackLines(lines, targetCount);
            bool showOnlyResponseSummaries = false;
            for (int i = 0; i < lines.Count; i++)
            {
                if (IsSweetTransferResponseLine(lines[i].Line))
                {
                    showOnlyResponseSummaries = true;
                    break;
                }
            }

            // 同一批次的计分明细仍按原顺序写入账本与发出事件，但所有结果动画
            // 在同一帧启动。这样保留正式因果顺序，同时恢复“一起触发”的节奏。
            for (int i = 0; i < lines.Count; i++)
            {
                ResultLineRef lineRef = lines[i];
                SettlementEffectGroup group = lineRef.Group;
                ScoreLine line = lineRef.Line;
                await WaitWhilePlaybackPausedAsync(cancellationToken);
                AdvanceSettlementCue(playback, CueKindFor(line));
                SettlementCue cue = null;
                if (TryBuildCue(line, out SettlementCue builtCue))
                {
                    cue = builtCue;
                    AttachPassiveSource(line, cue);
                    EmitPassiveTriggered(onPassiveTriggered, cue);
                    EmitReveal(onReveal, cue);
                }

                IReadOnlyList<SettlementPlaybackStep> sweetSteps = BuildSweetTransferSteps(line, cue);
                BeginTriggerSweetTransferStateIfNeeded(
                    sweetTransferPlayback,
                    sweetSteps,
                    dishViews,
                    cancellationToken);
                if (!skipTransferTravel)
                {
                    await UpdateSweetTransferVisualsAsync(
                        sweetTransferPlayback,
                        ResolveSweetTransferVisualContext(sweetSteps),
                        dishViews,
                        fxRoot,
                        cancellationToken);
                }

                if (line.Kind == ScoreLineKind.SweetTransferBuffApplied)
                {
                    ApplySweetTransferBuffMarkers(line, dishViews);
                }
                else if (line.Kind == ScoreLineKind.SweetTransferBuffTriggered
                    && !IsExtraTargetBuffLine(line)
                    && !skipTransferTravel)
                {
                    await PlaySweetTransferBuffTriggerAsync(line, dishViews, cancellationToken);
                }
                else if (line.Kind == ScoreLineKind.SweetTransferFailed)
                {
                    Awaitable failureTask = PlaySweetTransferFailureAsync(line, dishViews, cancellationToken);
                    if (skipTransferTravel)
                    {
                        resultTasks.Add(failureTask);
                    }
                    else
                    {
                        await failureTask;
                    }
                }

                BigDouble beforeTotal = ledger.CurrentTotal;
                BigDouble contribution = ledger.Apply(line);
                SettlementImpactTier impactTier = ImpactFor(line, targetCount);
                SettlementPacePhase nextPhase = ResolvePacePhase(
                    ledger.CurrentTotal,
                    session?.RequiredScore ?? 0,
                    _currentPacePhase);
                bool reachedTarget = _currentPacePhase < SettlementPacePhase.TargetReached
                    && nextPhase >= SettlementPacePhase.TargetReached;
                bool playPrimaryFeedback = primaryFeedbackLines.TryGetValue(
                        line.DishInstanceId,
                        out int primaryIndex)
                    && primaryIndex == i;
                dishViews.TryGetValue(ResultVisualDishInstanceId(line), out DishPieceView target);
                if (target != null && ChangesDishValue(line.Kind))
                {
                    target.SetDishValueBadge(contribution);
                    if (playPrimaryFeedback)
                    {
                        target.PunchDishValueBadge(
                            DishValuePunchScale,
                            ScaleSettlementDuration(DishValuePunchDuration));
                    }
                }

                renderScore?.Invoke(ledger.CurrentTotal);
                EmitScoreBeat(
                    onBeat,
                    group.SourceName,
                    line.DishInstanceId,
                    playback,
                    line.Kind,
                    beforeTotal,
                    ledger.CurrentTotal,
                    impactTier,
                    targetCount,
                    reachedTarget);
                if (!showOnlyResponseSummaries || IsSweetTransferResponseLine(line))
                {
                    resultTasks.Add(_stage.ShowResultAsync(
                        group,
                        line,
                        target,
                        contribution,
                        ledger.CurrentTotal,
                        resultDuration,
                        impactTier,
                        Mathf.Lerp(0.96f, 1.18f, NormalizedProgress(playback)),
                        playPrimaryFeedback,
                        cancellationToken,
                        holdUntilCleared: holdResultLabels && IsSweetTransferAnnounceLine(line)));
                }

                PromoteSettlementPace(playback, nextPhase);
            }

            for (int resultIndex = 0; resultIndex < resultTasks.Count; resultIndex++)
            {
                await resultTasks[resultIndex];
            }
        }

        private static bool ChangesDishValue(ScoreLineKind kind)
        {
            return kind == ScoreLineKind.DishBase
                || kind == ScoreLineKind.DishFlat
                || kind == ScoreLineKind.DishPermanentFlat
                || kind == ScoreLineKind.DishMultiplier
                || kind == ScoreLineKind.DishMultiplierAdd
                || kind == ScoreLineKind.ExtraSettlement;
        }

        private static SettlementCueKind CueKindFor(ScoreLine line)
        {
            if (line == null)
            {
                return SettlementCueKind.Source;
            }

            switch (line.Kind)
            {
                case ScoreLineKind.FinalFlat:
                case ScoreLineKind.FinalMultiplier:
                    return SettlementCueKind.FinalModifier;
                case ScoreLineKind.Gold:
                case ScoreLineKind.Layer:
                case ScoreLineKind.CopySkill:
                case ScoreLineKind.TriggerSweetTransfer:
                case ScoreLineKind.TriggeredSweetTransferSource:
                case ScoreLineKind.SweetTransferBuffApplied:
                case ScoreLineKind.SweetTransferBuffTriggered:
                case ScoreLineKind.SweetTransferFailed:
                case ScoreLineKind.CountAs:
                case ScoreLineKind.EmptyCountAs:
                case ScoreLineKind.TemporaryCategory:
                    return SettlementCueKind.SideEffect;
                default:
                    return SettlementCueKind.DishContribution;
            }
        }

        private void EmitBeat(
            Action<SettlementBeatSignal> onBeat,
            SettlementBeatKind kind,
            string sourceName,
            int dishInstanceId,
            SettlementPlaybackState playback)
        {
            if (onBeat == null || playback == null)
            {
                return;
            }

            float normalized = playback.CueCount <= 1
                ? 1f
                : Mathf.Clamp01((float)Mathf.Max(0, playback.CueIndex - 1) / (playback.CueCount - 1));
            onBeat(new SettlementBeatSignal(
                kind,
                sourceName,
                dishInstanceId,
                _currentSettlementSpeed,
                normalized));
        }

        private void EmitScoreBeat(
            Action<SettlementBeatSignal> onBeat,
            string sourceName,
            int dishInstanceId,
            SettlementPlaybackState playback,
            ScoreLineKind lineKind,
            BigDouble beforeScore,
            BigDouble afterScore,
            SettlementImpactTier impactTier,
            int targetCount,
            bool reachedTarget)
        {
            if (onBeat == null || playback == null)
            {
                return;
            }

            onBeat(new SettlementBeatSignal(
                SettlementBeatKind.ResultApplied,
                sourceName,
                dishInstanceId,
                _currentSettlementSpeed,
                NormalizedProgress(playback),
                lineKind,
                beforeScore,
                afterScore,
                impactTier,
                targetCount,
                reachedTarget));
        }

        private static float NormalizedProgress(SettlementPlaybackState playback)
        {
            if (playback == null || playback.CueCount <= 1)
            {
                return 1f;
            }

            return Mathf.Clamp01(
                (float)Mathf.Max(0, playback.CueIndex - 1)
                / (playback.CueCount - 1));
        }

        internal static SettlementImpactTier ImpactFor(ScoreLine line, int targetCount = 1)
        {
            SettlementImpactTier tier = line?.Kind switch
            {
                ScoreLineKind.DishBase => SettlementImpactTier.Base,
                ScoreLineKind.DishPermanentFlat
                    or ScoreLineKind.DishMultiplier
                    or ScoreLineKind.DishMultiplierAdd
                    or ScoreLineKind.FinalFlat
                    or ScoreLineKind.FinalMultiplier => SettlementImpactTier.Strong,
                ScoreLineKind.CopySkill
                    or ScoreLineKind.TriggerSweetTransfer
                    or ScoreLineKind.TriggeredSweetTransferSource
                    or ScoreLineKind.SweetTransferBuffApplied
                    or ScoreLineKind.SweetTransferBuffTriggered => SettlementImpactTier.Chain,
                ScoreLineKind.SweetTransferFailed => SettlementImpactTier.Normal,
                _ => SettlementImpactTier.Normal,
            };

            if (targetCount > 1 && tier < SettlementImpactTier.Chain)
            {
                tier++;
            }

            return tier;
        }

        private static int CountDistinctTargets(
            SettlementEffectGroup group,
            IReadOnlyDictionary<int, DishPieceView> dishViews)
        {
            if (group == null)
            {
                return 1;
            }

            var targets = new HashSet<int>();
            for (int i = 0; i < group.Lines.Count; i++)
            {
                int id = group.Lines[i].DishInstanceId;
                if (id > 0 && dishViews != null && dishViews.ContainsKey(id))
                {
                    targets.Add(id);
                }
            }

            return Mathf.Max(1, targets.Count);
        }

        private static SettlementImpactTier StrongestImpact(
            SettlementEffectGroup group,
            int targetCount)
        {
            SettlementImpactTier strongest = SettlementImpactTier.Base;
            if (group == null)
            {
                return strongest;
            }

            for (int i = 0; i < group.Lines.Count; i++)
            {
                SettlementImpactTier tier = ImpactFor(group.Lines[i], targetCount);
                if (tier > strongest)
                {
                    strongest = tier;
                }
            }

            return strongest;
        }

        internal static List<List<int>> BuildResultLineBatches(SettlementEffectGroup group)
        {
            var batches = new List<List<int>>();
            if (group == null || group.Lines.Count == 0)
            {
                return batches;
            }

            bool splitCakeLayerResults = group.Source?.Type == ScoreSourceType.TableTag
                && string.Equals(group.Source.Id, "cake_layer_buff", StringComparison.Ordinal);
            List<int> current = null;
            ScoreLineKind currentKind = default;
            for (int i = 0; i < group.Lines.Count; i++)
            {
                ScoreLineKind kind = group.Lines[i].Kind;
                if (current == null || (splitCakeLayerResults && kind != currentKind))
                {
                    current = new List<int>();
                    batches.Add(current);
                    currentKind = kind;
                }

                current.Add(i);
            }

            return batches;
        }

        private static Dictionary<int, int> BuildPrimaryFeedbackLines(
            IReadOnlyList<ResultLineRef> lines,
            int targetCount)
        {
            var result = new Dictionary<int, int>();
            if (lines == null)
            {
                return result;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                ScoreLine line = lines[i].Line;
                int targetId = line.DishInstanceId;
                if (targetId <= 0)
                {
                    continue;
                }

                if (!result.TryGetValue(targetId, out int current)
                    || ImpactFor(line, targetCount) > ImpactFor(lines[current].Line, targetCount))
                {
                    result[targetId] = i;
                }
            }

            return result;
        }

        private async Awaitable PlayCueAsync(
            SettlementCue cue,
            SettlementScopeSignal scope,
            DishPieceView view,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            Vector3 floatingAnchor,
            Transform fxRoot,
            SettlementPlaybackState playback,
            Dictionary<int, DishValuePlaybackAccumulator> dishValues,
            Action<SettlementRevealSignal> onReveal,
            Action<SettlementScopeSignal> onScope,
            Action<string> onPassiveTriggered,
            CancellationToken cancellationToken)
        {
            await WaitWhilePlaybackPausedAsync(cancellationToken);
            AdvanceSettlementCue(playback, cue.Kind);
            EmitScope(onScope, scope);
            EmitPassiveTriggered(onPassiveTriggered, cue);
            EmitReveal(onReveal, cue);
            ApplyDishValueChange(cue, view, dishValues);
            PlayActorFeedbackIfNeeded(scope, view.Instance != null ? view.Instance.Id : 0, cue.FeedbackKind, dishViews, cancellationToken);
            if (fxRoot != null && cue.ShowEffectLabel)
            {
                FloatingTextView.SpawnEffect(
                    _settlementEffectLabelPrefab,
                    fxRoot,
                    floatingAnchor,
                    cue.SourceName,
                    cue.Text,
                    cue.Rise,
                    ScaleSettlementDuration(cue.Duration),
                    effectColor: cue.EffectColor,
                    visualScale: _visualScale);
            }

            await view.PlaySettlementFeedbackAsync(cue.FeedbackKind, cancellationToken);
        }

        private async Awaitable PlayStepBatchAsync(
            IReadOnlyList<SettlementPlaybackStep> batch,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            DiningTableCoordinateMapper mapper,
            Transform fxRoot,
            SettlementPlaybackState playback,
            Dictionary<int, DishValuePlaybackAccumulator> dishValues,
            Action<SettlementRevealSignal> onReveal,
            Action<SettlementScopeSignal> onScope,
            Action<string> onPassiveTriggered,
            CancellationToken cancellationToken)
        {
            if (batch == null || batch.Count == 0)
            {
                return;
            }

            if (batch.Count == 1)
            {
                SettlementPlaybackStep step = batch[0];
                if (!dishViews.TryGetValue(step.DishInstanceId, out DishPieceView view) || view == null)
                {
                    return;
                }

                Vector3 dishValueAnchor = DishValueAnchor(view, mapper);
                Vector3 floatingAnchor = DishFloatingAnchor(dishValueAnchor, mapper);
                await PlayCueAsync(
                    step.Cue,
                    step.Scope,
                    view,
                    dishViews,
                    floatingAnchor,
                    fxRoot,
                    playback,
                    dishValues,
                    onReveal,
                    onScope,
                    onPassiveTriggered,
                    cancellationToken);
                return;
            }

            await WaitWhilePlaybackPausedAsync(cancellationToken);
            AdvanceSettlementCue(playback, batch[0].Cue.Kind);
            var triggeredActorIds = new HashSet<int>();
            var triggeredPassiveItemIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (SettlementPlaybackStep step in batch)
            {
                if (!dishViews.TryGetValue(step.DishInstanceId, out DishPieceView view) || view == null)
                {
                    continue;
                }

                Vector3 dishValueAnchor = DishValueAnchor(view, mapper);
                Vector3 floatingAnchor = DishFloatingAnchor(dishValueAnchor, mapper);
                SettlementCue cue = step.Cue;
                EmitScope(onScope, step.Scope);
                if (!string.IsNullOrEmpty(cue.SourceItemId)
                    && triggeredPassiveItemIds.Add(cue.SourceItemId))
                {
                    EmitPassiveTriggered(onPassiveTriggered, cue);
                }
                EmitReveal(onReveal, cue);
                ApplyDishValueChange(cue, view, dishValues);
                PlayActorFeedbackIfNeeded(
                    step.Scope,
                    step.DishInstanceId,
                    cue.FeedbackKind,
                    dishViews,
                    cancellationToken,
                    triggeredActorIds);
                if (fxRoot != null && cue.ShowEffectLabel)
                {
                    FloatingTextView.SpawnEffect(
                        _settlementEffectLabelPrefab,
                        fxRoot,
                        floatingAnchor,
                        cue.SourceName,
                        cue.Text,
                        cue.Rise,
                        ScaleSettlementDuration(cue.Duration),
                        effectColor: cue.EffectColor,
                        visualScale: _visualScale);
                }

                _ = PlayFeedbackSafelyAsync(view, cue.FeedbackKind, cancellationToken);
            }

            await Awaitable.WaitForSecondsAsync(ScaleSettlementDuration(BatchedCueHold), cancellationToken);
        }

        private static void PlayActorFeedbackIfNeeded(
            SettlementScopeSignal scope,
            int targetDishInstanceId,
            SettlementDishFeedbackKind targetFeedbackKind,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            CancellationToken cancellationToken,
            HashSet<int> triggeredActorIds = null)
        {
            SettlementDishFeedbackKind actorFeedbackKind = ActorFeedbackKindFor(targetFeedbackKind);
            if (actorFeedbackKind == SettlementDishFeedbackKind.None || dishViews == null)
            {
                return;
            }

            int actorDishInstanceId = scope.RuntimeSelfDishInstanceId > 0
                ? scope.RuntimeSelfDishInstanceId
                : scope.OwnerDishInstanceId;
            if (actorDishInstanceId == 0 || actorDishInstanceId == targetDishInstanceId)
            {
                return;
            }

            if (triggeredActorIds != null && !triggeredActorIds.Add(actorDishInstanceId))
            {
                return;
            }

            if (dishViews.TryGetValue(actorDishInstanceId, out DishPieceView actorView) && actorView != null)
            {
                _ = PlayFeedbackSafelyAsync(actorView, actorFeedbackKind, cancellationToken);
            }
        }

        private static SettlementDishFeedbackKind ActorFeedbackKindFor(SettlementDishFeedbackKind targetFeedbackKind)
        {
            switch (targetFeedbackKind)
            {
                case SettlementDishFeedbackKind.CopiedSkillTriggered:
                    return targetFeedbackKind;
                case SettlementDishFeedbackKind.PassiveFlatBonus:
                case SettlementDishFeedbackKind.PassiveMultiplier:
                case SettlementDishFeedbackKind.PassiveMultiplierAdd:
                case SettlementDishFeedbackKind.CountAsChanged:
                case SettlementDishFeedbackKind.TemporaryCategoryApplied:
                    return SettlementDishFeedbackKind.GenericSkillTriggered;
                default:
                    return SettlementDishFeedbackKind.None;
            }
        }

        private static SweetTransferVisualContext ResolveSweetTransferVisualContext(
            IReadOnlyList<SettlementPlaybackStep> batch)
        {
            if (batch == null)
            {
                return default;
            }

            int fallbackSourceDishId = 0;
            for (int i = 0; i < batch.Count; i++)
            {
                SettlementPlaybackStep step = batch[i];
                if (step.Cue?.TriggerSweetTransferPhase == TriggerSweetTransferCuePhase.SourceStarted
                    || step.Cue?.TriggerSweetTransferPhase == TriggerSweetTransferCuePhase.FinalSourceStarted)
                {
                    return new SweetTransferVisualContext(step.DishInstanceId, 0, playSourceIntro: false);
                }

                if (step.Scope.Trace?.Kind == SkillExecutionKind.SweetTransfer)
                {
                    return new SweetTransferVisualContext(
                        step.Scope.OwnerDishInstanceId,
                        step.Scope.RuntimeSelfDishInstanceId);
                }

                if (fallbackSourceDishId == 0)
                {
                    fallbackSourceDishId = ResolveSweetTransferSourceDishId(step.Scope, step.Cue?.BatchKey);
                }
            }

            return new SweetTransferVisualContext(fallbackSourceDishId, 0);
        }

        private static void BeginTriggerSweetTransferStateIfNeeded(
            SweetTransferPlaybackState playback,
            IReadOnlyList<SettlementPlaybackStep> batch,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            CancellationToken cancellationToken)
        {
            if (playback == null || batch == null)
            {
                return;
            }

            for (int i = 0; i < batch.Count; i++)
            {
                SettlementPlaybackStep step = batch[i];
                TriggerSweetTransferCuePhase phase = step.Cue?.TriggerSweetTransferPhase
                    ?? TriggerSweetTransferCuePhase.None;
                if (phase == TriggerSweetTransferCuePhase.ActivatorStarted)
                {
                    EndTriggerSweetTransferActivatorFeedback(playback, dishViews);
                    playback.TriggerSweetTransferActivatorDishInstanceId = step.DishInstanceId;
                    playback.TriggerSweetTransferFinalSourceDishInstanceId = 0;
                    if (dishViews != null
                        && dishViews.TryGetValue(step.DishInstanceId, out DishPieceView activator)
                        && activator != null)
                    {
                        activator.BeginTriggerSweetTransferActivatorFeedback();
                    }
                }
                else if (phase == TriggerSweetTransferCuePhase.FinalSourceStarted)
                {
                    playback.TriggerSweetTransferFinalSourceDishInstanceId = step.DishInstanceId;
                }

                if ((phase == TriggerSweetTransferCuePhase.SourceStarted
                        || phase == TriggerSweetTransferCuePhase.FinalSourceStarted)
                    && playback.TriggerSweetTransferActivatorDishInstanceId > 0
                    && dishViews != null
                    && dishViews.TryGetValue(
                        playback.TriggerSweetTransferActivatorDishInstanceId,
                        out DishPieceView pulseActivator)
                    && pulseActivator != null)
                {
                    _ = PlayFeedbackSafelyAsync(
                        pulseActivator,
                        SettlementDishFeedbackKind.TriggerSweetTransferActivatorPulse,
                        cancellationToken);
                }
            }
        }

        private static void CompleteSweetTransferStateIfNeeded(
            SweetTransferPlaybackState playback,
            IReadOnlyList<SettlementPlaybackStep> nextBatch,
            SettlementSweetTransferPresentationContext nextPresentation,
            IReadOnlyDictionary<int, DishPieceView> dishViews)
        {
            if (playback == null)
            {
                return;
            }

            SweetTransferVisualContext next = ResolveSweetTransferVisualContext(nextBatch);
            SynchronizeSweetTransferSource(
                playback,
                next,
                dishViews,
                allowBeginOrSwitch: false);
            SynchronizeSweetTransferExecutor(
                playback,
                nextPresentation.IsValid ? nextPresentation.ExecutorDishInstanceId : 0,
                dishViews,
                allowBeginOrSwitch: false);
            if (!nextPresentation.IsValid)
            {
                playback.PresentationKey = default;
            }

            if (playback.TriggerSweetTransferActivatorDishInstanceId <= 0
                || playback.TriggerSweetTransferFinalSourceDishInstanceId <= 0)
            {
                return;
            }

            if (next.SourceDishInstanceId == playback.TriggerSweetTransferFinalSourceDishInstanceId)
            {
                return;
            }

            EndTriggerSweetTransferActivatorFeedback(playback, dishViews);
        }

        private static void EndTriggerSweetTransferActivatorFeedback(
            SweetTransferPlaybackState playback,
            IReadOnlyDictionary<int, DishPieceView> dishViews)
        {
            if (playback == null)
            {
                return;
            }

            int activatorId = playback.TriggerSweetTransferActivatorDishInstanceId;
            if (activatorId > 0
                && dishViews != null
                && dishViews.TryGetValue(activatorId, out DishPieceView activator)
                && activator != null)
            {
                activator.EndTriggerSweetTransferActivatorFeedback();
            }

            playback.TriggerSweetTransferActivatorDishInstanceId = 0;
            playback.TriggerSweetTransferFinalSourceDishInstanceId = 0;
        }

        private static int ResolveSweetTransferSourceDishId(SettlementScopeSignal scope, string batchKey)
        {
            if (scope.Trace?.Kind == SkillExecutionKind.SweetTransfer)
            {
                return scope.OwnerDishInstanceId;
            }

            return scope.OwnerDishInstanceId > 0
                && !string.IsNullOrEmpty(batchKey)
                && batchKey.Contains("甜蜜传递")
                    ? scope.OwnerDishInstanceId
                    : 0;
        }

        private async Awaitable UpdateSweetTransferVisualsAsync(
            SweetTransferPlaybackState playback,
            SweetTransferVisualContext next,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            Transform fxRoot,
            CancellationToken cancellationToken)
        {
            bool beganSource = SynchronizeSweetTransferSource(
                playback,
                next,
                dishViews,
                allowBeginOrSwitch: true);
            if (beganSource && next.PlaySourceIntro)
            {
                PlaySweetTransferSourceIntro(
                    playback.SourceDishInstanceId,
                    dishViews,
                    cancellationToken);
            }

            if (next.ReceiverDishInstanceId <= 0
                || playback.ReceiverDishInstanceId == next.ReceiverDishInstanceId)
            {
                return;
            }

            playback.ReceiverDishInstanceId = next.ReceiverDishInstanceId;
            if (_sweetTransferParticlePrefab == null
                || dishViews == null
                || !dishViews.TryGetValue(playback.SourceDishInstanceId, out DishPieceView source)
                || source == null
                || !dishViews.TryGetValue(playback.ReceiverDishInstanceId, out DishPieceView receiver)
                || receiver == null)
            {
                playback.ReceiverDishInstanceId = 0;
                return;
            }

            await SweetTransferParticleView.PlayAsync(
                _sweetTransferParticlePrefab,
                fxRoot != null ? fxRoot : transform,
                source.WorldBounds.center,
                receiver.WorldBounds.center,
                ScaleSettlementDuration(SweetTransferParticleDuration),
                cancellationToken,
                visualScale: _visualScale);
        }

        private static void ApplySweetTransferBuffMarkers(
            ScoreLine line,
            IReadOnlyDictionary<int, DishPieceView> dishViews)
        {
            if (line?.Trace == null || dishViews == null)
            {
                return;
            }

            IReadOnlyList<int> targetIds = line.Trace.VisualTargetDishInstanceIds;
            for (int i = 0; i < targetIds.Count; i++)
            {
                if (dishViews.TryGetValue(targetIds[i], out DishPieceView target) && target != null)
                {
                    target.AddSweetTransferBuffMarker(line.Trace.ActionType);
                }
            }
        }

        private async Awaitable PlaySweetTransferBuffTriggerAsync(
            ScoreLine line,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            CancellationToken cancellationToken)
        {
            SkillExecutionTrace trace = line?.Trace;
            if (trace == null || dishViews == null)
            {
                return;
            }

            dishViews.TryGetValue(trace.RuntimeSelfDishInstanceId, out DishPieceView source);
            dishViews.TryGetValue(trace.OwnerDishInstanceId, out DishPieceView owner);
            await _stage.PlaySweetTransferBuffTriggerAsync(
                source,
                owner,
                _sweetTransferParticlePrefab,
                ScaleSettlementDuration(SweetTransferParticleDuration),
                cancellationToken);
        }

        private async Awaitable PlaySweetTransferFailureAsync(
            ScoreLine line,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            CancellationToken cancellationToken)
        {
            int sourceId = line?.Trace?.RuntimeSelfDishInstanceId ?? line?.DishInstanceId ?? 0;
            DishPieceView source = TryGetDishView(sourceId, dishViews);
            await _stage.PlaySweetTransferFailureAsync(
                source,
                _sweetTransferParticlePrefab,
                ScaleSettlementDuration(SweetTransferParticleDuration),
                cancellationToken);
        }

        private static void ClearSweetTransferBuffMarkers(
            IReadOnlyDictionary<int, DishPieceView> dishViews)
        {
            if (dishViews == null)
            {
                return;
            }

            foreach (DishPieceView view in dishViews.Values)
            {
                view?.ClearSweetTransferBuffMarkers();
            }
        }

        private static bool SynchronizeSweetTransferSource(
            SweetTransferPlaybackState playback,
            SweetTransferVisualContext next,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            bool allowBeginOrSwitch)
        {
            if (playback == null)
            {
                return false;
            }

            SettlementSweetTransferTransition transition = SettlementSweetTransferTransitionResolver.Resolve(
                playback.SourceDishInstanceId,
                next.SourceDishInstanceId);
            if (transition == SettlementSweetTransferTransition.Clear)
            {
                EndSweetTransferSourceFeedback(playback.SourceDishInstanceId, dishViews);
                playback.SourceDishInstanceId = 0;
                playback.ReceiverDishInstanceId = 0;
                return false;
            }

            if (!allowBeginOrSwitch
                || (transition != SettlementSweetTransferTransition.Begin
                    && transition != SettlementSweetTransferTransition.Switch))
            {
                if (transition == SettlementSweetTransferTransition.None)
                {
                    playback.ReceiverDishInstanceId = 0;
                }

                return false;
            }

            if (transition == SettlementSweetTransferTransition.Switch)
            {
                EndSweetTransferSourceFeedback(playback.SourceDishInstanceId, dishViews);
            }

            playback.SourceDishInstanceId = 0;
            playback.ReceiverDishInstanceId = 0;
            if (next.SourceDishInstanceId <= 0
                || dishViews == null
                || !dishViews.TryGetValue(next.SourceDishInstanceId, out DishPieceView sourceView)
                || sourceView == null)
            {
                return false;
            }

            playback.SourceDishInstanceId = next.SourceDishInstanceId;
            sourceView.BeginSweetTransferSourceFeedback();
            return true;
        }

        private static void SynchronizeSweetTransferExecutor(
            SweetTransferPlaybackState playback,
            int nextExecutorDishInstanceId,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            bool allowBeginOrSwitch)
        {
            if (playback == null)
            {
                return;
            }

            SettlementSweetTransferTransition transition = SettlementSweetTransferTransitionResolver.Resolve(
                playback.ExecutorDishInstanceId,
                nextExecutorDishInstanceId);
            if (transition == SettlementSweetTransferTransition.Clear)
            {
                EndSweetTransferExecutorFeedback(playback.ExecutorDishInstanceId, dishViews);
                playback.ExecutorDishInstanceId = 0;
                return;
            }

            if (!allowBeginOrSwitch
                || (transition != SettlementSweetTransferTransition.Begin
                    && transition != SettlementSweetTransferTransition.Switch))
            {
                return;
            }

            if (transition == SettlementSweetTransferTransition.Switch)
            {
                EndSweetTransferExecutorFeedback(playback.ExecutorDishInstanceId, dishViews);
            }

            playback.ExecutorDishInstanceId = TryGetDishView(nextExecutorDishInstanceId, dishViews) != null
                ? nextExecutorDishInstanceId
                : 0;
        }

        private static void PlaySweetTransferSourceIntro(
            int sourceDishInstanceId,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            CancellationToken cancellationToken)
        {
            if (sourceDishInstanceId > 0
                && dishViews != null
                && dishViews.TryGetValue(sourceDishInstanceId, out DishPieceView sourceView)
                && sourceView != null)
            {
                _ = PlayFeedbackSafelyAsync(
                    sourceView,
                    SettlementDishFeedbackKind.SweetTransferSkillTriggered,
                    cancellationToken);
            }
        }

        private static void ClearSweetTransferVisuals(
            SweetTransferPlaybackState playback,
            IReadOnlyDictionary<int, DishPieceView> dishViews)
        {
            if (playback == null)
            {
                return;
            }

            SynchronizeSweetTransferSource(
                playback,
                default,
                dishViews,
                allowBeginOrSwitch: true);
            EndSweetTransferExecutorFeedback(playback.ExecutorDishInstanceId, dishViews);
            playback.ExecutorDishInstanceId = 0;
            playback.ReceiverDishInstanceId = 0;
            playback.PresentationKey = default;
            EndTriggerSweetTransferActivatorFeedback(playback, dishViews);
        }

        private static void EndSweetTransferSourceFeedback(
            int sourceDishInstanceId,
            IReadOnlyDictionary<int, DishPieceView> dishViews)
        {
            if (sourceDishInstanceId > 0
                && dishViews != null
                && dishViews.TryGetValue(sourceDishInstanceId, out DishPieceView source)
                && source != null)
            {
                source.EndSweetTransferSourceFeedback();
            }
        }

        private static void EndSweetTransferExecutorFeedback(
            int executorDishInstanceId,
            IReadOnlyDictionary<int, DishPieceView> dishViews)
        {
            TryGetDishView(executorDishInstanceId, dishViews)?.EndSweetTransferExecutorFeedback();
        }

        private static DishPieceView TryGetDishView(
            int dishInstanceId,
            IReadOnlyDictionary<int, DishPieceView> dishViews)
        {
            return dishInstanceId > 0
                && dishViews != null
                && dishViews.TryGetValue(dishInstanceId, out DishPieceView view)
                    ? view
                    : null;
        }

        private static async Awaitable PlayFeedbackSafelyAsync(
            DishPieceView view,
            SettlementDishFeedbackKind feedbackKind,
            CancellationToken cancellationToken)
        {
            if (view == null)
            {
                return;
            }

            try
            {
                await view.PlaySettlementFeedbackAsync(feedbackKind, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 结算被中断时，已经启动的并行动画正常退出。
            }
        }

        private void ApplyDishValueChange(
            SettlementCue cue,
            DishPieceView view,
            Dictionary<int, DishValuePlaybackAccumulator> dishValues)
        {
            if (cue == null
                || cue.ValueChange.Kind == DishValueChangeKind.None
                || view == null
                || view.Instance == null
                || dishValues == null)
            {
                return;
            }

            DishInstance instance = view.Instance;
            if (!dishValues.TryGetValue(instance.Id, out DishValuePlaybackAccumulator value))
            {
                value = new DishValuePlaybackAccumulator(
                    instance.BaseScoreBeforeSettlement,
                    instance.BaseMultiplierBeforeSettlement);
                dishValues[instance.Id] = value;
            }

            value.Apply(cue.ValueChange);
            view.SetDishValueBadge(value.Contribution);
            view.PunchDishValueBadge(
                DishValuePunchScale,
                ScaleSettlementDuration(DishValuePunchDuration));
        }

        public void ClearRetainedDishValueBadges()
        {
            _retainedDishValues.Clear();
        }

        private static void SeedDishValues(
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            SettlementBaselineSnapshot baselineSnapshot,
            IDictionary<int, DishValuePlaybackAccumulator> values)
        {
            if (dishViews == null || values == null)
            {
                return;
            }

            foreach (KeyValuePair<int, DishPieceView> entry in dishViews)
            {
                DishPieceView view = entry.Value;
                DishInstance dish = view?.Instance;
                if (dish == null)
                {
                    continue;
                }

                BigDouble baseScore = dish.BaseScoreBeforeSettlement;
                BigDouble multiplier = dish.BaseMultiplierBeforeSettlement;
                if (baselineSnapshot != null
                    && baselineSnapshot.TryGet(dish.Id, out SettlementDishBaseline baseline))
                {
                    baseScore = baseline.BaseScore;
                    multiplier = baseline.Multiplier;
                }

                values[dish.Id] =
                    new DishValuePlaybackAccumulator(baseScore, multiplier);
            }
        }

        /// <summary>待领奖读档或页面往返后，按结算明细无动画恢复食物贡献值。</summary>
        public void RestoreDishValueBadges(
            IReadOnlyList<DishScore> dishScores,
            IReadOnlyDictionary<int, DishPieceView> dishViews)
        {
            ClearRetainedDishValueBadges();
            if (dishScores == null || dishViews == null)
            {
                return;
            }

            foreach (DishScore score in dishScores)
            {
                if (score == null
                    || !dishViews.TryGetValue(score.DishInstanceId, out DishPieceView view)
                    || view == null
                    || view.Instance == null)
                {
                    continue;
                }

                var value =
                    new DishValuePlaybackAccumulator(score.BaseValue, score.Multiplier);
                value.Apply(DishValueChange.FlatBonus(score.FlatBonus));
                view.SetDishValueBadge(score.Contribution);
                _retainedDishValues[score.DishInstanceId] = value;
            }
        }

        private static void ApplyFinalDishValues(
            IReadOnlyList<DishScore> dishScores,
            IReadOnlyDictionary<int, DishPieceView> dishViews)
        {
            if (dishScores == null || dishViews == null)
            {
                return;
            }

            foreach (DishScore score in dishScores)
            {
                if (score != null
                    && dishViews.TryGetValue(score.DishInstanceId, out DishPieceView view)
                    && view != null)
                {
                    view.SetDishValueBadge(score.Contribution);
                }
            }
        }

        private async Awaitable PlayFinalScorePopupAsync(
            int total,
            Vector3 center,
            Transform fxRoot,
            CancellationToken cancellationToken)
        {
            await WaitWhilePlaybackPausedAsync(cancellationToken);
            if (fxRoot != null)
            {
                FloatingTextView.SpawnEffect(
                    _settlementEffectLabelPrefab,
                    fxRoot,
                    center + new Vector3(
                        0f,
                        0.72f * _visualScale,
                        0f),
                    "结算",
                    $"总分 {total}",
                    FinalScorePopupRise,
                    ScaleSettlementDuration(FinalScorePopupDuration),
                    visualScale: _visualScale);
            }

            await Awaitable.WaitForSecondsAsync(ScaleSettlementDuration(FinalScorePopupHold), cancellationToken);
        }

        private async Awaitable PlayFinalCuesAsync(
            IReadOnlyList<SettlementCue> cues,
            Vector3 center,
            Transform fxRoot,
            SettlementPlaybackState playback,
            Action<SettlementRevealSignal> onReveal,
            Action<string> onPassiveTriggered,
            CancellationToken cancellationToken)
        {
            for (int i = 0; i < cues.Count; i++)
            {
                await WaitWhilePlaybackPausedAsync(cancellationToken);
                SettlementCue cue = cues[i];
                AdvanceSettlementCue(playback, cue.Kind);
                EmitPassiveTriggered(onPassiveTriggered, cue);
                EmitReveal(onReveal, cue);
                if (fxRoot != null)
                {
                    Vector3 offset = new(
                        0f,
                        (0.52f + SourceCueStackOffset * i) * _visualScale,
                        0f);
                    FloatingTextView.SpawnEffect(
                        _settlementEffectLabelPrefab,
                        fxRoot,
                        center + offset,
                        cue.SourceName,
                        cue.Text,
                        cue.Rise,
                        ScaleSettlementDuration(cue.Duration),
                        effectColor: cue.EffectColor,
                        visualScale: _visualScale);
                }

                await Awaitable.WaitForSecondsAsync(ScaleSettlementDuration(FinalCueInterval), cancellationToken);
            }
        }

        private float ScaleSettlementDuration(float duration)
        {
            return Mathf.Max(0.0001f, duration / Mathf.Max(0.0001f, _currentSettlementSpeed));
        }

        private void BeginSettlementPace()
        {
            _currentPacePhase = SettlementPacePhase.BelowTarget;
            _settlementDoubleSpeed =
                GameApp.Settings != null && GameApp.Settings.SettlementDoubleSpeed;
            _currentSettlementSpeed = ConfiguredSpeedForPhase(_currentPacePhase);
        }

        private void RestoreSettlementPace()
        {
            _currentSettlementSpeed = 1f;
            _currentPacePhase = SettlementPacePhase.BelowTarget;
            _settlementDoubleSpeed = false;
        }

        private void AdvanceSettlementCue(SettlementPlaybackState playback, SettlementCueKind kind)
        {
            if (playback == null)
            {
                return;
            }

            float normalized = playback.CueCount <= 1
                ? 1f
                : Mathf.Clamp01((float)playback.CueIndex / (playback.CueCount - 1));
            playback.CueIndex++;

            NotifySettlementCue(kind, _currentSettlementSpeed, normalized);
        }

        private void PromoteSettlementPace(
            SettlementPlaybackState playback,
            SettlementPacePhase nextPhase)
        {
            if (playback == null || nextPhase <= _currentPacePhase)
            {
                return;
            }

            _currentPacePhase = nextPhase;
            _currentSettlementSpeed = ConfiguredSpeedForPhase(nextPhase);
            playback.ScoreFire?.SetPhase(nextPhase, _currentSettlementSpeed);
            playback.ScoreFire?.Burst(
                nextPhase == SettlementPacePhase.DoubleTarget ? 1f : 0.82f);
            _cameraFeedback?.SetPhase(
                nextPhase,
                _currentSettlementSpeed,
                playTransition: true);
        }

        private float ConfiguredSpeedForPhase(SettlementPacePhase phase)
        {
            float phaseSpeed = phase switch
            {
                SettlementPacePhase.TargetReached => _targetReachedSpeed,
                SettlementPacePhase.DoubleTarget => _doubleTargetSpeed,
                _ => _belowTargetSpeed,
            };
            return Mathf.Max(0.0001f, phaseSpeed) * (_settlementDoubleSpeed ? 2f : 1f);
        }

        internal static SettlementPacePhase ResolvePacePhase(
            BigDouble currentScore,
            int requiredScore,
            SettlementPacePhase currentPhase = SettlementPacePhase.BelowTarget)
        {
            if (requiredScore <= 0)
            {
                return currentPhase;
            }

            SettlementPacePhase resolved = currentScore >= (BigDouble)requiredScore * 2d
                ? SettlementPacePhase.DoubleTarget
                : currentScore >= requiredScore
                    ? SettlementPacePhase.TargetReached
                    : SettlementPacePhase.BelowTarget;
            return resolved > currentPhase ? resolved : currentPhase;
        }

        internal static float DefaultSpeedForPhase(
            SettlementPacePhase phase,
            bool doubleSpeed)
        {
            float speed = phase switch
            {
                SettlementPacePhase.TargetReached => 1.4f,
                SettlementPacePhase.DoubleTarget => 1.8f,
                _ => 1f,
            };
            return speed * (doubleSpeed ? 2f : 1f);
        }

        internal static float CameraFollowFactorFor(SettlementPacePhase phase)
        {
            return phase switch
            {
                SettlementPacePhase.TargetReached => 0.10f,
                SettlementPacePhase.DoubleTarget => 0.14f,
                _ => 0f,
            };
        }

        internal static float CameraImpactStrengthFor(
            SettlementPacePhase phase,
            SettlementImpactTier tier)
        {
            return phase switch
            {
                SettlementPacePhase.TargetReached => tier switch
                {
                    SettlementImpactTier.Strong => 0.025f,
                    SettlementImpactTier.Chain => 0.05f,
                    SettlementImpactTier.Finale => 0.06f,
                    _ => 0f,
                },
                SettlementPacePhase.DoubleTarget => tier switch
                {
                    SettlementImpactTier.Normal => 0.025f,
                    SettlementImpactTier.Strong => 0.055f,
                    SettlementImpactTier.Chain => 0.075f,
                    SettlementImpactTier.Finale => 0.09f,
                    _ => 0f,
                },
                _ => 0f,
            };
        }

        internal static float CameraTransitionShakeStrengthFor(SettlementPacePhase phase) =>
            phase == SettlementPacePhase.DoubleTarget ? 0.085f : 0f;

        internal static float CameraFinaleShakeStrengthFor(SettlementPacePhase phase) =>
            phase == SettlementPacePhase.DoubleTarget ? 0.08f : 0f;

        internal static float CameraHomeScaleFor(SettlementPacePhase phase)
        {
            return phase switch
            {
                SettlementPacePhase.DoubleTarget => 0.985f,
                _ => 1f,
            };
        }

        internal static float CameraFocusScaleFor(SettlementPacePhase phase)
        {
            return phase switch
            {
                SettlementPacePhase.TargetReached => 0.985f,
                SettlementPacePhase.DoubleTarget => 0.965f,
                _ => 1f,
            };
        }

        internal static float CameraTransitionPunchScaleFor(SettlementPacePhase phase) =>
            phase == SettlementPacePhase.DoubleTarget ? 0.978f : 1f;

        internal static float CameraFinaleScaleFor(SettlementPacePhase phase)
        {
            return phase switch
            {
                SettlementPacePhase.TargetReached => 0.992f,
                SettlementPacePhase.DoubleTarget => 0.97f,
                _ => 1f,
            };
        }

        private void NotifySettlementCue(SettlementCueKind kind, float speed, float normalized)
        {
            // 预留音效入口：后续可在这里按 kind 播放结算音效，并用 speed 映射 pitch。
        }

        private static void EmitReveal(Action<SettlementRevealSignal> onReveal, SettlementCue cue)
        {
            if (onReveal == null || cue == null || cue.Reveal.IsEmpty)
            {
                return;
            }

            onReveal(cue.Reveal);
        }

        private static void EmitPassiveTriggered(Action<string> onPassiveTriggered, SettlementCue cue)
        {
            if (onPassiveTriggered == null || cue == null || string.IsNullOrEmpty(cue.SourceItemId))
            {
                return;
            }

            onPassiveTriggered(cue.SourceItemId);
        }

        private static void EmitScope(Action<SettlementScopeSignal> onScope, SettlementScopeSignal scope)
        {
            if (onScope == null || scope.IsEmpty)
            {
                return;
            }

            onScope(scope);
        }

        /// <summary>
        /// 传递卡片现在统一在交接节拍揭示；计分明细不再重复增加卡片数量。
        /// 保留此入口以兼容旧 cue 构建路径。
        /// </summary>
        private static int SweetTransferCardDelta(ScoreSource source)
        {
            return 0;
        }

        internal static int CountNewTransferredSkills(
            SettlementSweetTransferPresentationContext context,
            DishPieceView executorView,
            SettlementBaselineSnapshot baselineSnapshot)
        {
            if (!context.IsValid || executorView?.Instance?.TransferredSkills == null)
            {
                return 0;
            }

            int baselineCount = baselineSnapshot != null
                && baselineSnapshot.TryGet(context.ExecutorDishInstanceId, out SettlementDishBaseline baseline)
                    ? baseline.TransferredSkillCount
                    : 0;
            IReadOnlyList<TransferredSkill> transferred = executorView.Instance.TransferredSkills;
            int count = 0;
            for (int i = Mathf.Clamp(baselineCount, 0, transferred.Count); i < transferred.Count; i++)
            {
                TransferredSkill entry = transferred[i];
                if (entry == null
                    || entry.SourceInstanceId != context.SourceDishInstanceId
                    || !string.Equals(
                        entry.Effect?.Rule?.SkillId,
                        context.SkillId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private static int CountSettlementCues(SettlementPlaybackPlan plan)
        {
            int count = 1 + CountStepBatches(plan?.Steps) + (plan?.FinalCues.Count ?? 0);
            return Mathf.Max(1, count);
        }

        private static int CountStepBatches(IReadOnlyList<SettlementPlaybackStep> steps)
        {
            if (steps == null || steps.Count == 0)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < steps.Count;)
            {
                CollectStepBatch(steps, i, out int nextIndex);
                count++;
                i = nextIndex;
            }

            return count;
        }

        private static IReadOnlyList<SettlementPlaybackStep> CollectStepBatch(
            IReadOnlyList<SettlementPlaybackStep> steps,
            int startIndex,
            out int nextIndex)
        {
            nextIndex = startIndex + 1;
            if (steps == null || startIndex < 0 || startIndex >= steps.Count)
            {
                return Array.Empty<SettlementPlaybackStep>();
            }

            SettlementPlaybackStep first = steps[startIndex];
            if (string.IsNullOrEmpty(first.Cue.BatchKey))
            {
                return new[] { first };
            }

            var result = new List<SettlementPlaybackStep> { first };
            var dishIds = new HashSet<int> { first.DishInstanceId };
            for (int i = startIndex + 1; i < steps.Count; i++)
            {
                SettlementPlaybackStep next = steps[i];
                if (!CanBatchStepWith(first, next) || !dishIds.Add(next.DishInstanceId))
                {
                    break;
                }

                result.Add(next);
                nextIndex = i + 1;
            }

            return result;
        }

        private static bool CanBatchStepWith(SettlementPlaybackStep first, SettlementPlaybackStep next)
        {
            return first?.Cue != null
                && next?.Cue != null
                && !string.IsNullOrEmpty(first.Cue.BatchKey)
                && string.Equals(first.Cue.BatchKey, next.Cue.BatchKey, StringComparison.Ordinal);
        }

        private async Awaitable TweenScoreAsync(BigDouble from, BigDouble to, float duration, Action<BigDouble> renderScore, CancellationToken cancellationToken)
        {
            if (renderScore == null)
            {
                return;
            }

            await TweenScoreWithPauseAsync(from, to, duration, renderScore, cancellationToken);
        }

        private async Awaitable TweenScoreWithPauseAsync(BigDouble from, BigDouble to, float duration, Action<BigDouble> renderScore, CancellationToken cancellationToken)
        {
            float clampedDuration = Mathf.Max(0.0001f, duration);
            float elapsed = 0f;

            while (elapsed < clampedDuration)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsPlaybackPaused)
                {
                    elapsed = Mathf.Min(clampedDuration, elapsed + Time.unscaledDeltaTime);
                    float t = elapsed / clampedDuration;
                    renderScore(BigDouble.Round(from + (to - from) * t, MidpointRounding.AwayFromZero));
                }

                await Awaitable.NextFrameAsync(cancellationToken);
            }

            renderScore(BigDouble.Round(to, MidpointRounding.AwayFromZero));
        }

        private async Awaitable WaitWhilePlaybackPausedAsync(CancellationToken cancellationToken)
        {
            while (IsPlaybackPaused)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Awaitable.NextFrameAsync(cancellationToken);
            }
        }

        private void ApplyPlaybackPauseState()
        {
            if (IsPlaybackPaused)
            {
                if (!_playbackHasSavedTimeScale)
                {
                    _playbackSavedTimeScale = Time.timeScale;
                    _playbackHasSavedTimeScale = true;
                }

                Time.timeScale = 0f;
                return;
            }

            if (_playbackHasSavedTimeScale)
            {
                Time.timeScale = _playbackSavedTimeScale;
                _playbackHasSavedTimeScale = false;
            }
        }

        private void ResetPlaybackPauseState()
        {
            _externalPlaybackPaused = false;
            ApplyPlaybackPauseState();
        }

        internal void ForceRestorePlaybackTimeScale()
        {
            _externalPlaybackPaused = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _debugScorePaused = false;
            _debugScoreControlsActive = false;
#endif
            ApplyPlaybackPauseState();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD

        private async Awaitable MonitorDebugScorePauseAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    ToggleDebugScorePauseIfRequested();
                    await Awaitable.NextFrameAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // 结算结束或被打断时正常退出后台监听。
            }
        }

        private void ToggleDebugScorePauseIfRequested()
        {
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                SetDebugScorePaused(!_debugScorePaused);
            }
        }

        private void SetDebugScorePaused(bool paused)
        {
            if (_debugScorePaused == paused)
            {
                return;
            }

            _debugScorePaused = paused;
            ApplyPlaybackPauseState();
        }

        private void ClearDebugScorePauseState()
        {
            _debugScorePaused = false;
            _debugScoreControlsActive = false;
            ApplyPlaybackPauseState();
        }

        private GUIStyle GetDebugScoreOverlayStyle()
        {
            if (_debugScoreOverlayStyle != null)
            {
                return _debugScoreOverlayStyle;
            }

            _debugScoreOverlayStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(10, 10, 4, 4)
            };
            return _debugScoreOverlayStyle;
        }
#endif

        private static SettlementPlaybackPlan BuildSettlementPlaybackPlan(
            ScoreResult result,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            SettlementBaselineSnapshot baselineSnapshot)
        {
            var plan = new SettlementPlaybackPlan();
            bool hasGoldCue = false;
            bool hasLayerCue = false;
            bool hasFinalModifierCue = false;
            var shownDishBases = new HashSet<int>();
            AddInitialDishBaseBatch(plan, result.DishScores, dishViews, shownDishBases, baselineSnapshot);

            IReadOnlyList<ScoreLine> scoreLines = result.ScoreLines;
            for (int i = 0; i < scoreLines.Count; i++)
            {
                ScoreLine line = scoreLines[i];
                if (!TryBuildCue(line, out SettlementCue cue))
                {
                    continue;
                }

                AttachPassiveSource(line, cue);

                TrackCueFlags(
                    line,
                    ref hasGoldCue,
                    ref hasLayerCue,
                    ref hasFinalModifierCue);

                if (!string.IsNullOrEmpty(cue.BatchKey))
                {
                    List<PendingLineCue> cueBatch = CollectLineCueBatch(scoreLines, i, cue);
                    if (cueBatch.Count > 1)
                    {
                        for (int b = 1; b < cueBatch.Count; b++)
                        {
                            TrackCueFlags(
                                cueBatch[b].Line,
                                ref hasGoldCue,
                                ref hasLayerCue,
                                ref hasFinalModifierCue);
                        }

                        AddCueBatchSteps(plan, cueBatch, dishViews, shownDishBases, baselineSnapshot);
                        i += cueBatch.Count - 1;
                        continue;
                    }
                }

                AddCueStep(plan, line, cue, dishViews, shownDishBases, baselineSnapshot);
            }

            // 甜蜜传递的「卡片揭示」不单独补 cue，而是绑定在目标食物触发传递效果的那条明细上
            //（该明细来源名带 <甜蜜传递> 标签，见 SweetTransferCardDelta），做到触发即显示、时机与演出一致。

            if (!hasFinalModifierCue && HasFinalModifier(result))
            {
                plan.FinalCues.Add(BuildFinalSummaryCue(result));
            }

            if (!hasGoldCue && Mathf.Abs(result.GoldDelta) > 0.001f)
            {
                plan.FinalCues.Add(new SettlementCue(
                    SettlementCueKind.SideEffect,
                    FormatSigned(result.GoldDelta),
                    sourceName: "金币"));
            }

            if (!hasLayerCue && result.HappyCakeLayerDelta != 0)
            {
                plan.FinalCues.Add(new SettlementCue(
                    SettlementCueKind.SideEffect,
                    FormatSigned(result.HappyCakeLayerDelta),
                    sourceName: "蛋糕层数",
                    reveal: SettlementRevealSignal.CakeLayerReveal(result.HappyCakeLayerDelta)));
            }

            // if (result.PermanentFlatDeltas.Count > 0)
            // {
            //     plan.FinalCues.Add(new SettlementCue(SettlementCueKind.SideEffect, $"永久美味值 +{result.PermanentFlatDeltas.Count} 个食物"));
            // }

            // if (result.PermanentMultDeltas.Count > 0)
            // {
            //     plan.FinalCues.Add(new SettlementCue(SettlementCueKind.SideEffect, $"永久倍率 +{result.PermanentMultDeltas.Count} 个食物"));
            // }

            return plan;
        }

        private static void AddInitialDishBaseBatch(
            SettlementPlaybackPlan plan,
            IReadOnlyList<DishScore> dishScores,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            HashSet<int> shownDishBases,
            SettlementBaselineSnapshot baselineSnapshot)
        {
            if (plan == null || dishScores == null || dishViews == null || shownDishBases == null)
            {
                return;
            }

            for (int i = 0; i < dishScores.Count; i++)
            {
                DishScore dishScore = dishScores[i];
                if (dishScore == null
                    || shownDishBases.Contains(dishScore.DishInstanceId)
                    || !dishViews.TryGetValue(dishScore.DishInstanceId, out DishPieceView view)
                    || view == null
                    || view.Instance == null)
                {
                    continue;
                }

                shownDishBases.Add(dishScore.DishInstanceId);
                plan.Steps.Add(new SettlementPlaybackStep(
                    dishScore.DishInstanceId,
                    BuildDishBaseCue(view.Instance, baselineSnapshot, InitialDishBaseBatchKey),
                    BuildDishFocusSignal(view.Instance)));
            }
        }

        private static void TrackCueFlags(
            ScoreLine line,
            ref bool hasGoldCue,
            ref bool hasLayerCue,
            ref bool hasFinalModifierCue)
        {
            hasGoldCue |= line.Kind == ScoreLineKind.Gold;
            hasLayerCue |= line.Kind == ScoreLineKind.Layer;
            hasFinalModifierCue |= line.Kind == ScoreLineKind.FinalFlat
                || line.Kind == ScoreLineKind.FinalMultiplier;
        }

        private static List<PendingLineCue> CollectLineCueBatch(
            IReadOnlyList<ScoreLine> scoreLines,
            int startIndex,
            SettlementCue firstCue)
        {
            var result = new List<PendingLineCue>
            {
                new PendingLineCue(scoreLines[startIndex], firstCue)
            };
            var dishIds = new HashSet<int> { scoreLines[startIndex].DishInstanceId };

            for (int i = startIndex + 1; i < scoreLines.Count; i++)
            {
                ScoreLine line = scoreLines[i];
                if (!TryBuildCue(line, out SettlementCue cue)
                    || !string.Equals(firstCue.BatchKey, cue.BatchKey, StringComparison.Ordinal)
                    || !dishIds.Add(line.DishInstanceId))
                {
                    break;
                }

                AttachPassiveSource(line, cue);
                result.Add(new PendingLineCue(line, cue));
            }

            return result;
        }

        private static void AddCueBatchSteps(
            SettlementPlaybackPlan plan,
            IReadOnlyList<PendingLineCue> cueBatch,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            HashSet<int> shownDishBases,
            SettlementBaselineSnapshot baselineSnapshot)
        {
            if (!CanAddAsDishCueBatch(cueBatch, dishViews))
            {
                for (int i = 0; i < cueBatch.Count; i++)
                {
                    AddCueStep(plan, cueBatch[i].Line, cueBatch[i].Cue, dishViews, shownDishBases, baselineSnapshot);
                }

                return;
            }

            string baseBatchKey = $"base:{cueBatch[0].Cue.BatchKey}";
            for (int i = 0; i < cueBatch.Count; i++)
            {
                ScoreLine line = cueBatch[i].Line;
                SettlementCue cue = cueBatch[i].Cue;
                dishViews.TryGetValue(line.DishInstanceId, out DishPieceView view);

                if (cue.ValueChange.Kind != DishValueChangeKind.None
                    && !shownDishBases.Contains(line.DishInstanceId))
                {
                    SettlementScopeSignal effectScope = SettlementScopeSignal.FromScoreLine(line);
                    plan.Steps.Add(new SettlementPlaybackStep(
                        line.DishInstanceId,
                        BuildDishBaseCue(view.Instance, baselineSnapshot, baseBatchKey),
                        BuildDishFocusSignal(view.Instance, effectScope.OwnerDishInstanceId)));
                    shownDishBases.Add(line.DishInstanceId);
                }
            }

            for (int i = 0; i < cueBatch.Count; i++)
            {
                AddCueStep(plan, cueBatch[i].Line, cueBatch[i].Cue, dishViews, shownDishBases, baselineSnapshot);
            }
        }

        private static bool CanAddAsDishCueBatch(
            IReadOnlyList<PendingLineCue> cueBatch,
            IReadOnlyDictionary<int, DishPieceView> dishViews)
        {
            if (cueBatch == null || cueBatch.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < cueBatch.Count; i++)
            {
                ScoreLine line = cueBatch[i].Line;
                SettlementCue cue = cueBatch[i].Cue;
                if (line.DishInstanceId == 0
                    || cue.Kind == SettlementCueKind.FinalModifier
                    || !dishViews.TryGetValue(line.DishInstanceId, out DishPieceView view)
                    || view == null)
                {
                    return false;
                }
            }

            return true;
        }

        private static void AddCueStep(
            SettlementPlaybackPlan plan,
            ScoreLine line,
            SettlementCue cue,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            HashSet<int> shownDishBases,
            SettlementBaselineSnapshot baselineSnapshot)
        {
            if (line.DishInstanceId != 0
                && dishViews.TryGetValue(line.DishInstanceId, out DishPieceView view)
                && view != null
                && cue.Kind != SettlementCueKind.FinalModifier)
            {
                if (cue.ValueChange.Kind == DishValueChangeKind.Base)
                {
                    if (shownDishBases.Contains(line.DishInstanceId))
                    {
                        return;
                    }

                    shownDishBases.Add(line.DishInstanceId);
                }
                else if (cue.ValueChange.Kind != DishValueChangeKind.None
                    && !shownDishBases.Contains(line.DishInstanceId))
                {
                    SettlementScopeSignal effectScope = SettlementScopeSignal.FromScoreLine(line);
                    plan.Steps.Add(new SettlementPlaybackStep(
                        line.DishInstanceId,
                        BuildDishBaseCue(view.Instance, baselineSnapshot),
                        BuildDishFocusSignal(view.Instance, effectScope.OwnerDishInstanceId)));
                    shownDishBases.Add(line.DishInstanceId);
                }

                plan.Steps.Add(new SettlementPlaybackStep(line.DishInstanceId, cue, SettlementScopeSignal.FromScoreLine(line)));
            }
            else
            {
                plan.FinalCues.Add(cue);
            }
        }

        private static SettlementCue BuildDishBaseCue(
            DishInstance instance,
            SettlementBaselineSnapshot baselineSnapshot,
            string batchKey = null)
        {
            BigDouble baseScore = BigDouble.Zero;
            if (instance != null)
            {
                baseScore = baselineSnapshot != null && baselineSnapshot.TryGet(instance.Id, out SettlementDishBaseline baseline)
                    ? baseline.BaseScore
                    : instance.BaseScoreBeforeSettlement;
            }

            return new SettlementCue(
                SettlementCueKind.Source,
                $"{Strong("分数")} {Semantic("score", FormatSigned(baseScore))}",
                feedbackKind: SettlementDishFeedbackKind.DishBase,
                valueChange: DishValueChange.Base(baseScore),
                batchKey: batchKey,
                sourceName: instance?.Def?.Name,
                showEffectLabel: false);
        }

        private static SettlementScopeSignal BuildDishFocusSignal(DishInstance instance, int ownerDishInstanceId = 0)
        {
            return instance != null
                ? new SettlementScopeSignal(
                    ownerDishInstanceId > 0 ? ownerDishInstanceId : instance.Id,
                    instance.Id,
                    null)
                : default;
        }

        private static bool TryBuildCue(ScoreLine line, out SettlementCue cue)
        {
            cue = null;
            if (line == null)
            {
                return false;
            }

            bool isExecutedDishSkill = line.Trace != null
                || line.Source?.Type == ScoreSourceType.DishSkill;
            if (BigDouble.Abs(line.Value) <= 0.001f && !isExecutedDishSkill)
            {
                return false;
            }

            string sourceName = SourceName(line);
            switch (line.Kind)
            {
                case ScoreLineKind.DishBase:
                    cue = new SettlementCue(
                        SettlementCueKind.Source,
                        $"{Strong("分数")} {Semantic("score", FormatSigned(line.Value))}",
                        feedbackKind: SettlementDishFeedbackKind.DishBase,
                        valueChange: DishValueChange.Base(line.After),
                        sourceName: sourceName,
                        showEffectLabel: false);
                    return true;

                case ScoreLineKind.DishFlat:
                    if (!IsReadableDishSource(line.Source))
                    {
                        return false;
                    }

                    cue = new SettlementCue(
                        SettlementCueKind.Source,
                        $"{Strong("分数")} {Semantic("score", FormatSigned(line.Value))}",
                        feedbackKind: BuildDishFeedbackKind(line),
                        reveal: SettlementRevealSignal.FlatReveal(line.DishInstanceId, line.After, SweetTransferCardDelta(line.Source)),
                        valueChange: DishValueChange.FlatBonus(line.After),
                        batchKey: BuildDishSkillBatchKey(line),
                        sourceName: sourceName);
                    return true;

                case ScoreLineKind.DishPermanentFlat:
                    if (!IsReadableDishSource(line.Source))
                    {
                        return false;
                    }

                    cue = new SettlementCue(
                        SettlementCueKind.Source,
                        $"{Strong("分数")} · 永久{Semantic("score", FormatSigned(line.Value))}",
                        rise: 0.28f,
                        duration: 0.82f,
                        feedbackKind: SettlementDishFeedbackKind.PermanentFlatBonus,
                        reveal: SettlementRevealSignal.FlatReveal(line.DishInstanceId, line.After, SweetTransferCardDelta(line.Source)),
                        valueChange: DishValueChange.FlatBonus(line.After),
                        batchKey: BuildDishSkillBatchKey(line),
                        sourceName: sourceName,
                        effectColor: SettlementColorPalette.PermanentScore);
                    return true;

                case ScoreLineKind.DishMultiplier:
                    cue = new SettlementCue(
                        SettlementCueKind.Source,
                        $"{Strong("倍率")} {Semantic("multmul", FormatMultiplier(line.Value))}",
                        feedbackKind: BuildDishFeedbackKind(line),
                        reveal: SettlementRevealSignal.MultiplierReveal(line.DishInstanceId, line.After, SweetTransferCardDelta(line.Source)),
                        valueChange: DishValueChange.Multiplier(line.After),
                        batchKey: BuildDishSkillBatchKey(line),
                        sourceName: sourceName);
                    return true;

                case ScoreLineKind.DishMultiplierAdd:
                    cue = new SettlementCue(
                        SettlementCueKind.Source,
                        $"{Strong("倍率")} {Semantic("multadd", FormatSigned(line.Value))}",
                        feedbackKind: BuildDishFeedbackKind(line),
                        reveal: SettlementRevealSignal.MultiplierReveal(line.DishInstanceId, line.After, SweetTransferCardDelta(line.Source)),
                        valueChange: DishValueChange.Multiplier(line.After),
                        batchKey: BuildDishSkillBatchKey(line),
                        sourceName: sourceName);
                    return true;

                case ScoreLineKind.ExtraSettlement:
                    cue = new SettlementCue(
                        SettlementCueKind.Source,
                        Semantic("benefit", "额外结算"),
                        rise: 0.4f,
                        duration: 0.9f,
                        feedbackKind: SettlementDishFeedbackKind.GenericValueChanged,
                        valueChange: DishValueChange.ExtraContribution(line.After),
                        sourceName: sourceName,
                        effectColor: SettlementColorPalette.BaseScore);
                    return true;

                case ScoreLineKind.FinalFlat:
                    cue = new SettlementCue(
                        SettlementCueKind.FinalModifier,
                        $"{Strong("分数")} {Semantic("score", FormatSigned(line.Value))}",
                        rise: 0.7f,
                        duration: 1.1f,
                        sourceName: sourceName);
                    return true;

                case ScoreLineKind.FinalMultiplier:
                    cue = new SettlementCue(
                        SettlementCueKind.FinalModifier,
                        $"{Strong("倍率")} {Semantic("multmul", FormatMultiplier(line.Value))}",
                        rise: 0.7f,
                        duration: 1.1f,
                        sourceName: sourceName);
                    return true;

                case ScoreLineKind.Gold:
                    cue = new SettlementCue(
                        SettlementCueKind.SideEffect,
                        Semantic("gold", FormatSigned(line.Value)),
                        sourceName: sourceName);
                    return true;

                case ScoreLineKind.Layer:
                    cue = new SettlementCue(
                        SettlementCueKind.SideEffect,
                        FormatSigned(line.Value),
                        sourceName: sourceName,
                        reveal: SettlementRevealSignal.CakeLayerReveal(RoundCount(line.Value)));
                    return true;

                case ScoreLineKind.CopySkill:
                    cue = new SettlementCue(
                        SettlementCueKind.SideEffect,
                        $"获得技能 ×{RoundCount(line.Value)}",
                        feedbackKind: SettlementDishFeedbackKind.CopySkillTriggered,
                        reveal: SettlementRevealSignal.CopySkillReveal(line.DishInstanceId, RoundCount(line.Value)),
                        sourceName: sourceName);
                    return true;

                case ScoreLineKind.TriggerSweetTransfer:
                    cue = new SettlementCue(
                        SettlementCueKind.SideEffect,
                        line.Value > 1f
                            ? $"发动甜蜜传递 ×{RoundCount(line.Value)}"
                            : "发动甜蜜传递",
                        feedbackKind: SettlementDishFeedbackKind.GenericSkillTriggered,
                        triggerSweetTransferPhase: TriggerSweetTransferCuePhase.ActivatorStarted,
                        sourceName: sourceName);
                    return true;

                case ScoreLineKind.TriggeredSweetTransferSource:
                    cue = new SettlementCue(
                        SettlementCueKind.SideEffect,
                        string.Empty,
                        feedbackKind: SettlementDishFeedbackKind.SweetTransferSkillTriggered,
                        triggerSweetTransferPhase: line.Value >= line.After
                            ? TriggerSweetTransferCuePhase.FinalSourceStarted
                            : TriggerSweetTransferCuePhase.SourceStarted,
                        sourceName: sourceName,
                        showEffectLabel: false);
                    return true;

                case ScoreLineKind.SweetTransferBuffApplied:
                    cue = new SettlementCue(
                        SettlementCueKind.SideEffect,
                        SettlementStageView.SweetTransferBuffName(line),
                        feedbackKind: SettlementDishFeedbackKind.GenericSkillTriggered,
                        sourceName: sourceName);
                    return true;

                case ScoreLineKind.SweetTransferBuffTriggered:
                    cue = new SettlementCue(
                        SettlementCueKind.SideEffect,
                        SettlementStageView.ResultText(
                            line,
                            BigDouble.Zero,
                            BigDouble.Zero),
                        feedbackKind: SettlementDishFeedbackKind.GenericSkillTriggered,
                        sourceName: sourceName);
                    return true;

                case ScoreLineKind.SweetTransferFailed:
                    cue = new SettlementCue(
                        SettlementCueKind.SideEffect,
                        "没有可传递目标",
                        feedbackKind: SettlementDishFeedbackKind.SweetTransferFailed,
                        sourceName: sourceName);
                    return true;

                case ScoreLineKind.CountAs:
                    cue = new SettlementCue(
                        SettlementCueKind.SideEffect,
                        FormatSigned(line.Value),
                        rise: 0.34f,
                        duration: 0.84f,
                        // 效果组开场已由蛋黄酥播放主动发动；目标这里只播放数值变化反馈。
                        feedbackKind: SettlementDishFeedbackKind.GenericValueChanged,
                        reveal: SettlementRevealSignal.CountAsReveal(
                            line.DishInstanceId,
                            RoundCount(line.After)),
                        batchKey: BuildDishSkillBatchKey(line),
                        sourceName: sourceName,
                        effectColor: SettlementColorPalette.CountAs);
                    return true;

                case ScoreLineKind.EmptyCountAs:
                    cue = new SettlementCue(
                        SettlementCueKind.SideEffect,
                        FormatSigned(line.Value),
                        rise: 0.34f,
                        duration: 0.84f,
                        feedbackKind: SettlementDishFeedbackKind.CountAsChanged,
                        sourceName: sourceName,
                        effectColor: SettlementColorPalette.CountAs);
                    return true;

                case ScoreLineKind.TemporaryCategory:
                    cue = new SettlementCue(
                        SettlementCueKind.SideEffect,
                        NormalizeTemporaryCategoryText(line.Message),
                        rise: 0.38f,
                        duration: 0.92f,
                        feedbackKind: BuildDishFeedbackKind(line),
                        reveal: SettlementRevealSignal.TemporaryEffectReveal(line.DishInstanceId),
                        batchKey: BuildDishSkillBatchKey(line),
                        sourceName: sourceName,
                        effectColor: SettlementColorPalette.TemporaryCategory);
                    return true;

                default:
                    return false;
            }
        }

        private static string NormalizeTemporaryCategoryText(string message)
        {
            string text = string.IsNullOrWhiteSpace(message) ? "视为蛋糕" : message.Trim();
            return text.StartsWith("本场", StringComparison.Ordinal)
                ? text.Substring(2).TrimStart()
                : text;
        }

        private static void AttachPassiveSource(ScoreLine line, SettlementCue cue)
        {
            if (cue == null || line?.Source?.Type != ScoreSourceType.Relic)
            {
                return;
            }

            cue.SetSourceItemId(line.Source.Id);
        }

        private static string BuildDishSkillBatchKey(ScoreLine line)
        {
            ScoreSource source = line?.Source;
            if (source == null
                || source.Type != ScoreSourceType.DishSkill)
            {
                return null;
            }

            return string.Join(
                "|",
                line.Phase,
                line.Kind,
                source.Id,
                source.Name,
                source.DishInstanceId.ToString(CultureInfo.InvariantCulture),
                line.Value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static SettlementDishFeedbackKind BuildDishFeedbackKind(ScoreLine line)
        {
            if (line == null)
            {
                return SettlementDishFeedbackKind.None;
            }

            if (line.Kind == ScoreLineKind.CopySkill)
            {
                return SettlementDishFeedbackKind.CopySkillTriggered;
            }

            if (line.Kind == ScoreLineKind.TriggerSweetTransfer)
            {
                return SettlementDishFeedbackKind.GenericSkillTriggered;
            }

            if (line.Trace != null && line.Trace.Kind == SkillExecutionKind.CopiedSkill)
            {
                return SettlementDishFeedbackKind.CopiedSkillTriggered;
            }

            bool isDishSkill = line.Trace != null || line.Source?.Type == ScoreSourceType.DishSkill;
            if (!isDishSkill)
            {
                return SettlementDishFeedbackKind.GenericValueChanged;
            }

            bool active = IsActiveDishSkillTarget(line);
            switch (line.Kind)
            {
                case ScoreLineKind.DishFlat:
                    return active
                        ? SettlementDishFeedbackKind.ActiveFlatBonus
                        : SettlementDishFeedbackKind.PassiveFlatBonus;
                case ScoreLineKind.DishPermanentFlat:
                    return SettlementDishFeedbackKind.PermanentFlatBonus;
                case ScoreLineKind.DishMultiplier:
                    return active
                        ? SettlementDishFeedbackKind.ActiveMultiplier
                        : SettlementDishFeedbackKind.PassiveMultiplier;
                case ScoreLineKind.DishMultiplierAdd:
                    return active
                        ? SettlementDishFeedbackKind.ActiveMultiplierAdd
                        : SettlementDishFeedbackKind.PassiveMultiplierAdd;
                case ScoreLineKind.CountAs:
                    return SettlementDishFeedbackKind.GenericValueChanged;
                case ScoreLineKind.EmptyCountAs:
                    return SettlementDishFeedbackKind.CountAsChanged;
                case ScoreLineKind.TemporaryCategory:
                    return active
                        ? SettlementDishFeedbackKind.GenericSkillTriggered
                        : SettlementDishFeedbackKind.GenericValueChanged;
                default:
                    return active
                        ? SettlementDishFeedbackKind.GenericSkillTriggered
                        : SettlementDishFeedbackKind.GenericValueChanged;
            }
        }

        private static bool IsActiveDishSkillTarget(ScoreLine line)
        {
            if (line == null || line.DishInstanceId == 0)
            {
                return false;
            }

            int runtimeSelfId = line.Trace != null
                ? line.Trace.RuntimeSelfDishInstanceId
                : line.Source?.DishInstanceId ?? 0;
            return runtimeSelfId != 0 && runtimeSelfId == line.DishInstanceId;
        }

        private static bool IsReadableDishSource(ScoreSource source)
        {
            if (source == null)
            {
                return false;
            }

            return source.Type == ScoreSourceType.DishSkill
                || source.Type == ScoreSourceType.DishFlavor
                || source.Type == ScoreSourceType.TableTag
                || source.Type == ScoreSourceType.Relic;
        }

        private static string SourceName(ScoreLine line)
        {
            if (line?.Source == null)
            {
                return "结算";
            }

            if (!string.IsNullOrEmpty(line.Source.Name))
            {
                return line.Source.Name;
            }

            return string.IsNullOrEmpty(line.Source.Id) ? "结算" : line.Source.Id;
        }

        private static bool HasFinalModifier(ScoreResult result)
        {
            return BigDouble.Abs(result.FinalFlat) > 0.001f
                || BigDouble.Abs(result.FinalMultiplier - 1f) > 0.001f;
        }

        private static SettlementCue BuildFinalSummaryCue(ScoreResult result)
        {
            string summary = string.Empty;
            if (BigDouble.Abs(result.FinalMultiplier - 1f) > 0.001f)
            {
                summary += Semantic("multmul", FormatMultiplier(result.FinalMultiplier));
            }

            if (BigDouble.Abs(result.FinalFlat) > 0.001f)
            {
                if (summary.Length > 0)
                {
                    summary += "  ";
                }

                summary += Semantic("score", FormatSigned(result.FinalFlat));
            }

            return new SettlementCue(
                SettlementCueKind.FinalModifier,
                summary,
                rise: 0.7f,
                duration: 1.1f,
                sourceName: "局加成");
        }

        private static string FormatSigned(BigDouble value)
        {
            return $"{(value >= 0f ? "+" : string.Empty)}{FormatPlain(value)}";
        }

        private static string FormatMultiplier(BigDouble value)
        {
            return $"×{FormatPlain(value)}";
        }

        private static string FormatPlain(BigDouble value)
        {
            return BigDouble.Abs(value) < ScoreNumberFormatter.ScientificThreshold
                ? value.ToString("G3")
                : ScoreNumberFormatter.Format(value);
        }

        private static string Strong(string value) => $"[strong]{value}[/strong]";

        private static string Semantic(string tag, string value)
            => $"[{tag}]{value}[/{tag}]";

        private static int RoundCount(BigDouble value)
        {
            return (int)Math.Round(value.ToDouble(), MidpointRounding.AwayFromZero);
        }

        private static Vector3 DishValueAnchor(
            DishPieceView view,
            DiningTableCoordinateMapper mapper)
        {
            return view != null
                ? view.DishValueBadgeWorldPosition
                : mapper.Center;
        }

        private Vector3 DishFloatingAnchor(Vector3 dishValueAnchor, DiningTableCoordinateMapper mapper)
        {
            Vector3 offset = new(
                0f,
                _dishFloatingVerticalOffset * _visualScale,
                0f);
            if (mapper.Root != null)
            {
                offset = mapper.Root.TransformVector(offset);
            }

            return dishValueAnchor + offset;
        }

        private sealed class SettlementPlaybackPlan
        {
            public List<SettlementPlaybackStep> Steps { get; } = new();

            public List<SettlementCue> FinalCues { get; } = new();
        }

        private readonly struct SweetTransferVisualContext
        {
            public SweetTransferVisualContext(
                int sourceDishInstanceId,
                int receiverDishInstanceId,
                bool playSourceIntro = true)
            {
                SourceDishInstanceId = sourceDishInstanceId;
                ReceiverDishInstanceId = receiverDishInstanceId;
                PlaySourceIntro = playSourceIntro;
            }

            public int SourceDishInstanceId { get; }

            public int ReceiverDishInstanceId { get; }

            public bool PlaySourceIntro { get; }
        }

        private readonly struct ResultLineRef
        {
            public ResultLineRef(SettlementEffectGroup group, int lineIndex)
            {
                Group = group;
                LineIndex = lineIndex;
            }

            public SettlementEffectGroup Group { get; }

            public int LineIndex { get; }

            public ScoreLine Line => Group.Lines[LineIndex];
        }

        private sealed class SweetTransferPlaybackState
        {
            public int SourceDishInstanceId { get; set; }

            public int ReceiverDishInstanceId { get; set; }

            public int ExecutorDishInstanceId { get; set; }

            public SettlementSweetTransferPresentationKey PresentationKey { get; set; }

            public int TriggerSweetTransferActivatorDishInstanceId { get; set; }

            public int TriggerSweetTransferFinalSourceDishInstanceId { get; set; }
        }

        private sealed class SettlementPlaybackStep
        {
            public SettlementPlaybackStep(int dishInstanceId, SettlementCue cue, SettlementScopeSignal scope = default)
            {
                DishInstanceId = dishInstanceId;
                Cue = cue;
                Scope = scope;
            }

            public int DishInstanceId { get; }

            public SettlementCue Cue { get; }

            public SettlementScopeSignal Scope { get; }
        }

        private sealed class SettlementCue
        {
            public SettlementCue(
                SettlementCueKind kind,
                string text,
                float rise = SourceCueRise,
                float duration = SourceCueDuration,
                SettlementDishFeedbackKind feedbackKind = SettlementDishFeedbackKind.GenericValueChanged,
                SettlementRevealSignal reveal = default,
                DishValueChange valueChange = default,
                string batchKey = null,
                TriggerSweetTransferCuePhase triggerSweetTransferPhase = TriggerSweetTransferCuePhase.None,
                string sourceName = null,
                bool showEffectLabel = true,
                Color? effectColor = null)
            {
                Kind = kind;
                Text = text;
                SourceName = sourceName ?? string.Empty;
                Rise = rise;
                Duration = duration;
                FeedbackKind = feedbackKind;
                Reveal = reveal;
                ValueChange = valueChange;
                BatchKey = batchKey;
                TriggerSweetTransferPhase = triggerSweetTransferPhase;
                ShowEffectLabel = showEffectLabel;
                EffectColor = effectColor;
            }

            public SettlementCueKind Kind { get; }

            public string Text { get; }

            public string SourceName { get; }

            public string SourceItemId { get; private set; }

            public void SetSourceItemId(string itemId)
            {
                SourceItemId = itemId ?? string.Empty;
            }

            public float Rise { get; }

            public float Duration { get; }

            public SettlementDishFeedbackKind FeedbackKind { get; }

            /// <summary>该 cue 播放时对 tips 发出的渐进揭示信号（默认 None）。</summary>
            public SettlementRevealSignal Reveal { get; }

            public DishValueChange ValueChange { get; }

            public string BatchKey { get; }

            public TriggerSweetTransferCuePhase TriggerSweetTransferPhase { get; }

            public bool ShowEffectLabel { get; }

            public Color? EffectColor { get; }
        }

        private sealed class PendingLineCue
        {
            public PendingLineCue(ScoreLine line, SettlementCue cue)
            {
                Line = line;
                Cue = cue;
            }

            public ScoreLine Line { get; }

            public SettlementCue Cue { get; }
        }

        private enum DishValueChangeKind
        {
            None = 0,
            Base = 1,
            FlatBonus = 2,
            Multiplier = 3,
            ExtraContribution = 4,
        }

        private readonly struct DishValueChange
        {
            private DishValueChange(DishValueChangeKind kind, BigDouble value)
            {
                Kind = kind;
                Value = value;
            }

            public DishValueChangeKind Kind { get; }

            public BigDouble Value { get; }

            public static DishValueChange Base(BigDouble value)
            {
                return new DishValueChange(DishValueChangeKind.Base, value);
            }

            public static DishValueChange FlatBonus(BigDouble value)
            {
                return new DishValueChange(DishValueChangeKind.FlatBonus, value);
            }

            public static DishValueChange Multiplier(BigDouble value)
            {
                return new DishValueChange(DishValueChangeKind.Multiplier, value);
            }

            public static DishValueChange ExtraContribution(BigDouble value)
            {
                return new DishValueChange(DishValueChangeKind.ExtraContribution, value);
            }
        }

        private sealed class DishValuePlaybackAccumulator
        {
            public DishValuePlaybackAccumulator(BigDouble baseScore, BigDouble multiplier)
            {
                BaseScore = baseScore;
                Multiplier = multiplier;
            }

            private BigDouble BaseScore { get; set; }

            private BigDouble FlatBonus { get; set; }

            private BigDouble Multiplier { get; set; }

            private BigDouble ExtraContribution { get; set; }

            public BigDouble Contribution => DishScore.CeilContribution(BaseScore + FlatBonus, Multiplier)
                + ExtraContribution;

            public void Apply(DishValueChange change)
            {
                switch (change.Kind)
                {
                    case DishValueChangeKind.Base:
                        BaseScore = change.Value;
                        break;
                    case DishValueChangeKind.FlatBonus:
                        FlatBonus = change.Value;
                        break;
                    case DishValueChangeKind.Multiplier:
                        Multiplier = change.Value;
                        break;
                    case DishValueChangeKind.ExtraContribution:
                        ExtraContribution = change.Value;
                        break;
                }
            }
        }

        /// <summary>
        /// 只叠加在餐桌世界相机上的结算镜头语言。左右 UGUI 不经过该相机，
        /// 因此推拉和微震不会牺牲 HUD 可读性。
        /// </summary>
        private sealed class SettlementCameraFeedback
        {
            private Camera _camera;
            private Vector3 _basePosition;
            private Vector3 _focusPosition;
            private Vector3 _tableCenter;
            private float _baseOrthographicSize;
            private float _phaseHomeOrthographicSize;
            private float _focusOrthographicSize;
            private SettlementPacePhase _phase;
            private float _speed = 1f;
            private Tween _motion;
            private bool _active;

            public void Begin(Camera camera, Vector3 tableCenter)
            {
                RestoreImmediate();
                _camera = camera;
                if (_camera == null)
                {
                    return;
                }

                _active = true;
                _basePosition = _camera.transform.position;
                _focusPosition = _basePosition;
                _tableCenter = tableCenter;
                _baseOrthographicSize = _camera.orthographicSize;
                _phaseHomeOrthographicSize = _baseOrthographicSize;
                _focusOrthographicSize = _baseOrthographicSize;
            }

            public void SetPhase(
                SettlementPacePhase phase,
                float effectiveSpeed,
                bool playTransition)
            {
                _phase = phase;
                _speed = Mathf.Max(0.0001f, effectiveSpeed);
                if (!_active || _camera == null)
                {
                    return;
                }

                _phaseHomeOrthographicSize = _baseOrthographicSize * CameraHomeScaleFor(phase);
                _focusOrthographicSize = _baseOrthographicSize * CameraFocusScaleFor(phase);
                if (!playTransition || phase == SettlementPacePhase.BelowTarget)
                {
                    SetOrthographicSize(_phaseHomeOrthographicSize);
                    return;
                }

                _motion?.Kill();
                float strength = CameraTransitionShakeStrengthFor(phase);
                float punchScale = CameraTransitionPunchScaleFor(phase);
                if (strength <= 0f && Mathf.Approximately(punchScale, 1f))
                {
                    return;
                }

                float transitionDuration = Scaled(0.16f);
                float punchSize = _focusOrthographicSize * punchScale;
                Sequence sequence = DOTween.Sequence();
                Tween punchTween = DOVirtual.Float(
                        _camera.orthographicSize,
                        punchSize,
                        transitionDuration * 0.55f,
                        SetOrthographicSize)
                    .SetEase(Ease.OutQuad);
                if (strength > 0f)
                {
                    sequence.Append(_camera.transform.DOShakePosition(
                        transitionDuration,
                        strength,
                        vibrato: 12,
                        randomness: 30f,
                        snapping: false,
                        fadeOut: true));
                    sequence.Join(punchTween);
                }
                else
                {
                    sequence.Append(punchTween);
                }

                sequence
                    .Append(DOVirtual.Float(
                            _camera.orthographicSize,
                            _focusOrthographicSize,
                            transitionDuration * 0.45f,
                            SetOrthographicSize)
                        .SetEase(Ease.OutBack))
                    .Append(_camera.transform.DOMove(
                        _focusPosition,
                        Scaled(0.045f)).SetEase(Ease.OutQuad))
                    .SetLink(_camera.gameObject);
                _motion = sequence.OnComplete(() => _motion = null);
            }

            public void FocusSource(Vector3 worldPosition, float duration)
            {
                if (!_active || _camera == null)
                {
                    return;
                }

                Vector3 towardSource = Vector3.ClampMagnitude(worldPosition - _tableCenter, 1.6f);
                towardSource.z = 0f;
                _focusPosition = _basePosition + towardSource * CameraFollowFactorFor(_phase);
                _focusOrthographicSize = _baseOrthographicSize * CameraFocusScaleFor(_phase);
                TweenTo(
                    _focusPosition,
                    _focusOrthographicSize,
                    Mathf.Clamp(duration * 0.72f, 0.04f, 0.24f),
                    Ease.OutCubic);
            }

            public void PlayImpact(SettlementImpactTier tier, int targetCount)
            {
                if (!_active || _camera == null)
                {
                    return;
                }

                float strength = CameraImpactStrengthFor(_phase, tier);
                if (strength <= 0f)
                {
                    return;
                }

                if (targetCount > 1)
                {
                    strength = Mathf.Min(0.12f, strength * 1.18f);
                }

                _motion?.Kill();
                float pulseSize = _focusOrthographicSize * (tier >= SettlementImpactTier.Chain ? 0.982f : 0.990f);
                Sequence sequence = DOTween.Sequence()
                    .Append(DOVirtual.Float(
                            _camera.orthographicSize,
                            pulseSize,
                            Scaled(0.07f),
                            value => SetOrthographicSize(value))
                        .SetEase(Ease.OutQuad))
                    .Join(_camera.transform.DOShakePosition(
                        Scaled(0.14f),
                        strength,
                        vibrato: tier >= SettlementImpactTier.Chain ? 12 : 8,
                        randomness: 38f,
                        snapping: false,
                        fadeOut: true))
                    .Append(DOVirtual.Float(
                            pulseSize,
                            _focusOrthographicSize,
                            Scaled(0.08f),
                            value => SetOrthographicSize(value))
                        .SetEase(Ease.OutCubic))
                    .Append(_camera.transform.DOMove(
                        _focusPosition,
                        Scaled(0.05f)).SetEase(Ease.OutQuad))
                    .SetLink(_camera.gameObject);
                _motion = sequence.OnComplete(() => _motion = null);
            }

            public void ReturnHome(float duration)
            {
                if (!_active || _camera == null)
                {
                    return;
                }

                _focusPosition = _basePosition;
                _focusOrthographicSize = _phaseHomeOrthographicSize;
                TweenTo(
                    _basePosition,
                    _phaseHomeOrthographicSize,
                    Mathf.Clamp(duration, 0.04f, 0.20f),
                    Ease.OutCubic);
            }

            public void PlayFinale(float duration)
            {
                if (!_active || _camera == null)
                {
                    return;
                }

                _motion?.Kill();
                float pushDuration = Mathf.Clamp(duration * 0.22f, 0.05f, 0.22f);
                float restoreDuration = Mathf.Clamp(duration * 0.34f, 0.08f, 0.36f);
                float finaleStrength = CameraFinaleShakeStrengthFor(_phase);
                float finaleScale = CameraFinaleScaleFor(_phase);
                Sequence sequence = DOTween.Sequence()
                    .Append(_camera.transform.DOMove(_basePosition, pushDuration).SetEase(Ease.OutCubic))
                    .Join(DOVirtual.Float(
                            _camera.orthographicSize,
                            _baseOrthographicSize * finaleScale,
                            pushDuration,
                            value => SetOrthographicSize(value))
                        .SetEase(Ease.OutCubic));
                if (finaleStrength > 0f)
                {
                    sequence.Append(_camera.transform.DOShakePosition(
                        Mathf.Max(0.04f, duration * 0.17f),
                        finaleStrength,
                        vibrato: 12,
                        randomness: 28f,
                        snapping: false,
                        fadeOut: true));
                }

                sequence
                    .Append(_camera.transform.DOMove(_basePosition, restoreDuration).SetEase(Ease.OutBack))
                    .Join(DOVirtual.Float(
                            _camera.orthographicSize,
                            _baseOrthographicSize,
                            restoreDuration,
                            value => SetOrthographicSize(value))
                        .SetEase(Ease.OutBack))
                    .SetLink(_camera.gameObject);
                _motion = sequence.OnComplete(() =>
                {
                    RestoreValues();
                    _motion = null;
                });
            }

            public void RestoreImmediate()
            {
                _motion?.Kill();
                _motion = null;
                if (_active && _camera != null)
                {
                    RestoreValues();
                }

                _active = false;
                _camera = null;
                _phase = SettlementPacePhase.BelowTarget;
                _speed = 1f;
            }

            private float Scaled(float duration) =>
                Mathf.Max(0.015f, duration / Mathf.Max(0.0001f, _speed));

            private void TweenTo(Vector3 position, float orthographicSize, float duration, Ease ease)
            {
                _motion?.Kill();
                Sequence sequence = DOTween.Sequence()
                    .Append(_camera.transform.DOMove(position, duration).SetEase(ease))
                    .Join(DOVirtual.Float(
                            _camera.orthographicSize,
                            orthographicSize,
                            duration,
                            value => SetOrthographicSize(value))
                        .SetEase(ease))
                    .SetLink(_camera.gameObject);
                _motion = sequence.OnComplete(() => _motion = null);
            }

            private void SetOrthographicSize(float value)
            {
                if (_camera != null && _camera.orthographic)
                {
                    _camera.orthographicSize = value;
                }
            }

            private void RestoreValues()
            {
                _camera.transform.position = _basePosition;
                SetOrthographicSize(_baseOrthographicSize);
            }
        }

        private sealed class SettlementPlaybackState
        {
            public SettlementPlaybackState(int cueCount, SettlementScoreFireView scoreFire)
            {
                CueCount = Mathf.Max(1, cueCount);
                ScoreFire = scoreFire;
            }

            public int CueIndex { get; set; }

            public int CueCount { get; }

            public SettlementScoreFireView ScoreFire { get; }
        }
    }
}
