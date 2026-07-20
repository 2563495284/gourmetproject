using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using DG.Tweening;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Scoring;
using UnityEngine;
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
        private static readonly Color GainColor = Color.green;
        private static readonly Color FinalColor = Color.red;
        private static readonly Color SkillColor = Color.blue;
        private static readonly Color FlavorColor = Color.pink;
        private static readonly Color MaterialColor = Color.black;
        private static readonly Color MultiplierColor = Color.red;
        private static readonly Color SideEffectColor = Color.cyan;
        private static readonly Color DefaultCueColor = Color.white;
        private static readonly Color DishValueColor = Color.white;

        private const float SourceCueRise = 0.48f;
        private const float SourceCueDuration = 0.62f;
        private const float SourceCueCharacterSize = 0.12f;
        private const float SourceCueStackOffset = 0.12f;
        private const float DishValueCharacterSize = 0.13f;
        private const float DishValueVerticalOffset = 0.08f;
        private const float DishValuePunchScale = 0.18f;
        private const float DishValuePunchDuration = 0.18f;
        private const float FinalCueInterval = 0.22f;
        private const float FinalScorePopupCharacterSize = 0.18f;
        private const float FinalScorePopupRise = 0.78f;
        private const float FinalScorePopupDuration = 1.1f;
        private const float FinalScorePopupHold = 0.28f;
        private const float BatchedCueHold = 0.24f;

        [Header("结算加速（小丑牌式：按 cue 进度越来越快）")]
        [SerializeField] private bool _useGlobalTimeScale = true;
        [SerializeField] private float _startSpeed = 1f;
        [SerializeField] private float _maxSpeed = 3f;
        [SerializeField] private float _speedCurveExponent = 1.35f;

        [SerializeField] private FloatingTextView _floatingTextPrefab;

        private bool _hasSavedTimeScale;
        private float _savedTimeScale = 1f;
        private float _currentSettlementSpeed = 1f;
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
            SettlementBaselineSnapshot baselineSnapshot,
            CancellationToken cancellationToken)
        {
            if (result == null)
            {
                return;
            }

            float runningTotal = 0f;
            renderScore?.Invoke(0);
            SettlementPlaybackPlan plan = BuildSettlementPlaybackPlan(result, dishViews, baselineSnapshot);
            var playback = new SettlementPlaybackState(CountSettlementCues(plan), scoreFire);
            var dishValueBadges = new Dictionary<int, DishValueBadge>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using CancellationTokenSource debugScorePauseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _debugScorePaused = false;
            _debugScoreControlsActive = true;
            RestoreDebugScorePauseTimeScale();
            _ = MonitorDebugScorePauseAsync(debugScorePauseCts.Token);
#endif
            BeginSettlementSpeed();
            scoreFire?.Show();

            try
            {
                for (int i = 0; i < plan.Steps.Count;)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    await WaitWhileDebugScorePausedAsync(cancellationToken);
#endif
                    IReadOnlyList<SettlementPlaybackStep> batch = CollectStepBatch(plan.Steps, i, out int nextIndex);
                    await PlayStepBatchAsync(batch, dishViews, mapper, fxRoot, playback, dishValueBadges, onReveal, onScope, cancellationToken);
                    i = nextIndex;
                }

                onScope?.Invoke(default);
                await PlayFinalCuesAsync(plan.FinalCues, mapper.Center, fxRoot, playback, onReveal, cancellationToken);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                await WaitWhileDebugScorePausedAsync(cancellationToken);
#endif
                AdvanceSettlementSpeed(playback, SettlementCueKind.FinalScore);
                await TweenScoreAsync(runningTotal, result.Total, 0.45f, renderScore, cancellationToken);
                renderScore?.Invoke(result.Total);
                await PlayFinalScorePopupAsync(result.Total, mapper.Center, fxRoot, cancellationToken);
            }
            finally
            {
                onScope?.Invoke(default);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                debugScorePauseCts.Cancel();
                ClearDebugScorePauseState();
#endif
                RestoreSettlementSpeed();
                scoreFire?.Hide();
                ClearDishValueBadges(dishValueBadges);
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
        }

        private async Awaitable PlayCueAsync(
            SettlementCue cue,
            SettlementScopeSignal scope,
            DishPieceView view,
            Vector3 center,
            Transform fxRoot,
            SettlementPlaybackState playback,
            Dictionary<int, DishValueBadge> dishValueBadges,
            Action<SettlementRevealSignal> onReveal,
            Action<SettlementScopeSignal> onScope,
            CancellationToken cancellationToken)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            await WaitWhileDebugScorePausedAsync(cancellationToken);
