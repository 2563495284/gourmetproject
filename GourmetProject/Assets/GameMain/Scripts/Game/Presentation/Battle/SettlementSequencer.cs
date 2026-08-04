using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
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

        [Header("结算加速（小丑牌式：按 cue 进度越来越快）")]
        [SerializeField] private float _startSpeed = 1f;
        [SerializeField] private float _maxSpeed = 3f;
        [SerializeField] private float _speedCurveExponent = 1.35f;

        [FormerlySerializedAs("_floatingTextPrefab")]
        [SerializeField] private FloatingTextView _settlementEffectLabelPrefab;
        [SerializeField] private SweetTransferParticleView _sweetTransferParticlePrefab;

        [Header("餐桌舞台节拍（统一速度下的秒数）")]
        [SerializeField] private float _baseDishDuration = 0.45f;
        [SerializeField] private float _sourceFocusDuration = 0.55f;
        [SerializeField] private float _scopeRevealDuration = 0.40f;
        [SerializeField] private float _resultBeatDuration = 0.80f;
        [SerializeField] private float _groupSettleDuration = 0.20f;
        [SerializeField] private float _finaleDuration = 1.20f;
        [SerializeField] private float _sweetTransferSourceDuration = 0.22f;
        [SerializeField] private float _sweetTransferTravelDuration = 0.38f;
        [SerializeField] private float _sweetTransferExecutorDuration = 0.30f;

        [Header("结算标签布局")]
        [Tooltip("结算效果标签相对常驻美味值的垂直偏移，负值表示显示在下方。")]
        [SerializeField] private float _dishFloatingVerticalOffset = -0.45f;

        private float _currentSettlementSpeed = 1f;
        private bool _settlementAccelerationEnabled;
        private SettlementStageView _stage;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [SerializeField, Tooltip("开发版结算调试：Space 暂停/继续时的当前状态。")]
        private bool _debugScorePaused;
        private bool _debugScoreControlsActive;
        private bool _debugScoreHasSavedTimeScale;
        private float _debugScoreSavedTimeScale = 1f;
        private GUIStyle _debugScoreOverlayStyle;
