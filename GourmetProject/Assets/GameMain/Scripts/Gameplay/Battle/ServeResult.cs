using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Battle
{
    public enum ServePrepareOutcome
    {
        Prepared,
        AlreadyPrepared,
        SlotEmpty,
        NoFittingDish,
        LimitReached,
    }

    public enum ServeOutcome
    {
        /// <summary>成功上菜并摆上餐桌。</summary>
        Placed,

        /// <summary>该食谱已无可上食物。</summary>
        SlotEmpty,

        /// <summary>食谱里没有任何1 个食物能放进当前餐桌（餐桌空间不足）。</summary>
        NoFittingDish,

        /// <summary>本局上菜次数已达上限（星级评鉴机制「限量供应」修正）。</summary>
        LimitReached,

        /// <summary>出菜口没有等待摆放的食物。</summary>
        NoPreparedDish,

        /// <summary>玩家选择的位置已失效或不能容纳本次食物。</summary>
        InvalidPlacement,
    }

    /// <summary>
    /// 已从食谱随机取出、正在出菜口等待玩家摆放的食物。
    /// 在 <see cref="BattleSession.CommitPreparedServe"/> 前不会占用餐桌，也不会触发上菜效果。
    /// </summary>
    public sealed class PreparedServeDish
    {
        internal PreparedServeDish(
            int slotIndex,
            RecipeSlotEntry entry,
            DishInstance dish,
            IReadOnlyList<Placement> placements)
        {
            SlotIndex = slotIndex;
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
            Dish = dish ?? throw new ArgumentNullException(nameof(dish));
            Placements = placements ?? Array.Empty<Placement>();
        }

        public int SlotIndex { get; }

        public RecipeSlotEntry Entry { get; }

        public DishInstance Dish { get; }

        public DishDef Definition => Dish.Def;

        public IReadOnlyList<Placement> Placements { get; }

        public bool Contains(Placement placement)
        {
            for (int i = 0; i < Placements.Count; i++)
            {
                Placement candidate = Placements[i];
                if (candidate.RotationIndex == placement.RotationIndex
                    && candidate.Origin.Equals(placement.Origin))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public readonly struct ServePrepareResult
    {
        public ServePrepareResult(ServePrepareOutcome outcome, PreparedServeDish preparedDish)
        {
            Outcome = outcome;
            PreparedDish = preparedDish;
        }

        public ServePrepareOutcome Outcome { get; }

        public PreparedServeDish PreparedDish { get; }

        public bool Success => Outcome == ServePrepareOutcome.Prepared && PreparedDish != null;

        public static ServePrepareResult Fail(ServePrepareOutcome outcome)
            => new ServePrepareResult(outcome, null);
    }

    /// <summary>一次「上菜」的结果。</summary>
    public readonly struct ServeResult
    {
        public ServeResult(ServeOutcome outcome, DishInstance dish, bool removedAfterServe = false)
        {
            Outcome = outcome;
            Dish = dish;
            RemovedAfterServe = removedAfterServe;
        }

        public ServeOutcome Outcome { get; }

        public DishInstance Dish { get; }

        /// <summary>
        /// 食物完成上菜触发后是否立刻被局内规则移除。
        /// 表现层仍可用 <see cref="Dish"/> 播完落地与消失演出，但不能把它重建回餐桌。
        /// </summary>
        public bool RemovedAfterServe { get; }

        public bool Success => Outcome == ServeOutcome.Placed;

        public static ServeResult Fail(ServeOutcome outcome) => new ServeResult(outcome, null);
    }
}
