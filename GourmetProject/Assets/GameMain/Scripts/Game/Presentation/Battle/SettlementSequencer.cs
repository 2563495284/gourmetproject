using System;
using System.Collections;
using System.Collections.Generic;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Scoring;
using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 背包乱斗式的结算演出：点「吃」后，按结算顺序逐道菜依次触发标签演出、
    /// 飘出贡献分，并把总分逐步累加。纯消费 <see cref="ScoreResult"/>，不改动任何计分逻辑。
    /// </summary>
    public sealed class SettlementSequencer : MonoBehaviour
    {
        private static readonly Color GainColor = new Color(1f, 0.92f, 0.5f, 1f);
        private static readonly Color FinalColor = new Color(0.6f, 0.95f, 1f, 1f);

        private const float PerDishInterval = 0.32f;
        private const float ScoreTweenStep = 0.28f;

        [Header("结算加速（小丑牌式：按 cue 进度越来越快）")]
        [SerializeField] private bool _useGlobalTimeScale = true;
        [SerializeField] private float _startSpeed = 1f;
        [SerializeField] private float _maxSpeed = 3f;
        [SerializeField] private float _speedCurveExponent = 1.35f;

        [SerializeField] private FloatingTextView _floatingTextPrefab;

        private bool _hasSavedTimeScale;
        private float _savedTimeScale = 1f;
        private float _currentSettlementSpeed = 1f;

        private enum SettlementCueKind
        {
            Tag = 0,
            DishContribution = 1,
            FinalModifier = 2,
            FinalScore = 3,
        }

        public IEnumerator Play(
            BattleSession session,
            ScoreResult result,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            BoardCoordinateMapper mapper,
            Transform fxRoot,
            SettlementScoreFireView scoreFire,
            Action<int> renderScore,
            Action onComplete)
        {
            if (result == null)
            {
                onComplete?.Invoke();
                yield break;
            }

            float runningTotal = 0f;
            renderScore?.Invoke(0);
            var playback = new SettlementPlaybackState(CountSettlementCues(result, dishViews), scoreFire);
            BeginSettlementSpeed();
            scoreFire?.Show();

            try
            {
                foreach (DishScore dishScore in result.DishScores)
                {
                    if (!dishViews.TryGetValue(dishScore.DishInstanceId, out DishPieceView view) || view == null)
                    {
                        runningTotal += dishScore.Contribution;
                        continue;
                    }

                    DishInstance instance = view.Instance;
                    Vector3 center = DishCenter(instance, mapper);

                    yield return PlayTagCues(result, dishScore.DishInstanceId, view, center, fxRoot, mapper, playback);

                    AdvanceSettlementSpeed(playback, SettlementCueKind.DishContribution);
                    if (fxRoot != null)
                    {
                        FloatingTextView.Spawn(_floatingTextPrefab, fxRoot, center + new Vector3(0f, 0.35f, 0f), FormatGain(dishScore), GainColor);
                    }

                    float from = runningTotal;
                    runningTotal += dishScore.Contribution;
                    yield return TweenScore(from, runningTotal, ScoreTweenStep, renderScore);

                    yield return new WaitForSeconds(PerDishInterval);
                }

                // 局级修正（被动道具等）单独演出一次。
                bool hasFinalFlat = Mathf.Abs(result.FinalFlat) > 0.001f;
                bool hasFinalMult = Mathf.Abs(result.FinalMultiplier - 1f) > 0.001f;
                if ((hasFinalFlat || hasFinalMult) && fxRoot != null)
                {
                    AdvanceSettlementSpeed(playback, SettlementCueKind.FinalModifier);

                    string summary = string.Empty;
                    if (hasFinalMult)
                    {
                        summary += $"×{result.FinalMultiplier:0.##}";
                    }

                    if (hasFinalFlat)
                    {
                        if (summary.Length > 0)
                        {
                            summary += "  ";
                        }

                        summary += $"{(result.FinalFlat >= 0 ? "+" : string.Empty)}{result.FinalFlat:0.#}";
                    }

                    FloatingTextView.Spawn(_floatingTextPrefab, fxRoot, mapper.Center + new Vector3(0f, 0.6f, 0f), $"局加成 {summary}", FinalColor, 0.18f, 0.7f, 1.1f);
                    yield return new WaitForSeconds(0.4f);
                }

                AdvanceSettlementSpeed(playback, SettlementCueKind.FinalScore);
                yield return TweenScore(runningTotal, result.Total, 0.45f, renderScore);
                renderScore?.Invoke(result.Total);
            }
            finally
            {
                RestoreSettlementSpeed();
                scoreFire?.Hide();
            }

            onComplete?.Invoke();
        }

        private void OnDisable()
        {
            RestoreSettlementSpeed();
        }

        private void OnDestroy()
        {
            RestoreSettlementSpeed();
        }

        private IEnumerator PlayTagCues(
            ScoreResult result,
            int dishInstanceId,
            DishPieceView view,
            Vector3 center,
            Transform fxRoot,
            BoardCoordinateMapper mapper,
            SettlementPlaybackState playback)
        {
            foreach (ScoreLine line in result.ScoreLines)
            {
                if (line.DishInstanceId != dishInstanceId || !ShouldPlayTagCue(line))
                {
                    continue;
                }

                yield return PlayTagCue(line, view, center, fxRoot, mapper, playback);
            }
        }

        private IEnumerator PlayTagCue(
            ScoreLine line,
            DishPieceView view,
            Vector3 center,
            Transform fxRoot,
            BoardCoordinateMapper mapper,
            SettlementPlaybackState playback)
        {
            AdvanceSettlementSpeed(playback, SettlementCueKind.Tag);

            // 标签专属演出的扩展出口：后续按 line.Source.Id 分支即可保留结算层不变。
            switch (line.Source.Id)
            {
                default:
                    yield return view.PlayDeliciousnessGainFeedback();
                    break;
            }
        }

        private static bool ShouldPlayTagCue(ScoreLine line)
        {
            if (line == null || line.Source == null || line.Value <= 0f || line.Kind != ScoreLineKind.DishFlat)
            {
                return false;
            }

            return line.Source.Type == ScoreSourceType.DishTag || line.Source.Type == ScoreSourceType.CellTag;
        }

        private void BeginSettlementSpeed()
        {
            _currentSettlementSpeed = Mathf.Max(0.0001f, _startSpeed);
            if (!_useGlobalTimeScale || _hasSavedTimeScale)
            {
                return;
            }

            _savedTimeScale = Time.timeScale;
            _hasSavedTimeScale = true;
            Time.timeScale = _currentSettlementSpeed;
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

            float curve = Mathf.Pow(normalized, Mathf.Max(0.0001f, _speedCurveExponent));
            float start = Mathf.Max(0.0001f, _startSpeed);
            float max = Mathf.Max(start, _maxSpeed);
            _currentSettlementSpeed = Mathf.Lerp(start, max, curve);
            if (_useGlobalTimeScale && _hasSavedTimeScale)
            {
                Time.timeScale = _currentSettlementSpeed;
            }

            playback.ScoreFire?.SetIntensity(normalized, _currentSettlementSpeed);
            NotifySettlementCue(kind, _currentSettlementSpeed, normalized);
        }

        private void NotifySettlementCue(SettlementCueKind kind, float speed, float normalized)
        {
            // 预留音效入口：后续可在这里按 kind 播放结算音效，并用 speed 映射 pitch。
        }

        private static int CountSettlementCues(ScoreResult result, IReadOnlyDictionary<int, DishPieceView> dishViews)
        {
            int count = 1; // 最终滚分。
            foreach (DishScore dishScore in result.DishScores)
            {
                if (!dishViews.TryGetValue(dishScore.DishInstanceId, out DishPieceView view) || view == null)
                {
                    continue;
                }

                count++; // 本菜贡献飘字 + 滚分。
                foreach (ScoreLine line in result.ScoreLines)
                {
                    if (line.DishInstanceId == dishScore.DishInstanceId && ShouldPlayTagCue(line))
                    {
                        count++;
                    }
                }
            }

            if (Mathf.Abs(result.FinalFlat) > 0.001f || Mathf.Abs(result.FinalMultiplier - 1f) > 0.001f)
            {
                count++;
            }

            return Mathf.Max(1, count);
        }

        private static IEnumerator TweenScore(float from, float to, float duration, Action<int> renderScore)
        {
            if (renderScore == null)
            {
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                renderScore((int)Math.Round(Mathf.Lerp(from, to, t), MidpointRounding.AwayFromZero));
                yield return null;
            }

            renderScore((int)Math.Round(to, MidpointRounding.AwayFromZero));
        }

        private static string FormatGain(DishScore dishScore)
        {
            string text = $"+{Mathf.RoundToInt(dishScore.Contribution)}";
            if (dishScore.Multiplier > 1.001f)
            {
                text += $"  ×{dishScore.Multiplier:0.#}";
            }

            return text;
        }

        private static Vector3 DishCenter(DishInstance dish, BoardCoordinateMapper mapper)
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
