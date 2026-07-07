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
            int initialHappyCakeLayers = 0)
        {
            Board = board ?? throw new ArgumentNullException(nameof(board));
            Db = db ?? throw new ArgumentNullException(nameof(db));
            InitialFinalFlat = finalFlat;
            InitialFinalMultiplier = finalMultiplier;
            InitialHappyCakeLayers = initialHappyCakeLayers < 0 ? 0 : initialHappyCakeLayers;
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

        public IScoreHistory History { get; }

        public IReadOnlyList<IScoreEffectSource> EffectSources { get; }
    }
}
