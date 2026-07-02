using System.Collections.Generic;
using NUnit.Framework;
using GourmetProject.Game.Meta;

namespace GourmetProject.Tests
{
    /// <summary>
    /// 局外行动轴核心逻辑单测：天数推进/走完判定、区间节点检测、利息计算、周筛选、前置条件求值。
    /// 这些是局外循环「同种子可复现 + 规则正确」的基石，全部走纯逻辑（无引擎/配置依赖）。
    /// </summary>
    public class MetaTimelineTests
    {
        // —— 天数推进 ——

        [Test]
        public void Advance_AddsCost_AndClampsToLength()
        {
            Assert.AreEqual(2f, TimelineMath.Advance(0f, 2f, 7f), 1e-4f);
            Assert.AreEqual(7f, TimelineMath.Advance(6f, 3f, 7f), 1e-4f, "推进不应超过轴长度");
            Assert.AreEqual(5f, TimelineMath.Advance(5f, 0f, 7f), 1e-4f, "0 消耗不推进");
            Assert.AreEqual(5f, TimelineMath.Advance(5f, -3f, 7f), 1e-4f, "负消耗按 0 处理");
        }

        [Test]
        public void Advance_TenthDayGranularity_NoFloatDrift()
        {
            Assert.AreEqual(0.3f, TimelineMath.Advance(0f, 0.3f, 7f), 1e-4f);

            // 连续三次 +0.1，量化后应精确为 0.3，不出现 0.30000004 之类的漂移。
            float day = 0f;
            day = TimelineMath.Advance(day, 0.1f, 7f);
            day = TimelineMath.Advance(day, 0.1f, 7f);
            day = TimelineMath.Advance(day, 0.1f, 7f);
            Assert.AreEqual(0.3f, day, 1e-4f);
            Assert.AreEqual("0.3", day.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));

            // 小数消耗累加到整天也应精确落在整天上，供整天节点判定。
            Assert.AreEqual(1.2f, TimelineMath.Advance(0.5f, 0.7f, 7f), 1e-4f);
        }

        [Test]
        public void IsFinished_TrueAtOrBeyondLength()
        {
            Assert.IsFalse(TimelineMath.IsFinished(6f, 7f));
            Assert.IsFalse(TimelineMath.IsFinished(6.9f, 7f));
            Assert.IsTrue(TimelineMath.IsFinished(7f, 7f));
            Assert.IsTrue(TimelineMath.IsFinished(8f, 7f));
        }

        // —— 区间节点检测 ——

        [Test]
        public void CollectPassed_ReturnsNodesInHalfOpenInterval_SortedByDay()
        {
            var nodes = new List<NodeRef>
            {
                new NodeRef("d5", 5),
                new NodeRef("d2", 2),
                new NodeRef("d3", 3),
                new NodeRef("d7", 7),
            };

            // 从第 2 天推进到第 5 天：应包含 day 3、5（含右端），不含 2（<=prev）与 7（>new），并按 day 升序。
            List<NodeRef> passed = TimelineMath.CollectPassed(nodes, prevDay: 2f, newDay: 5f, triggered: null);

            Assert.AreEqual(2, passed.Count);
            Assert.AreEqual("d3", passed[0].Id);
            Assert.AreEqual("d5", passed[1].Id);
        }

        [Test]
        public void CollectPassed_FractionalProgress_HitsWholeDayNodes()
        {
            var nodes = new List<NodeRef>
            {
                new NodeRef("d3", 3),
                new NodeRef("d4", 4),
                new NodeRef("d5", 5),
            };

            // 从 2.5 天推进到 4.0 天：命中整天节点 3、4，不含 5。
            List<NodeRef> passed = TimelineMath.CollectPassed(nodes, prevDay: 2.5f, newDay: 4.0f, triggered: null);

            Assert.AreEqual(2, passed.Count);
            Assert.AreEqual("d3", passed[0].Id);
            Assert.AreEqual("d4", passed[1].Id);
        }

        [Test]
        public void CollectPassed_SkipsAlreadyTriggered()
        {
            var nodes = new List<NodeRef> { new NodeRef("a", 2), new NodeRef("b", 3) };
            var triggered = new HashSet<string> { "a" };

            List<NodeRef> passed = TimelineMath.CollectPassed(nodes, 0f, 4f, triggered);

            Assert.AreEqual(1, passed.Count);
            Assert.AreEqual("b", passed[0].Id);
        }

