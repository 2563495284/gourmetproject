using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Battle
{
    public enum ServeCueSourceKind
    {
        PassiveItem,
        BossDebuff,
    }

    public enum ServeCueEffectKind
    {
        MultiplierFlat,
        MultiplierFactor,
        BaseScoreFactor,
        GoldDelta,
    }

    public enum ServeCuePresentationKind
    {
        Gain,
        Cancel,
        Penalty,
    }

    /// <summary>正式上菜或“最后菜”目标变化时产生的一条即时表现信号。</summary>
    public sealed class ServeTriggerCue
    {
        public ServeTriggerCue(
            ServeCueSourceKind sourceKind,
            string sourceId,
            string sourceName,
            int dishId,
            ServeCueEffectKind effectKind,
            float value,
            string text,
            ServeCuePresentationKind presentationKind,
            bool pulseSource = true)
        {
            SourceKind = sourceKind;
            SourceId = sourceId ?? string.Empty;
            SourceName = sourceName ?? string.Empty;
            DishId = dishId;
            EffectKind = effectKind;
            Value = value;
            Text = text ?? string.Empty;
            PresentationKind = presentationKind;
            PulseSource = pulseSource;
        }

        public ServeCueSourceKind SourceKind { get; }

        public string SourceId { get; }

        public string SourceName { get; }

        /// <summary>目标菜实例 Id；金币等无菜目标效果为 0。</summary>
        public int DishId { get; }

        public ServeCueEffectKind EffectKind { get; }

        public float Value { get; }

        public string Text { get; }

        public ServeCuePresentationKind PresentationKind { get; }

        /// <summary>同一来源的一组转移 Cue 只让来源 UI 脉冲一次。</summary>
        public bool PulseSource { get; }
    }

    public enum PendingDishActionKind
    {
        Serve,
        Confirm,
    }

    /// <summary>已经占用餐桌格子、但仍等待玩家执行“上菜”或“确认”的食物。</summary>
    public sealed class PendingDishPlacement
    {
        internal PendingDishPlacement(
            DishInstance dish,
            PendingDishActionKind actionKind,
            PreparedServeDish preparedServe,
            bool isOnDiningTable)
        {
            Dish = dish ?? throw new ArgumentNullException(nameof(dish));
            ActionKind = actionKind;
            PreparedServe = preparedServe;
            IsOnDiningTable = isOnDiningTable;
        }

        public DishInstance Dish { get; }

        public PendingDishActionKind ActionKind { get; }

        public bool IsOnDiningTable { get; internal set; }

        internal PreparedServeDish PreparedServe { get; }
    }

    public readonly struct PendingDishConfirmResult
    {
        public PendingDishConfirmResult(
            bool success,
            DishInstance dish,
            PendingDishActionKind actionKind,
            bool removedAfterServe = false)
        {
            Success = success;
            Dish = dish;
            ActionKind = actionKind;
            RemovedAfterServe = removedAfterServe;
        }

        public bool Success { get; }

        public DishInstance Dish { get; }

        public PendingDishActionKind ActionKind { get; }

        public bool RemovedAfterServe { get; }

        public static PendingDishConfirmResult Fail()
            => new PendingDishConfirmResult(false, null, PendingDishActionKind.Confirm);
    }

    public enum ServePrepareOutcome
    {
        Prepared,
        AlreadyPrepared,
        SlotEmpty,
        NoFittingDish,
        LimitReached,

        /// <summary>餐桌上仍有等待“上菜/确认”的预摆菜。</summary>
        PendingPlacement,
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
    /// 预摆前不占用餐桌；预摆后会参与实时预览，确认前不会触发正式上菜效果。
    /// </summary>
    public sealed class PreparedServeDish
    {
        internal PreparedServeDish(
            int slotIndex,
            RecipeSlotEntry entry,
            DishInstance dish,
            IReadOnlyList<Placement> placements,
            bool isBossInsertedDish = false)
        {
            SlotIndex = slotIndex;
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
            Dish = dish ?? throw new ArgumentNullException(nameof(dish));
            Placements = placements ?? Array.Empty<Placement>();
            IsBossInsertedDish = isBossInsertedDish;
        }

        public int SlotIndex { get; }

        public RecipeSlotEntry Entry { get; }

        public DishInstance Dish { get; }

        public DishDef Definition => Dish.Def;

        public IReadOnlyList<Placement> Placements { get; }

        /// <summary>是否为 Boss Debuff 在自动出菜序列中额外插入的食物。</summary>
        public bool IsBossInsertedDish { get; }

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
