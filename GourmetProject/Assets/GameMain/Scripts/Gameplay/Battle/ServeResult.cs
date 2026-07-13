using GourmetProject.Gameplay.Board;

namespace GourmetProject.Gameplay.Battle
{
    public enum ServeOutcome
    {
        /// <summary>成功上菜并摆上餐桌。</summary>
        Placed,

        /// <summary>该菜谱已无可上菜品。</summary>
        SlotEmpty,

        /// <summary>菜谱里没有任何一道菜能放进当前餐桌（餐桌空间不足）。</summary>
        NoFittingDish,

        /// <summary>本局上菜次数已达上限（Boss 机制「限量供应」修正）。</summary>
        LimitReached,
    }

    /// <summary>一次「上菜」的结果。</summary>
    public readonly struct ServeResult
    {
        public ServeResult(ServeOutcome outcome, DishInstance dish)
        {
            Outcome = outcome;
            Dish = dish;
        }

        public ServeOutcome Outcome { get; }

        public DishInstance Dish { get; }

        public bool Success => Outcome == ServeOutcome.Placed;

        public static ServeResult Fail(ServeOutcome outcome) => new ServeResult(outcome, null);
    }
}
