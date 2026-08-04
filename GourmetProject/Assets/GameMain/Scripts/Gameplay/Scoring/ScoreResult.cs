using System.Collections.Generic;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>单个食物的结算明细，供 UI 展示与单测断言。</summary>
    public sealed class DishScore
    {
        private const double ContributionIntegerEpsilon = 0.0001d;

        public DishScore(
            int dishInstanceId,
            string dishId,
            float baseValue,
            float flatBonus,
            float multiplier,
            int effectiveCountAs = 1)
        {
            DishInstanceId = dishInstanceId;
            DishId = dishId;
            BaseValue = baseValue;
            FlatBonus = flatBonus;
            Multiplier = multiplier;
            EffectiveCountAs = System.Math.Max(1, effectiveCountAs);
        }

        public int DishInstanceId { get; }

        public string DishId { get; }

        public float BaseValue { get; }

        public float FlatBonus { get; }

        public float Multiplier { get; }

        /// <summary>本次结算使用的实际「视为食物数」（含 live AddCountAs 与全局加成）。</summary>
        public int EffectiveCountAs { get; }

        /// <summary>本食物最终贡献 = (美味值 + 加法) × 倍率 后向上取整。</summary>
        public float Contribution => CeilContribution(BaseValue + FlatBonus, Multiplier);

        public static float CeilContribution(float score, float multiplier)
        {
            double value = (double)score * multiplier;
            double nearestInteger = System.Math.Round(value);
            if (System.Math.Abs(value - nearestInteger) <= ContributionIntegerEpsilon)
            {
                return (float)nearestInteger;
            }

            return (float)System.Math.Ceiling(value);
        }
    }

    /// <summary>一次结算的完整结果。</summary>
    public sealed class ScoreResult
    {
        public ScoreResult(
            IReadOnlyList<DishScore> dishScores,
            float rawSum,
            float finalFlat,
            float finalMultiplier,
            IReadOnlyList<ScoreLine> scoreLines = null,
            IReadOnlyList<ScoreEvent> scoreEvents = null,
            float goldDelta = 0f,
            int happyCakeLayerDelta = 0,
            IReadOnlyList<SkillTransferSideEffect> skillTransfers = null,
            IReadOnlyDictionary<int, float> permanentFlatDeltas = null,
            IReadOnlyDictionary<int, float> permanentMultDeltas = null,
            int silverItemRollRequests = 0,
            IReadOnlyList<CopySkillRequest> copySkillRequests = null)
        {
            DishScores = dishScores;
            RawSum = rawSum;
            FinalFlat = finalFlat;
            FinalMultiplier = finalMultiplier;
            ScoreLines = scoreLines ?? System.Array.Empty<ScoreLine>();
            ScoreEvents = scoreEvents ?? System.Array.Empty<ScoreEvent>();
            GoldDelta = goldDelta;
            HappyCakeLayerDelta = happyCakeLayerDelta;
            SkillTransfers = skillTransfers ?? System.Array.Empty<SkillTransferSideEffect>();
            PermanentFlatDeltas = permanentFlatDeltas ?? EmptyFloatDeltas;
            PermanentMultDeltas = permanentMultDeltas ?? EmptyFloatDeltas;
            SilverItemRollRequests = silverItemRollRequests;
            CopySkillRequests = copySkillRequests ?? System.Array.Empty<CopySkillRequest>();
        }

        private static readonly IReadOnlyDictionary<int, float> EmptyFloatDeltas = new Dictionary<int, float>();

        /// <summary>逐菜结算明细（按结算顺序）。</summary>
        public IReadOnlyList<DishScore> DishScores { get; }

        /// <summary>所有食物贡献之和（未计局级修正）。</summary>
        public float RawSum { get; }

        /// <summary>局级加法修正（装饰品和消耗品/Buff 注入）。</summary>
        public float FinalFlat { get; }

        /// <summary>局级倍率修正（装饰品和消耗品/Buff 注入）。</summary>
        public float FinalMultiplier { get; }

        /// <summary>可解释结算明细（按实际执行顺序）。</summary>
        public IReadOnlyList<ScoreLine> ScoreLines { get; }

        /// <summary>结算生命周期事件（按实际发生顺序）。</summary>
        public IReadOnlyList<ScoreEvent> ScoreEvents { get; }

        /// <summary>金币增量（经济运营行为产生；正式结算后由 Game 层写回 GameRun.Gold）。</summary>
        public float GoldDelta { get; }

        /// <summary>全局「欢乐蛋糕层数」增量。正式结算后由 Game 层写回经营挑战状态。</summary>
        public int HappyCakeLayerDelta { get; }

        /// <summary>技能传递副作用。正式结算后应用到目标实例运行时技能集。</summary>
        public IReadOnlyList<SkillTransferSideEffect> SkillTransfers { get; }

        /// <summary>永久加法分增量（实例 Id → 累加值）。正式结算后写回实例。</summary>
        public IReadOnlyDictionary<int, float> PermanentFlatDeltas { get; }

        /// <summary>永久倍率增量（实例 Id → 累乘倍数）。正式结算后写回实例。</summary>
        public IReadOnlyDictionary<int, float> PermanentMultDeltas { get; }

        /// <summary>银材质登记的「1/3 获得消耗品」掷骰请求次数。正式结算后由 Game 层掷骰发放（预览不掷）。</summary>
        public int SilverItemRollRequests { get; }

        /// <summary>结算阶段登记的技能复制请求。正式结算后由 BattleSession 用随机流落地。</summary>
        public IReadOnlyList<CopySkillRequest> CopySkillRequests { get; }

        /// <summary>最终得分（四舍五入到整数，0.5 向上取整）。</summary>
        public int Total => (int)System.Math.Round((RawSum + FinalFlat) * FinalMultiplier, System.MidpointRounding.AwayFromZero);
    }

    /// <summary>结算生命周期事件。它记录“发生了什么”，不直接改变分数。</summary>
    public enum ScoreEventType
    {
        CalculationStarted = 0,
        CalculationFinished = 1,
        DishStarted = 2,
        DishCompleted = 3,
        EffectStarted = 4,
        EffectFinished = 5,
        CommandExecuted = 6,
    }

    /// <summary>一次结算事件记录，供调试、回放或 UI 演出使用。</summary>
    public sealed class ScoreEvent
    {
        public ScoreEvent(
            ScoreEventType type,
            ScorePhase phase,
            ScoreSource source,
            int dishInstanceId,
            string dishId,
            GridPos? cell,
            string message,
            SkillExecutionTrace trace = null,
            int executionGroupId = 0)
        {
            Type = type;
            Phase = phase;
            Source = source;
            DishInstanceId = dishInstanceId;
            DishId = dishId ?? string.Empty;
            Cell = cell;
            Message = message ?? string.Empty;
            Trace = trace;
            ExecutionGroupId = executionGroupId;
        }

        public ScoreEventType Type { get; }

        public ScorePhase Phase { get; }

        public ScoreSource Source { get; }

        public int DishInstanceId { get; }

        public string DishId { get; }

        public GridPos? Cell { get; }

        public string Message { get; }

        public SkillExecutionTrace Trace { get; }

        /// <summary>与同次效果产生的 <see cref="ScoreLine"/> 对齐，仅供解释与演出分组。</summary>
        public int ExecutionGroupId { get; }
    }
}
