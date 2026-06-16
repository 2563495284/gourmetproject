using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GpBoard = GourmetProject.Gameplay.Board.Board;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>
    /// 旧标签上下文名的兼容包装。新结算效果请直接使用 <see cref="ScoreContext"/>。
    /// </summary>
    public sealed class EffectContext : ScoreContext
    {
        public EffectContext(GpBoard board, GameplayDatabase db, DishInstance dish)
            : base(new ScoreSnapshot(board, db))
        {
            BeginDish(dish);
        }
    }
}
