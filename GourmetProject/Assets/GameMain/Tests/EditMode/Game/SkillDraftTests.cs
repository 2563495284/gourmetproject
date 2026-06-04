using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Roguelike;
using NUnit.Framework;

namespace GourmetProject.Tests.Game
{
    /// <summary>
    /// 技能抽取核心规则的 EditMode 单元测试：确定性、互斥、前置、叠加上限、
    /// 移动技不重复、经济无限叠加、Tag 软引导加权、候选不足降级。
    /// 纯逻辑（构造 SkillDef + Core 随机流），不依赖 Unity/Config。
    /// </summary>
    public sealed class SkillDraftTests
    {
        private static SkillDef Make(
            string id,
            SkillTag tag = SkillTag.Move,
            int tier = 1,
            float weight = 10f,
            bool isMovement = false,
            int maxStacks = 3,
            string[] prereqAny = null,
            string[] mutex = null)
        {
            return new SkillDef
            {
                Id = id,
                Tier = tier,
                Tag = tag,
                Name = id,
                Desc = id,
                Weight = weight,
                IsMovement = isMovement,
                MaxStacks = maxStacks,
                PrereqAny = prereqAny ?? System.Array.Empty<string>(),
                Mutex = mutex ?? System.Array.Empty<string>(),
            };
        }

        private static IRandomStream Stream(string seed)
        {
            var svc = new RandomService();
            svc.Init(seed);
            return svc.Stream(RunSkillController.DraftStream);
        }

        [Test]
        public void Draft_SameSeed_IsReproducible()
        {
            var pool = new List<SkillDef>
            {
                Make("a"), Make("b"), Make("c"), Make("d"), Make("e"),
            };

            List<SkillDef> first = SkillDraftService.Draft(pool, new RunSkillState(), Stream("balatro-like-seed"), 3);
            List<SkillDef> second = SkillDraftService.Draft(pool, new RunSkillState(), Stream("balatro-like-seed"), 3);

            CollectionAssert.AreEqual(Ids(first), Ids(second));
            Assert.AreEqual(3, first.Count);
        }

        [Test]
        public void Draft_ResultsAreDistinct()
        {
            var pool = new List<SkillDef> { Make("a"), Make("b"), Make("c"), Make("d") };
            List<SkillDef> drawn = SkillDraftService.Draft(pool, new RunSkillState(), Stream("seed-1"), 3);

            var seen = new HashSet<string>();
            foreach (SkillDef d in drawn) Assert.IsTrue(seen.Add(d.Id), $"duplicate {d.Id}");
        }

        [Test]
        public void Mutex_OwnedSkill_ExcludesItsCounterpart()
        {
            SkillDef wall = Make("wall", isMovement: true, maxStacks: 1, mutex: new[] { "doublejump" });
            SkillDef dj = Make("doublejump", isMovement: true, maxStacks: 1, mutex: new[] { "wall" });

            var state = new RunSkillState();
            state.Apply(dj);

            Assert.IsFalse(SkillDraftService.IsEligible(wall, state), "互斥技能应被排除");
            Assert.IsFalse(SkillDraftService.IsEligible(dj, state), "已选的移动技不应重复");
        }

        [Test]
        public void Prereq_FiltersUntilOwned()
        {
            SkillDef baseSkill = Make("wall_cling", isMovement: true, maxStacks: 1);
            SkillDef gated = Make("wall_jump", isMovement: true, maxStacks: 1, prereqAny: new[] { "wall_cling" });

            var state = new RunSkillState();
            Assert.IsFalse(SkillDraftService.IsEligible(gated, state), "前置未满足应被过滤");

            state.Apply(baseSkill);
            Assert.IsTrue(SkillDraftService.IsEligible(gated, state), "前置满足后应可出现");
        }

        [Test]
        public void Prereq_Any_OneOfMultipleSatisfies()
        {
            SkillDef a = Make("a", isMovement: true, maxStacks: 1);
            SkillDef gated = Make("dash_keep_jump", isMovement: true, maxStacks: 1, prereqAny: new[] { "a", "b" });

            var state = new RunSkillState();
            state.Apply(a); // 仅满足其中之一

            Assert.IsTrue(SkillDraftService.IsEligible(gated, state), "拥有任一前置即解锁");
        }