#endif
            AdvanceSettlementSpeed(playback, cue.Kind);
            EmitScope(onScope, scope);
            EmitReveal(onReveal, cue);
            ApplyDishValueChange(cue, view, center, fxRoot, dishValueBadges);
            if (fxRoot != null)
            {
                FloatingTextView.Spawn(
                    _floatingTextPrefab,
                    fxRoot,
                    center + new Vector3(0f, 0.34f, 0f),
                    cue.Text,
                    cue.Color,
                    cue.CharacterSize,
                    cue.Rise,
                    cue.Duration);
            }

            await view.PlayDeliciousnessGainFeedbackAsync(cancellationToken);
        }

        private async Awaitable PlayStepBatchAsync(
            IReadOnlyList<SettlementPlaybackStep> batch,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            DiningTableCoordinateMapper mapper,
            Transform fxRoot,
            SettlementPlaybackState playback,
            Dictionary<int, DishValueBadge> dishValueBadges,
            Action<SettlementRevealSignal> onReveal,
            Action<SettlementScopeSignal> onScope,
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

                DishInstance instance = view.Instance;
                Vector3 center = DishCenter(instance, mapper);
                await PlayCueAsync(step.Cue, step.Scope, view, center, fxRoot, playback, dishValueBadges, onReveal, onScope, cancellationToken);
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            await WaitWhileDebugScorePausedAsync(cancellationToken);
#endif
            AdvanceSettlementSpeed(playback, batch[0].Cue.Kind);
            foreach (SettlementPlaybackStep step in batch)
            {
                if (!dishViews.TryGetValue(step.DishInstanceId, out DishPieceView view) || view == null)
                {
                    continue;
                }

                DishInstance instance = view.Instance;
                Vector3 center = DishCenter(instance, mapper);
                SettlementCue cue = step.Cue;
                EmitScope(onScope, step.Scope);
                EmitReveal(onReveal, cue);
                ApplyDishValueChange(cue, view, center, fxRoot, dishValueBadges);
                if (fxRoot != null)
                {
                    FloatingTextView.Spawn(
                        _floatingTextPrefab,
                        fxRoot,
                        center + new Vector3(0f, 0.34f, 0f),
                        cue.Text,
                        cue.Color,
                        cue.CharacterSize,
                        cue.Rise,
                        cue.Duration);
                }

                _ = PlayFeedbackSafelyAsync(view, cancellationToken);
            }

            await Awaitable.WaitForSecondsAsync(BatchedCueHold, cancellationToken);
        }

        private static async Awaitable PlayFeedbackSafelyAsync(DishPieceView view, CancellationToken cancellationToken)
        {
            if (view == null)
            {
                return;
            }

            try
            {
                await view.PlayDeliciousnessGainFeedbackAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 结算被中断时，已经启动的并行动画正常退出。
            }
        }

        private void ApplyDishValueChange(
            SettlementCue cue,
            DishPieceView view,
            Vector3 center,
            Transform fxRoot,
            Dictionary<int, DishValueBadge> dishValueBadges)
        {
            if (cue == null
                || cue.ValueChange.Kind == DishValueChangeKind.None
                || view == null
                || view.Instance == null
                || dishValueBadges == null)
            {
                return;
            }

            DishInstance instance = view.Instance;
            if (!dishValueBadges.TryGetValue(instance.Id, out DishValueBadge badge))
            {
                badge = new DishValueBadge(instance.BaseScoreBeforeSettlement, instance.BaseMultiplierBeforeSettlement);
                dishValueBadges[instance.Id] = badge;
            }

            badge.Apply(cue.ValueChange);

            if (!badge.IsVisible && cue.ValueChange.Kind != DishValueChangeKind.Base)
            {
                return;
            }

            if (badge.View == null)
            {
                if (fxRoot == null)
                {
                    return;
                }

                badge.View = FloatingTextView.SpawnStatic(
                    _floatingTextPrefab,
                    fxRoot,
                    center + new Vector3(0f, DishValueVerticalOffset, 0f),
                    FormatDishValue(badge.Contribution),
                    DishValueColor,
                    DishValueCharacterSize);
                badge.IsVisible = badge.View != null;
            }
            else
            {
                badge.View.SetStaticText(FormatDishValue(badge.Contribution), DishValueColor, DishValueCharacterSize);
            }

            PunchDishValueBadge(badge.View);
        }

        private static void PunchDishValueBadge(FloatingTextView view)
        {
            if (view == null)
            {
                return;
            }

            Transform t = view.transform;
            t.DOKill(false);
            t.localScale = Vector3.one;
            t.DOPunchScale(
                    Vector3.one * DishValuePunchScale,
                    DishValuePunchDuration,
                    1,
                    0.45f)
                .SetLink(view.gameObject);
        }

        private static void ClearDishValueBadges(Dictionary<int, DishValueBadge> dishValueBadges)
        {
            if (dishValueBadges == null)
            {
                return;
            }

            foreach (DishValueBadge badge in dishValueBadges.Values)
            {
                if (badge?.View != null)
                {
                    Destroy(badge.View.gameObject);
                }
            }

            dishValueBadges.Clear();
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
                FloatingTextView.Spawn(
                    _floatingTextPrefab,
                    fxRoot,
                    center + new Vector3(0f, 0.72f, 0f),
                    $"总分 {total}",
                    FinalColor,
                    FinalScorePopupCharacterSize,
                    FinalScorePopupRise,
                    FinalScorePopupDuration);
            }

            await Awaitable.WaitForSecondsAsync(FinalScorePopupHold, cancellationToken);
        }

        private async Awaitable PlayFinalCuesAsync(
            IReadOnlyList<SettlementCue> cues,
            Vector3 center,
            Transform fxRoot,
            SettlementPlaybackState playback,
            Action<SettlementRevealSignal> onReveal,
            CancellationToken cancellationToken)
        {
            for (int i = 0; i < cues.Count; i++)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                await WaitWhileDebugScorePausedAsync(cancellationToken);
#endif
                SettlementCue cue = cues[i];
                AdvanceSettlementSpeed(playback, cue.Kind);
                EmitReveal(onReveal, cue);
                if (fxRoot != null)
                {
                    Vector3 offset = new(0f, 0.52f + SourceCueStackOffset * i, 0f);
                    FloatingTextView.Spawn(
                        _floatingTextPrefab,
                        fxRoot,
                        center + offset,
                        cue.Text,
                        cue.Color,
                        cue.CharacterSize,
                        cue.Rise,
                        cue.Duration);
                }

                await Awaitable.WaitForSecondsAsync(FinalCueInterval, cancellationToken);
            }
        }

        private void BeginSettlementSpeed()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _currentSettlementSpeed = 1f;
