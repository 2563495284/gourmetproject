using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Runtime;
using Log = GourmetProject.Core.Diagnostics.Log;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 行动轴服务：按当前 Week 配置随机一条行动轴，推进天数游标，检测推进区间内经过的节点。
    /// 节点配置来自 <c>TbTimelineNode</c>（按 timelineId 关联），运行态记录在 <see cref="GameRun"/>。
    /// </summary>
    public static class TimelineService
    {
        private const string Tag = "Timeline";
        private const float DefaultLengthDays = 7f;

        /// <summary>为当前周随机一条行动轴并初始化天数游标。返回选中的行动轴 id。</summary>
        public static string RollWeekTimeline(GameRun run, IRandomStream rng)
        {
            if (run == null || rng == null)
            {
                return string.Empty;
            }

            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            cfg.Character character = tables.TbCharacter.GetOrDefault(run.CharacterId);
            cfg.Week week = run.CurrentWeek ?? tables.TbWeek.GetOrDefault(run.TotalWeeks);
            IReadOnlyList<string> timelineIds = week?.TimelineIds;
            IReadOnlyList<float> timelineWeights = week?.TimelineWeights;

            var candidates = new List<cfg.Timeline>();
            var weights = new List<float>();
            int idCount = timelineIds?.Count ?? 0;
            int weightCount = timelineWeights?.Count ?? 0;
            int pairCount = System.Math.Min(idCount, weightCount);

            if (idCount != weightCount)
            {
                Log.Warning($"第 {run.WeekIndex} 周行动轴配置长度不一致（ids={idCount}, weights={weightCount}），只使用两边都有值的部分。", Tag);
            }

            for (int i = 0; i < pairCount; i++)
            {
                string timelineId = timelineIds[i]?.Trim();
                float weight = timelineWeights[i];
                if (string.IsNullOrEmpty(timelineId) || weight <= 0f || !MatchesPool(character?.TimelinePool, timelineId))
                {
                    continue;
                }

                cfg.Timeline tl = tables.TbTimeline.GetOrDefault(timelineId);
                if (tl == null)
                {
                    Log.Warning($"第 {run.WeekIndex} 周配置了不存在的行动轴：{timelineId}。", Tag);
                    continue;
                }

                candidates.Add(tl);
                weights.Add(weight);
            }

            if (candidates.Count == 0)
            {
                Log.Warning($"第 {run.WeekIndex} 周无匹配行动轴（character={run.CharacterId}），回退为 {DefaultLengthDays} 天空轴。", Tag);
                run.BeginTimeline(string.Empty, DefaultLengthDays);
                return string.Empty;
            }

            cfg.Timeline chosen = candidates[rng.WeightedPickIndex(weights)];
            List<RuntimeTimelineNode> nodes = BuildTimelineNodes(chosen);
            float length = ResolveTimelineLength(run, chosen, nodes);
            run.BeginTimeline(chosen.Id, length, nodes);
            Log.Info($"第 {run.WeekIndex} 周行动轴 = {chosen.Id}（{length} 天）。", Tag);
            return chosen.Id;
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

        /// <summary>当前行动轴的全部节点（静态配置 + 主动道具动态追加，按 day 升序）。</summary>
        public static List<cfg.TimelineNode> GetNodes(GameRun run)
        {
            var nodes = new List<cfg.TimelineNode>();
            if (string.IsNullOrEmpty(run.CurrentTimelineId))
            {
                return nodes;
            }

            // 当前周节点已在 BeginTimeline 时复制到 Run；道具后续直接改这份运行态快照。
            foreach (RuntimeTimelineNode rt in run.RuntimeTimelineNodes)
            {
                if (rt.TimelineId == run.CurrentTimelineId)
                {
                    nodes.Add(BuildRuntimeNode(rt));
                }
            }

            nodes.Sort((a, b) => a.Day.CompareTo(b.Day));
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
            int bossDay = 0;
            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            if (nodes != null)
            {
                foreach (RuntimeTimelineNode node in nodes)
                {
                    cfg.GameAction action = tables.TbAction.GetOrDefault(node.ActionId);
                    if (action != null
                        && action.Behavior == cfg.ActionBehavior.Food
                        && !string.IsNullOrEmpty(action.FoodId))
                    {
                        bossDay = System.Math.Max(bossDay, node.Day);
                    }
                }
            }

            if (bossDay > 0)
            {
                return bossDay;
            }

            return timeline != null && timeline.BaseLengthDays > 0 ? timeline.BaseLengthDays : DefaultLengthDays;
        }

        /// <summary>取当前行动轴上尚未结算、day 最小的下一个节点（含运行时节点）；无则返回 null。「加急单」用。</summary>
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

        /// <summary>收集天数从 prevDay 推进到 newDay 经过的、尚未结算的节点（按 day 升序）。节点落在整天，比较含浮点容差。</summary>
        public static List<cfg.TimelineNode> CollectPassedNodes(GameRun run, float prevDay, float newDay)
        {
            var passed = new List<cfg.TimelineNode>();
            foreach (cfg.TimelineNode node in GetNodes(run))
            {
                if (node.Day > prevDay + TimelineMath.Epsilon && node.Day <= newDay + TimelineMath.Epsilon && !run.IsNodeTriggered(node.Id))
                {
                    passed.Add(node);
                }
            }

            return passed;
        }

        /// <summary>行动轴是否已走完（天数游标到达/超过轴长度）。</summary>
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

        /// <summary>逗号分隔池匹配：空池=任意通过；否则需包含 value。</summary>
        private static bool MatchesPool(string pool, string value)
        {
            if (string.IsNullOrEmpty(pool))
            {
                return true;
            }

            foreach (string part in pool.Split(','))
            {
                if (part.Trim() == value)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
