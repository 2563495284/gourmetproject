using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Gameplay.Scoring
{
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
            IReadOnlyList<UnservedRecipeDish> unservedRecipeDishes = null)
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

            // 结算优先级层级（甜=+1、苦=-1，多风味累加）：层级高者先结算；同层再按棋盘从上到下、从左到右。
            IEnumerable<DishInstance> alive = DiningTable.Dishes.Where(d => !d.ExcludedFromScore);
            IEnumerable<DishInstance> ordered = reverseDishOrder
                ? alive
                    .OrderByDescending(d => SettlementLayerOf(d, Db))
                    .ThenByDescending(d => d.Placement.Origin.Y)
                    .ThenByDescending(d => d.Placement.Origin.X)
                    .ThenBy(d => d.Id)
                : alive
                    .OrderByDescending(d => SettlementLayerOf(d, Db))
                    .ThenBy(d => d.Placement.Origin.Y)
                    .ThenBy(d => d.Placement.Origin.X)
                    .ThenBy(d => d.Id);

            DishesInDefaultOrder = ordered.ToArray();
        }

        /// <summary>
        /// 该菜的结算优先级层级：累加其所有 <see cref="Model.FlavorEffectType.SettlementLayer"/> 风味的效果值
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

        public GpTable DiningTable { get; }

        public GameplayDatabase Db { get; }

        public IReadOnlyList<DishInstance> DishesInDefaultOrder { get; }

        public float InitialFinalFlat { get; }

        public float InitialFinalMultiplier { get; }

        /// <summary>本次品鉴（meal）开始结算时的全局「欢乐蛋糕层数」。</summary>
        public int InitialHappyCakeLayers { get; }

        /// <summary>由被动道具（如「小份主义」）提供的每道菜额外「视为食物数」加成，计入计数类前提。</summary>
        public int ExtraCountAsPerDish { get; }

        /// <summary>由被动道具（「蛋糕捷径」）提供的蛋糕层数 buff 阈值下调值（每档需求层数 -reduction）。</summary>
        public int CakeLayerThresholdReduction { get; }

        public IScoreHistory History { get; }

        public IReadOnlyList<IScoreEffectSource> EffectSources { get; }

        /// <summary>本次结算时仍未上菜的菜谱条目（槽索引 + dishId），供酸/咸在整体结算末尾遍历。</summary>
        public IReadOnlyList<UnservedRecipeDish> UnservedRecipeDishes { get; }
    }

    /// <summary>一条未上菜的菜谱条目：来自哪个菜谱槽（0 基）+ 菜品变体 id。</summary>
    public readonly struct UnservedRecipeDish
    {
        public UnservedRecipeDish(int slotIndex, string dishId)
        {
            SlotIndex = slotIndex;
            DishId = dishId;
        }

        public int SlotIndex { get; }

        public string DishId { get; }
    }
}