#else
            _currentSettlementSpeed = Mathf.Max(0.0001f, _startSpeed);
            if (!_useGlobalTimeScale || _hasSavedTimeScale)
            {
                return;
            }

            _savedTimeScale = Time.timeScale;
            _hasSavedTimeScale = true;
            Time.timeScale = _currentSettlementSpeed;
#endif
        }

        private void RestoreSettlementSpeed()
        {
            if (!_hasSavedTimeScale)
            {
                return;
            }

            Time.timeScale = _savedTimeScale;
            _currentSettlementSpeed = Mathf.Max(0.0001f, _savedTimeScale);
            _hasSavedTimeScale = false;
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _currentSettlementSpeed = 1f;
#else
            float curve = Mathf.Pow(normalized, Mathf.Max(0.0001f, _speedCurveExponent));
            float start = Mathf.Max(0.0001f, _startSpeed);
            float max = Mathf.Max(start, _maxSpeed);
            _currentSettlementSpeed = Mathf.Lerp(start, max, curve);
            if (_useGlobalTimeScale && _hasSavedTimeScale)
            {
                Time.timeScale = _currentSettlementSpeed;
            }
#endif

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

            IReadOnlyList<ScoreLine> scoreLines = result.ScoreLines;
            for (int i = 0; i < scoreLines.Count; i++)
            {
                ScoreLine line = scoreLines[i];
                if (!TryBuildCue(line, out SettlementCue cue))
                {
                    continue;
                }

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
                plan.FinalCues.Add(new SettlementCue(SettlementCueKind.SideEffect, $"金币 {FormatSigned(result.GoldDelta)}", SideEffectColor));
            }

            if (!hasLayerCue && result.HappyCakeLayerDelta != 0)
            {
                plan.FinalCues.Add(new SettlementCue(SettlementCueKind.SideEffect, $"蛋糕层 {FormatSigned(result.HappyCakeLayerDelta)}", SideEffectColor));
            }

            if (!hasSilverItemRollCue && result.SilverItemRollRequests > 0)
            {
                plan.FinalCues.Add(new SettlementCue(SettlementCueKind.SideEffect, $"银材质抽道具 ×{result.SilverItemRollRequests}", SideEffectColor));
            }

            // if (result.PermanentFlatDeltas.Count > 0)
            // {
            //     plan.FinalCues.Add(new SettlementCue(SettlementCueKind.SideEffect, $"永久美味 +{result.PermanentFlatDeltas.Count} 道菜", SideEffectColor));
            // }

            // if (result.PermanentMultDeltas.Count > 0)
            // {
            //     plan.FinalCues.Add(new SettlementCue(SettlementCueKind.SideEffect, $"永久倍率 +{result.PermanentMultDeltas.Count} 道菜", SideEffectColor));
            // }

            return plan;
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
                GainColor,
                valueChange: DishValueChange.Base(baseScore),
                batchKey: batchKey);
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
            if (line == null || Mathf.Abs(line.Value) <= 0.001f)
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
                        GainColor,
                        valueChange: DishValueChange.Base(line.After));
                    return true;

                case ScoreLineKind.DishFlat:
                    if (!IsReadableDishSource(line.Source))
                    {
                        return false;
                    }

                    cue = new SettlementCue(
                        SettlementCueKind.Source,
                        $"{sourceName} {FormatSigned(line.Value)}",
                        ColorForSource(line.Source),
                        reveal: SettlementRevealSignal.FlatReveal(line.DishInstanceId, line.After, SweetTransferCardDelta(line.Source)),
                        valueChange: DishValueChange.FlatBonus(line.After),
                        batchKey: BuildDishSkillBatchKey(line));
                    return true;

                case ScoreLineKind.DishMultiplier:
                    cue = new SettlementCue(
                        SettlementCueKind.Source,
                        $"倍率 {FormatMultiplier(line.Value)}",
                        MultiplierColor,
                        reveal: SettlementRevealSignal.MultiplierReveal(line.DishInstanceId, line.After, SweetTransferCardDelta(line.Source)),
                        valueChange: DishValueChange.Multiplier(line.After),
                        batchKey: BuildDishSkillBatchKey(line));
                    return true;

                case ScoreLineKind.DishMultiplierAdd:
                    cue = new SettlementCue(
                        SettlementCueKind.Source,
                        $"倍率 {FormatSigned(line.Value)}",
                        MultiplierColor,
                        reveal: SettlementRevealSignal.MultiplierReveal(line.DishInstanceId, line.After, SweetTransferCardDelta(line.Source)),
                        valueChange: DishValueChange.Multiplier(line.After),
                        batchKey: BuildDishSkillBatchKey(line));
                    return true;

                case ScoreLineKind.FinalFlat:
                    cue = new SettlementCue(
                        SettlementCueKind.FinalModifier,
                        $"{sourceName} {FormatSigned(line.Value)}",
                        FinalColor,
                        0.18f,
                        0.7f,
                        1.1f);
                    return true;

                case ScoreLineKind.FinalMultiplier:
                    cue = new SettlementCue(
                        SettlementCueKind.FinalModifier,
                        $"{sourceName} {FormatMultiplier(line.Value)}",
                        FinalColor,
                        0.18f,
                        0.7f,
                        1.1f);
                    return true;

                case ScoreLineKind.Gold:
                    cue = new SettlementCue(SettlementCueKind.SideEffect, $"金币 {FormatSigned(line.Value)}", SideEffectColor);
                    return true;

                case ScoreLineKind.Layer:
                    cue = new SettlementCue(SettlementCueKind.SideEffect, $"蛋糕层 {FormatSigned(line.Value)}", SideEffectColor);
                    return true;

                case ScoreLineKind.ExtraSettlement:
                    cue = new SettlementCue(SettlementCueKind.SideEffect, $"额外结算 {FormatSigned(line.Value)}", SideEffectColor);
                    return true;

                case ScoreLineKind.SilverItemRoll:
                    cue = new SettlementCue(SettlementCueKind.SideEffect, $"银材质抽道具 ×{Mathf.RoundToInt(line.Value)}", SideEffectColor);
                    return true;

                case ScoreLineKind.CopySkill:
                    cue = new SettlementCue(
                        SettlementCueKind.SideEffect,
                        $"复制技能 ×{Mathf.RoundToInt(line.Value)}",
                        SideEffectColor,
                        reveal: SettlementRevealSignal.CopySkillReveal(line.DishInstanceId, Mathf.RoundToInt(line.Value)));
                    return true;

                default:
                    return false;
            }
        }

        private static string BuildDishSkillBatchKey(ScoreLine line)
        {
            ScoreSource source = line?.Source;
            if (source == null
                || source.Type != ScoreSourceType.DishSkill
                || SweetTransferCardDelta(source) > 0)
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
                || source.Type == ScoreSourceType.Relic
                || source.Type == ScoreSourceType.WeekModifier;
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

        private static Color ColorForSource(ScoreSource source)
        {
            if (source == null)
            {
                return DefaultCueColor;
            }

            switch (source.Type)
            {
                case ScoreSourceType.DishSkill:
                    return SkillColor;
                case ScoreSourceType.DishFlavor:
                    return FlavorColor;
                case ScoreSourceType.Material:
                    return MaterialColor;
                case ScoreSourceType.TableTag:
                case ScoreSourceType.Relic:
                case ScoreSourceType.WeekModifier:
                    return MultiplierColor;
                default:
                    return DefaultCueColor;
            }
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
                $"局加成 {summary}",
                FinalColor,
                0.18f,
                0.7f,
                1.1f);
        }

        private static string FormatSigned(float value)
        {
            return $"{(value >= 0f ? "+" : string.Empty)}{value:0.#}";
        }

        private static string FormatMultiplier(float value)
        {
            return $"×{value:0.##}";
        }

        private static string FormatDishValue(float value)
        {
            float rounded = (float)Math.Round(Mathf.Max(0f, value), 1, MidpointRounding.AwayFromZero);
            float whole = (float)Math.Round(rounded, MidpointRounding.AwayFromZero);
            return Mathf.Abs(rounded - whole) <= 0.001f
                ? $"{whole:0}"
                : $"{rounded:0.#}";
        }

        private static Vector3 DishCenter(DishInstance dish, DiningTableCoordinateMapper mapper)
        {
            if (dish == null || dish.OccupiedCells.Count == 0)
            {
                return mapper.Center;
            }

            Vector3 sum = Vector3.zero;
            foreach (var cell in dish.OccupiedCells)
            {
                sum += mapper.CellCenter(cell);
            }

            return sum / dish.OccupiedCells.Count;
        }

        private sealed class SettlementPlaybackPlan
        {
            public List<SettlementPlaybackStep> Steps { get; } = new();

            public List<SettlementCue> FinalCues { get; } = new();
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
                Color color,
                float characterSize = SourceCueCharacterSize,
                float rise = SourceCueRise,
                float duration = SourceCueDuration,
                SettlementRevealSignal reveal = default,
                DishValueChange valueChange = default,
                string batchKey = null)
            {
                Kind = kind;
                Text = text;
                Color = color;
                CharacterSize = characterSize;
                Rise = rise;
                Duration = duration;
                Reveal = reveal;
                ValueChange = valueChange;
                BatchKey = batchKey;
            }

            public SettlementCueKind Kind { get; }

            public string Text { get; }

            public Color Color { get; }

            public float CharacterSize { get; }

            public float Rise { get; }

            public float Duration { get; }

            /// <summary>该 cue 播放时对 tips 发出的渐进揭示信号（默认 None）。</summary>
            public SettlementRevealSignal Reveal { get; }

            public DishValueChange ValueChange { get; }

            public string BatchKey { get; }
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

        private sealed class DishValueBadge
        {
            public DishValueBadge(float baseScore, float multiplier)
            {
                BaseScore = baseScore;
                Multiplier = multiplier;
            }

            public FloatingTextView View { get; set; }

            public bool IsVisible { get; set; }

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