        [Test]
        public void CollectPassed_EmptyWhenNoProgress()
        {
            var nodes = new List<NodeRef> { new NodeRef("a", 3) };
            Assert.AreEqual(0, TimelineMath.CollectPassed(nodes, 3f, 3f, null).Count);
        }

        // —— 利息 ——

        [Test]
        public void Interest_FloorsByThreshold()
        {
            Assert.AreEqual(5, TimelineMath.Interest(55, 10, 1));
            Assert.AreEqual(10, TimelineMath.Interest(55, 10, 2));
            Assert.AreEqual(0, TimelineMath.Interest(9, 10, 1), "不足一档没有利息");
            Assert.AreEqual(0, TimelineMath.Interest(100, 0, 1), "阈值非法返回 0");
        }

        // —— 周筛选 ——

        [Test]
        public void MatchesWeek_EmptyMatchesAny()
        {
            Assert.IsTrue(TimelineService.MatchesWeek("", 3, false));
            Assert.IsTrue(TimelineService.MatchesWeek(null, 4, true));
        }

        [Test]
        public void MatchesWeek_NormalAndBossKeywords()
        {
            Assert.IsTrue(TimelineService.MatchesWeek("normal", 3, false));
            Assert.IsFalse(TimelineService.MatchesWeek("normal", 4, true));
            Assert.IsTrue(TimelineService.MatchesWeek("boss", 4, true));
            Assert.IsFalse(TimelineService.MatchesWeek("boss", 3, false));
        }

        [Test]
        public void MatchesWeek_ExplicitWeekList()
        {
            Assert.IsTrue(TimelineService.MatchesWeek("2,4,6", 4, false));
            Assert.IsFalse(TimelineService.MatchesWeek("2,4,6", 3, false));
        }

        // —— 前置条件（行动/事件过滤）——

        [Test]
        public void Precondition_EmptyIsAlwaysSatisfied()
        {
            var ctx = new FakeContext { Gold = 0, WeekIndex = 1 };
            Assert.IsTrue(PreconditionEvaluator.IsSatisfied(ctx, ""));
            Assert.IsTrue(PreconditionEvaluator.IsSatisfied(ctx, null));
        }

        [Test]
        public void Precondition_GoldAndWeekClauses()
        {
            var ctx = new FakeContext { Gold = 50, WeekIndex = 4 };
            Assert.IsTrue(PreconditionEvaluator.IsSatisfied(ctx, "minGold:50"));
            Assert.IsFalse(PreconditionEvaluator.IsSatisfied(ctx, "minGold:51"));
            Assert.IsTrue(PreconditionEvaluator.IsSatisfied(ctx, "maxGold:50"));
            Assert.IsTrue(PreconditionEvaluator.IsSatisfied(ctx, "minWeek:4"));
            Assert.IsFalse(PreconditionEvaluator.IsSatisfied(ctx, "minWeek:5"));
        }

        [Test]
        public void Precondition_MultipleClausesNeedAll()
        {
            var ctx = new FakeContext { Gold = 80, WeekIndex = 2, IsBossWeek = false };
            ctx.Items.Add("item_x");

            Assert.IsTrue(PreconditionEvaluator.IsSatisfied(ctx, "minGold:50|hasItem:item_x"));
            Assert.IsFalse(PreconditionEvaluator.IsSatisfied(ctx, "minGold:50|hasItem:item_y"));
        }

        [Test]
        public void Precondition_BossWeekKeyword()
        {
            var boss = new FakeContext { IsBossWeek = true };
            var normal = new FakeContext { IsBossWeek = false };
            Assert.IsTrue(PreconditionEvaluator.IsSatisfied(boss, "bossWeek"));
            Assert.IsFalse(PreconditionEvaluator.IsSatisfied(normal, "bossWeek"));
        }

        private sealed class FakeContext : IPreconditionContext
        {
            public int Gold { get; set; }
            public int WeekIndex { get; set; }
            public bool IsBossWeek { get; set; }
            public HashSet<string> Items { get; } = new HashSet<string>();

            public bool HasItem(string itemId) => Items.Contains(itemId);
        }
    }
}
