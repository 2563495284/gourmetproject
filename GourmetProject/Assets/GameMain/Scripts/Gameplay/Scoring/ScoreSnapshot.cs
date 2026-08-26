using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>每轮甜蜜传递额外目标数的二选一加权随机规格。</summary>
    public readonly struct SweetTransferExtraTargetRollSpec
    {
        private const int RollScale = 10000;

        public SweetTransferExtraTargetRollSpec(
            int firstCount,
            int secondCount,
            int firstWeight,
            int secondWeight)
        {
            FirstCount = Math.Max(0, firstCount);
            SecondCount = Math.Max(0, secondCount);
            FirstWeight = Math.Max(0, firstWeight);
            SecondWeight = Math.Max(0, secondWeight);
        }

        public int FirstCount { get; }

        public int SecondCount { get; }

        public int FirstWeight { get; }

        public int SecondWeight { get; }

        public bool IsValid => FirstWeight + SecondWeight > 0;

        /// <summary>有随机流时按权重抽取；纯预计算取权重更高者，同权重时取较小值。</summary>
        public int Resolve(Func<int, int, int> randomIntegerSelector)
        {
            if (!IsValid)
            {
                return 0;
            }

            if (FirstCount == SecondCount)
            {
                return FirstCount;
            }

            if (randomIntegerSelector == null)
            {
                if (FirstWeight == SecondWeight)
                {
                    return Math.Min(FirstCount, SecondCount);
                }

                return FirstWeight > SecondWeight ? FirstCount : SecondCount;
            }

            int totalWeight = FirstWeight + SecondWeight;
            int firstThreshold = Math.Max(
                0,
                Math.Min(
                    RollScale,
                    (int)Math.Round(
                        FirstWeight / (double)totalWeight * RollScale,
                        MidpointRounding.AwayFromZero)));
            int roll = Math.Max(0, Math.Min(RollScale - 1, randomIntegerSelector(0, RollScale - 1)));
            return roll < firstThreshold ? FirstCount : SecondCount;
        }
    }

    /// <summary>一次结算开始时捕获的只读输入。</summary>
    public sealed class ScoreSnapshot
    {
        public ScoreSnapshot(
            GpTable board,
            GameplayDatabase db,
            float finalFlat = 0f,
            float finalMultiplier = 1f,
            IEnumerable<IScoreEffectSource> effectSources = null,
            IScoreHistory history = null,
            int initialHappyCakeLayers = 0,
            int extraCountAsPerDish = 0,
            int cakeLayerThresholdReduction = 0,
            bool reverseDishOrder = false,
            IReadOnlyList<UnservedRecipeDish> unservedRecipeDishes = null,
            Func<IReadOnlyList<string>, int, IReadOnlyList<string>> copySkillSelector = null,
            Func<IReadOnlyList<int>, int, IReadOnlyList<int>> transferTargetSelector = null,
            Func<int, int, int> randomIntegerSelector = null,
            int passiveItemCount = 0,
            int remainingFoodDiscards = 0,
            int sweetTransferExtraTargetCount = 0,
            float sweetTransferTargetMultiplierFlat = 0f,
            float sweetTransferSourceMultiplierFlat = 0f,
            bool captureDiagnostics = true,
            IReadOnlyList<SweetTransferExtraTargetRollSpec> sweetTransferExtraTargetRolls = null)
        {
            DiningTable = board ?? throw new ArgumentNullException(nameof(board));
            Db = db ?? throw new ArgumentNullException(nameof(db));
            UnservedRecipeDishes = unservedRecipeDishes ?? Array.Empty<UnservedRecipeDish>();
            InitialFinalFlat = finalFlat;
            InitialFinalMultiplier = finalMultiplier;
            InitialHappyCakeLayers = initialHappyCakeLayers < 0 ? 0 : initialHappyCakeLayers;
            ExtraCountAsPerDish = extraCountAsPerDish < 0 ? 0 : extraCountAsPerDish;
            CakeLayerThresholdReduction = cakeLayerThresholdReduction < 0 ? 0 : cakeLayerThresholdReduction;
            History = history ?? EmptyScoreHistory.Instance;
            EffectSources = (effectSources ?? Array.Empty<IScoreEffectSource>()).ToArray();
            CopySkillSelector = copySkillSelector;
            TransferTargetSelector = transferTargetSelector;
            RandomIntegerSelector = randomIntegerSelector;
            PassiveItemCount = Math.Max(0, passiveItemCount);
            RemainingFoodDiscards = Math.Max(0, remainingFoodDiscards);
            SweetTransferExtraTargetCount = Math.Max(0, sweetTransferExtraTargetCount);
            SweetTransferExtraTargetRolls = (sweetTransferExtraTargetRolls
                ?? Array.Empty<SweetTransferExtraTargetRollSpec>())
                .Where(spec => spec.IsValid)
                .ToArray();
            SweetTransferTargetMultiplierFlat = Math.Max(0f, sweetTransferTargetMultiplierFlat);
            SweetTransferSourceMultiplierFlat = Math.Max(0f, sweetTransferSourceMultiplierFlat);
            CaptureDiagnostics = captureDiagnostics;

            // 结算优先级层级（甜=+1、苦=-1，多风味累加）：层级高者先结算；同层再按棋盘从上到下、从左到右。
            IEnumerable<DishInstance> alive = DiningTable.Dishes.Where(d => !d.ExcludedFromScore);
            IEnumerable<DishInstance> ordered = reverseDishOrder
                ? alive
                    .OrderByDescending(d => SettlementLayerOf(d, Db))
                    .ThenByDescending(d => SettlementBoardOrderOf(d, DiningTable.Width))
                    .ThenBy(d => d.Id)
                : alive
                    .OrderByDescending(d => SettlementLayerOf(d, Db))
                    .ThenBy(d => SettlementBoardOrderOf(d, DiningTable.Width))
                    .ThenBy(d => d.Id);

            DishesInDefaultOrder = ordered.ToArray();
        }

        /// <summary>
        /// 该食物的结算优先级层级：累加其所有 <see cref="Model.FlavorEffectType.SettlementLayer"/> 风味的效果值
        /// （甜 +1、苦 -1）。默认 0。层级越大越先结算。
        /// </summary>
        public static int SettlementLayerOf(DishInstance dish, GameplayDatabase db)
        {
            if (dish == null || db == null)
            {
                return 0;
            }

            int layer = 0;
            foreach (string flavorId in dish.FlavorIds)
            {
                Model.FlavorDef flavor = db.GetFlavor(flavorId);
                if (flavor != null && flavor.EffectType == Model.FlavorEffectType.SettlementLayer)
                {
                    layer += (int)flavor.EffectValue;
                }
            }

            return layer;
        }

        /// <summary>
        /// 同层食物按实际占用格中最靠前的一格排序。
        /// 避免带空洞形状与其他菜共享 placement origin 时退化为实例创建顺序。
        /// </summary>
        public static int SettlementBoardOrderOf(DishInstance dish, int boardWidth)
        {
            if (dish == null)
            {
                return int.MaxValue;
            }

            int width = Math.Max(1, boardWidth);
            int result = int.MaxValue;
            foreach (GridPos cell in dish.OccupiedCells)
            {
                int order = cell.Y * width + cell.X;
                result = Math.Min(result, order);
            }

            if (result != int.MaxValue)
            {
                return result;
            }

            return dish.Placement.Origin.Y * width + dish.Placement.Origin.X;
        }

        public GpTable DiningTable { get; }

        public GameplayDatabase Db { get; }

        public IReadOnlyList<DishInstance> DishesInDefaultOrder { get; }

        public float InitialFinalFlat { get; }

        public float InitialFinalMultiplier { get; }

        /// <summary>本场经营挑战（meal）开始结算时的全局「欢乐蛋糕层数」。</summary>
        public int InitialHappyCakeLayers { get; }

        /// <summary>调用方直接提供的每个食物额外「视为食物数」加成；装饰品效果改由 BeforeAll 结算队列触发。</summary>
        public int ExtraCountAsPerDish { get; }

        /// <summary>由装饰品（「蛋糕捷径」）提供的蛋糕层数 buff 阈值下调值（每档需求层数 -reduction）。</summary>
        public int CakeLayerThresholdReduction { get; }

        public IScoreHistory History { get; }

        public IReadOnlyList<IScoreEffectSource> EffectSources { get; }

        /// <summary>
        /// 技能复制的选择器。正式结算由 BattleSession 注入随机流；预览未注入时使用稳定顺序。
        /// </summary>
        public Func<IReadOnlyList<string>, int, IReadOnlyList<string>> CopySkillSelector { get; }

        /// <summary>
        /// 甜蜜传递目标选择器。正式结算由 BattleSession 注入随机流；预览未注入时使用稳定顺序。
        /// </summary>
        public Func<IReadOnlyList<int>, int, IReadOnlyList<int>> TransferTargetSelector { get; }

        /// <summary>闭区间整数随机选择器；正式结算注入会话 RNG，预览为空时使用区间下限。</summary>
        public Func<int, int, int> RandomIntegerSelector { get; }

        /// <summary>本场结算开始时持有的被动装饰品数量。</summary>
        public int PassiveItemCount { get; }

        /// <summary>本次结算开始时尚未使用的食物丢弃次数。</summary>
        public int RemainingFoodDiscards { get; }

        /// <summary>装饰品为每次甜蜜传递额外增加的目标数。</summary>
        public int SweetTransferExtraTargetCount { get; }

        /// <summary>装饰品为每次甜蜜传递独立判定的额外目标加权随机规格。</summary>
        public IReadOnlyList<SweetTransferExtraTargetRollSpec> SweetTransferExtraTargetRolls { get; }

        /// <summary>每次成功甜蜜传递时，被传递方在本次结算获得的倍率加值。</summary>
        public float SweetTransferTargetMultiplierFlat { get; }

        /// <summary>每成功传递一个目标时，传递方在本次结算获得的倍率加值。</summary>
        public float SweetTransferSourceMultiplierFlat { get; }

        /// <summary>本次结算时仍未上菜的食谱条目（槽索引 + dishId），供酸/咸在结算开始时遍历。</summary>
        public IReadOnlyList<UnservedRecipeDish> UnservedRecipeDishes { get; }

        /// <summary>
        /// 是否捕获仅供解释与演出的 ScoreLine、ScoreEvent 和 SkillExecutionTrace。
        /// 关闭时计分规则、命令及副作用请求仍完整执行。
        /// </summary>
        public bool CaptureDiagnostics { get; }
    }

    /// <summary>一条未上菜的食谱条目：来源槽、食物 id，以及按获得顺序排列的全部风味。</summary>
    public readonly struct UnservedRecipeDish
    {
        public UnservedRecipeDish(int slotIndex, string dishId, IReadOnlyList<string> flavorIds = null)
        {
            SlotIndex = slotIndex;
            DishId = dishId;
            FlavorIds = flavorIds ?? Array.Empty<string>();
        }

        public int SlotIndex { get; }

        public string DishId { get; }

        public IReadOnlyList<string> FlavorIds { get; }
    }
}
