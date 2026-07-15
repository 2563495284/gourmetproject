using System;
using System.Collections.Generic;
using System.Threading;
using DG.Tweening;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Scoring;
using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 背包乱斗式的结算演出：点「吃」后，按结算顺序逐道菜依次触发标签演出、
    /// 飘出贡献分，并把总分逐步累加。纯消费 <see cref="ScoreResult"/>，不改动任何计分逻辑。
    /// </summary>
    public sealed class SettlementSequencer : MonoBehaviour
    {
        private static readonly Color GainColor = new(1f, 0.15f, 0.08f);
        private static readonly Color FinalColor = new(1f, 0.24f, 0.12f);
        private static readonly Color SkillColor = new(1f, 0.65f, 0.05f);
        private static readonly Color FlavorColor = new(1f, 0.35f, 0.85f);
        private static readonly Color MaterialColor = new(0.25f, 0.85f, 1f);
        private static readonly Color MultiplierColor = new(1f, 0.95f, 0.2f);
        private static readonly Color SideEffectColor = new(0.55f, 1f, 0.35f);
        private static readonly Color DefaultCueColor = Color.white;

        private const float PerDishInterval = 0.32f;
        private const float ScoreTweenStep = 0.28f;
        private const float SourceCueRise = 0.48f;
        private const float SourceCueDuration = 0.62f;
        private const float SourceCueCharacterSize = 0.12f;
        private const float SourceCueStackOffset = 0.12f;
        private const float FinalCueInterval = 0.22f;

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
        [SerializeField, Tooltip("开发版结算调试：是否打印结算演出关键流程日志。")]
        private bool _debugLogSettlementFlow = true;
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
            CancellationToken cancellationToken)
        {
            if (result == null)
            {
                return;
            }

            float runningTotal = 0f;
            renderScore?.Invoke(0);
            SettlementCueCollection cues = BuildSettlementCues(result, dishViews);
            var playback = new SettlementPlaybackState(CountSettlementCues(cues, result, dishViews), scoreFire);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using CancellationTokenSource debugScorePauseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _debugScorePaused = false;
            _debugScoreControlsActive = true;
            RestoreDebugScorePauseTimeScale();
            _ = MonitorDebugScorePauseAsync(debugScorePauseCts.Token);
            LogDebugSettlementFlow(
                $"开始：dishScores={result.DishScores.Count}, scoreLines={result.ScoreLines.Count}, cues={playback.CueCount}, total={result.Total}");
#endif
            BeginSettlementSpeed();
            scoreFire?.Show();

            try
            {
                int dishIndex = 0;
                foreach (DishScore dishScore in result.DishScores)
                {
                    dishIndex++;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    await WaitWhileDebugScorePausedAsync(cancellationToken);
#endif
                    if (!dishViews.TryGetValue(dishScore.DishInstanceId, out DishPieceView view) || view == null)
                    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        LogDebugSettlementFlow(
                            $"菜品 {dishIndex}/{result.DishScores.Count} 跳过：id={dishScore.DishInstanceId}, missingView=true, contribution={dishScore.Contribution:0.##}");
#endif
                        runningTotal += dishScore.Contribution;
                        continue;
                    }

                    DishInstance instance = view.Instance;
                    Vector3 center = DishCenter(instance, mapper);
                    IReadOnlyList<SettlementCue> sourceCues = cues.GetDishCues(dishScore.DishInstanceId);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    LogDebugSettlementFlow(
                        $"菜品 {dishIndex}/{result.DishScores.Count} 开始：id={dishScore.DishInstanceId}, sourceCues={sourceCues.Count}, contribution={dishScore.Contribution:0.##}, multiplier={dishScore.Multiplier:0.##}, running={runningTotal:0.##}");
#endif

                    await PlaySourceCuesAsync(sourceCues, view, center, fxRoot, playback, cancellationToken);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    await WaitWhileDebugScorePausedAsync(cancellationToken);
                    LogDebugSettlementFlow($"菜品 {dishIndex}/{result.DishScores.Count} 贡献飘字：{FormatGain(dishScore)}");
#endif
                    AdvanceSettlementSpeed(playback, SettlementCueKind.DishContribution);
                    if (fxRoot != null)
                    {
                        FloatingTextView.Spawn(_floatingTextPrefab, fxRoot, center + new Vector3(0f, 0.35f, 0f), FormatGain(dishScore), GainColor);
                    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    LogDebugSettlementFlow($"菜品 {dishIndex}/{result.DishScores.Count} 反馈开始。");
#endif
                    await view.PlayDeliciousnessGainFeedbackAsync(cancellationToken);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    LogDebugSettlementFlow($"菜品 {dishIndex}/{result.DishScores.Count} 反馈结束。");
#endif

                    float from = runningTotal;
                    runningTotal += dishScore.Contribution;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    LogDebugSettlementFlow($"菜品 {dishIndex}/{result.DishScores.Count} 滚分开始：{from:0.##} -> {runningTotal:0.##}");
#endif
                    await TweenScoreAsync(from, runningTotal, ScoreTweenStep, renderScore, cancellationToken);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    LogDebugSettlementFlow($"菜品 {dishIndex}/{result.DishScores.Count} 滚分结束：running={runningTotal:0.##}");
                    LogDebugSettlementFlow($"菜品 {dishIndex}/{result.DishScores.Count} 间隔等待：{PerDishInterval:0.##}s");
#endif

                    await Awaitable.WaitForSecondsAsync(PerDishInterval, cancellationToken);
                }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogDebugSettlementFlow($"最终 cue 开始：count={cues.FinalCues.Count}");
#endif
                await PlayFinalCuesAsync(cues.FinalCues, mapper.Center, fxRoot, playback, cancellationToken);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogDebugSettlementFlow("最终 cue 结束。");
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                await WaitWhileDebugScorePausedAsync(cancellationToken);
                LogDebugSettlementFlow($"最终滚分开始：{runningTotal:0.##} -> {result.Total}");
#endif
                AdvanceSettlementSpeed(playback, SettlementCueKind.FinalScore);
                await TweenScoreAsync(runningTotal, result.Total, 0.45f, renderScore, cancellationToken);
                renderScore?.Invoke(result.Total);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogDebugSettlementFlow($"完成：total={result.Total}");
#endif
            }
            finally
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogDebugSettlementFlow("清理：停止暂停监听并恢复结算状态。");
                debugScorePauseCts.Cancel();
                ClearDebugScorePauseState();
#endif
                RestoreSettlementSpeed();
                scoreFire?.Hide();
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

        private async Awaitable PlaySourceCuesAsync(
            IReadOnlyList<SettlementCue> cues,
            DishPieceView view,
            Vector3 center,
            Transform fxRoot,
            SettlementPlaybackState playback,
            CancellationToken cancellationToken)
        {
            for (int i = 0; i < cues.Count; i++)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                await WaitWhileDebugScorePausedAsync(cancellationToken);
#endif
                SettlementCue cue = cues[i];
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogDebugSettlementFlow($"Source cue {i + 1}/{cues.Count}：kind={cue.Kind}, text={cue.Text}");
#endif
                AdvanceSettlementSpeed(playback, cue.Kind);
                if (fxRoot != null)
                {
                    Vector3 offset = new(0f, 0.34f + SourceCueStackOffset * i, 0f);
                    FloatingTextView.Spawn(
                        _floatingTextPrefab,
                        fxRoot,
                        center + offset,
                        cue.Text,
                        cue.Color,
                        SourceCueCharacterSize,
                        SourceCueRise,
                        SourceCueDuration);
                }

                await view.PlayDeliciousnessGainFeedbackAsync(cancellationToken);
            }
        }

        private async Awaitable PlayFinalCuesAsync(
            IReadOnlyList<SettlementCue> cues,
            Vector3 center,
            Transform fxRoot,
            SettlementPlaybackState playback,
            CancellationToken cancellationToken)
        {
            for (int i = 0; i < cues.Count; i++)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                await WaitWhileDebugScorePausedAsync(cancellationToken);
#endif
                SettlementCue cue = cues[i];
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogDebugSettlementFlow($"Final cue {i + 1}/{cues.Count}：kind={cue.Kind}, text={cue.Text}");
#endif
                AdvanceSettlementSpeed(playback, cue.Kind);
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

        private static int CountSettlementCues(
            SettlementCueCollection cues,
            ScoreResult result,
            IReadOnlyDictionary<int, DishPieceView> dishViews)
        {
            int count = 1; // 最终滚分。
            foreach (DishScore dishScore in result.DishScores)
            {
                if (!dishViews.TryGetValue(dishScore.DishInstanceId, out DishPieceView view) || view == null)
                {
                    continue;
                }

                count++; // 本菜贡献飘字 + 滚分。
                count += cues.GetDishCues(dishScore.DishInstanceId).Count;
            }

            count += cues.FinalCues.Count;

            return Mathf.Max(1, count);
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

            Debug.Log($"结算演出{(_debugScorePaused ? "暂停" : "继续")}（Space 切换）。", this);
        }

        private void LogDebugSettlementFlow(string message)
        {
            if (!_debugLogSettlementFlow)
            {
                return;
            }

            Debug.Log($"[SettlementSequencer] {message}", this);
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

        private static string FormatGain(DishScore dishScore)
        {
            string text = $"+{Mathf.RoundToInt(dishScore.Contribution)}";
            if (dishScore.Multiplier > 1.001f)
            {
                text += $"  ×{dishScore.Multiplier:0.#}";
            }

            return text;
        }

        private static SettlementCueCollection BuildSettlementCues(
            ScoreResult result,
            IReadOnlyDictionary<int, DishPieceView> dishViews)
        {
            var cues = new SettlementCueCollection();
            bool hasGoldCue = false;
            bool hasLayerCue = false;
            bool hasSilverItemRollCue = false;
            bool hasFinalModifierCue = false;

            foreach (ScoreLine line in result.ScoreLines)
            {
                if (!TryBuildCue(line, out SettlementCue cue))
                {
                    continue;
                }

                hasGoldCue |= line.Kind == ScoreLineKind.Gold;
                hasLayerCue |= line.Kind == ScoreLineKind.Layer;
                hasSilverItemRollCue |= line.Kind == ScoreLineKind.SilverItemRoll;
                hasFinalModifierCue |= line.Kind == ScoreLineKind.FinalFlat
                    || line.Kind == ScoreLineKind.FinalMultiplier;

                if (line.DishInstanceId != 0
                    && dishViews.TryGetValue(line.DishInstanceId, out DishPieceView view)
                    && view != null
                    && cue.Kind != SettlementCueKind.FinalModifier)
                {
                    cues.AddDishCue(line.DishInstanceId, cue);
                }
                else
                {
                    cues.FinalCues.Add(cue);
                }
            }

            if (!hasFinalModifierCue && HasFinalModifier(result))
            {
                cues.FinalCues.Add(BuildFinalSummaryCue(result));
            }

            if (!hasGoldCue && Mathf.Abs(result.GoldDelta) > 0.001f)
            {
                cues.FinalCues.Add(new SettlementCue(SettlementCueKind.SideEffect, $"金币 {FormatSigned(result.GoldDelta)}", SideEffectColor));
            }

            if (!hasLayerCue && result.HappyCakeLayerDelta != 0)
            {
                cues.FinalCues.Add(new SettlementCue(SettlementCueKind.SideEffect, $"蛋糕层 {FormatSigned(result.HappyCakeLayerDelta)}", SideEffectColor));
            }

            if (!hasSilverItemRollCue && result.SilverItemRollRequests > 0)
            {
                cues.FinalCues.Add(new SettlementCue(SettlementCueKind.SideEffect, $"银材质抽道具 ×{result.SilverItemRollRequests}", SideEffectColor));
            }

            if (result.PermanentFlatDeltas.Count > 0)
            {
                cues.FinalCues.Add(new SettlementCue(SettlementCueKind.SideEffect, $"永久美味 +{result.PermanentFlatDeltas.Count} 道菜", SideEffectColor));
            }

            if (result.PermanentMultDeltas.Count > 0)
            {
                cues.FinalCues.Add(new SettlementCue(SettlementCueKind.SideEffect, $"永久倍率 +{result.PermanentMultDeltas.Count} 道菜", SideEffectColor));
            }

            return cues;
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
                case ScoreLineKind.DishFlat:
                    if (!IsReadableDishSource(line.Source))
                    {
                        return false;
                    }

                    cue = new SettlementCue(
                        SettlementCueKind.Source,
                        $"{sourceName} {FormatSigned(line.Value)}",
                        ColorForSource(line.Source));
                    return true;

                case ScoreLineKind.DishMultiplier:
                    cue = new SettlementCue(
                        SettlementCueKind.Source,
                        $"{sourceName} {FormatMultiplier(line.Value)}",
                        MultiplierColor);
                    return true;

                case ScoreLineKind.DishMultiplierAdd:
                    cue = new SettlementCue(
                        SettlementCueKind.Source,
                        $"{sourceName} 倍率 {FormatSigned(line.Value)}",
                        MultiplierColor);
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

                default:
                    return false;
            }
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

        private sealed class SettlementCueCollection
        {
            private readonly Dictionary<int, List<SettlementCue>> _dishCues = new();

            public List<SettlementCue> FinalCues { get; } = new();

            public void AddDishCue(int dishInstanceId, SettlementCue cue)
            {
                if (!_dishCues.TryGetValue(dishInstanceId, out List<SettlementCue> cues))
                {
                    cues = new List<SettlementCue>();
                    _dishCues.Add(dishInstanceId, cues);
                }

                cues.Add(cue);
            }

            public IReadOnlyList<SettlementCue> GetDishCues(int dishInstanceId)
            {
                return _dishCues.TryGetValue(dishInstanceId, out List<SettlementCue> cues)
                    ? cues
                    : Array.Empty<SettlementCue>();
            }
        }

        private sealed class SettlementCue
        {
            public SettlementCue(
                SettlementCueKind kind,
                string text,
                Color color,
                float characterSize = SourceCueCharacterSize,
                float rise = SourceCueRise,
                float duration = SourceCueDuration)
            {
                Kind = kind;
                Text = text;
                Color = color;
                CharacterSize = characterSize;
                Rise = rise;
                Duration = duration;
            }

            public SettlementCueKind Kind { get; }

            public string Text { get; }

            public Color Color { get; }

            public float CharacterSize { get; }

            public float Rise { get; }

            public float Duration { get; }
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
