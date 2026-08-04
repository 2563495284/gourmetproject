using System.Collections.Generic;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>
    /// 结算需要的只读历史 / 食谱输入。供「大局/小局相同检测」「食谱检测」等前提读取。
    /// 读取的是「本次结算之前」的历史，避免自累加。
    /// </summary>
    public interface IScoreHistory
    {
        /// <summary>整局（GameRun）中该 BaseId 已结算过的次数。</summary>
        int RunSettledCount(string baseId);

        /// <summary>本场经营挑战经营挑战（本场经营挑战）中该 BaseId 已结算过的次数。</summary>
        int MealSettledCount(string baseId);

        /// <summary>本场经营挑战食谱内的全部食物 BaseId（含已上/未上/在盘）。</summary>
        IReadOnlyList<string> RecipeBaseIds { get; }
    }

    /// <summary>空历史：所有计数为 0，食谱为空。用于预览或无历史场景。</summary>
    public sealed class EmptyScoreHistory : IScoreHistory
    {
        public static readonly EmptyScoreHistory Instance = new EmptyScoreHistory();

        private static readonly IReadOnlyList<string> Empty = System.Array.Empty<string>();

        public int RunSettledCount(string baseId) => 0;

        public int MealSettledCount(string baseId) => 0;

        public IReadOnlyList<string> RecipeBaseIds => Empty;
    }

    /// <summary>可变的历史实现：由 BattleSession 维护并以只读快照注入结算。</summary>
    public sealed class ScoreHistory : IScoreHistory
    {
        private readonly Dictionary<string, int> _runSettled;
        private readonly Dictionary<string, int> _mealSettled;

        public ScoreHistory(
            Dictionary<string, int> runSettled,
            Dictionary<string, int> mealSettled,
            IReadOnlyList<string> recipeBaseIds)
        {
            _runSettled = runSettled ?? new Dictionary<string, int>();
            _mealSettled = mealSettled ?? new Dictionary<string, int>();
            RecipeBaseIds = recipeBaseIds ?? System.Array.Empty<string>();
        }

        public int RunSettledCount(string baseId)
            => baseId != null && _runSettled.TryGetValue(baseId, out int v) ? v : 0;

        public int MealSettledCount(string baseId)
            => baseId != null && _mealSettled.TryGetValue(baseId, out int v) ? v : 0;

        public IReadOnlyList<string> RecipeBaseIds { get; }
    }
}