#endif

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
            float? duration = null)
        {
            FloatingTextView.Spawn(
                _settlementEffectLabelPrefab,
                parent != null ? parent : transform,
                worldPos,
                text,
                rise,
                duration);
        }

        public async Awaitable PlayAsync(
            BattleSession session,
            ScoreResult result,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            DiningTableCoordinateMapper mapper,
            Transform fxRoot,
            SettlementScoreFireView scoreFire,
            Action<int> renderScore,
            Action<SettlementRevealSignal> onReveal,
            Action<SettlementScopeSignal> onScope,
            Action<string> onPassiveTriggered,
            Action<SettlementBeatSignal> onBeat,
            SettlementBaselineSnapshot baselineSnapshot,
            CancellationToken cancellationToken)
        {
            if (result == null)
            {
                return;
            }

            renderScore?.Invoke(0);
            SettlementPresentationPlan plan = SettlementPresentationPlan.Build(result);
            var playback = new SettlementPlaybackState(plan.ResultBeatCount, null);
            var ledger = new SettlementRunningLedger(result.DishScores, baselineSnapshot);
            ClearRetainedDishValueBadges();
            var sweetTransferPlayback = new SweetTransferPlaybackState();
            bool completed = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using CancellationTokenSource debugScorePauseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _debugScorePaused = false;
            _debugScoreControlsActive = true;
            RestoreDebugScorePauseTimeScale();
            _ = MonitorDebugScorePauseAsync(debugScorePauseCts.Token);
#endif
            BeginSettlementSpeed();
            scoreFire?.Hide();
            EnsureStage();
            _stage.Configure(dishViews, mapper, fxRoot);

            try
            {
                for (int i = 0; i < plan.BaseBeats.Count; i++)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    await WaitWhileDebugScorePausedAsync(cancellationToken);
#endif
                    SettlementBaseBeat beat = plan.BaseBeats[i];
                    AdvanceSettlementSpeed(playback, SettlementCueKind.DishContribution);
                    float contribution = ledger.ApplyBase(beat.DishInstanceId, beat.BaseValue);
                    dishViews.TryGetValue(beat.DishInstanceId, out DishPieceView view);
                    if (view != null)
                    {
                        view.SetDishValueBadge(contribution);
                        view.PunchDishValueBadge(DishValuePunchScale, ScaleSettlementDuration(DishValuePunchDuration));
                    }

                    renderScore?.Invoke(ledger.CurrentTotal);
                    EmitBeat(
                        onBeat,
                        SettlementBeatKind.ResultApplied,
                        view?.Instance?.Def?.Name ?? beat.DishId,
                        beat.DishInstanceId,
                        playback);
                    await _stage.PlayBaseAsync(
                        view,
                        view?.Instance?.Def?.Name ?? beat.DishId,
                        contribution,
                        ScaleSettlementDuration(_baseDishDuration),
                        cancellationToken);
                }

                for (int groupIndex = 0; groupIndex < plan.Groups.Count; groupIndex++)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    await WaitWhileDebugScorePausedAsync(cancellationToken);
#endif
                    SettlementEffectGroup group = plan.Groups[groupIndex];
                    SettlementSweetTransferPresentationContext handoffContext =
                        SettlementSweetTransferPresentationContext.FromGroup(group);
                    SweetTransferVisualContext groupSweetContext = handoffContext.IsValid
                        ? new SweetTransferVisualContext(
                            handoffContext.SourceDishInstanceId,
                            handoffContext.ExecutorDishInstanceId,
                            playSourceIntro: false)
                        : ResolveSweetTransferVisualContext(BuildFirstSweetTransferSteps(group));
                    SynchronizeSweetTransferSource(
                        sweetTransferPlayback,
                        groupSweetContext,
                        dishViews,
                        allowBeginOrSwitch: true);

                    bool playSweetTransferHandoff = handoffContext.RequiresHandoffAfter(
                        sweetTransferPlayback.PresentationKey);
                    if (handoffContext.IsValid)
                    {
                        SynchronizeSweetTransferExecutor(
                            sweetTransferPlayback,
                            handoffContext.ExecutorDishInstanceId,
                            dishViews,
                            allowBeginOrSwitch: true);
                        if (playSweetTransferHandoff)
                        {
                            sweetTransferPlayback.PresentationKey = handoffContext.Key;
                            DishPieceView sourceView = TryGetDishView(
                                handoffContext.SourceDishInstanceId,
                                dishViews);
                            DishPieceView executorView = TryGetDishView(
                                handoffContext.ExecutorDishInstanceId,
                                dishViews);
                            await _stage.PlaySweetTransferHandoffAsync(
                                handoffContext,
                                sourceView,
                                executorView,
                                _sweetTransferParticlePrefab,
                                ScaleSettlementDuration(_sweetTransferSourceDuration),
                                ScaleSettlementDuration(_sweetTransferTravelDuration),
                                ScaleSettlementDuration(_sweetTransferExecutorDuration),
                                cancellationToken);
                            sweetTransferPlayback.ReceiverDishInstanceId =
                                handoffContext.ExecutorDishInstanceId;
                        }
                    }
                    else
                    {
                        SynchronizeSweetTransferExecutor(
                            sweetTransferPlayback,
                            0,
                            dishViews,
                            allowBeginOrSwitch: true);
                        sweetTransferPlayback.PresentationKey = default;
                    }

                    EmitBeat(
                        onBeat,
                        SettlementBeatKind.SourceStarted,
                        group.SourceName,
                        group.ActorDishInstanceId,
                        playback);
                    await _stage.FocusSourceAsync(
                        group,
                        ScaleSettlementDuration(_sourceFocusDuration),
                        cancellationToken,
                        actorAlreadyIntroduced: playSweetTransferHandoff);

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

                    var resultTasks = new List<Awaitable>(group.Lines.Count);
                    var resultStackCounts = new Dictionary<int, int>();
                    var resultStackIndices = new Dictionary<int, int>();
                    for (int lineIndex = 0; lineIndex < group.Lines.Count; lineIndex++)
                    {
                        int targetId = group.Lines[lineIndex].DishInstanceId;
                        resultStackCounts.TryGetValue(targetId, out int count);
                        resultStackCounts[targetId] = count + 1;
                    }

                    // 同一技能组的计分明细仍按原顺序写入账本与发出事件，但所有结果动画
                    // 在同一帧启动。这样保留正式因果顺序，同时恢复“一起触发”的节奏。
                    for (int lineIndex = 0; lineIndex < group.Lines.Count; lineIndex++)
                    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        await WaitWhileDebugScorePausedAsync(cancellationToken);
#endif
                        ScoreLine line = group.Lines[lineIndex];
                        AdvanceSettlementSpeed(playback, CueKindFor(line));
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
                        await UpdateSweetTransferVisualsAsync(
                            sweetTransferPlayback,
                            ResolveSweetTransferVisualContext(sweetSteps),
                            dishViews,
                            fxRoot,
                            cancellationToken);

                        float contribution = ledger.Apply(line);
                        dishViews.TryGetValue(line.DishInstanceId, out DishPieceView target);
                        if (target != null && ChangesDishValue(line.Kind))
                        {
                            target.SetDishValueBadge(contribution);
                            target.PunchDishValueBadge(
                                DishValuePunchScale,
                                ScaleSettlementDuration(DishValuePunchDuration));
                        }

                        renderScore?.Invoke(ledger.CurrentTotal);
                        EmitBeat(
                            onBeat,
                            SettlementBeatKind.ResultApplied,
                            group.SourceName,
                            line.DishInstanceId,
                            playback);
                        resultStackIndices.TryGetValue(line.DishInstanceId, out int stackIndex);
                        resultStackIndices[line.DishInstanceId] = stackIndex + 1;
                        resultTasks.Add(_stage.ShowResultAsync(
                            group,
                            line,
                            target,
                            contribution,
                            ledger.CurrentTotal,
                            ScaleSettlementDuration(_resultBeatDuration),
                            stackIndex,
                            resultStackCounts[line.DishInstanceId],
                            cancellationToken));
                    }

                    for (int resultIndex = 0; resultIndex < resultTasks.Count; resultIndex++)
                    {
                        await resultTasks[resultIndex];
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
                    await _stage.EndGroupAsync(
                        ScaleSettlementDuration(_groupSettleDuration),
                        cancellationToken);
                    EmitBeat(
                        onBeat,
                        SettlementBeatKind.GroupCompleted,
                        group.SourceName,
                        group.ActorDishInstanceId,
                        playback);
                }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                await WaitWhileDebugScorePausedAsync(cancellationToken);
