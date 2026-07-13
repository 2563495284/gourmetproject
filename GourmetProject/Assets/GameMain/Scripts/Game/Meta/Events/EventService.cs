using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Runtime;
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

            var weights = new List<float>(candidates.Count);
            foreach (cfg.GameEvent ev in candidates)
            {
                weights.Add(ev.Weight > 0f ? ev.Weight : 1f);
            }

            return candidates[rng.WeightedPickIndex(weights)];
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

            run.Gold += new ItemRuntime(run).EventCompleteGold();
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

            return run.LastActionContext != null
                ? HiddenScoreService.TargetScore(run, run.LastActionContext)
                : run.RequiredScore;
        }

        private static int RoundToInt(float v) => (int)System.Math.Round(v, System.MidpointRounding.AwayFromZero);
    }
}
