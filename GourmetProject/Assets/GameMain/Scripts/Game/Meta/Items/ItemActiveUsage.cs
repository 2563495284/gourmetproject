using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 消耗品目标类型 → 「是否需选目标」「在哪些情境可用」的推导。
    /// usableContext 不单独配置，由 <c>targetKind</c> 推导（见 docs/design/装饰品和消耗品.md 第 4 节）。
    /// </summary>
    public static class ItemActiveUsage
    {
        /// <summary>该目标类型是否需要玩家先选目标（None/Global 为无目标行为）。</summary>
        public static bool RequiresTarget(cfg.ItemTargetKind kind)
        {
            switch (kind)
            {
                case cfg.ItemTargetKind.None:
                case cfg.ItemTargetKind.Global:
                    return false;
                default:
                    return true;
            }
        }

        public static bool RequiresTarget(ItemDefinition item)
        {
            if (item == null)
            {
                return false;
            }

            return IsTimelineAddEffect(item.EffectType)
                || item.EffectType == ItemEffectTypes.TimelineDeleteNode
                || item.EffectType == ItemEffectTypes.TimelineExecuteFuture
                || item.EffectType == ItemEffectTypes.TimelineExecutePast
                || RequiresTarget(item.TargetKind);
        }

        /// <summary>该目标类型在指定情境是否天然存在（能否使用）。</summary>
        public static bool IsUsableIn(cfg.ItemTargetKind kind, ActiveUseContextKind ctx)
        {
            switch (kind)
            {
                case cfg.ItemTargetKind.DiningTableDish:
                    // 餐桌菜是本局临时目标，只在经营挑战内存在。
                    return ctx == ActiveUseContextKind.Battle;
                case cfg.ItemTargetKind.RecipeDish:
                    // 永久改食谱：任意情境（含经营挑战）都可用。
                    return true;
                case cfg.ItemTargetKind.DiningTableCell:
                case cfg.ItemTargetKind.FlavorSlot:
                    // 需要「可编辑内容」的情境：奖励界面只做领取，不开放。
                    return ctx != ActiveUseContextKind.Reward;
                case cfg.ItemTargetKind.None:
                case cfg.ItemTargetKind.Global:
                default:
                    // 无目标/全局：全情境放行（时间轴类等由 effectType 处理端再限制）。
                    return true;
            }
        }

        /// <summary>某装饰品和消耗品是否可在指定情境使用（由 kind + targetKind + effectType 推导）。</summary>
        public static bool CanUse(ItemDefinition item, ActiveUseContextKind ctx)
        {
            if (item == null || item.Kind != cfg.ItemKind.Active)
            {
                return false;
            }

            if (IsTimelineAxisTargetEffect(item.EffectType))
            {
                return IsActionAxisContext(ctx);
            }

            if (IsScheduleEffect(item.EffectType))
            {
                return IsScheduleUsableIn(item.EffectType, ctx);
            }

            if (RequiresFoodBattle(item))
            {
                return ctx == ActiveUseContextKind.Battle;
            }

            return IsUsableIn(item.TargetKind, ctx);
        }

        /// <summary>
        /// 实机与无界面模拟共用的局内使用资格。界面锁定、编辑态等表现限制由调用方另行检查。
        /// </summary>
        public static bool CanUse(
            GameRun run,
            ItemDefinition item,
            ActiveUseContextKind ctx,
            out string reason)
        {
            reason = string.Empty;
            if (run == null || item == null || !run.HasItem(item.Id))
            {
                reason = "没有可用装饰品和消耗品。";
                return false;
            }

            if (new ItemRuntime(run).BlocksActiveItems())
            {
                reason = "当前被动效果禁止使用消耗品。";
                return false;
            }

            if (IsTodoTimelineEffect(item.EffectType))
            {
                reason = "功能开发中：需要玩家在时间轴上指定位置/节点。";
                return false;
            }

            if (!CanUse(item, ctx))
            {
                reason = "现在不是使用时机。";
                return false;
            }

            return true;
        }

        /// <summary>调味小票只能在尚未结算的 Food 经营挑战主界面使用。</summary>
        public static bool RequiresFoodBattle(ItemDefinition item)
        {
            return item != null && item.EffectType == ItemEffectTypes.AddFlavor;
        }

        /// <summary>排程类小票效果；每种效果的实际使用情境由 <see cref="IsScheduleUsableIn"/> 判断。</summary>
        public static bool IsScheduleEffect(string effectType)
        {
            switch (effectType)
            {
                case ItemEffectTypes.RerollAction:
                case ItemEffectTypes.DoubleNextBusinessReward:
                case ItemEffectTypes.ResetBossDebuff:
                case ItemEffectTypes.TimelineExecuteNext:
                case ItemEffectTypes.TimelineExecuteFuture:
                case ItemEffectTypes.TimelineExecutePast:
                case ItemEffectTypes.TimelineAddRewardNode:
                case ItemEffectTypes.TimelineAddInterestNode:
                case ItemEffectTypes.TimelineAddShopNode:
                case ItemEffectTypes.TimelineAddLotteryNode:
                case ItemEffectTypes.TimelineAddRestoreHeartNode:
                case ItemEffectTypes.TimelineDeleteNode:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsScheduleUsableIn(string effectType, ActiveUseContextKind ctx)
        {
            switch (effectType)
            {
                case ItemEffectTypes.RerollAction:
                    return ctx == ActiveUseContextKind.ActionSelect;
                case ItemEffectTypes.DoubleNextBusinessReward:
                    return true;
                case ItemEffectTypes.ResetBossDebuff:
                    return ctx == ActiveUseContextKind.ActionSelect
                        || ctx == ActiveUseContextKind.Shop
                        || ctx == ActiveUseContextKind.Event
                        || ctx == ActiveUseContextKind.Reward;
                case ItemEffectTypes.TimelineExecuteNext:
                    return IsActionAxisContext(ctx);
                default:
                    return false;
            }
        }

        /// <summary>需要直接在行动轴上选择日期或节点的消耗品效果。</summary>
        public static bool IsTimelineAxisTargetEffect(string effectType)
        {
            return IsTimelineAddEffect(effectType)
                || effectType == ItemEffectTypes.TimelineDeleteNode
                || effectType == ItemEffectTypes.TimelineExecuteFuture
                || effectType == ItemEffectTypes.TimelineExecutePast;
        }

        /// <summary>常规页面中会显示行动轴的使用情境。</summary>
        internal static bool IsActionAxisContext(ActiveUseContextKind ctx)
        {
            return ctx == ActiveUseContextKind.ActionSelect
                || ctx == ActiveUseContextKind.Shop
                || ctx == ActiveUseContextKind.Event;
        }

        public static bool IsTimelineAddEffect(string effectType)
        {
            return effectType == ItemEffectTypes.TimelineAddRewardNode
                || effectType == ItemEffectTypes.TimelineAddInterestNode
                || effectType == ItemEffectTypes.TimelineAddShopNode
                || effectType == ItemEffectTypes.TimelineAddLotteryNode
                || effectType == ItemEffectTypes.TimelineAddRestoreHeartNode;
        }

        public static bool IsTodoTimelineEffect(string effectType)
        {
            return false;
        }
    }
}
