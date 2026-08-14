using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Balance
{
    internal readonly struct HeadlessEventOptionDecision
    {
        public HeadlessEventOptionDecision(int index, string optionId, string reason)
        {
            Index = index;
            OptionId = optionId ?? string.Empty;
            Reason = reason ?? string.Empty;
        }

        public int Index { get; }
        public string OptionId { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// 只读评估事件选项及其后续效果树。它不会预执行事件，也不会消费局内规则随机；
    /// Normal 的选择随机仅使用自动玩家自己的 policy stream。
    /// </summary>
    internal static class HeadlessEventOptionPolicy
    {
        private const float NormalTemperature = 12f;

        public static HeadlessEventOptionDecision Pick(
            GameRun run,
            string eventTitle,
            IReadOnlyList<string> displayedOptions,
            IReadOnlyList<bool> optionEnabled,
            AutoPlayerLevel playerLevel,
            IRandomStream policyRandom)
        {
            var enabled = new List<int>();
            for (int i = 0; i < (displayedOptions?.Count ?? 0); i++)
            {
                if (optionEnabled == null || i >= optionEnabled.Count || optionEnabled[i])
                {
                    enabled.Add(i);
                }
            }

            if (enabled.Count == 0)
            {
                return new HeadlessEventOptionDecision(-1, string.Empty, "无可用事件选项");
            }

            Dictionary<int, cfg.EventOption> resolved = ResolveDisplayedOptions(
                run,
                eventTitle,
                displayedOptions,
                enabled);
            if (resolved.Count != enabled.Count)
            {
                int fallback = playerLevel == AutoPlayerLevel.Expert || policyRandom == null
                    ? enabled[0]
                    : enabled[policyRandom.Range(0, enabled.Count)];
                return new HeadlessEventOptionDecision(
                    fallback,
                    resolved.TryGetValue(fallback, out cfg.EventOption option) ? option.Id : string.Empty,
                    "事件配置无法完整匹配，使用稳定回退");
            }

            var scores = new List<float>(enabled.Count);
            for (int i = 0; i < enabled.Count; i++)
            {
                cfg.EventOption option = resolved[enabled[i]];
                scores.Add(ScoreTree(run, option, new HashSet<string>(StringComparer.Ordinal)));
            }

            int local;
            string mode;
            if (playerLevel == AutoPlayerLevel.Expert || policyRandom == null)
            {
                local = IndexOfMax(scores);
                mode = "最高事件效用";
            }
            else
            {
                float maximum = scores.Max();
                var weights = scores.Select(score =>
                    Math.Max(0.000001f, (float)Math.Exp(
                        Math.Max(-50f, Math.Min(0f, (score - maximum) / NormalTemperature)))))
                    .ToList();
                local = policyRandom.WeightedPickIndex(weights);
                local = Math.Max(0, Math.Min(enabled.Count - 1, local));
                mode = "事件policy-softmax";
            }

            int selectedIndex = enabled[local];
            cfg.EventOption selected = resolved[selectedIndex];
            string candidates = string.Join(
                ",",
                enabled.Select((index, i) => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}={1:0.0}",
                    resolved[index].Id,
                    scores[i])));
            return new HeadlessEventOptionDecision(
                selectedIndex,
                selected.Id,
                $"{mode}|心={run?.HeartsRemaining ?? 0}/{run?.HeartCapacity ?? 0}|候选=[{candidates}]");
        }

        private static Dictionary<int, cfg.EventOption> ResolveDisplayedOptions(
            GameRun run,
            string eventTitle,
            IReadOnlyList<string> displayedOptions,
            IReadOnlyList<int> enabled)
        {
            var result = new Dictionary<int, cfg.EventOption>();
            if (run?.Tables == null || displayedOptions == null)
            {
                return result;
            }

            HashSet<string> eventIds = run.Tables.TbEvent.DataList
                .Where(value => string.Equals(
                    EventService.FormatRuntimeText(run, value.Name),
                    eventTitle ?? string.Empty,
                    StringComparison.Ordinal))
                .Select(value => value.Id)
                .ToHashSet(StringComparer.Ordinal);
            IEnumerable<cfg.EventOption> pool = run.Tables.TbEventOption.DataList;
            if (eventIds.Count > 0)
            {
                pool = pool.Where(option => eventIds.Contains(option.EventId));
            }

            List<cfg.EventOption> options = pool.ToList();
            foreach (int index in enabled)
            {
                string displayed = displayedOptions[index] ?? string.Empty;
                cfg.EventOption match = options.FirstOrDefault(option => string.Equals(
                    EventService.FormatRuntimeText(run, option.Text),
                    displayed,
                    StringComparison.Ordinal));
                if (match != null)
                {
                    result[index] = match;
                }
            }

            return result;
        }

        private static float ScoreTree(
            GameRun run,
            cfg.EventOption option,
            HashSet<string> visiting)
        {
            if (option == null || !visiting.Add(option.Id))
            {
                return 0f;
            }

            float score = ScoreDirectEffects(run, option);
            List<cfg.EventOption> children = EventService.GetChildOptions(
                    run,
                    option.EventId,
                    option.Id)
                .Where(child => PreconditionEvaluator.IsSatisfied(run, child.Condition))
                .ToList();
            if (children.Count > 0)
            {
                bool weighted = children.Any(child => child.BranchWeight > 0f);
                if (weighted)
                {
                    float totalWeight = children.Sum(child => Math.Max(0f, child.BranchWeight));
                    if (totalWeight > 0f)
                    {
                        float expected = 0f;
                        foreach (cfg.EventOption child in children)
                        {
                            float weight = Math.Max(0f, child.BranchWeight);
                            var childPath = new HashSet<string>(visiting, StringComparer.Ordinal);
                            expected += weight / totalWeight * ScoreTree(run, child, childPath);
                        }

                        score += expected;
                    }
                }
                else
                {
                    float best = float.MinValue;
                    foreach (cfg.EventOption child in children)
                    {
                        var childPath = new HashSet<string>(visiting, StringComparer.Ordinal);
                        best = Math.Max(best, ScoreTree(run, child, childPath));
                    }

                    if (best > float.MinValue)
                    {
                        score += best;
                    }
                }
            }

            visiting.Remove(option.Id);
            return score;
        }

        private static float ScoreDirectEffects(GameRun run, cfg.EventOption option)
        {
            float score = 0f;
            for (int i = 0; i < option.EffectTypes.Count; i++)
            {
                cfg.EffectType type = option.EffectTypes[i];
                float value = i < option.EffectValues.Count ? option.EffectValues[i] : 0f;
                string param = i < option.EffectParams.Count ? option.EffectParams[i] ?? string.Empty : string.Empty;
                int count = Math.Max(1, (int)Math.Round(Math.Abs(value)));
                switch (type)
                {
                    case cfg.EffectType.None:
                        break;
                    case cfg.EffectType.RestoreHearts:
                    {
                        int missing = Math.Max(0, (run?.HeartCapacity ?? 0) - (run?.HeartsRemaining ?? 0));
                        score += Math.Min(missing, Math.Max(0, (int)Math.Round(value))) * 100f;
                        break;
                    }
                    case cfg.EffectType.AddAllRecipeScoreFlat:
                        score += Math.Max(0f, value)
                            * Math.Max(1, Math.Min(8, run?.RecipeEntries?.Count ?? 1)) * 0.8f;
                        break;
                    case cfg.EffectType.GainGold:
                        score += value / 10f;
                        break;
                    case cfg.EffectType.LoseAllGold:
                        score -= (run?.Gold ?? 0) / 8f;
                        break;
                    case cfg.EffectType.LoseEscalatingGold:
                        score -= Math.Abs(value) / 8f;
                        break;
                    case cfg.EffectType.AddBossTargetScorePct:
                        score -= value * 300f;
                        break;
                    case cfg.EffectType.AddBossBaseGoldPct:
                    case cfg.EffectType.AddBusinessGoldPct:
                        score += value * 12f;
                        break;
                    case cfg.EffectType.GrantRandomActiveItems:
                        score += count * 9f;
                        break;
                    case cfg.EffectType.GrantRandomPassiveItems:
                        score += param.IndexOf("Negative", StringComparison.OrdinalIgnoreCase) >= 0
                            ? -count * 20f
                            : count * 14f;
                        break;
                    case cfg.EffectType.GrantFragmentPack:
                        score += count * 12f;
                        break;
                    case cfg.EffectType.AddRandomRecipeFlavor:
                        score += count * 8f;
                        break;
                    case cfg.EffectType.GainRandomFlavoredDishes:
                    case cfg.EffectType.EnqueueDishChoice:
                    case cfg.EffectType.AddDish:
                        score += count * 6f;
                        break;
                    case cfg.EffectType.GainLegendaryItem:
                        score += 35f;
                        break;
                    case cfg.EffectType.FoodBattle:
                        score -= (run?.HeartsRemaining ?? 0) <= 1 ? 120f : 45f;
                        break;
                    case cfg.EffectType.GameOver:
                        score -= 10000f;
                        break;
                    case cfg.EffectType.Victory:
                        score += 10000f;
                        break;
                    case cfg.EffectType.RemoveActiveItemsForGold:
                        score -= Math.Max(0, run?.ActiveItemCount ?? 0) * 4f;
                        break;
                    case cfg.EffectType.RemoveRandomRecipeDish:
                        score -= count * 10f;
                        break;
                    default:
                        score += Math.Max(-10f, Math.Min(10f, value));
                        break;
                }
            }

            return score;
        }

        private static int IndexOfMax(IReadOnlyList<float> values)
        {
            int best = 0;
            for (int i = 1; i < values.Count; i++)
            {
                if (values[i] > values[best])
                {
                    best = i;
                }
            }

            return best;
        }
    }
}
