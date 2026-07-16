using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Runtime;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    public enum EventFollowUpKind
    {
        None,
        Battle,
        Shop,
        GameOver,
        Victory,
    }

    public sealed class EventResolveResult
    {
        private EventResolveResult(string feedback, EventFollowUpKind followUpKind, int requiredScore, string modifier)
        {
            Feedback = feedback ?? string.Empty;
            FollowUpKind = followUpKind;
            RequiredScore = requiredScore;
            Modifier = modifier ?? string.Empty;
        }

        public string Feedback { get; }

        public EventFollowUpKind FollowUpKind { get; }

        public int RequiredScore { get; }

        public string Modifier { get; }

        public bool IsBattle => FollowUpKind == EventFollowUpKind.Battle;

        public static EventResolveResult Immediate(string feedback)
        {
            return new EventResolveResult(feedback, EventFollowUpKind.None, 0, string.Empty);
        }

        public static EventResolveResult Battle(string feedback, int requiredScore, string modifier)
        {
            return new EventResolveResult(feedback, EventFollowUpKind.Battle, requiredScore, modifier);
        }

        public static EventResolveResult Shop(string feedback)
        {
            return new EventResolveResult(feedback, EventFollowUpKind.Shop, 0, string.Empty);
        }

        public static EventResolveResult GameOver(string feedback)
        {
            return new EventResolveResult(feedback, EventFollowUpKind.GameOver, 0, string.Empty);
        }

        public static EventResolveResult Victory(string feedback)
        {
            return new EventResolveResult(feedback, EventFollowUpKind.Victory, 0, string.Empty);
        }
    }

    /// <summary>
    /// 事件结算：像《杀戮尖塔2》一样，事件是一张「页面树」，但只用两张表表达。
    /// - <see cref="cfg.GameEvent"/>（TbEvent）是事件池条目：正文(desc=根页文本)/权重/前置/可重复。
    /// - <see cref="cfg.EventOption"/>（TbEventOption）既是选项(边)也承载分支页：
    ///   按 <see cref="cfg.EventOption.ParentId"/> 挂树（空=根页选项）；选中后施加效果，
    ///   若存在以其为父的子选项则进入子页（子页正文=<see cref="cfg.EventOption.ResultText"/>），否则结算后结束。
    ///   跟进类效果（FoodBattle/Shop/GameOver/Victory）为终止分支。
    /// 页面导航全程内存态，仅在事件结束（onDone→Commit）时存档；结束时写入 UsedEventIds。
    /// </summary>
    public static class EventService
    {
        /// <summary>取事件根页选项（parentId 为空，按配置顺序）。</summary>
        public static List<cfg.EventOption> GetRootOptions(GameRun run, string eventId)
        {
            var options = new List<cfg.EventOption>();
            if (string.IsNullOrEmpty(eventId))
            {
                return options;
            }

            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            foreach (cfg.EventOption opt in tables.TbEventOption.DataList)
            {
                if (opt.EventId == eventId && string.IsNullOrEmpty(opt.ParentId))
                {
                    options.Add(opt);
                }
            }

            return options;
        }

        /// <summary>取某选项的子页选项（parentId 指向该选项，按配置顺序）。</summary>
        public static List<cfg.EventOption> GetChildOptions(GameRun run, string parentOptionId)
        {
            var options = new List<cfg.EventOption>();
            if (string.IsNullOrEmpty(parentOptionId))
            {
                return options;
            }

            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            foreach (cfg.EventOption opt in tables.TbEventOption.DataList)
            {
                if (opt.ParentId == parentOptionId)
                {
                    options.Add(opt);
                }
            }

            return options;
        }

        /// <summary>从指定分类池（eventType=Event/Reward/Negative）中按权重/前置/可重复随机一个事件。</summary>
        public static cfg.GameEvent RollEvent(GameRun run, IRandomStream rng, cfg.ActionBehavior eventType)
        {
            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            var candidates = new List<cfg.GameEvent>();
            foreach (cfg.GameEvent ev in tables.TbEvent.DataList)
            {
                if (ev.EventType != eventType || ev.Weight <= 0f)
                {
                    continue;
                }

                // repeatable=false 且本局已命中过 → 不再进池。
                if (!ev.Repeatable && run != null && run.HasUsedEvent(ev.Id))
                {
                    continue;
                }

                if (PreconditionEvaluator.IsSatisfied(run, ev.Preconditions))
                {
                    candidates.Add(ev);
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            var itemRuntime = new ItemRuntime(run);
            float rewardMul = 1f + itemRuntime.LuckyEventChanceBonus();
            float eventMul = 1f + itemRuntime.MoreEventsBonus();
            var weights = new List<float>(candidates.Count);
            foreach (cfg.GameEvent ev in candidates)
            {
                float weight = ev.Weight > 0f ? ev.Weight : 1f;
                if (ev.EventType == cfg.ActionBehavior.Reward)
                {
                    weight *= System.Math.Max(0f, rewardMul);
                }
                else if (ev.EventType == cfg.ActionBehavior.Event)
                {
                    weight *= System.Math.Max(0f, eventMul);
                }

                weights.Add(weight);
            }

            return candidates[rng.WeightedPickIndex(weights)];
        }

        /// <summary>
        /// act_event 的「全类型合并池」抽取：候选纳入 Event/Reward/Negative 全部类型，
        /// 并按被动道具修正权重——Reward 项 ×(1+LuckyEventChance)、Event 项 ×(1+MoreEvents)，Negative 不变。
        /// 复用 <see cref="RollEvent"/> 的 weight&gt;0 / repeatable / 前置过滤。
        /// </summary>
        public static cfg.GameEvent RollActionEvent(GameRun run, IRandomStream rng)
        {
            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            var itemRuntime = new ItemRuntime(run);
            float rewardMul = 1f + itemRuntime.LuckyEventChanceBonus();
            float eventMul = 1f + itemRuntime.MoreEventsBonus();

            var candidates = new List<cfg.GameEvent>();
            var weights = new List<float>();
            foreach (cfg.GameEvent ev in tables.TbEvent.DataList)
            {
                if (ev.Weight <= 0f)
                {
                    continue;
                }

                // repeatable=false 且本局已命中过 → 不再进池。
                if (!ev.Repeatable && run != null && run.HasUsedEvent(ev.Id))
                {
                    continue;
                }

                if (!PreconditionEvaluator.IsSatisfied(run, ev.Preconditions))
                {
                    continue;
                }

                float w = ev.Weight;
                if (ev.EventType == cfg.ActionBehavior.Reward)
                {
                    w *= rewardMul;
                }
                else if (ev.EventType == cfg.ActionBehavior.Event)
                {
                    w *= eventMul;
                }

                candidates.Add(ev);
                // 修正后权重下限保护：负修正等极端配置也保证仍是正权重可被选中。
                weights.Add(w > 0f ? w : 0.0001f);
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            cfg.GameEvent picked = candidates[rng.WeightedPickIndex(weights)];
            if (picked != null && picked.EventType == cfg.ActionBehavior.Reward && System.Math.Abs(rewardMul - 1f) > 0.0001f)
            {
                itemRuntime.FlashTriggered(m => System.Math.Abs(m.LuckyEventChanceBonus()) > 0.0001f);
            }
            else if (picked != null && picked.EventType == cfg.ActionBehavior.Event && System.Math.Abs(eventMul - 1f) > 0.0001f)
            {
                itemRuntime.FlashTriggered(m => System.Math.Abs(m.MoreEventsBonus()) > 0.0001f);
            }

            return picked;
        }

        /// <summary>
        /// act_event 的完整抽取入口（唯一 choke point）：
        /// - 未持有 LuckyEventGuarantee 道具时，仅走 <see cref="RollActionEvent"/> 合并池抽取，不触碰保底计数。
        /// - 持有时：累计到 x-1 个 Event 型结果后本次强制从 Reward 池抽（命中则清零计数）；否则走合并池抽取，
        ///   结果为 Event 型才累加计数。计数状态存于该道具模型（只在持有时存在）。
        /// </summary>
        public static cfg.GameEvent RollActionEventWithGuarantee(GameRun run, IRandomStream rng)
        {
            if (run != null && run.TryConsumeForcedEventId(out string forcedEventId))
            {
                cfg.Tables tables = run.Tables ?? GameApp.Config.Tables;
                cfg.GameEvent forced = tables.TbEvent.GetOrDefault(forcedEventId);
                if (forced != null && (forced.Repeatable || !run.HasUsedEvent(forced.Id)))
                {
                    return forced;
                }
            }

            PassiveItemModel guarantee = FindGuaranteeModel(run);
            if (guarantee != null)
            {
                int every = guarantee.LuckyEventGuaranteeEvery();
                if (every > 0 && guarantee.EventGuaranteeStreak >= every - 1)
                {
                    cfg.GameEvent forced = RollEvent(run, rng, cfg.ActionBehavior.Reward);
                    if (forced != null)
                    {
                        guarantee.ResetEventGuaranteeStreak();
                        guarantee.Flash();
                        return forced;
                    }
                    // Reward 池为空：回退合并池抽取，且不清零计数（保底名额留到下次）。
                }
            }

            cfg.GameEvent ev = RollActionEvent(run, rng);
            if (ev != null && guarantee != null && ev.EventType == cfg.ActionBehavior.Event)
            {
                guarantee.IncrementEventGuaranteeStreak();
            }

            return ev;
        }

        private static PassiveItemModel FindGuaranteeModel(GameRun run)
        {
            if (run == null)
            {
                return null;
            }

            foreach (PassiveItemModel m in run.PassiveModels)
            {
                if (m.LuckyEventGuaranteeEvery() > 0)
                {
                    return m;
                }
            }

            return null;
        }

        /// <summary>
        /// 结算某个选项：按序施加它的所有效果（多效果并列 list），返回结果。
        /// 跟进类效果（FoodBattle/Shop/GameOver/Victory）至多一个，作为终止分支返回。
        /// 不在此写 UsedEventIds / 发放事件金币——那些放到事件「终止」时（<see cref="OnEventFinished"/>）。
        /// </summary>
        public static EventResolveResult ResolveOption(GameRun run, cfg.EventOption option, IRandomStream rng)
        {
            if (option == null)
            {
                return EventResolveResult.Immediate(string.Empty);
            }

            EventResolveResult followUp = null;
            var feedbacks = new List<string>();
            int count = option.EffectTypes.Count;
            for (int i = 0; i < count; i++)
            {
                cfg.EffectType type = option.EffectTypes[i];
                float value = i < option.EffectValues.Count ? option.EffectValues[i] : 0f;
                string param = i < option.EffectParams.Count ? option.EffectParams[i] : string.Empty;
                EventResolveResult r = ResolveEffect(run, type, value, param, option.Text, rng);
                if (r.FollowUpKind != EventFollowUpKind.None)
                {
                    followUp = r;
                }
                else if (!string.IsNullOrEmpty(r.Feedback))
                {
                    feedbacks.Add(r.Feedback);
                }
            }

            if (followUp != null)
            {
                return followUp;
            }

            return EventResolveResult.Immediate(feedbacks.Count > 0 ? string.Join("\n", feedbacks) : string.Empty);
        }

        /// <summary>事件到达终止（选项无子页、终止展示页、或跟进类效果）时调用：记录使用并发放事件金币。</summary>
        public static void OnEventFinished(GameRun run, cfg.GameEvent ev, EventResolveResult result)
        {
            if (run == null || ev == null)
            {
                return;
            }

            run.MarkEventUsed(ev.Id);
            GrantEventCompleteGold(run, result);
        }

        /// <summary>事件即时结算完成时发放道具「事件红包」金币（战斗/结局类事件不在此发放）。</summary>
        private static void GrantEventCompleteGold(GameRun run, EventResolveResult result)
        {
            if (run == null || result == null)
            {
                return;
            }

            if (result.FollowUpKind != EventFollowUpKind.None && result.FollowUpKind != EventFollowUpKind.Shop)
            {
                return;
            }

            var itemRuntime = new ItemRuntime(run);
            int gold = itemRuntime.EventCompleteGold();
            if (gold != 0)
            {
                itemRuntime.FlashTriggered(m => m.EventCompleteGold() != 0);
                run.Gold += gold;
            }
        }

        private static EventResolveResult ResolveEffect(GameRun run, cfg.EffectType effectType, float effectValue, string effectParam, string fallback, IRandomStream rng)
        {
            switch (effectType)
            {
                case cfg.EffectType.FoodBattle:
                {
                    int required = effectValue > 0f ? RoundToInt(effectValue) : EventBattleRequiredScore(run);
                    string feedback = string.IsNullOrEmpty(fallback) ? $"触发美食挑战，目标分 {required}。" : fallback;
                    return EventResolveResult.Battle(feedback, required, effectParam);
                }

                case cfg.EffectType.GameOver:
                {
                    string feedback = string.IsNullOrEmpty(effectParam) ? fallback : effectParam;
                    return EventResolveResult.GameOver(feedback);
                }

                case cfg.EffectType.Shop:
                {
                    string feedback = string.IsNullOrEmpty(effectParam) ? fallback : effectParam;
                    return EventResolveResult.Shop(feedback);
                }

                case cfg.EffectType.Victory:
                {
                    string feedback = string.IsNullOrEmpty(effectParam) ? fallback : effectParam;
                    return EventResolveResult.Victory(feedback);
                }

                default:
                {
                    string feedback = EffectResolver.Apply(run, effectType, effectValue, effectParam, rng);
                    return EventResolveResult.Immediate(string.IsNullOrEmpty(feedback) ? fallback : feedback);
                }
            }
        }

        private static int EventBattleRequiredScore(GameRun run)
        {
            if (run == null)
            {
                return 0;
            }

            if (run.LastActionContext != null)
            {
                cfg.Food food = FoodService.Resolve(run.Tables, run.LastActionContext.Action);
                if (food != null
                    && food.ActionKind != cfg.FoodActionKind.Feast
                    && run.ScoreToOneRemaining > 0)
                {
                    return 1;
                }

                return HiddenScoreService.TargetScore(run, run.LastActionContext);
            }

            return run.RequiredScore;
        }

        private static int RoundToInt(float v) => (int)System.Math.Round(v, System.MidpointRounding.AwayFromZero);
    }
}
