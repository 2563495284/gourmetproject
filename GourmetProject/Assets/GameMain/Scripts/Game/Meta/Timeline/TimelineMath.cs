using System;
using System.Collections.Generic;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>行动轴节点的轻量引用（id + 整天位置），用于纯逻辑运算与单测。</summary>
    public readonly struct NodeRef
    {
        public NodeRef(string id, int day)
        {
            Id = id;
            Day = day;
        }

        public string Id { get; }
        public int Day { get; }
    }

    /// <summary>
    /// 行动轴纯数学：天数推进、区间节点检测、利息计算。无任何引擎/配置依赖，便于 EditMode 单测。
    /// 运行时的 <see cref="TimelineService"/> 与编排层复用这里的规则，避免逻辑漂移。
    /// </summary>
    public static class TimelineMath
    {
        /// <summary>推进天数并夹取到轴长度。</summary>
        public static int Advance(int currentDay, int costDays, int lengthDays)
        {
            int next = currentDay + Math.Max(0, costDays);
            return Math.Min(next, lengthDays);
        }

        /// <summary>是否走完行动轴。</summary>
        public static bool IsFinished(int currentDay, int lengthDays) => currentDay >= lengthDays;

        /// <summary>利息：每满 threshold 金币发放 goldPer 金币。</summary>
        public static int Interest(int gold, int threshold, int goldPer)
        {
            if (threshold <= 0)
            {
                return 0;
            }

            return (gold / threshold) * Math.Max(0, goldPer);
        }

        /// <summary>收集天数从 prevDay 推进到 newDay 经过的、未触发过的节点（按 day 升序）。</summary>
        public static List<NodeRef> CollectPassed(IEnumerable<NodeRef> nodes, int prevDay, int newDay, ICollection<string> triggered)
        {
            var passed = new List<NodeRef>();
            if (nodes == null)
            {
                return passed;
            }

            foreach (NodeRef node in nodes)
            {
                bool already = triggered != null && triggered.Contains(node.Id);
                if (node.Day > prevDay && node.Day <= newDay && !already)
                {
                    passed.Add(node);
                }
            }

            passed.Sort((a, b) => a.Day.CompareTo(b.Day));
            return passed;
        }
    }
}
