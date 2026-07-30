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
            Add(new EffectBehaviorHandler());
            return map;
        }

        public static IActionBehaviorHandler Get(cfg.ActionBehavior behavior)
        {
            return Handlers.TryGetValue(behavior, out IActionBehaviorHandler handler) ? handler : null;
        }
    }

    /// <summary>美食行为：普通/困难读 TbFood 隐藏分目标；Boss 槽取唯一 Boss 美食并随机 Debuff，产出 Boss 战。</summary>
    public sealed class FoodBehaviorHandler : IActionBehaviorHandler
    {
        public cfg.ActionBehavior Behavior => cfg.ActionBehavior.Food;

        public ActionOutcome Execute(GameRun run, ActionExecutionContext context, IRandomStream rng)
        {
            cfg.GameAction action = context.Action;
            cfg.Food food = FoodService.Resolve(run?.Tables, action);
            bool bossAction = FoodService.IsBossAction(run?.Tables, action);
            cfg.Food boss = FoodService.ResolveBoss(run, action);
            if (bossAction)
            {
                string bossKey = string.IsNullOrEmpty(context.SourceKey)
                    ? $"w{run.WeekIndex}_{action.Id}_s{context.StepIndex}"
                    : $"w{run.WeekIndex}_{context.SourceKey}";
                if (boss == null)
                {
                    return ActionOutcome.Immediate(string.Empty);
                }

                IRandomStream debuffRng = GameApp.Random.DomainStream(
                    SeedDomains.Boss,
                    BossService.BuildBossDebuffSeedKey(run, bossKey, context.SourceKey));
                cfg.BossDebuff debuff = BossService.RollBossDebuff(run, debuffRng);
                int bossRequired = RequiredScoreForFood(run, context, boss, debuff?.TargetScoreHiddenOffset ?? 0);
                string battleKey = $"boss_{bossKey}_{debuff?.Id ?? "none"}";
                return ActionOutcome.Battle(
                    bossRequired,
                    string.Empty,
                    battleKey,
                    isBoss: true,
                    bossId: boss.Id,
                    bossDebuffId: debuff?.Id);
            }

            int required = RequiredScoreForFood(run, context, food, 0f);
            string key = string.IsNullOrEmpty(context.SourceKey)
                ? $"food_w{run.WeekIndex}_s{context.StepIndex}_d{run.CurrentDay.ToString("0.0", CultureInfo.InvariantCulture)}_{action.Id}"
                : $"food_{context.SourceKey}_{action.Id}_repeat{System.Math.Max(1, context.NodeRepeatIndex)}";
            return ActionOutcome.Battle(required, string.Empty, key);
        }

        private static int RequiredScoreForFood(
            GameRun run,
            ActionExecutionContext context,
            cfg.Food food,
            float extraTargetScoreHiddenOffset)
        {
            if (run != null)
            {
                extraTargetScoreHiddenOffset += run.ConsumeNextFoodTargetScoreHiddenOffset();
            }

            if (run != null
                && food != null
                && food.ActionKind != cfg.FoodActionKind.Feast
                && run.ScoreToOneRemaining > 0)
            {
                return 1;
            }

            int hiddenCurveTarget = HiddenScoreService.TargetScore(run, context, extraTargetScoreHiddenOffset);
            return food != null && food.ActionKind == cfg.FoodActionKind.Feast
                ? new ItemRuntime(run).ModifyRequiredScore(hiddenCurveTarget, cfg.FoodActionKind.Feast)
                : hiddenCurveTarget;
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

    /// <summary>利息行为：打开行动配置指向的专用事件；具体结算由事件选项效果执行。</summary>
    public sealed class InterestBehaviorHandler : IActionBehaviorHandler
    {
        public cfg.ActionBehavior Behavior => cfg.ActionBehavior.Interest;

        public ActionOutcome Execute(GameRun run, ActionExecutionContext context, IRandomStream rng)
        {
            string eventId = context.Action.EffectParam;
            cfg.GameEvent ev = string.IsNullOrWhiteSpace(eventId)
                ? null
                : run.Tables.TbEvent.GetOrDefault(eventId);
            if (ev == null)
            {
                return ActionOutcome.Immediate($"事件配置缺失：{eventId}");
            }

            return ActionOutcome.Event(ev.Id);
        }
    }

    /// <summary>配置驱动的通用即时效果节点。</summary>
    public sealed class EffectBehaviorHandler : IActionBehaviorHandler
    {
        public cfg.ActionBehavior Behavior => cfg.ActionBehavior.Effect;

        public ActionOutcome Execute(GameRun run, ActionExecutionContext context, IRandomStream rng)
        {
            cfg.GameAction action = context.Action;
            string feedback = EffectResolver.Apply(run, action.EffectType, action.EffectValue, action.EffectParam, rng);
            return ActionOutcome.Immediate(feedback);
        }
    }
}
