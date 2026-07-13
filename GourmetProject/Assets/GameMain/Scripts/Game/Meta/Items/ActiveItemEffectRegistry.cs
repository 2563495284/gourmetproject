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
        public static ActiveItemUseResult Apply(IActiveUseContext ctx, cfg.Item item, IReadOnlyList<ActiveTarget> targets)
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

                default:
                    return new ActiveItemUseResult(true, false, $"使用了 {item.Name}。");
            }
        }

        /// <summary>战斗情境便捷入口（构造 <see cref="BattleUseContext"/> 后走统一 <see cref="Apply"/>）。</summary>
        public static ActiveItemUseResult TryUse(BattleSession session, GameRun run, cfg.Item item)
        {
            if (session == null || item == null || session.IsSettled)
            {
                return new ActiveItemUseResult(false, false, "现在不能使用主动道具。");
            }

            return Apply(new BattleUseContext(session, run), item, Array.Empty<ActiveTarget>());
        }
    }
}
