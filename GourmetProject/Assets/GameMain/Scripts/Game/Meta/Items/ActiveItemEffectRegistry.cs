using System;
using System.Collections.Generic;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;

namespace GourmetProject.Game.Meta
{
    public readonly struct ActiveItemUseResult
    {
        public ActiveItemUseResult(
            bool success,
            bool boardChanged,
            string message,
            bool actionChoicesChanged = false,
            string createdTimelineNodeId = "")
        {
            Success = success;
            BoardChanged = boardChanged;
            Message = message;
            ActionChoicesChanged = actionChoicesChanged;
            CreatedTimelineNodeId = createdTimelineNodeId ?? string.Empty;
        }

        public bool Success { get; }

        public bool BoardChanged { get; }

        public string Message { get; }

        public bool ActionChoicesChanged { get; }

        public string CreatedTimelineNodeId { get; }
    }

    /// <summary>
    /// 消耗品效果派发入口。效果语义集中在此、与情境解耦：
    /// 各 <see cref="IActiveUseContext"/>（经营挑战 / 局外）只提供能力钩子，表现层只负责点击/选目标与刷新。
    /// </summary>
    public static class ActiveItemEffectRegistry
    {
        /// <summary>情境无关的消耗品效果落地。</summary>
        public static ActiveItemUseResult Apply(IActiveUseContext ctx, ItemDefinition item, IReadOnlyList<ActiveTarget> targets)
        {
            if (ctx == null || item == null)
            {
                return new ActiveItemUseResult(false, false, "现在不能使用消耗品。");
            }

            switch (item.EffectType)
            {
                case ItemEffectTypes.ClearBoard:
                    return ctx.ClearBoard()
                        ? new ActiveItemUseResult(true, true, $"{item.Name}：已清空餐桌。")
                        : new ActiveItemUseResult(false, false, $"{item.Name}：现在无法清空餐桌。");

                case ItemEffectTypes.ExtraServe:
                    return ctx.ExtraServe()
                        ? new ActiveItemUseResult(true, true, $"{item.Name}：额外上了1 个食物。")
                        : new ActiveItemUseResult(false, false, $"{item.Name}：没有能放下的食物了。");

                case ItemEffectTypes.GoldNow:
                    if (ctx.Run != null)
                    {
                        ctx.Run.Gold += (int)item.EffectValue;
                    }

                    return new ActiveItemUseResult(true, false, $"{item.Name}：获得 {(int)item.EffectValue} 金币。");

                // —— 需选目标的目标操作族（对标杀戮尖塔2 药水的 OnUse(target)）——
                case ItemEffectTypes.AddScore:
                    return ApplyToTargets(targets, t => ctx.AddPermanentScore(t, item.EffectValue),
                        item, "已强化选中的食物。", "现在无法强化选中的食物。");

                case ItemEffectTypes.AddCountAs:
                    return ApplyToTargets(targets, t => ctx.AddCountAs(t, (int)item.EffectValue),
                        item, "已增加选中菜的计数。", "现在无法增加选中菜的计数。");

                case ItemEffectTypes.DestroyDish:
                    return ApplyToTargets(targets, ctx.DestroyDish,
                        item, "已移除选中的食物。", "现在无法移除选中的食物。");

                case ItemEffectTypes.DuplicateDish:
                    // 复制/生成类推进主动使用序号，保证同种子下可复现（见 docs/design/装饰品和消耗品.md §7）。
                    return ApplyToTargets(targets, t => ctx.DuplicateDish(t, ctx.Run?.NextActiveUseKey()),
                        item, "已复制选中的食物。", "现在无法复制（空位不足）。");

                // —— 调味小票：给食谱目标食物永久附加风味（effectParam=风味 id）——
                case ItemEffectTypes.AddFlavor:
                    return ApplyToTargets(targets, t => ctx.AddFlavorToDish(t, item.EffectParam),
                        item, "已为选中的食物附加风味。", "现在无法为选中的食物附加风味。");

                case ItemEffectTypes.EnhanceFlavor:
                    return ApplyToTargets(targets, t => ctx.AddFlavorToDish(t, item.EffectParam),
                        item, "已强化选中的风味。", "现在无法强化选中的风味。");

                case ItemEffectTypes.ConvertFlavor:
                    return ApplyToTargets(targets, t => ctx.ConvertFlavorOnDish(t, item.EffectParam),
                        item, "已转换选中的风味。", "现在无法转换选中的风味。");

                case ItemEffectTypes.RemoveFlavor:
                    return ApplyToTargets(targets, t => ctx.RemoveFlavorFromDish(t, item.EffectParam),
                        item, "已移除选中的风味。", "现在无法移除选中的风味。");

                case ItemEffectTypes.ConvertCategory:
                    return ApplyToTargets(targets, t => ctx.ConvertDishCategory(t, item.EffectParam),
                        item, "已转换选中的分类。", "当前食物分类暂不支持运行时转换。");

                // —— 铺台小票：给餐桌格永久附加材质（effectParam=材质 id）——
                case ItemEffectTypes.AddMaterial:
                    return ApplyToTargets(targets, t => ctx.AddMaterialToCell(t, item.EffectParam),
                        item, "已为选中的格子附加材质。", "现在无法为选中的格子附加材质。");

                case ItemEffectTypes.GenerateDish:
                    return ApplyToTargets(targets, t => ctx.GenerateDish(t, item.EffectParam, ctx.Run?.NextActiveUseKey()),
                        item, "已生成新的食物。", "现在无法生成新的食物。");

                // —— 排程小票：Global 无目标，直接调情境钩子（仅地图支持，经营挑战返回 false）——
                case ItemEffectTypes.RerollAction:
                    return ctx.RerollCurrentAction()
                        ? new ActiveItemUseResult(true, false, $"{item.Name}：已重掷当前行动选项。", actionChoicesChanged: true)
                        : new ActiveItemUseResult(false, false, $"{item.Name}：现在无法重掷行动。");

                case ItemEffectTypes.DoubleNextBusinessReward:
                    return ctx.AddNextBusinessRewardDoubleStack()
                        ? new ActiveItemUseResult(true, false, $"{item.Name}：下一次营业将随机额外重抽一类奖励。")
                        : new ActiveItemUseResult(false, false, $"{item.Name}：现在无法使用。");

                case ItemEffectTypes.HalfNextActionCost:
                    return ctx.AddNextActionHalfCostStack()
                        ? new ActiveItemUseResult(true, false, $"{item.Name}：下一次普通行动耗时减半。", actionChoicesChanged: true)
                        : new ActiveItemUseResult(false, false, $"{item.Name}：现在无法使用。");

                case ItemEffectTypes.ResetBossDebuff:
                    // TODO: 为星级评鉴调整单补专属重掷表现；当前先完成确定性随机结果与时间轴提示刷新。
                    return ctx.ResetLastBossDebuff()
                        ? new ActiveItemUseResult(true, false, $"{item.Name}：已重新随机最后一个 星级评鉴节点的餐食类别。")
                        : new ActiveItemUseResult(false, false, $"{item.Name}：没有可重掷的 星级评鉴节点。");

                case ItemEffectTypes.TimelineExecuteFuture:
                case ItemEffectTypes.TimelineExecutePast:
                    if (targets == null || targets.Count == 0)
                    {
                        return new ActiveItemUseResult(false, false, $"{item.Name}：请先选择节点。");
                    }

                    string clonedNodeId =
                        ctx.CloneTimelineNodeToCurrentOrNextIntegerDay(targets[0].Id, item.Id);
                    int destinationDay = ctx.Run != null
                        ? TimelineMath.CurrentOrNextIntegerDay(ctx.Run.CurrentDay)
                        : 0;
                    return !string.IsNullOrEmpty(clonedNodeId)
                        ? new ActiveItemUseResult(
                            true,
                            false,
                            $"{item.Name}：已复制到第 {destinationDay} 天。",
                            createdTimelineNodeId: clonedNodeId)
                        : new ActiveItemUseResult(
                            false,
                            false,
                            $"{item.Name}：当前或下一个整数日无法放置所选节点。");

                case ItemEffectTypes.TimelineAddRewardNode:
                case ItemEffectTypes.TimelineAddInterestNode:
                case ItemEffectTypes.TimelineAddShopNode:
                    if (targets == null || targets.Count == 0)
                    {
                        return new ActiveItemUseResult(false, false, $"{item.Name}：请先选择日期。");
                    }

                    string addedNodeId = ctx.AddTimelineNodeWithId(item.EffectParam, targets[0].X, item.Id);
                    return !string.IsNullOrEmpty(addedNodeId)
                        ? new ActiveItemUseResult(
                            true,
                            false,
                            $"{item.Name}：已添加到第 {targets[0].X} 天。",
                            createdTimelineNodeId: addedNodeId)
                        : new ActiveItemUseResult(false, false, $"{item.Name}：无法添加到所选日期。");

                case ItemEffectTypes.TimelineAddLotteryNode:
                    if (targets == null || targets.Count == 0)
                    {
                        return new ActiveItemUseResult(false, false, $"{item.Name}：请先选择日期。");
                    }

                    cfg.GameAction slotAction =
                        ctx.Run?.Tables?.TbAction?.GetOrDefault(item.EffectParam);
                    if (!SlotService.TryGetConfig(
                            ctx.Run,
                            slotAction,
                            out _,
                            out string slotConfigError))
                    {
                        string reason = string.IsNullOrWhiteSpace(slotConfigError)
                            ? "抽奖机配置无效。"
                            : slotConfigError;
                        return new ActiveItemUseResult(false, false, $"{item.Name}：{reason}");
                    }

                    string lotteryNodeId =
                        ctx.AddTimelineNodeWithId(item.EffectParam, targets[0].X, item.Id);
                    return !string.IsNullOrEmpty(lotteryNodeId)
                        ? new ActiveItemUseResult(
                            true,
                            false,
                            $"{item.Name}：已添加到第 {targets[0].X} 天。",
                            createdTimelineNodeId: lotteryNodeId)
                        : new ActiveItemUseResult(false, false, $"{item.Name}：无法添加到所选日期。");

                case ItemEffectTypes.TimelineDeleteNode:
                    if (targets == null || targets.Count == 0)
                    {
                        return new ActiveItemUseResult(false, false, $"{item.Name}：请先选择节点。");
                    }

                    return ctx.DeleteTimelineNode(targets[0].Id)
                        ? new ActiveItemUseResult(
                            true,
                            false,
                            $"{item.Name}：已删除所选节点。")
                        : new ActiveItemUseResult(false, false, $"{item.Name}：所选节点已经无法删除。");

                default:
                    return new ActiveItemUseResult(true, false, $"使用了 {item.Name}。");
            }
        }

