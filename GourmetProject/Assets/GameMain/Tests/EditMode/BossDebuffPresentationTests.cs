using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
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

        [Test]
        public void PrepareServe_AppliesDebuffStatesBeforeOutletTips()
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            var definition = new DishDef(
                "dish_test",
                "测试食物",
                1,
                shape,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                allowRotate: false);
            var entry = new RecipeSlotEntry(definition.Id);
            entry.MarkSkillsDisabled();
            entry.MarkExcludedFromScore();
            var database = new GameplayDatabase(
                new[] { definition },
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var session = new BattleSession(
                new DiningTable(1, 1),
                database,
                new Xoshiro256SS(321UL),
                new[] { new RecipeSlot("slot", new[] { entry }) },
                requiredScore: 1);

            ServePrepareResult result = session.PrepareServe(0);
            Assert.That(result.Success, Is.True);
            DishInstance dish = result.PreparedDish.Dish;

            FoodTipsData tips = FoodTipsDataFactory.Build(dish, null, database);

            Assert.That(dish.SkillsDisabled, Is.True);
            Assert.That(dish.ExcludedFromScore, Is.True);
            Assert.That(tips.Summary.SkillsDisabled, Is.True);
            Assert.That(tips.SpecialTags.Select(tag => tag.Title),
                Does.Contain("技能失效").And.Contain("不计分"));
        }
    }
}
