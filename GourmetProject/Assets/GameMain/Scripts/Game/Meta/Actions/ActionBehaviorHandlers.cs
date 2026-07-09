using System.Collections.Generic;
using System.Globalization;
using GourmetProject.Core.Rng;
using GourmetProject.Runtime;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 原子行动的「行为」执行器：一种 <see cref="cfg.ActionBehavior"/> 对应一个 handler。
    /// 新增行为 = 新增枚举值 + 一个 handler + 配置，不改动编排层，这是统一模型「易扩展」的落点。
    /// handler 只做同步「解析/即时结算」，产出 <see cref="ActionOutcome"/> 交给编排层做异步表现。
    /// </summary>
    public interface IActionBehaviorHandler
    {
        cfg.ActionBehavior Behavior { get; }

        ActionOutcome Execute(GameRun run, ActionExecutionContext context, IRandomStream rng);
    }

    /// <summary>行为 handler 注册表：按 behavior 分派。</summary>
    public static class ActionBehaviorRegistry
    {
        private static readonly Dictionary<cfg.ActionBehavior, IActionBehaviorHandler> Handlers = Build();

        private static Dictionary<cfg.ActionBehavior, IActionBehaviorHandler> Build()
        {
            var map = new Dictionary<cfg.ActionBehavior, IActionBehaviorHandler>();
            void Add(IActionBehaviorHandler h) => map[h.Behavior] = h;
            Add(new FoodBehaviorHandler());
            // Event/Reward/Negative 都是「从对应分类事件池随机一个具体事件」，产出统一的 Event outcome。
            Add(new EventBehaviorHandler(cfg.ActionBehavior.Event));
            Add(new EventBehaviorHandler(cfg.ActionBehavior.Reward));
            Add(new EventBehaviorHandler(cfg.ActionBehavior.Negative));
            Add(new ShopBehaviorHandler());
            Add(new InterestBehaviorHandler());
            return map;
        }

        public static IActionBehaviorHandler Get(cfg.ActionBehavior behavior)
        {
            return Handlers.TryGetValue(behavior, out IActionBehaviorHandler handler) ? handler : null;
        }
    }

    /// <summary>美食行为：普通/困难读 TbFood 隐藏分目标；Boss 槽先抽 Boss 美食，产出 Boss 战。</summary>
    public sealed class FoodBehaviorHandler : IActionBehaviorHandler
    {
        public cfg.ActionBehavior Behavior => cfg.ActionBehavior.Food;

        public ActionOutcome Execute(GameRun run, ActionExecutionContext context, IRandomStream rng)
        {
            cfg.GameAction action = context.Action;
            if (FoodService.IsBossSlot(action))
            {
                string bossKey = string.IsNullOrEmpty(context.SourceKey)
                    ? $"w{run.WeekIndex}_{action.Id}_s{context.StepIndex}"
                    : $"w{run.WeekIndex}_{context.SourceKey}";
                IRandomStream bossRng = GameApp.Random.DomainStream(SeedDomains.Boss, bossKey);
                cfg.Food boss = BossService.RollBoss(run, bossRng);
                if (boss == null)
                {
                    return ActionOutcome.Immediate(string.Empty);
                }

                int bossRequired = run.ComputeBossRequiredScore(boss.ScoreProfileId);
                string battleKey = $"boss_w{run.WeekIndex}_{boss.Id}";
                return ActionOutcome.Battle(bossRequired, boss.Modifier, battleKey, isBoss: true, bossId: boss.Id);
            }

            cfg.Food food = FoodService.Resolve(run.Tables, action);
            int required = HiddenScoreService.TargetScore(run, context);
            string modifier = food?.Modifier ?? string.Empty;
            string key = string.IsNullOrEmpty(context.SourceKey)
                ? $"food_w{run.WeekIndex}_s{context.StepIndex}_d{run.CurrentDay.ToString("0.0", CultureInfo.InvariantCulture)}_{action.Id}"
                : $"food_{context.SourceKey}_{action.Id}";
            return ActionOutcome.Battle(required, modifier, key);
        }
    }

    /// <summary>
    /// 事件类行为（Event/Reward/Negative）：只产出「需要事件表现」的 outcome；
    /// 具体事件由编排层 + <see cref="EventService"/> 按 behavior 对应的分类池随机并结算。
    /// </summary>
    public sealed class EventBehaviorHandler : IActionBehaviorHandler
    {
        public EventBehaviorHandler(cfg.ActionBehavior behavior)
        {
            Behavior = behavior;
        }

        public cfg.ActionBehavior Behavior { get; }

        public ActionOutcome Execute(GameRun run, ActionExecutionContext context, IRandomStream rng)
        {
            return ActionOutcome.Event(context.Action.Id);
        }
    }

    /// <summary>商店行为：产出打开商店的 outcome。</summary>
    public sealed class ShopBehaviorHandler : IActionBehaviorHandler
    {
        public cfg.ActionBehavior Behavior => cfg.ActionBehavior.Shop;

        public ActionOutcome Execute(GameRun run, ActionExecutionContext context, IRandomStream rng)
        {
            return ActionOutcome.Shop();
        }
    }

    /// <summary>利息行为：按当前金币每满阈值发放金币（受利息上限限制），即时结算。</summary>
    public sealed class InterestBehaviorHandler : IActionBehaviorHandler
    {
        public cfg.ActionBehavior Behavior => cfg.ActionBehavior.Interest;

        public ActionOutcome Execute(GameRun run, ActionExecutionContext context, IRandomStream rng)
        {
            int threshold = run.InterestThreshold;
            int goldPer = run.InterestGoldPer > 0 ? run.InterestGoldPer : 1;
            int maxGain = run.InterestCap;
            int gold = TimelineMath.Interest(run.Gold, threshold, goldPer, maxGain);
            run.Gold += gold;

            string msg;
            if (gold > 0)
            {
                msg = $"利息结算：金币 +{gold}（每满 {threshold} 金币得 {goldPer}，最高 {maxGain}），当前 {run.Gold}。";
            }
            else if (maxGain <= 0)
            {
                msg = "当前利息上限为 0，本次没有利息。";
            }
            else
            {
                msg = $"金币不足 {threshold}，本次没有利息。";
            }

            return ActionOutcome.Immediate(msg);
        }
    }
}
