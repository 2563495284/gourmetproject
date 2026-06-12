using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GpBoard = GourmetProject.Gameplay.Board.Board;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>
    /// 单个菜品标签结算的上下文与累加器。效果通过 <see cref="AddFlat"/> / <see cref="MultiplyBy"/>
    /// 修改本菜品的加法区与乘区；最终贡献 = (美味度 + 加法) × 乘区。
    /// </summary>
    public sealed class EffectContext
    {
        public EffectContext(GpBoard board, GameplayDatabase db, DishInstance dish)
        {
            Board = board;
            Db = db;
            Dish = dish;
            FlatBonus = 0f;
            Multiplier = 1f;
        }

        public GpBoard Board { get; }

        public GameplayDatabase Db { get; }

        public DishInstance Dish { get; }

        /// <summary>当前正在结算的标签。</summary>
        public TagDef Tag { get; set; }

        /// <summary>本菜品累计加法加成。</summary>
        public float FlatBonus { get; private set; }

        /// <summary>本菜品累计乘区（初始 1）。</summary>
        public float Multiplier { get; private set; }

        public void AddFlat(float value) => FlatBonus += value;

        public void MultiplyBy(float value) => Multiplier *= value;
    }
}
