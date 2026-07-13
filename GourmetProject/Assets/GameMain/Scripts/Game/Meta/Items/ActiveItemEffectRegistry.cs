using System;
using System.Collections.Generic;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;

namespace GourmetProject.Game.Meta
{
    public readonly struct ActiveItemUseResult
    {
        public ActiveItemUseResult(bool success, bool boardChanged, string message)
        {
            Success = success;
            BoardChanged = boardChanged;
            Message = message;
        }

        public bool Success { get; }

        public bool BoardChanged { get; }

        public string Message { get; }
    }

    /// <summary>
    /// 主动道具效果派发入口。效果语义集中在此、与情境解耦：
    /// 各 <see cref="IActiveUseContext"/>（战斗 / 局外）只提供能力钩子，表现层只负责点击/选目标与刷新。
    /// </summary>
    public static class ActiveItemEffectRegistry
    {
        /// <summary>情境无关的主动道具效果落地。</summary>
        public static ActiveItemUseResult Apply(IActiveUseContext ctx, ItemDefinition item, IReadOnlyList<ActiveTarget> targets)
        {
            if (ctx == null || item == null)
            {
                return new ActiveItemUseResult(false, false, "现在不能使用主动道具。");
            }

            switch (item.EffectType)
            {
                case ItemEffectTypes.ClearBoard:
                    return ctx.ClearBoard()
                        ? new ActiveItemUseResult(true, true, $"{item.Name}：已清空餐桌。")
                        : new ActiveItemUseResult(false, false, $"{item.Name}：现在无法清空餐桌。");

                case ItemEffectTypes.ExtraServe:
                    return ctx.ExtraServe()
                        ? new ActiveItemUseResult(true, true, $"{item.Name}：额外上了一道菜。")
                        : new ActiveItemUseResult(false, false, $"{item.Name}：没有能放下的菜了。");

                case ItemEffectTypes.GoldNow:
                    if (ctx.Run != null)
                    {
                        ctx.Run.Gold += (int)item.EffectValue;
                    }

                    return new ActiveItemUseResult(true, false, $"{item.Name}：获得 {(int)item.EffectValue} 金币。");

                case ItemEffectTypes.AdjustCountBonus:
                {
                    int amount = (int)item.EffectValue;
                    if (ctx.Run != null && ctx.Run.AddFoodAdjustCount(amount))
                    {
                        return new ActiveItemUseResult(true, false, $"{item.Name}：食物调整次数 +{amount}。");
                    }

                    return new ActiveItemUseResult(false, false, $"{item.Name}：现在无法增加食物调整次数。");
                }

                // —— 需选目标的目标操作族（对标杀戮尖塔2 药水的 OnUse(target)）——
                case ItemEffectTypes.AddScore:
                    return ApplyToTargets(targets, t => ctx.AddPermanentScore(t, item.EffectValue),
                        item, "已强化选中的菜。", "现在无法强化选中的菜。");

                case ItemEffectTypes.DestroyDish:
                    return ApplyToTargets(targets, ctx.DestroyDish,
                        item, "已移除选中的菜。", "现在无法移除选中的菜。");

                case ItemEffectTypes.DuplicateDish:
                    // 复制/生成类推进主动使用序号，保证同种子下可复现（见 docs/design/道具.md §7）。
                    return ApplyToTargets(targets, t => ctx.DuplicateDish(t, ctx.Run?.NextActiveUseKey()),
                        item, "已复制选中的菜。", "现在无法复制（空位不足）。");

                // —— 调味小票：给菜谱目标菜永久附加风味（effectParam=风味 id）——
                case ItemEffectTypes.AddFlavor:
                    return ApplyToTargets(targets, t => ctx.AddFlavorToDish(t, item.EffectParam),
                        item, "已为选中的菜附加风味。", "现在无法为选中的菜附加风味。");

                // —— 铺台小票：给餐桌格永久附加材质（effectParam=材质 id）——
                case ItemEffectTypes.AddMaterial:
                    return ApplyToTargets(targets, t => ctx.AddMaterialToCell(t, item.EffectParam),
                        item, "已为选中的格子附加材质。", "现在无法为选中的格子附加材质。");

                // —— 排程小票：Global 无目标，直接调情境钩子（仅地图支持，战斗返回 false）——
                case ItemEffectTypes.RerollAction:
                    return ctx.RerollCurrentAction()
                        ? new ActiveItemUseResult(true, false, $"{item.Name}：已重掷当前行动选项。")
                        : new ActiveItemUseResult(false, false, $"{item.Name}：现在无法重掷行动。");

                case ItemEffectTypes.ResetBossDebuff:
                    return ctx.ResetWeekBoss()
                        ? new ActiveItemUseResult(true, false, $"{item.Name}：已重置本周 Boss。")
                        : new ActiveItemUseResult(false, false, $"{item.Name}：现在无法重置本周 Boss。");

                case ItemEffectTypes.TimelineExecuteNext:
                    return ctx.ExecuteNextTimelineNode()
                        ? new ActiveItemUseResult(true, false, $"{item.Name}：已执行下一个行动轴节点。")
                        : new ActiveItemUseResult(false, false, $"{item.Name}：行动轴上没有可执行的节点。");

                case ItemEffectTypes.TimelineAddRewardNode:
                    return ctx.AddRewardNodeToTimeline(item.EffectParam)
                        ? new ActiveItemUseResult(true, false, $"{item.Name}：已在行动轴上添加奖励节点。")
                        : new ActiveItemUseResult(false, false, $"{item.Name}：现在无法添加奖励节点。");

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

        /// <summary>战斗情境便捷入口（构造 <see cref="BattleUseContext"/> 后走统一 <see cref="Apply"/>）。</summary>
        public static ActiveItemUseResult TryUse(BattleSession session, GameRun run, ItemDefinition item)
        {
            if (session == null || item == null || session.IsSettled)
            {
                return new ActiveItemUseResult(false, false, "现在不能使用主动道具。");
            }

            return Apply(new BattleUseContext(session, run), item, Array.Empty<ActiveTarget>());
        }
    }
}
