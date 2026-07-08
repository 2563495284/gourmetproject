using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GpBoard = GourmetProject.Gameplay.Board.Board;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>一次结算开始时捕获的只读输入。</summary>
    public sealed class ScoreSnapshot
    {
        public ScoreSnapshot(
            GpBoard board,
            GameplayDatabase db,
            float finalFlat = 0f,
            float finalMultiplier = 1f,
            IEnumerable<IScoreEffectSource> effectSources = null,
            IScoreHistory history = null,
            int initialHappyCakeLayers = 0,
            int extraCountAsPerDish = 0,
            int cakeLayerThresholdReduction = 0)
        {
            Board = board ?? throw new ArgumentNullException(nameof(board));
            Db = db ?? throw new ArgumentNullException(nameof(db));
            InitialFinalFlat = finalFlat;
            InitialFinalMultiplier = finalMultiplier;
            InitialHappyCakeLayers = initialHappyCakeLayers < 0 ? 0 : initialHappyCakeLayers;
            ExtraCountAsPerDish = extraCountAsPerDish < 0 ? 0 : extraCountAsPerDish;
            CakeLayerThresholdReduction = cakeLayerThresholdReduction < 0 ? 0 : cakeLayerThresholdReduction;
            History = history ?? EmptyScoreHistory.Instance;
            EffectSources = (effectSources ?? Array.Empty<IScoreEffectSource>()).ToArray();
            DishesInDefaultOrder = Board.Dishes
                .OrderBy(d => d.Placement.Origin.Y)
                .ThenBy(d => d.Placement.Origin.X)
                .ThenBy(d => d.Id)
                .ToArray();
        }

        public GpBoard Board { get; }

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
    }
}