#endif
                ClearSweetTransferVisuals(sweetTransferPlayback, dishViews);
                onScope?.Invoke(default);
                AdvanceSettlementSpeed(playback, SettlementCueKind.FinalScore);
                renderScore?.Invoke(result.Total);
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
                onScope?.Invoke(default);
                _stage?.ClearImmediate();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                debugScorePauseCts.Cancel();
                ClearDebugScorePauseState();
#endif
                RestoreSettlementSpeed();
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ClearDebugScorePauseState();
#endif
            RestoreSettlementSpeed();
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ClearDebugScorePauseState();
#endif
            RestoreSettlementSpeed();
            ClearRetainedDishValueBadges();
        }

        private void EnsureStage()
        {
            if (_stage == null)
            {
                _stage = GetComponent<SettlementStageView>();
            }

            if (_stage == null)
            {
                _stage = gameObject.AddComponent<SettlementStageView>();
            }
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

        private static IReadOnlyList<SettlementPlaybackStep> BuildFirstSweetTransferSteps(
            SettlementEffectGroup group)
        {
            if (group == null || group.Lines.Count == 0)
            {
                return Array.Empty<SettlementPlaybackStep>();
            }

            ScoreLine firstLine = group.Lines[0];
            return TryBuildCue(firstLine, out SettlementCue cue)
                ? BuildSweetTransferSteps(firstLine, cue)
                : Array.Empty<SettlementPlaybackStep>();
        }

        private static bool ChangesDishValue(ScoreLineKind kind)
        {
            return kind == ScoreLineKind.DishBase
                || kind == ScoreLineKind.DishFlat
                || kind == ScoreLineKind.DishMultiplier
                || kind == ScoreLineKind.DishMultiplierAdd;
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
                case ScoreLineKind.SilverItemRoll:
                case ScoreLineKind.CopySkill:
                case ScoreLineKind.TriggerSweetTransfer:
                case ScoreLineKind.TriggeredSweetTransferSource:
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            await WaitWhileDebugScorePausedAsync(cancellationToken);
#endif
            AdvanceSettlementSpeed(playback, cue.Kind);
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
                    ScaleSettlementDuration(cue.Duration));
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            await WaitWhileDebugScorePausedAsync(cancellationToken);
#endif
            AdvanceSettlementSpeed(playback, batch[0].Cue.Kind);
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
                        ScaleSettlementDuration(cue.Duration));
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
                cancellationToken);
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

                float baseScore = dish.BaseScoreBeforeSettlement;
                float multiplier = dish.BaseMultiplierBeforeSettlement;
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

        /// <summary>待领奖读档或页面往返后，按结算明细无动画恢复菜品贡献值。</summary>
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            await WaitWhileDebugScorePausedAsync(cancellationToken);
#endif
            if (fxRoot != null)
            {
                FloatingTextView.SpawnEffect(
                    _settlementEffectLabelPrefab,
                    fxRoot,
                    center + new Vector3(0f, 0.72f, 0f),
                    "结算",
                    $"总分 {total}",
                    FinalScorePopupRise,
                    ScaleSettlementDuration(FinalScorePopupDuration));
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                await WaitWhileDebugScorePausedAsync(cancellationToken);
#endif
                SettlementCue cue = cues[i];
                AdvanceSettlementSpeed(playback, cue.Kind);
                EmitPassiveTriggered(onPassiveTriggered, cue);
                EmitReveal(onReveal, cue);
                if (fxRoot != null)
                {
                    Vector3 offset = new(0f, 0.52f + SourceCueStackOffset * i, 0f);
                    FloatingTextView.SpawnEffect(
                        _settlementEffectLabelPrefab,
                        fxRoot,
                        center + offset,
                        cue.SourceName,
                        cue.Text,
                        cue.Rise,
                        ScaleSettlementDuration(cue.Duration));
                }

                await Awaitable.WaitForSecondsAsync(ScaleSettlementDuration(FinalCueInterval), cancellationToken);
            }
        }

        private float ScaleSettlementDuration(float duration)
        {
            return Mathf.Max(0.0001f, duration / Mathf.Max(0.0001f, _currentSettlementSpeed));
        }

        private void BeginSettlementSpeed()
        {
            _currentSettlementSpeed = 1f;
            _settlementAccelerationEnabled =
                GameApp.Settings != null && GameApp.Settings.SettlementAcceleration;
            if (!_settlementAccelerationEnabled)
            {
                return;
            }

            _currentSettlementSpeed = Mathf.Max(0.0001f, _startSpeed);
        }

        private void RestoreSettlementSpeed()
        {
            _currentSettlementSpeed = 1f;
            _settlementAccelerationEnabled = false;
        }

        private void AdvanceSettlementSpeed(SettlementPlaybackState playback, SettlementCueKind kind)
        {
            if (playback == null)
            {
                return;
            }

            float normalized = playback.CueCount <= 1
                ? 1f
                : Mathf.Clamp01((float)playback.CueIndex / (playback.CueCount - 1));
            playback.CueIndex++;

            if (_settlementAccelerationEnabled)
            {
                float curve = Mathf.Pow(normalized, Mathf.Max(0.0001f, _speedCurveExponent));
                float start = Mathf.Max(0.0001f, _startSpeed);
                float max = Mathf.Max(start, _maxSpeed);
                _currentSettlementSpeed = Mathf.Lerp(start, max, curve);
            }
            else
            {
                _currentSettlementSpeed = 1f;
            }

            playback.ScoreFire?.SetIntensity(normalized, _currentSettlementSpeed);
            NotifySettlementCue(kind, _currentSettlementSpeed, normalized);
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
        /// 判定某条明细是否来自「甜蜜传递」外来子技能（来源标签带 &lt;甜蜜传递&gt;）。
        /// 是则该明细在揭示分数/倍率的同时，一并揭示 1 张传递卡片（最终数量由 tips 工厂按实际条数封顶）。
        /// </summary>
        private static int SweetTransferCardDelta(ScoreSource source)
        {
            return source != null && !string.IsNullOrEmpty(source.Name) && source.Name.Contains("甜蜜传递") ? 1 : 0;
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

        private async Awaitable TweenScoreAsync(float from, float to, float duration, Action<int> renderScore, CancellationToken cancellationToken)
        {
            if (renderScore == null)
            {
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            await TweenScoreWithDebugControlsAsync(from, to, duration, renderScore, cancellationToken);
#else
            Tween tween = DOVirtual.Float(0f, 1f, Mathf.Max(0.0001f, duration), t =>
                {
                    renderScore((int)Math.Round(Mathf.Lerp(from, to, Mathf.Clamp01(t)), MidpointRounding.AwayFromZero));
                })
                .SetEase(Ease.Linear);
            await PresentationTween.AwaitCompletionAsync(tween, cancellationToken);

            renderScore((int)Math.Round(to, MidpointRounding.AwayFromZero));
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private async Awaitable TweenScoreWithDebugControlsAsync(float from, float to, float duration, Action<int> renderScore, CancellationToken cancellationToken)
        {
            float clampedDuration = Mathf.Max(0.0001f, duration);
            float elapsed = 0f;

            while (elapsed < clampedDuration)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_debugScorePaused)
                {
                    elapsed = Mathf.Min(clampedDuration, elapsed + Time.unscaledDeltaTime);
                    float t = elapsed / clampedDuration;
                    renderScore((int)Math.Round(Mathf.Lerp(from, to, t), MidpointRounding.AwayFromZero));
                }

                await Awaitable.NextFrameAsync(cancellationToken);
            }

            renderScore((int)Math.Round(to, MidpointRounding.AwayFromZero));
        }

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

        private async Awaitable WaitWhileDebugScorePausedAsync(CancellationToken cancellationToken)
        {
            while (_debugScorePaused)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Awaitable.NextFrameAsync(cancellationToken);
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
            if (_debugScorePaused)
            {
                if (!_debugScoreHasSavedTimeScale)
                {
                    _debugScoreSavedTimeScale = Time.timeScale;
                    _debugScoreHasSavedTimeScale = true;
                }

                Time.timeScale = 0f;
            }
            else
            {
                RestoreDebugScorePauseTimeScale();
            }

        }

        private void ClearDebugScorePauseState()
        {
            _debugScorePaused = false;
            _debugScoreControlsActive = false;
            RestoreDebugScorePauseTimeScale();
        }

        private void RestoreDebugScorePauseTimeScale()
        {
            if (!_debugScoreHasSavedTimeScale)
            {
                return;
            }

            Time.timeScale = _debugScoreSavedTimeScale;
            _debugScoreHasSavedTimeScale = false;
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
            bool hasSilverItemRollCue = false;
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
                    ref hasSilverItemRollCue,
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
                                ref hasSilverItemRollCue,
                                ref hasFinalModifierCue);
                        }

                        AddCueBatchSteps(plan, cueBatch, dishViews, shownDishBases, baselineSnapshot);
                        i += cueBatch.Count - 1;
                        continue;
                    }
                }

                AddCueStep(plan, line, cue, dishViews, shownDishBases, baselineSnapshot);
            }

            // 甜蜜传递的「卡片揭示」不单独补 cue，而是绑定在目标菜触发传递效果的那条明细上
            //（该明细来源名带 <甜蜜传递> 标签，见 SweetTransferCardDelta），做到触发即显示、时机与演出一致。

            if (!hasFinalModifierCue && HasFinalModifier(result))
            {
                plan.FinalCues.Add(BuildFinalSummaryCue(result));
            }

            if (!hasGoldCue && Mathf.Abs(result.GoldDelta) > 0.001f)
            {
                plan.FinalCues.Add(new SettlementCue(
                    SettlementCueKind.SideEffect,
                    $"金币 {FormatSigned(result.GoldDelta)}",
                    sourceName: "结算"));
            }

            if (!hasLayerCue && result.HappyCakeLayerDelta != 0)
            {
                plan.FinalCues.Add(new SettlementCue(
                    SettlementCueKind.SideEffect,
                    $"层数 {FormatSigned(result.HappyCakeLayerDelta)}",
                    sourceName: "快乐蛋糕",
                    reveal: SettlementRevealSignal.CakeLayerReveal(result.HappyCakeLayerDelta)));
            }

            if (!hasSilverItemRollCue && result.SilverItemRollRequests > 0)
            {
                plan.FinalCues.Add(new SettlementCue(
                    SettlementCueKind.SideEffect,
                    $"获得道具 ×{result.SilverItemRollRequests}",
                    sourceName: "银材质"));
            }

            // if (result.PermanentFlatDeltas.Count > 0)
            // {
            //     plan.FinalCues.Add(new SettlementCue(SettlementCueKind.SideEffect, $"永久美味 +{result.PermanentFlatDeltas.Count} 道菜"));
            // }

            // if (result.PermanentMultDeltas.Count > 0)
            // {
            //     plan.FinalCues.Add(new SettlementCue(SettlementCueKind.SideEffect, $"永久倍率 +{result.PermanentMultDeltas.Count} 道菜"));
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
            ref bool hasSilverItemRollCue,
            ref bool hasFinalModifierCue)
        {
            hasGoldCue |= line.Kind == ScoreLineKind.Gold;
            hasLayerCue |= line.Kind == ScoreLineKind.Layer;
            hasSilverItemRollCue |= line.Kind == ScoreLineKind.SilverItemRoll;
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
            float baseScore = 0f;
            if (instance != null)
            {
                baseScore = baselineSnapshot != null && baselineSnapshot.TryGet(instance.Id, out SettlementDishBaseline baseline)
                    ? baseline.BaseScore
                    : instance.BaseScoreBeforeSettlement;
            }

            return new SettlementCue(
                SettlementCueKind.Source,
                $"分数 {FormatSigned(baseScore)}",
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
            if (Mathf.Abs(line.Value) <= 0.001f && !isExecutedDishSkill)
            {
                return false;
            }

            string sourceName = SourceName(line);
            switch (line.Kind)
            {
                case ScoreLineKind.DishBase:
                    cue = new SettlementCue(
                        SettlementCueKind.Source,
                        $"分数 {FormatSigned(line.Value)}",
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
                        $"分数 {FormatSigned(line.Value)}",
                        feedbackKind: BuildDishFeedbackKind(line),
                        reveal: SettlementRevealSignal.FlatReveal(line.DishInstanceId, line.After, SweetTransferCardDelta(line.Source)),
                        valueChange: DishValueChange.FlatBonus(line.After),
                        batchKey: BuildDishSkillBatchKey(line),
                        sourceName: sourceName);
                    return true;

                case ScoreLineKind.DishMultiplier:
                    cue = new SettlementCue(
                        SettlementCueKind.Source,
                        $"倍率 {FormatMultiplier(line.Value)}",
                        feedbackKind: BuildDishFeedbackKind(line),
                        reveal: SettlementRevealSignal.MultiplierReveal(line.DishInstanceId, line.After, SweetTransferCardDelta(line.Source)),
                        valueChange: DishValueChange.Multiplier(line.After),
                        batchKey: BuildDishSkillBatchKey(line),
                        sourceName: sourceName);
                    return true;

                case ScoreLineKind.DishMultiplierAdd:
                    cue = new SettlementCue(
                        SettlementCueKind.Source,
                        $"倍率 {FormatSigned(line.Value)}",
                        feedbackKind: BuildDishFeedbackKind(line),
                        reveal: SettlementRevealSignal.MultiplierReveal(line.DishInstanceId, line.After, SweetTransferCardDelta(line.Source)),
                        valueChange: DishValueChange.Multiplier(line.After),
                        batchKey: BuildDishSkillBatchKey(line),
                        sourceName: sourceName);
                    return true;

                case ScoreLineKind.FinalFlat:
                    cue = new SettlementCue(
                        SettlementCueKind.FinalModifier,
                        $"分数 {FormatSigned(line.Value)}",
                        rise: 0.7f,
                        duration: 1.1f,
                        sourceName: sourceName);
                    return true;

                case ScoreLineKind.FinalMultiplier:
                    cue = new SettlementCue(
                        SettlementCueKind.FinalModifier,
                        $"倍率 {FormatMultiplier(line.Value)}",
                        rise: 0.7f,
                        duration: 1.1f,
                        sourceName: sourceName);
                    return true;

                case ScoreLineKind.Gold:
                    cue = new SettlementCue(
                        SettlementCueKind.SideEffect,
                        $"金币 {FormatSigned(line.Value)}",
                        sourceName: sourceName);
                    return true;

                case ScoreLineKind.Layer:
                    cue = new SettlementCue(
                        SettlementCueKind.SideEffect,
                        $"层数 {FormatSigned(line.Value)}",
                        sourceName: sourceName,
                        reveal: SettlementRevealSignal.CakeLayerReveal(Mathf.RoundToInt(line.Value)));
                    return true;

                case ScoreLineKind.SilverItemRoll:
                    cue = new SettlementCue(
                        SettlementCueKind.SideEffect,
                        $"获得道具 ×{Mathf.RoundToInt(line.Value)}",
                        sourceName: sourceName);
                    return true;

                case ScoreLineKind.CopySkill:
                    cue = new SettlementCue(
                        SettlementCueKind.SideEffect,
                        $"获得技能 ×{Mathf.RoundToInt(line.Value)}",
                        feedbackKind: SettlementDishFeedbackKind.CopySkillTriggered,
                        reveal: SettlementRevealSignal.CopySkillReveal(line.DishInstanceId, Mathf.RoundToInt(line.Value)),
                        sourceName: sourceName);
                    return true;

                case ScoreLineKind.TriggerSweetTransfer:
                    cue = new SettlementCue(
                        SettlementCueKind.SideEffect,
                        line.Value > 1f ? $"触发甜蜜传递 ×{Mathf.RoundToInt(line.Value)}" : "触发甜蜜传递",
                        feedbackKind: SettlementDishFeedbackKind.GenericSkillTriggered,
                        triggerSweetTransferPhase: TriggerSweetTransferCuePhase.ActivatorStarted,
                        sourceName: sourceName);
                    return true;

                case ScoreLineKind.TriggeredSweetTransferSource:
                    cue = new SettlementCue(
                        SettlementCueKind.SideEffect,
                        "触发甜蜜传递",
                        feedbackKind: SettlementDishFeedbackKind.SweetTransferSkillTriggered,
                        triggerSweetTransferPhase: line.Value >= line.After
                            ? TriggerSweetTransferCuePhase.FinalSourceStarted
                            : TriggerSweetTransferCuePhase.SourceStarted,
                        sourceName: sourceName);
                    return true;

                default:
                    return false;
            }
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
                case ScoreLineKind.DishMultiplier:
                    return active
                        ? SettlementDishFeedbackKind.ActiveMultiplier
                        : SettlementDishFeedbackKind.PassiveMultiplier;
                case ScoreLineKind.DishMultiplierAdd:
                    return active
                        ? SettlementDishFeedbackKind.ActiveMultiplierAdd
                        : SettlementDishFeedbackKind.PassiveMultiplierAdd;
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
                || source.Type == ScoreSourceType.Material
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
            return Mathf.Abs(result.FinalFlat) > 0.001f
                || Mathf.Abs(result.FinalMultiplier - 1f) > 0.001f;
        }

        private static SettlementCue BuildFinalSummaryCue(ScoreResult result)
        {
            string summary = string.Empty;
            if (Mathf.Abs(result.FinalMultiplier - 1f) > 0.001f)
            {
                summary += FormatMultiplier(result.FinalMultiplier);
            }

            if (Mathf.Abs(result.FinalFlat) > 0.001f)
            {
                if (summary.Length > 0)
                {
                    summary += "  ";
                }

                summary += FormatSigned(result.FinalFlat);
            }

            return new SettlementCue(
                SettlementCueKind.FinalModifier,
                summary,
                rise: 0.7f,
                duration: 1.1f,
                sourceName: "局加成");
        }

        private static string FormatSigned(float value)
        {
            return $"{(value >= 0f ? "+" : string.Empty)}{value:0.#}";
        }

        private static string FormatMultiplier(float value)
        {
            return $"×{value:0.##}";
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
            Vector3 offset = new(0f, _dishFloatingVerticalOffset, 0f);
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
                bool showEffectLabel = true)
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
        }

        private readonly struct DishValueChange
        {
            private DishValueChange(DishValueChangeKind kind, float value)
            {
                Kind = kind;
                Value = value;
            }

            public DishValueChangeKind Kind { get; }

            public float Value { get; }

            public static DishValueChange Base(float value)
            {
                return new DishValueChange(DishValueChangeKind.Base, value);
            }

            public static DishValueChange FlatBonus(float value)
            {
                return new DishValueChange(DishValueChangeKind.FlatBonus, value);
            }

            public static DishValueChange Multiplier(float value)
            {
                return new DishValueChange(DishValueChangeKind.Multiplier, value);
            }
        }

        private sealed class DishValuePlaybackAccumulator
        {
            public DishValuePlaybackAccumulator(float baseScore, float multiplier)
            {
                BaseScore = baseScore;
                Multiplier = multiplier;
            }

            private float BaseScore { get; set; }

            private float FlatBonus { get; set; }

            private float Multiplier { get; set; }

            public float Contribution => DishScore.CeilContribution(BaseScore + FlatBonus, Multiplier);

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
                }
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
