using System.Collections.Generic;
using System.Globalization;
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
    /// 事件结算：
    /// - <see cref="cfg.GameEvent"/>（TbEvent）是事件池条目：正文/权重/前置/可重复。
    /// - <see cref="cfg.EventOption"/>（TbEventOption）是流程边：parentId 为空时属于根页，
    ///   否则挂在父选项之后。选中后先施加效果；有子选项时 ResultText 成为下一页描述，
    ///   无子选项时 ResultText 成为结束按钮文本。
    /// - 跟进类效果（FoodBattle/Shop/GameOver/Victory）以及奖励弹窗会终止当前分支。
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

        /// <summary>取父选项之后的下一组选项；同时校验 eventId，避免跨事件串线。</summary>
        public static List<cfg.EventOption> GetChildOptions(GameRun run, string eventId, string parentOptionId)
        {
            var options = new List<cfg.EventOption>();
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(parentOptionId))
            {
                return options;
            }

            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            foreach (cfg.EventOption opt in tables.TbEventOption.DataList)
            {
                if (opt.EventId == eventId && opt.ParentId == parentOptionId)
                {
                    options.Add(opt);
                }
            }

            return options;
        }

        /// <summary>将事件正文/选项中的运行时利息占位符替换为当前数值。</summary>
        public static string FormatRuntimeText(GameRun run, string template)
        {
            if (string.IsNullOrEmpty(template) || run == null)
            {
                return template ?? string.Empty;
            }

            int threshold = run.InterestThreshold;
            int goldPer = run.InterestGoldPer > 0 ? run.InterestGoldPer : 1;
            int maxGain = run.InterestCap;
            int gain = TimelineMath.Interest(run.Gold, threshold, goldPer, maxGain);
            return template
                .Replace("{gain}", gain.ToString(CultureInfo.InvariantCulture))
                .Replace("{threshold}", threshold.ToString(CultureInfo.InvariantCulture))
                .Replace("{goldPer}", goldPer.ToString(CultureInfo.InvariantCulture))
                .Replace("{maxGain}", maxGain.ToString(CultureInfo.InvariantCulture))
                .Replace("{currentGold}", run.Gold.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>从指定分类池（eventTypes 包含 Event/Reward/Negative）中按权重/前置/可重复随机一个事件。</summary>
        public static cfg.GameEvent RollEvent(GameRun run, IRandomStream rng, cfg.ActionBehavior eventType)
        {
            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            var candidates = new List<cfg.GameEvent>();
            foreach (cfg.GameEvent ev in tables.TbEvent.DataList)
            {
                if (!ev.HasEventType(eventType) || ev.Weight <= 0f)
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
                float defaultWeight = System.Math.Max(float.Epsilon, tables.TbGameBase.DefaultRandomWeight);
                float weight = ev.Weight > 0f ? ev.Weight : defaultWeight;
                weights.Add(weight);
            }

            return candidates[rng.WeightedPickIndex(weights)];
        }

        /// <summary>
        /// act_event 的「全类型合并池」抽取：候选纳入 Event/Reward/Negative 全部类型，
        /// 并把 eventTypes 包含 Reward 的候选权重 ×(1+LuckyEventChance)；多分类只修正一次，其余候选不变。
        /// MoreEvents 只作用于行动排期，不参与事件内容池。
        /// 复用 <see cref="RollEvent"/> 的 weight&gt;0 / repeatable / 前置过滤。
        /// </summary>
        public static cfg.GameEvent RollActionEvent(GameRun run, IRandomStream rng)
        {
            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            var itemRuntime = new ItemRuntime(run);
            float rewardMul = 1f + itemRuntime.LuckyEventChanceBonus();

            var candidates = new List<cfg.GameEvent>();
            var weights = new List<float>();
            foreach (cfg.GameEvent ev in tables.TbEvent.DataList)
            {
                if (!ev.IsActionEventPoolMember || ev.Weight <= 0f)
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
                if (ev.HasEventType(cfg.ActionBehavior.Reward))
                {
                    w *= rewardMul;
                }

                candidates.Add(ev);
                // 修正后权重下限保护：负修正等极端配置也保证仍是正权重可被选中。
                float minimumWeight = System.Math.Max(float.Epsilon, tables.TbGameBase.MinimumRandomWeight);
                weights.Add(w > 0f ? w : minimumWeight);
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            cfg.GameEvent picked = candidates[rng.WeightedPickIndex(weights)];
            if (picked != null
                && picked.HasEventType(cfg.ActionBehavior.Reward)
                && System.Math.Abs(rewardMul - 1f) > 0.0001f)
            {
                itemRuntime.FlashTriggered(m => System.Math.Abs(m.LuckyEventChanceBonus()) > 0.0001f);
            }

            return picked;
        }

        /// <summary>
        /// act_event 的完整抽取入口（唯一 choke point）：
        /// - 未持有 LuckyEventGuarantee 装饰品和消耗品时，仅走 <see cref="RollActionEvent"/> 合并池抽取，不触碰保底计数。
        /// - 持有时：配置值表示保底前需要经历的自然抽取数；达到后，下一次强制从 Reward 池抽。
        /// - 自然抽到 Reward 也只累计、不重置；强制事件队列优先且不消费保底，保底顺延到下一次抽取。
        ///   计数状态存于该装饰品和消耗品模型（只在持有时存在）。
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
            bool guaranteeDue = false;
            if (guarantee != null)
            {
                int precedingNaturalDraws = guarantee.LuckyEventGuaranteeEvery();
                guaranteeDue = precedingNaturalDraws > 0
                    && guarantee.EventGuaranteeStreak >= precedingNaturalDraws;
                if (guaranteeDue)
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
            if (ev != null && guarantee != null && !guaranteeDue)
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
        /// 不在此写 UsedEventIds；事件进入金币由行动编排层在根事件页面打开前发放。
        /// </summary>
        public static EventResolveResult ResolveOption(GameRun run, cfg.EventOption option, IRandomStream rng)
        {
            return ResolveOption(run, option, rng, cfg.EffectType.None);
        }

        public static EventResolveResult ResolveOption(GameRun run, cfg.EventOption option, IRandomStream rng, cfg.EffectType skipEffectType)
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
                if (skipEffectType != cfg.EffectType.None && type == skipEffectType)
                {
                    continue;
                }

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

        /// <summary>事件到达终止（选项无子页、终止展示页、或跟进类效果）时调用：记录使用。</summary>
        public static void OnEventFinished(GameRun run, cfg.GameEvent ev, EventResolveResult result)
        {
            if (run == null || ev == null)
            {
                return;
            }

            run.MarkEventUsed(ev.Id);
        }

        /// <summary>
        /// 尝试发放当前待处理根事件的进入金币。即使没有对应装饰品和消耗品也会记为已处理，
        /// 防止玩家在同一事件流程中获得装饰品和消耗品后倒发；调用方负责紧接着存档。
        /// </summary>
        public static bool TryGrantEventEntryGold(GameRun run, out int gold)
        {
            gold = 0;
            PendingActionExecutionSaveData pending = run?.GetPendingActionExecution();
            cfg.GameAction action = pending == null
                ? null
                : run.Tables?.TbAction.GetOrDefault(pending.ActionId);
            cfg.ActionBehavior behavior = action?.Behavior ?? cfg.ActionBehavior.Effect;
            bool eligible = behavior == cfg.ActionBehavior.Event
                || behavior == cfg.ActionBehavior.Reward
                || behavior == cfg.ActionBehavior.Negative;
            if (!eligible || !run.TryMarkPendingEventEntryGoldGranted())
            {
                return false;
            }

            var itemRuntime = new ItemRuntime(run);
            gold = itemRuntime.EventEnterGold();
            if (gold != 0)
            {
                itemRuntime.FlashTriggered(m => m.EventEnterGold() != 0);
                run.Gold += gold;
            }

            return true;
        }

        private static EventResolveResult ResolveEffect(GameRun run, cfg.EffectType effectType, float effectValue, string effectParam, string fallback, IRandomStream rng)
        {
            switch (effectType)
            {
                case cfg.EffectType.FoodBattle:
                {
                    int required = effectValue > 0f ? RoundToInt(effectValue) : EventBattleRequiredScore(run);
                    string feedback = string.IsNullOrEmpty(fallback) ? $"触发经营挑战，目标美味值 {required}。" : fallback;
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
