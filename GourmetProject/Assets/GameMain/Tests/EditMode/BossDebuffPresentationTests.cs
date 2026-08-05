using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BossDebuffPresentationTests
    {
        [Test]
        public void DialogueBag_DoesNotRepeatBeforeExhaustion()
        {
            var bag = new BossDialogueShuffleBag(
                new[] { "甲", "乙", "丙", "丁" },
                new Xoshiro256SS(123UL));
            var firstCycle = new HashSet<string>();

            for (int i = 0; i < 4; i++)
            {
                firstCycle.Add(bag.Draw());
            }

            Assert.That(firstCycle, Has.Count.EqualTo(4));
        }

        [Test]
        public void DialogueBag_ReshuffleAvoidsImmediateRepeat()
        {
            var bag = new BossDialogueShuffleBag(
                new[] { "甲", "乙", "丙", "丁" },
                new Xoshiro256SS(456UL));
            string last = string.Empty;
            for (int i = 0; i < 4; i++)
            {
                last = bag.Draw();
            }

            Assert.That(bag.Draw(), Is.Not.EqualTo(last));
        }

        [Test]
        public void DialogueBag_SingleDialogueCanRepeat()
        {
            var bag = new BossDialogueShuffleBag(
                new[] { "唯一一句" },
                new Xoshiro256SS(789UL));

            Assert.That(bag.Draw(), Is.EqualTo("唯一一句"));
            Assert.That(bag.Draw(), Is.EqualTo("唯一一句"));
        }

        [Test]
        public void PresentationPlan_TracksAllRevealKinds()
        {
            var plan = new BossDebuffPresentationPlan("debuff_test", new[] { "对白" });
            plan.AddedCells.Add(new GridPos(1, 2));
            plan.RemovedCells.Add(new GridPos(2, 3));
            plan.DisabledCells.Add(new GridPos(3, 4));
            plan.DuplicatedDishIds.Add("mantou");
            plan.SetRecipeEntryCounts(5, 10);

            Assert.That(plan.HasIntroPresentation, Is.True);
            Assert.That(plan.InitialRecipeEntryCount, Is.EqualTo(5));
            Assert.That(plan.FinalRecipeEntryCount, Is.EqualTo(10));
            Assert.That(plan.Dialogues, Is.EqualTo(new[] { "对白" }));
        }
    }
}
