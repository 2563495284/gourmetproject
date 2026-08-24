using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Runtime;
using Log = GourmetProject.Core.Diagnostics.Log;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 时间轴服务：按当前 Week 配置随机一条时间轴，推进天数游标，检测推进区间内经过的节点。
    /// 节点配置内嵌于 <c>TbTimeline</c>，进入周时复制到 <see cref="GameRun"/> 的运行态快照。
    /// </summary>
    public static class TimelineService
    {
        private const string Tag = "Timeline";
        private const float DefaultLengthDays = 7f;

        /// <summary>为当前周随机一条时间轴并初始化天数游标。返回选中的时间轴 id。</summary>
        public static string RollWeekTimeline(GameRun run, IRandomStream rng)
        {
            if (run == null || rng == null)
            {
                return string.Empty;
            }

            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            cfg.Week week = run.CurrentWeek ?? tables.TbWeek.GetOrDefault(run.TotalWeeks);
            IReadOnlyList<string> timelineIds = week?.TimelineIds;
            IReadOnlyList<float> timelineWeights = week?.TimelineWeights;

            var candidates = new List<cfg.Timeline>();
            var weights = new List<float>();
            int idCount = timelineIds?.Count ?? 0;
            int weightCount = timelineWeights?.Count ?? 0;
            int pairCount = System.Math.Min(idCount, weightCount);
            bool allowsExternalSideEffects = run.Execution?.AllowsExternalSideEffects == true;

            if (idCount != weightCount && allowsExternalSideEffects)
            {
                Log.Warning($"第 {run.WeekIndex} 周时间轴配置长度不一致（ids={idCount}, weights={weightCount}），只使用两边都有值的部分。", Tag);
            }

            for (int i = 0; i < pairCount; i++)
            {
                string timelineId = timelineIds[i]?.Trim();
                float weight = timelineWeights[i];
                if (string.IsNullOrEmpty(timelineId) || weight <= 0f)
                {
                    continue;
                }

                cfg.Timeline tl = tables.TbTimeline.GetOrDefault(timelineId);
                if (tl == null)
                {
                    if (allowsExternalSideEffects)
                    {
                        Log.Warning($"第 {run.WeekIndex} 周配置了不存在的时间轴：{timelineId}。", Tag);
                    }
                    continue;
                }

                candidates.Add(tl);
                weights.Add(weight);
            }

            if (candidates.Count == 0)
            {
                if (allowsExternalSideEffects)
                {
                    Log.Warning($"第 {run.WeekIndex} 周无可用时间轴，回退为 {DefaultLengthDays} 天空轴。", Tag);
                }
                run.BeginTimeline(string.Empty, DefaultLengthDays);
                ApplyWeekTimelinePassives(run);
                return string.Empty;
            }

            cfg.Timeline chosen = candidates[rng.WeightedPickIndex(weights)];
            List<RuntimeTimelineNode> nodes = BuildTimelineNodes(chosen);
            float length = ResolveTimelineLength(run, chosen, nodes);
            run.BeginTimeline(chosen.Id, length, nodes);
            ApplyWeekTimelinePassives(run);
            if (allowsExternalSideEffects)
            {
                Log.Info($"第 {run.WeekIndex} 周时间轴 = {chosen.Id}（{length} 天）。", Tag);
            }
            return chosen.Id;
        }

        private static void ApplyWeekTimelinePassives(GameRun run)
        {
            new ItemRuntime(run).ApplyWeekTimelinePassives();
        }

        /// <summary>节点引用的原子行动（放置来源）。节点只是「在某天放置某个 action」的引用。</summary>
        public static cfg.GameAction NodeAction(GameRun run, cfg.TimelineNode node)
        {
            if (node == null || string.IsNullOrEmpty(node.ActionId))
            {
                return null;
            }

            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            return tables.TbAction.GetOrDefault(node.ActionId);
        }

        /// <summary>当前时间轴的全部节点（静态配置 + 消耗品动态追加，按 day 升序）。</summary>
        public static List<cfg.TimelineNode> GetNodes(GameRun run)
        {
            var nodes = new List<cfg.TimelineNode>();
            if (string.IsNullOrEmpty(run.CurrentTimelineId))
            {
                return nodes;
            }

            // 当前周节点已在 BeginTimeline 时复制到 Run；装饰品和消耗品后续直接改这份运行态快照。
            foreach (RuntimeTimelineNode rt in run.RuntimeTimelineNodes)
            {
                if (rt.TimelineId == run.CurrentTimelineId)
                {
                    nodes.Add(BuildRuntimeNode(rt));
                }
            }

            nodes.Sort((a, b) =>
            {
                int cmp = a.Day.CompareTo(b.Day);
                return cmp != 0 ? cmp : CompareTimelineNodeIds(a.Id, b.Id);
            });
            return nodes;
        }

        public static List<RuntimeTimelineNode> BuildTimelineNodes(cfg.Timeline timeline)
        {
            var nodes = new List<RuntimeTimelineNode>();
            if (timeline == null)
            {
                return nodes;
            }

            int dayCount = timeline.NodeDays?.Count ?? 0;
            int actionCount = timeline.NodeActionIds?.Count ?? 0;
            int count = System.Math.Min(dayCount, actionCount);
            for (int i = 0; i < count; i++)
            {
                int day = timeline.NodeDays[i];
                string actionId = timeline.NodeActionIds[i];
                if (day <= 0 || string.IsNullOrEmpty(actionId))
                {
                    continue;
                }

                string id = $"cfg_{timeline.Id}_d{day}_{i}";
                nodes.Add(new RuntimeTimelineNode(id, timeline.Id, day, actionId));
            }

            nodes.Sort((a, b) =>
            {
                int cmp = a.Day.CompareTo(b.Day);
                return cmp != 0 ? cmp : string.CompareOrdinal(a.Id, b.Id);
            });
            return nodes;
        }

        private static float ResolveTimelineLength(GameRun run, cfg.Timeline timeline, IReadOnlyList<RuntimeTimelineNode> nodes)
        {
            float length = timeline != null && timeline.BaseLengthDays > 0
                ? timeline.BaseLengthDays
                : DefaultLengthDays;
            if (nodes != null)
            {
                foreach (RuntimeTimelineNode node in nodes)
                {
                    length = System.Math.Max(length, node.Day);
                }
            }

            return TimelineMath.Quantize(length);
        }

        /// <summary>取当前时间轴上尚未结算、day 最小的下一个节点（含运行时节点）；无则返回 null。「加急单」用。</summary>
        public static cfg.TimelineNode GetNextUntriggeredNode(GameRun run)
        {
            if (run == null)
            {
                return null;
            }

            foreach (cfg.TimelineNode node in GetNodes(run))
            {
                if (!run.IsNodeTriggered(node.Id))
                {
                    return node;
                }
            }

            return null;
        }

        public static cfg.TimelineNode GetNode(GameRun run, string nodeId)
        {
            if (run == null || string.IsNullOrEmpty(nodeId))
            {
                return null;
            }

            foreach (cfg.TimelineNode node in GetNodes(run))
            {
                if (node.Id == nodeId)
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>节点复制单候选：当前时间轴上的非星级评鉴节点，不论是否已执行。</summary>
        public static List<cfg.TimelineNode> GetCloneableNodes(GameRun run)
        {
            var result = new List<cfg.TimelineNode>();
            if (run == null)
            {
                return result;
            }

            foreach (cfg.TimelineNode node in GetNodes(run))
            {
                if (!IsRatingEvaluationNode(run, node))
                {
                    result.Add(node);
                }
            }

            return result;
        }

        public static List<cfg.TimelineNode> GetFutureUntriggeredNodes(GameRun run)
        {
            var result = new List<cfg.TimelineNode>();
            if (run == null)
            {
                return result;
            }

            foreach (cfg.TimelineNode node in GetNodes(run))
            {
                if (node.Day > run.CurrentDay + TimelineMath.Epsilon && !run.IsNodeTriggered(node.Id))
                {
                    result.Add(node);
                }
            }

            return result;
        }

        public static List<cfg.TimelineNode> GetPastTriggeredNodes(GameRun run)
        {
            var result = new List<cfg.TimelineNode>();
            if (run == null)
            {
                return result;
            }

            foreach (cfg.TimelineNode node in GetNodes(run))
            {
                if (node.Day < run.CurrentDay - TimelineMath.Epsilon
                    && run.IsNodeTriggered(node.Id)
                    && !IsRatingEvaluationNode(run, node))
                {
                    result.Add(node);
                }
            }

            return result;
        }

        /// <summary>玩家可删除的节点：尚未结算且尚未开始执行，包含当前待执行链中的节点。</summary>
        public static List<cfg.TimelineNode> GetDeletableUnsettledNodes(GameRun run)
        {
            var result = new List<cfg.TimelineNode>();
            if (run == null)
            {
                return result;
            }

            foreach (cfg.TimelineNode node in GetNodes(run))
            {
                if (!run.IsNodeTriggered(node.Id)
                    && !run.IsTimelineNodeExecutionInProgress(node.Id)
                    && !IsRatingEvaluationNode(run, node))
                {
                    result.Add(node);
                }
            }

            return result;
        }

        public static bool IsRatingEvaluationNode(GameRun run, cfg.TimelineNode node)
        {
            cfg.GameAction action = run?.Tables?.TbAction.GetOrDefault(node?.ActionId);
            return node != null && FoodService.IsBossAction(run?.Tables, action);
        }

        /// <summary>取得时间轴上最近的尚未触发 星级评鉴节点；同一天按节点 ID 升序稳定选择。</summary>
        public static cfg.TimelineNode GetNearestUntriggeredBossNode(GameRun run)
        {
            cfg.TimelineNode result = null;
            if (run == null)
            {
                return null;
            }

            foreach (cfg.TimelineNode node in GetNodes(run))
            {
                if (run.IsNodeTriggered(node.Id))
                {
                    continue;
                }

                cfg.GameAction action = NodeAction(run, node);
                if (!FoodService.IsBossAction(run.Tables, action))
                {
                    continue;
                }

                if (result == null
                    || node.Day < result.Day
                    || (node.Day == result.Day && string.CompareOrdinal(node.Id, result.Id) < 0))
                {
                    result = node;
                }
            }

            return result;
        }

        [System.Obsolete("Use GetNearestUntriggeredBossNode.")]
        public static cfg.TimelineNode GetLastUntriggeredBossNode(GameRun run)
            => GetNearestUntriggeredBossNode(run);

        /// <summary>
        /// 由运行时节点数据构造 cfg.TimelineNode。Luban bean 仅有 JSON 构造，这里拼 JSON 串走
        /// 与配置加载相同的 <c>JSON.Parse</c> 反序列化路径（id/actionId 均为受控字符串，无需转义）。
        /// </summary>
        private static cfg.TimelineNode BuildRuntimeNode(RuntimeTimelineNode rt)
        {
            string json = "{\"id\":\"" + rt.Id + "\",\"timelineId\":\"" + rt.TimelineId
                + "\",\"day\":" + rt.Day.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"actionId\":\"" + rt.ActionId + "\"}";
            return new cfg.TimelineNode(Luban.SimpleJSON.JSON.Parse(json));
        }

        /// <summary>当前游标已经到达、但尚未结算的全部节点（按稳定时间轴顺序）。</summary>
        public static List<cfg.TimelineNode> GetDueUntriggeredNodes(GameRun run)
        {
            var due = new List<cfg.TimelineNode>();
            if (run == null)
            {
                return due;
            }

            foreach (cfg.TimelineNode node in GetNodes(run))
            {
                if (node.Day <= run.CurrentDay + TimelineMath.Epsilon
                    && !run.IsNodeTriggered(node.Id))
                {
                    due.Add(node);
                }
            }

            return due;
        }

        /// <summary>返回当前最早的到期未结算节点；无则返回 null。</summary>
        public static cfg.TimelineNode GetNextDueUntriggeredNode(GameRun run)
        {
            List<cfg.TimelineNode> due = GetDueUntriggeredNodes(run);
            return due.Count > 0 ? due[0] : null;
        }

        /// <summary>收集天数从 prevDay 推进到 newDay 经过的、尚未结算的节点（按 day 升序，起止日均包含）。</summary>
        public static List<cfg.TimelineNode> CollectPassedNodes(GameRun run, float prevDay, float newDay)
        {
            var passed = new List<cfg.TimelineNode>();
            foreach (cfg.TimelineNode node in GetNodes(run))
            {
                if (node.Day >= prevDay - TimelineMath.Epsilon
                    && node.Day <= newDay + TimelineMath.Epsilon
                    && !run.IsNodeTriggered(node.Id))
                {
                    passed.Add(node);
                }
            }

            return passed;
        }

        /// <summary>时间轴是否已走完（天数游标到达/超过轴长度）。</summary>
        public static bool IsWeekFinished(GameRun run)
        {
            return TimelineMath.IsFinished(run.CurrentDay, run.TimelineLengthDays);
        }

        /// <summary>把天数游标向前推进，结果不超过轴长度。返回推进前的天数。</summary>
        public static float AdvanceDays(GameRun run, float days)
        {
            float prev = run.CurrentDay;
            run.CurrentDay = TimelineMath.Advance(run.CurrentDay, days, run.TimelineLengthDays);
            return prev;
        }

        private static int CompareTimelineNodeIds(string left, string right)
        {
            bool leftDynamic = TryGetDynamicNodeSerial(left, out int leftSerial);
            bool rightDynamic = TryGetDynamicNodeSerial(right, out int rightSerial);
            if (leftDynamic != rightDynamic)
            {
                return leftDynamic ? 1 : -1;
            }

            if (leftDynamic && leftSerial != rightSerial)
            {
                return leftSerial.CompareTo(rightSerial);
            }

            return string.CompareOrdinal(left, right);
        }

        private static bool TryGetDynamicNodeSerial(string id, out int serial)
        {
            serial = 0;
            if (string.IsNullOrEmpty(id) || !id.StartsWith("dyn_", System.StringComparison.Ordinal))
            {
                return false;
            }

            int separator = id.LastIndexOf('_');
            return separator >= 0
                && separator + 1 < id.Length
                && int.TryParse(id.Substring(separator + 1), out serial);
        }
    }
}
