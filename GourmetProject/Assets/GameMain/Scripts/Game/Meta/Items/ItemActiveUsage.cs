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
                case cfg.ItemTargetKind.Material:
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

            if (item.EffectType == ItemEffectTypes.HalfNextActionCost)
            {
                return ctx != ActiveUseContextKind.Battle;
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

        /// <summary>调味与铺台小票只能在尚未结算的 Food 经营挑战主界面使用。</summary>
        public static bool RequiresFoodBattle(ItemDefinition item)
        {
            return item != null
                && (item.EffectType == ItemEffectTypes.AddFlavor
                    || item.EffectType == ItemEffectTypes.AddMaterial);
        }

        /// <summary>排程小票效果（操作时间轴/Boss，局外/地图专用）。</summary>
        public static bool IsScheduleEffect(string effectType)
        {
            switch (effectType)
            {
                case ItemEffectTypes.RerollAction:
                case ItemEffectTypes.ResetBossDebuff:
                case ItemEffectTypes.TimelineExecuteNext:
                case ItemEffectTypes.TimelineExecuteFuture:
                case ItemEffectTypes.TimelineExecutePast:
                case ItemEffectTypes.TimelineAddRewardNode:
                case ItemEffectTypes.TimelineAddInterestNode:
                case ItemEffectTypes.TimelineAddShopNode:
                case ItemEffectTypes.TimelineAddLotteryNode:
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
                case ItemEffectTypes.ResetBossDebuff:
                    return ctx == ActiveUseContextKind.ActionSelect
                        || ctx == ActiveUseContextKind.Shop
                        || ctx == ActiveUseContextKind.Event
                        || ctx == ActiveUseContextKind.Reward;
                case ItemEffectTypes.TimelineExecuteFuture:
                case ItemEffectTypes.TimelineExecutePast:
                    return ctx == ActiveUseContextKind.ActionSelect;
                case ItemEffectTypes.TimelineExecuteNext:
                case ItemEffectTypes.TimelineAddRewardNode:
                case ItemEffectTypes.TimelineAddInterestNode:
                case ItemEffectTypes.TimelineAddShopNode:
                case ItemEffectTypes.TimelineAddLotteryNode:
                case ItemEffectTypes.TimelineDeleteNode:
                    return ctx == ActiveUseContextKind.ActionSelect
                        || ctx == ActiveUseContextKind.Shop
                        || ctx == ActiveUseContextKind.Event;
                default:
                    return false;
            }
        }

        public static bool IsTimelineAddEffect(string effectType)
        {
            return effectType == ItemEffectTypes.TimelineAddRewardNode
                || effectType == ItemEffectTypes.TimelineAddInterestNode
                || effectType == ItemEffectTypes.TimelineAddShopNode
                || effectType == ItemEffectTypes.TimelineAddLotteryNode;
        }

        public static bool IsTodoTimelineEffect(string effectType)
        {
            return false;
        }
    }
}