        [Test]
        public void MaxStacks_CapsRepeatedPicks()
        {
            SkillDef stackable = Make("speed", maxStacks: 3);
            var state = new RunSkillState();

            Assert.IsTrue(SkillDraftService.IsEligible(stackable, state));
            state.Apply(stackable);
            state.Apply(stackable);
            Assert.IsTrue(SkillDraftService.IsEligible(stackable, state), "未到上限仍可出现");
            state.Apply(stackable);
            Assert.IsFalse(SkillDraftService.IsEligible(stackable, state), "达到 3 层上限应被过滤");
        }

        [Test]
        public void Movement_DoesNotRepeat()
        {
            SkillDef move = Make("air_dash", isMovement: true, maxStacks: 1);
            var state = new RunSkillState();

            Assert.IsTrue(SkillDraftService.IsEligible(move, state));
            state.Apply(move);
            Assert.IsFalse(SkillDraftService.IsEligible(move, state), "移动技选后不再重复");
        }

        [Test]
        public void Economy_UnlimitedStacks()
        {
            SkillDef eco = Make("energy_cap", tag: SkillTag.Economy, maxStacks: 0);
            var state = new RunSkillState();

            for (int i = 0; i < 8; i++)
            {
                Assert.IsTrue(SkillDraftService.IsEligible(eco, state), "经济技应可无限叠加");
                state.Apply(eco);
            }
        }

        [Test]
        public void TagBoost_IncreasesWeight()
        {
            SkillDef move = Make("m", tag: SkillTag.Move, weight: 10f);
            var state = new RunSkillState();

            Assert.AreEqual(10f, SkillDraftService.WeightFor(move, state), 1e-4f);

            state.Apply(Make("other_move", tag: SkillTag.Move, maxStacks: 5));
            Assert.AreEqual(13f, SkillDraftService.WeightFor(move, state), 1e-4f, "拥有 1 个同 tag → +30%");

            state.Apply(Make("other_move2", tag: SkillTag.Move, maxStacks: 5));
            Assert.AreEqual(16f, SkillDraftService.WeightFor(move, state), 1e-4f, "拥有 2 个同 tag → +60%");
        }

        [Test]
        public void TagBoost_IncreasesPickFrequency()
        {
            // 两个等权候选：A=Move、B=Light。比较"无偏好" vs "已偏好 Move"时 A 被抽中的频率。
            SkillDef a = Make("A", tag: SkillTag.Move, weight: 10f, maxStacks: 99);
            SkillDef b = Make("B", tag: SkillTag.Light, weight: 10f, maxStacks: 99);
            var pool = new List<SkillDef> { a, b };

            int neutralACount = CountFirstIsA(pool, new RunSkillState());

            var biased = new RunSkillState();
            biased.Apply(Make("mv1", tag: SkillTag.Move, maxStacks: 99));
            biased.Apply(Make("mv2", tag: SkillTag.Move, maxStacks: 99));
            int biasedACount = CountFirstIsA(pool, biased);

            Assert.Greater(biasedACount, neutralACount, "偏好 Move 后，Move 候选被抽中的频率应升高");
        }

        [Test]
        public void Draft_FewerEligibleThanRequested_Degrades()
        {
            var pool = new List<SkillDef> { Make("a"), Make("b") };
            List<SkillDef> drawn = SkillDraftService.Draft(pool, new RunSkillState(), Stream("seed-x"), 3);

            Assert.AreEqual(2, drawn.Count, "候选不足 3 时有几个给几个");
        }

        [Test]
        public void Draft_NoEligible_ReturnsEmpty()
        {
            SkillDef move = Make("only", isMovement: true, maxStacks: 1);
            var pool = new List<SkillDef> { move };
            var state = new RunSkillState();
            state.Apply(move); // 唯一候选已被选取

            List<SkillDef> drawn = SkillDraftService.Draft(pool, state, Stream("seed-y"), 3);
            Assert.AreEqual(0, drawn.Count);
        }

        private static int CountFirstIsA(List<SkillDef> pool, RunSkillState state)
        {
            int count = 0;
            for (int i = 0; i < 400; i++)
            {
                List<SkillDef> drawn = SkillDraftService.Draft(pool, state, Stream("freq-" + i), 1);
                if (drawn.Count > 0 && drawn[0].Id == "A") count++;
            }
            return count;
        }

        private static List<string> Ids(List<SkillDef> defs)
        {
            var list = new List<string>(defs.Count);
            foreach (SkillDef d in defs) list.Add(d.Id);
            return list;
        }
    }
}
