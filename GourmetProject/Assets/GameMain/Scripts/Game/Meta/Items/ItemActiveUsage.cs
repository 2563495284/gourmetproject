namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 主动道具目标类型 → 「是否需选目标」「在哪些情境可用」的推导。
    /// usableContext 不单独配置，由 <c>targetKind</c> 推导（见 docs/design/道具.md 第 4 节）。
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

        /// <summary>该目标类型在指定情境是否天然存在（能否使用）。</summary>
        public static bool IsUsableIn(cfg.ItemTargetKind kind, ActiveUseContextKind ctx)
        {
            switch (kind)
            {
                case cfg.ItemTargetKind.DiningTableDish:
                    // 餐桌菜是本局临时目标，只在战斗内存在。
                    return ctx == ActiveUseContextKind.Battle;
                case cfg.ItemTargetKind.RecipeDish:
                    // 永久改菜谱：任意情境（含战斗）都可用。
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

        /// <summary>某道具是否可在指定情境使用（由 kind + targetKind 推导）。</summary>
        public static bool CanUse(cfg.Item item, ActiveUseContextKind ctx)
        {
            return item != null
                && item.Kind == cfg.ItemKind.Active
                && IsUsableIn(item.TargetKind, ctx);
        }
    }
}
