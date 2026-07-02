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
    /// 天数以 0.1 天为粒度：推进结果统一量化到一位小数，避免多次 0.1 累加的浮点漂移。
    /// </summary>
    public static class TimelineMath
    {
        /// <summary>浮点比较容差；天数以 0.1 为粒度，半格容差足够判定“走完/命中”。</summary>
        public const float Epsilon = 0.05f;

        /// <summary>把天数量化到 0.1 粒度，消除浮点累加误差。</summary>
        public static float Quantize(float days) => (float)Math.Round(days, 1, MidpointRounding.AwayFromZero);

        /// <summary>推进天数并夹取到轴长度，结果量化到 0.1。</summary>
        public static float Advance(float currentDay, float costDays, float lengthDays)
        {
            float next = currentDay + Math.Max(0f, costDays);
            return Quantize(Math.Min(next, lengthDays));
        }

        /// <summary>是否走完行动轴（含浮点容差）。</summary>
        public static bool IsFinished(float currentDay, float lengthDays) => currentDay >= lengthDays - Epsilon;

        /// <summary>利息：每满 threshold 金币发放 goldPer 金币。</summary>
        public static int Interest(int gold, int threshold, int goldPer)
        {
            if (threshold <= 0)
            {
                return 0;
            }

            return (gold / threshold) * Math.Max(0, goldPer);
        }

        /// <summary>收集天数从 prevDay 推进到 newDay 经过的、未触发过的节点（按 day 升序）。节点落在整天，比较含浮点容差。</summary>
        public static List<NodeRef> CollectPassed(IEnumerable<NodeRef> nodes, float prevDay, float newDay, ICollection<string> triggered)
        {
            var passed = new List<NodeRef>();
            if (nodes == null)
            {
                return passed;
            }

            foreach (NodeRef node in nodes)
            {
                bool already = triggered != null && triggered.Contains(node.Id);
                if (node.Day > prevDay + Epsilon && node.Day <= newDay + Epsilon && !already)
                {
                    passed.Add(node);
                }
            }

            passed.Sort((a, b) => a.Day.CompareTo(b.Day));
            return passed;
        }
    }
}