        /// <summary>对每个选中目标施加操作：任一成功即视为使用成功并标记盘面变化；无目标时提示先选目标。</summary>
        private static ActiveItemUseResult ApplyToTargets(
            IReadOnlyList<ActiveTarget> targets,
            Func<ActiveTarget, bool> op,
            ItemDefinition item,
            string okMsg,
            string failMsg)
        {
            if (targets == null || targets.Count == 0)
            {
                return new ActiveItemUseResult(false, false, $"{item.Name}：请先选择目标。");
            }

            bool any = false;
            for (int i = 0; i < targets.Count; i++)
            {
                if (op(targets[i]))
                {
                    any = true;
                }
            }

            return any
                ? new ActiveItemUseResult(true, true, $"{item.Name}：{okMsg}")
                : new ActiveItemUseResult(false, false, $"{item.Name}：{failMsg}");
        }

        /// <summary>经营挑战情境便捷入口（构造 <see cref="BattleUseContext"/> 后走统一 <see cref="Apply"/>）。</summary>
        public static ActiveItemUseResult TryUse(BattleSession session, GameRun run, ItemDefinition item)
        {
            if (session == null || item == null || session.IsSettled)
            {
                return new ActiveItemUseResult(false, false, "现在不能使用消耗品。");
            }

            return Apply(new BattleUseContext(session, run), item, Array.Empty<ActiveTarget>());
        }
    }
}
