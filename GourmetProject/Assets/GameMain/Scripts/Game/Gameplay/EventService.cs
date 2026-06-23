using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Gameplay
{
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
            var candidates = new List<cfg.GameEvent>();
            foreach (cfg.GameEvent ev in GameApp.Config.Tables.TbEvent.DataList)
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

            return rng.Pick(candidates);
        }

        /// <summary>无选项事件：直接按 effectType 结算并记录使用。</summary>
        public static string ResolveImmediate(GameRun run, cfg.GameEvent ev, IRandomStream rng)
        {
            if (ev == null)
            {
                return string.Empty;
            }

            string feedback = EffectResolver.Apply(run, ev.EffectType, ev.EffectValue, string.Empty, rng);
            if (!ev.Repeatable)
            {
                run.MarkEventUsed(ev.Id);
            }

            return string.IsNullOrEmpty(feedback) ? ev.Desc : feedback;
        }

        /// <summary>有选项事件：按所选选项结算并记录使用。</summary>
        public static string ResolveOption(GameRun run, cfg.GameEvent ev, cfg.EventOption option, IRandomStream rng)
        {
            if (ev == null || option == null)
            {
                return string.Empty;
            }

            string feedback = EffectResolver.Apply(run, option.ResultType, option.ResultValue, option.ResultParam, rng);
            if (!ev.Repeatable)
            {
                run.MarkEventUsed(ev.Id);
            }

            return feedback;
        }
    }
}
