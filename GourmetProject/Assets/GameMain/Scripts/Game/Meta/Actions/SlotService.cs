using System;
using System.Collections.Generic;
using System.Globalization;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Run;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.Meta
{
    /// <summary>一台抽奖机的已校验配置快照。</summary>
    public sealed class SlotMachineConfig
    {
        public SlotMachineConfig(
            cfg.GameEvent ev,
            float emptyProbability,
            int freeSpins,
            int paidCost,
            int maxSpins,
            IReadOnlyList<cfg.RewardSlot> rewardSlots,
            IReadOnlyList<float> rewardProbabilities,
            IReadOnlyList<cfg.EventOption> options)
        {
            Event = ev;
            EmptyProbability = Math.Max(0f, emptyProbability);
            FreeSpins = Math.Max(0, freeSpins);
            PaidCost = Math.Max(0, paidCost);
            MaxSpins = Math.Max(1, maxSpins);
            RewardSlots = rewardSlots ?? Array.Empty<cfg.RewardSlot>();
            RewardProbabilities = rewardProbabilities ?? Array.Empty<float>();
            Options = options ?? Array.Empty<cfg.EventOption>();
        }

        public cfg.GameEvent Event { get; }

        public float EmptyProbability { get; }

        public int FreeSpins { get; }

        public int PaidCost { get; }

        public int MaxSpins { get; }

        public IReadOnlyList<cfg.RewardSlot> RewardSlots { get; }

        public IReadOnlyList<float> RewardProbabilities { get; }

        public IReadOnlyList<cfg.EventOption> Options { get; }
    }

    /// <summary>一次纯随机抽取的结果；金币扣除和 pending 状态由编排层原子提交。</summary>
    public sealed class SlotSpinResult
    {
        private SlotSpinResult(
            bool isEmpty,
            RewardOffer offer,
            cfg.RewardKind rewardKind,
            string error)
        {
            IsEmpty = isEmpty;
            Offer = offer;
            RewardKind = rewardKind;
            Error = error ?? string.Empty;
        }

        public bool IsEmpty { get; }

        public RewardOffer Offer { get; }

        public cfg.RewardKind RewardKind { get; }

        public string Error { get; }

        public bool Success => string.IsNullOrEmpty(Error);

        public static SlotSpinResult Empty() =>
            new SlotSpinResult(true, null, cfg.RewardKind.None, string.Empty);

        public static SlotSpinResult Reward(RewardOffer offer, cfg.RewardKind rewardKind) =>
            offer != null
                ? new SlotSpinResult(false, offer, rewardKind, string.Empty)
                : Failed("奖励配置未生成有效奖励。");

        public static SlotSpinResult Failed(string error) =>
            new SlotSpinResult(false, null, cfg.RewardKind.None, error);
    }

    /// <summary>
    /// 抽奖机配置与随机内核。这里只处理 Slot 自己的配置和概率，不调用 EventService。
    /// </summary>
    public static class SlotService
    {
        private const string Tag = "Slot";

        public const string SpinOptionId = "opt_slot_machine_spin";
        public const string LeaveOptionId = "opt_slot_machine_leave";

        public static bool IsConfigured(GameRun run, string actionId, out string error)
        {
            cfg.GameAction action = run?.Tables?.TbAction?.GetOrDefault(actionId);
            return TryGetConfig(run, action, out _, out error);
        }

        public static bool TryGetConfig(
            GameRun run,
            cfg.GameAction action,
            out SlotMachineConfig config,
            out string error)
        {
            return TryGetConfig(run, action, null, out config, out error);
        }

        public static bool TryGetConfig(
            GameRun run,
            cfg.GameAction action,
            string slotEventId,
            out SlotMachineConfig config,
            out string error)
        {
            config = null;
            error = string.Empty;
            if (run?.Tables == null)
            {
                error = "游戏配置尚未加载。";
                return false;
            }

            if (action == null)
            {
                error = "抽奖机行动不存在。";
                return false;
            }

            if (action.Behavior != cfg.ActionBehavior.Slot)
            {
                error = $"行动 {action.Id} 不是 Slot 类型。";
                return false;
            }

            string eventId = string.IsNullOrWhiteSpace(slotEventId)
                ? action.EffectParam
                : slotEventId;
            cfg.GameEvent ev = string.IsNullOrWhiteSpace(eventId)
                ? null
                : run.Tables.TbEvent.GetOrDefault(eventId);
            if (ev == null)
            {
                error = $"抽奖机事件配置缺失：{eventId}";
                return false;
            }

            bool slotType = false;
            if (ev.EventTypes != null)
            {
                for (int i = 0; i < ev.EventTypes.Count; i++)
                {
                    if (ev.EventTypes[i] == cfg.ActionBehavior.Slot)
                    {
                        slotType = true;
                        break;
                    }
                }
            }

            if (!slotType)
            {
                error = $"事件 {ev.Id} 未标记为 Slot 类型。";
                return false;
            }

            List<cfg.EventOption> options = EventService.GetRootOptions(run, ev.Id);
            cfg.EventOption spinOption = null;
            cfg.EventOption leaveOption = null;
            for (int i = 0; i < options.Count; i++)
            {
                cfg.EventOption option = options[i];
                if (option?.Id == SpinOptionId)
                {
                    spinOption = option;
                }
                else if (option?.Id == LeaveOptionId)
                {
                    leaveOption = option;
                }
            }

            if (options.Count != 2 || spinOption == null || leaveOption == null)
            {
                error =
                    $"抽奖机 {ev.Id} 必须配置根选项 {SpinOptionId} 与 {LeaveOptionId}，且不能包含其它根选项。";
                return false;
            }

            if (!IsNoOpOption(spinOption)
                || !IsNoOpOption(leaveOption)
                || spinOption.AutoEnd
                || !leaveOption.AutoEnd)
            {
                error =
                    $"抽奖机 {ev.Id} 的选项只能使用 None 效果；抽奖选项 autoEnd=false，离开选项 autoEnd=true。";
                return false;
            }

            IReadOnlyList<string> rewardSlotIds = ev.SlotRewardSlotIds;
            IReadOnlyList<float> rewardProbabilities = ev.SlotRewardProbabilities;
            var slots = new List<cfg.RewardSlot>();
            var probabilities = new List<float>();
            if (rewardSlotIds == null || rewardSlotIds.Count == 0)
            {
                error = $"抽奖机 {ev.Id} 未配置奖励槽。";
                return false;
            }

            if (rewardProbabilities == null || rewardSlotIds.Count != rewardProbabilities.Count)
            {
                error = $"抽奖机 {ev.Id} 的奖励槽数量与奖励概率数量不一致。";
                return false;
            }

            var seenSlotIds = new HashSet<string>(StringComparer.Ordinal);
            double totalProbability = ev.SlotEmptyProbability;
            if (!IsProbability(ev.SlotEmptyProbability))
            {
                error = $"抽奖机 {ev.Id} 的空奖概率必须在 0～1 之间。";
                return false;
            }

            for (int i = 0; i < rewardSlotIds.Count; i++)
            {
                string rewardSlotId = rewardSlotIds[i];
                if (string.IsNullOrWhiteSpace(rewardSlotId) || !seenSlotIds.Add(rewardSlotId))
                {
                    error = $"抽奖机 {ev.Id} 含有空白或重复的奖励槽 ID：{rewardSlotId}";
                    return false;
                }

                cfg.RewardSlot slot = run.Tables.TbRewardSlot.GetOrDefault(rewardSlotId);
                if (slot == null)
                {
                    error = $"抽奖机 {ev.Id} 引用了不存在的奖励槽：{rewardSlotId}";
                    return false;
                }

                float probability = rewardProbabilities[i];
                if (!IsProbability(probability))
                {
                    error = $"抽奖机 {ev.Id} 的奖励概率必须在 0～1 之间：{rewardSlotId}";
                    return false;
                }

                slots.Add(slot);
                probabilities.Add(probability);
                totalProbability += probability;
            }

            if (Math.Abs(totalProbability - 1d) > 0.0001d)
            {
                error = $"抽奖机 {ev.Id} 的空奖与奖励概率合计必须为 100%，当前为 {totalProbability:P2}。";
                return false;
            }

            cfg.TbGameBase gameBase = run.Tables.TbGameBase;
            int maxSpins = gameBase.SlotMaxSpins;
            int freeSpins = gameBase.SlotFreeSpins;
            int paidCost = gameBase.SlotPaidCost;
            if (maxSpins <= 0)
            {
                error = $"抽奖机 {ev.Id} 的最大次数必须大于 0。";
                return false;
            }

            if (freeSpins < 0 || paidCost < 0)
            {
                error = $"抽奖机 {ev.Id} 含有负数次数或价格。";
                return false;
            }

            config = new SlotMachineConfig(
                ev,
                ev.SlotEmptyProbability,
                freeSpins,
                paidCost,
                maxSpins,
                slots,
                probabilities,
                options);
            return true;
        }

        /// <summary>
        /// 测试友好的纯抽取入口：空奖与各 reward_slot 按事件配置概率只抽一次。
        /// </summary>
        public static SlotSpinResult Roll(
            GameRun run,
            SlotMachineConfig config,
            IRandomStream rng,
            ActionExecutionContext actionContext = null,
            bool fragmentRewardAlreadyGranted = false)
        {
            if (run == null || config == null || rng == null)
            {
                return SlotSpinResult.Failed("抽奖机随机参数不完整。");
            }

            List<float> weights = BuildRollWeights(
                run,
                config,
                fragmentRewardAlreadyGranted);

            int index = rng.WeightedPickIndex(weights);
            if (index == 0)
            {
                return SlotSpinResult.Empty();
            }

            cfg.RewardSlot chosen = config.RewardSlots[index - 1];
            RewardOffer offer = RewardGranter.BuildConfigOffer(run, rng, chosen, actionContext);
            return SlotSpinResult.Reward(offer, chosen.Kind);
        }

        /// <summary>
        /// 第 0 项是空奖，后续项与 RewardSlots 同序。装饰品先将归一后的总中奖率
        /// 乘以 (1 + bonus)，奖励槽内部比例保持不变，空奖占剩余概率。
        /// </summary>
        internal static List<float> BuildRollWeights(
            GameRun run,
            SlotMachineConfig config,
            bool fragmentRewardAlreadyGranted = false)
        {
            var baseWeights = new List<float>(config.RewardProbabilities.Count + 1)
            {
                Math.Max(0f, config.EmptyProbability)
            };
            for (int i = 0; i < config.RewardProbabilities.Count; i++)
            {
                baseWeights.Add(Math.Max(0f, config.RewardProbabilities[i]));
            }

            float bonus = new ItemRuntime(run).SlotWinChanceBonus();
            List<float> adjusted = ApplyWinChanceBonus(baseWeights, bonus, config.Event?.Id);
            return ApplyFragmentRewardLimit(
                adjusted,
                config.RewardSlots,
                fragmentRewardAlreadyGranted);
        }

        /// <summary>
        /// 同一台抽奖机已经出现过格子奖励后，将所有格子奖励槽的最终权重转为空奖。
        /// 该步骤在中奖率加成之后执行，因此其它奖励的最终权重保持不变。
        /// </summary>
        internal static List<float> ApplyFragmentRewardLimit(
            IReadOnlyList<float> weights,
            IReadOnlyList<cfg.RewardSlot> rewardSlots,
            bool fragmentRewardAlreadyGranted)
        {
            var result = new List<float>(weights?.Count ?? 0);
            if (weights == null)
            {
                return result;
            }

            for (int i = 0; i < weights.Count; i++)
            {
                result.Add(Math.Max(0f, weights[i]));
            }

            if (!fragmentRewardAlreadyGranted || result.Count == 0 || rewardSlots == null)
            {
                return result;
            }

            int count = Math.Min(rewardSlots.Count, result.Count - 1);
            for (int i = 0; i < count; i++)
            {
                if (rewardSlots[i]?.Kind != cfg.RewardKind.FragmentChoice)
                {
                    continue;
                }

                int weightIndex = i + 1;
                result[0] += result[weightIndex];
                result[weightIndex] = 0f;
            }

            return result;
        }

        internal static List<float> ApplyWinChanceBonus(
            IReadOnlyList<float> baseWeights,
            float bonus,
            string machineId = null)
        {
            var result = new List<float>(baseWeights?.Count ?? 0);
            if (baseWeights == null || baseWeights.Count == 0)
            {
                return result;
            }

            double total = 0d;
            for (int i = 0; i < baseWeights.Count; i++)
            {
                float weight = Math.Max(0f, baseWeights[i]);
                result.Add(weight);
                total += weight;
            }

            double emptyWeight = result[0];
            double rewardWeight = total - emptyWeight;
            if (!(total > 0d) || !(rewardWeight > 0d) || float.IsNaN(bonus))
            {
                return result;
            }

            double baseWinChance = rewardWeight / total;
            double multiplier = Math.Max(0d, 1d + bonus);
            double rawWinChance = baseWinChance * multiplier;
            double adjustedWinChance = Math.Min(1d, rawWinChance);
            if (rawWinChance > 1d && !string.IsNullOrEmpty(machineId))
            {
                Log.Warning(
                    $"抽奖机 {machineId} 中奖率加成后为 {rawWinChance:P2}，已封顶为 100%。",
                    Tag);
            }

            double adjustedRewardWeight = total * adjustedWinChance;
            double rewardScale = adjustedRewardWeight / rewardWeight;
            result[0] = (float)(total * (1d - adjustedWinChance));
            for (int i = 1; i < result.Count; i++)
            {
                result[i] = (float)(result[i] * rewardScale);
            }

            return result;
        }

        private static bool IsProbability(float value)
        {
            return !float.IsNaN(value)
                && !float.IsInfinity(value)
                && value >= 0f
                && value <= 1f;
        }

        public static int CostForNextSpin(SlotMachineConfig config, int spinsUsed)
        {
            if (config == null || spinsUsed < config.FreeSpins)
            {
                return 0;
            }

            return config.PaidCost;
        }

        public static int CostForNextSpin(GameRun run, SlotMachineConfig config, int spinsUsed)
        {
            int baseCost = CostForNextSpin(config, spinsUsed);
            return run == null ? baseCost : new ItemRuntime(run).ModifySlotSpinCost(baseCost);
        }

        public static string FormatOptionText(
            GameRun run,
            SlotMachineConfig config,
            cfg.EventOption option,
            int spinsUsed,
            bool canAfford)
        {
            if (option == null)
            {
                return string.Empty;
            }

            string text = EventService.FormatRuntimeText(run, option.Text);
            if (option.Id != SpinOptionId || config == null)
            {
                return text;
            }

            int safeSpins = Math.Max(0, Math.Min(spinsUsed, config.MaxSpins));
            int cost = CostForNextSpin(run, config, safeSpins);
            string costText = cost <= 0
                ? "免费抽一次"
                : $"投入 [gold]{cost} 金币[/gold]";
            string statusText = cost > 0 && !canAfford
                ? "[gold]金币[/gold]不足"
                : $"{safeSpins}/{config.MaxSpins}";
            return text
                .Replace("{slotCostText}", costText)
                .Replace("{slotSpins}", safeSpins.ToString(CultureInfo.InvariantCulture))
                .Replace("{slotMaxSpins}", config.MaxSpins.ToString(CultureInfo.InvariantCulture))
                .Replace("{slotStatus}", statusText);
        }

        public static string BuildSpinKey(GameRun run, ActionExecutionContext context, int spinIndex)
        {
            string source = context != null && !string.IsNullOrWhiteSpace(context.SourceKey)
                ? $"node_{context.SourceKey}_repeat{Math.Max(1, context.NodeRepeatIndex)}"
                : $"action_w{run?.WeekIndex ?? 0}_d{(run?.CurrentDay ?? 0f).ToString("0.0", CultureInfo.InvariantCulture)}_r{context?.RunStepIndex ?? 0}_s{context?.StepIndex ?? 0}";
            string actionId = context?.Action?.Id ?? "slot";
            return $"{source}_{actionId}_spin{Math.Max(1, spinIndex)}";
        }

        private static bool IsNoOpOption(cfg.EventOption option)
        {
            if (option?.EffectTypes == null || option.EffectTypes.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < option.EffectTypes.Count; i++)
            {
                if (option.EffectTypes[i] != cfg.EffectType.None)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
