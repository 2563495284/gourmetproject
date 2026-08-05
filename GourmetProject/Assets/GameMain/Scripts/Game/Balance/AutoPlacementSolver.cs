using System;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;

namespace GourmetProject.Game.Balance
{
    [Serializable]
    public sealed class AutoPlacementResult
    {
        public int Score;
        public bool Truncated;
        public bool HasLegalSolution = true;
        public int SearchNodes;
    }

    /// <summary>在正式 BattleSession 上摆盘；所有候选位置均来自正式合法性枚举。</summary>
    public static class AutoPlacementSolver
    {
        public static AutoPlacementResult Solve(BattleSession session, int beamWidth, int nodeBudget)
        {
            var result = new AutoPlacementResult();
            if (session == null) { result.HasLegalSolution = false; return result; }
            int budget = Math.Max(1, nodeBudget);
            while (session.CanServeAny())
            {
                ServePrepareResult prepared = session.PrepareServe(0);
                if (!prepared.Success) break;
                Placement best = default;
                bool found = false;
                float bestValue = float.MinValue;
                int limit = Math.Min(prepared.PreparedDish.Placements.Count, Math.Max(1, beamWidth));
                for (int i = 0; i < limit; i++)
                {
                    if (++result.SearchNodes > budget) { result.Truncated = true; break; }
                    Placement p = prepared.PreparedDish.Placements[i];
                    // 稳定、紧凑的启发式；最终价值始终由正式 PreviewScore 计算。
                    float value = -(p.Origin.Y * session.DiningTable.Width + p.Origin.X);
                    if (!found || value > bestValue) { found = true; best = p; bestValue = value; }
                }
                if (!found)
                {
                    result.HasLegalSolution = false;
                    break;
                }
                ServeResult placed = session.PreplacePreparedServe(best);
                if (!placed.Success || !session.ConfirmPendingDish(placed.Dish.Id).Success)
                {
                    result.HasLegalSolution = false;
                    break;
                }
                if (result.Truncated) break;
            }
            result.Score = session.PreviewScore().Total;
            return result;
        }
    }
}
