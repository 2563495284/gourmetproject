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
    /// 事件服务：提供事件选项查询、随机事件抽取与结算。
    /// 无选项事件直接按 effectType 结算；有选项事件由编排层弹窗后调用 <see cref="ResolveOption"/>。
    /// 不可重复事件命中后写入 UsedEventIds。
    /// </summary>
    public static class EventService
    {
        /// <summary>取某事件的全部选项（按配置顺序）。</summary>
        public static List<cfg.EventOption> GetOptions(string eventId)
        {
            var options = new List<cfg.EventOption>();
            if (string.IsNullOrEmpty(eventId))
            {
                return options;
            }

            foreach (cfg.EventOption opt in GameApp.Config.Tables.TbEventOption.DataList)
            {
                if (opt.EventId == eventId)
                {
                    options.Add(opt);
                }
            }

            return options;
        }

        public static bool HasOptions(string eventId) => GetOptions(eventId).Count > 0;

        /// <summary>从满足条件（可重复或未用过、前置满足）的事件中随机一个，用于事件节点。</summary>
        public static cfg.GameEvent RollEvent(GameRun run, IRandomStream rng)
        {
            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            var candidates = new List<cfg.GameEvent>();
            foreach (cfg.GameEvent ev in tables.TbEvent.DataList)
            {
                if ((ev.Repeatable || !run.IsEventUsed(ev.Id)) && PreconditionEvaluator.IsSatisfied(run, ev.Preconditions))
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

        /// <summary>无选项事件：直接按 effectType 结算，或返回后续动作（事件战斗/直接结局），并记录使用。</summary>
        public static EventResolveResult ResolveImmediate(GameRun run, cfg.GameEvent ev, IRandomStream rng)
        {
            if (ev == null)
            {
                return EventResolveResult.Immediate(string.Empty);
            }

            EventResolveResult result = ResolveEffect(run, ev.EffectType, ev.EffectValue, string.Empty, ev.Desc, rng);
            if (!ev.Repeatable)
            {
                run.MarkEventUsed(ev.Id);
            }

            GrantEventCompleteGold(run, result);
            return result;
        }

        /// <summary>有选项事件：按所选选项结算，或返回后续动作（事件战斗/直接结局），并记录使用。</summary>
        public static EventResolveResult ResolveOption(GameRun run, cfg.GameEvent ev, cfg.EventOption option, IRandomStream rng)
        {
            if (ev == null || option == null)
            {
                return EventResolveResult.Immediate(string.Empty);
            }

            EventResolveResult result = ResolveEffect(run, option.ResultType, option.ResultValue, option.ResultParam, option.Text, rng);
            if (!ev.Repeatable)
            {
                run.MarkEventUsed(ev.Id);
            }

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

        private static EventResolveResult ResolveEffect(GameRun run, string effectType, float effectValue, string effectParam, string fallback, IRandomStream rng)
        {
            switch (effectType)
            {
                case "Battle":
                case "FoodBattle":
                {
                    int required = effectValue > 0f ? RoundToInt(effectValue) : EventBattleRequiredScore(run);
                    string feedback = string.IsNullOrEmpty(fallback) ? $"触发美食挑战，目标分 {required}。" : fallback;
                    return EventResolveResult.Battle(feedback, required, effectParam);
                }

                case "GameOver":
                case "LoseRun":
                {
                    string feedback = string.IsNullOrEmpty(effectParam) ? fallback : effectParam;
                    return EventResolveResult.GameOver(feedback);
                }

                case "Shop":
                case "OpenShop":
                {
                    string feedback = string.IsNullOrEmpty(effectParam) ? fallback : effectParam;
                    return EventResolveResult.Shop(feedback);
                }

                case "Victory":
                case "WinRun":
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
