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
    /// 事件明细结算：事件正文/权重/前置/可重复都在 <see cref="cfg.GameEvent"/>（TbEvent），
    /// 选项分支在 <see cref="cfg.EventOption"/>（TbEventOption，按 eventId 关联）。
    /// Event/Reward/Negative 三种行动各自从对应 <see cref="cfg.ActionBehavior"/> 分类的事件池里随机一个具体事件。
    /// 单选项事件=自动结算（无需玩家点选，用于奖励/负面/即时事件）；多选项=玩家 n 选一分支。
    /// 事件结算后写入 UsedEventIds（整局级，供结算统计与跨局进度）。
    /// </summary>
    public static class EventService
    {
        /// <summary>取某事件的全部选项（按配置顺序）。</summary>
        public static List<cfg.EventOption> GetOptions(GameRun run, string eventId)
        {
            var options = new List<cfg.EventOption>();
            if (string.IsNullOrEmpty(eventId))
            {
                return options;
            }

            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            foreach (cfg.EventOption opt in tables.TbEventOption.DataList)
            {
                if (opt.EventId == eventId)
                {
                    options.Add(opt);
                }
            }

            return options;
        }

        public static bool HasOptions(GameRun run, string eventId) => GetOptions(run, eventId).Count > 0;

        /// <summary>从指定分类池（eventType=Event/Reward/Negative）中按权重/前置随机一个事件。</summary>
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

        /// <summary>无选项事件的兜底结算（正常事件至少配 1 个选项，走 <see cref="ResolveOption"/>）。</summary>
        public static EventResolveResult ResolveImmediate(GameRun run, cfg.GameEvent ev, IRandomStream rng)
        {
            if (ev == null)
            {
                return EventResolveResult.Immediate(string.Empty);
            }

            List<cfg.EventOption> options = GetOptions(run, ev.Id);
            if (options.Count > 0)
            {
                return ResolveOption(run, ev, options[0], rng);
            }

            run.MarkEventUsed(ev.Id);
            return EventResolveResult.Immediate(ev.Desc);
        }

        /// <summary>按所选选项结算，或返回后续动作（事件战斗/商店/结局），并记录使用。</summary>
        public static EventResolveResult ResolveOption(GameRun run, cfg.GameEvent ev, cfg.EventOption option, IRandomStream rng)
        {
            if (ev == null || option == null)
            {
                return EventResolveResult.Immediate(string.Empty);
            }

            EventResolveResult result = ResolveEffect(run, option.EffectType, option.EffectValue, option.EffectParam, option.Text, rng);
            run.MarkEventUsed(ev.Id);
            GrantEventCompleteGold(run, result);
            return result;
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
