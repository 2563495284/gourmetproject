using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Runtime;
using Log = GourmetProject.Core.Diagnostics.Log;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 行动轴服务：按周筛选并随机一条行动轴，推进天数游标，检测推进区间内经过的节点。
    /// 节点配置来自 <c>TbTimelineNode</c>（按 timelineId 关联），运行态记录在 <see cref="GameRun"/>。
    /// </summary>
    public static class TimelineService
    {
        private const string Tag = "Timeline";
        private const float DefaultLengthDays = 7f;

        /// <summary>为当前周随机一条行动轴并初始化天数游标。返回选中的行动轴 id。</summary>
        public static string RollWeekTimeline(GameRun run, IRandomStream rng)
        {
            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            bool isBoss = run.IsBossWeek;
            cfg.Character character = tables.TbCharacter.GetOrDefault(run.CharacterId);

            var candidates = new List<cfg.Timeline>();
            foreach (cfg.Timeline tl in tables.TbTimeline.DataList)
            {
                if (MatchesWeek(tl.WeekFilter, run.WeekIndex, isBoss) && MatchesPool(character?.TimelinePool, tl.Id))
                {
                    candidates.Add(tl);
                }
            }

            if (candidates.Count == 0)
            {
                Log.Warning($"第 {run.WeekIndex} 周无匹配行动轴（character={run.CharacterId}, isBoss={isBoss}），回退为 {DefaultLengthDays} 天空轴。", Tag);
                run.BeginTimeline(string.Empty, DefaultLengthDays);
                return string.Empty;
            }

            var weights = new List<float>(candidates.Count);
            foreach (cfg.Timeline tl in candidates)
            {
                weights.Add(tl.Weight > 0f ? tl.Weight : 1f);
            }

            cfg.Timeline chosen = candidates[rng.WeightedPickIndex(weights)];
            float length = chosen.BaseLengthDays > 0 ? chosen.BaseLengthDays : DefaultLengthDays;
            run.BeginTimeline(chosen.Id, length);
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

        /// <summary>当前行动轴的全部节点（按 day 升序）。</summary>
        public static List<cfg.TimelineNode> GetNodes(GameRun run)
        {
            var nodes = new List<cfg.TimelineNode>();
            if (string.IsNullOrEmpty(run.CurrentTimelineId))
            {
                return nodes;
            }

            cfg.Tables tables = run.Tables ?? GameApp.Config.Tables;
            foreach (cfg.TimelineNode node in tables.TbTimelineNode.DataList)
            {
                if (node.TimelineId == run.CurrentTimelineId)
                {
                    nodes.Add(node);
                }
            }

            nodes.Sort((a, b) => a.Day.CompareTo(b.Day));
            return nodes;
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

        /// <summary>weekFilter 匹配：空=任意；normal=非 Boss 周；boss=Boss 周；否则按逗号分隔的周号匹配。</summary>
        public static bool MatchesWeek(string weekFilter, int weekIndex, bool isBoss)
        {
            if (string.IsNullOrEmpty(weekFilter))
            {
                return true;
            }

            switch (weekFilter)
            {
                case "normal":
                    return !isBoss;
                case "boss":
                    return isBoss;
            }

            string[] parts = weekFilter.Split(',');
            foreach (string part in parts)
            {
                if (int.TryParse(part.Trim(), out int w) && w == weekIndex)
                {
                    return true;
                }
            }

            return false;
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
