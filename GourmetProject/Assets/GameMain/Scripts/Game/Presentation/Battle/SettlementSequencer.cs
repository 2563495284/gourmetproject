using System;
using System.Collections;
using System.Collections.Generic;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Scoring;
using UnityEngine;
using GpBoard = GourmetProject.Gameplay.Board.Board;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 背包乱斗式的结算演出：点「吃」后，按结算顺序逐道菜依次触发——高亮、点光脉冲、
    /// 向相邻菜品画协同连线、飘出贡献分，并把总分逐步累加。纯消费 <see cref="ScoreResult"/>，
    /// 不改动任何计分逻辑。
    /// </summary>
    public sealed class SettlementSequencer : MonoBehaviour
    {
        private static readonly Color LinkColor = new Color(1f, 0.78f, 0.32f, 1f);
        private static readonly Color GainColor = new Color(1f, 0.92f, 0.5f, 1f);
        private static readonly Color FinalColor = new Color(0.6f, 0.95f, 1f, 1f);

        private const float PerDishInterval = 0.32f;
        private const float DishPunch = 1.2f;
        private const float DishPunchDuration = 0.2f;
        private const float ScoreTweenStep = 0.28f;

        [SerializeField] private AdjacencyLinkView _linkPrefab;
        [SerializeField] private FloatingTextView _floatingTextPrefab;

        public IEnumerator Play(
            BattleSession session,
            ScoreResult result,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            BoardCoordinateMapper mapper,
            Transform fxRoot,
            Action<int> renderScore,
            Action onComplete)
        {
            if (result == null)
            {
                onComplete?.Invoke();
                yield break;
            }

            GpBoard board = session?.Board;
            float runningTotal = 0f;
            renderScore?.Invoke(0);

            foreach (DishScore dishScore in result.DishScores)
            {
                if (!dishViews.TryGetValue(dishScore.DishInstanceId, out DishPieceView view) || view == null)
                {
                    runningTotal += dishScore.Contribution;
                    continue;
                }

                DishInstance instance = view.Instance;
                Vector3 center = DishCenter(instance, mapper);

                yield return PresentationTween.PunchScale(view.transform, DishPunch, DishPunchDuration);

                if (board != null && fxRoot != null)
                {
                    foreach (DishInstance neighbor in board.GetAdjacentDishes(instance))
                    {
                        AdjacencyLinkView.Spawn(_linkPrefab, fxRoot, center, DishCenter(neighbor, mapper), LinkColor);
                    }
                }

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

            yield return TweenScore(runningTotal, result.Total, 0.45f, renderScore);
            renderScore?.Invoke(result.Total);

            onComplete?.Invoke();
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
    }
}
