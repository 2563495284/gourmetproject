using System;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class FoodTipsExternalSkillsTests
    {
        [Test]
        public void Build_PutsCopiedSkillsInExternalSkillsInsteadOfSummary()
        {
            DishInstance dish = CreateDish(
                out GameplayDatabase db,
                intrinsicId: "sk_own",
                copiedId: "sk_copied");
            dish.AddSkill("sk_copied", "双层蛋糕<技能复制>");

            FoodTipsData data = FoodTipsDataFactory.Build(dish, table: null, db);

            Assert.That(data.Summary.Skills, Has.Count.EqualTo(1));
            Assert.That(data.Summary.Skills[0].Title, Is.EqualTo("固有技能"));
            Assert.That(data.Summary.Skills[0].Desc, Is.EqualTo("自身加分"));
            Assert.That(data.ExternalSkills, Has.Count.EqualTo(1));
            Assert.That(data.ExternalSkills[0].Title, Is.EqualTo("双层蛋糕<技能复制>"));
            Assert.That(data.ExternalSkills[0].Desc, Is.EqualTo("复制来的加分"));
        }

        [Test]
        public void Build_KeepsTransferredSkillsInExternalSkillsAfterCopied()
        {
            DishInstance dish = CreateDish(
                out GameplayDatabase db,
                intrinsicId: "sk_own",
                copiedId: "sk_copied");
            dish.AddSkill("sk_copied", "双层蛋糕<技能复制>");
            dish.AddTransferredSkill(
                new SkillEffect(CreateRule("sk_transfer_1", "sk_own"), "传递加分"),
                "马卡龙<甜蜜传递>");

            FoodTipsData data = FoodTipsDataFactory.Build(dish, table: null, db);

            Assert.That(data.ExternalSkills, Has.Count.EqualTo(2));
            Assert.That(data.ExternalSkills[0].Title, Is.EqualTo("双层蛋糕<技能复制>"));
            Assert.That(data.ExternalSkills[1].Title, Is.EqualTo("马卡龙<甜蜜传递>"));
            Assert.That(data.ExternalSkills[1].Desc, Is.EqualTo("传递加分"));
        }

        [Test]
        public void BuildRevealed_ShowsCopiedSkillsOnlyAfterCopyReveal()
        {
            DishInstance dish = CreateDish(
                out GameplayDatabase db,
                intrinsicId: "sk_own",
                copiedId: "sk_copied");
            var reveal = new SettlementRevealState();
            reveal.CaptureBaseline(dish);
            dish.AddSkill("sk_copied", "双层蛋糕<技能复制>");

            Assert.That(reveal.TryBuildReveal(dish, out FoodTipsReveal beforeCopy), Is.True);
            FoodTipsData hidden = FoodTipsDataFactory.BuildRevealed(
                dish,
                table: null,
                db,
                beforeCopy);
            Assert.That(hidden.Summary.Skills, Has.Count.EqualTo(1));
            Assert.That(hidden.ExternalSkills, Is.Empty);

            reveal.RevealCopiedSkills(dish.Id, 1);
            Assert.That(reveal.TryBuildReveal(dish, out FoodTipsReveal afterCopy), Is.True);
            FoodTipsData shown = FoodTipsDataFactory.BuildRevealed(
                dish,
                table: null,
                db,
                afterCopy);
            Assert.That(shown.Summary.Skills, Has.Count.EqualTo(1));
            Assert.That(shown.ExternalSkills, Has.Count.EqualTo(1));
            Assert.That(shown.ExternalSkills[0].Title, Is.EqualTo("双层蛋糕<技能复制>"));
        }

        private static DishInstance CreateDish(
            out GameplayDatabase db,
            string intrinsicId,
            string copiedId)
        {
            SkillDef own = new SkillDef(
                intrinsicId,
                "固有技能",
                "自身加分",
                Array.Empty<string>());
            SkillDef copied = new SkillDef(
                copiedId,
                "复制技能",
                "复制来的加分",
                Array.Empty<string>());
            DishShape shape = DishShape.FromRows(new[] { "X" });
            var def = new DishDef(
                "cake",
                "蛋糕",
                10,
                shape,
                0,
                0,
                1f,
                new[] { intrinsicId },
                string.Empty);
            db = new GameplayDatabase(
                new[] { def },
                new[] { own, copied },
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            return new DishInstance(
                1,
                def,
                new Placement(shape, 0, new GridPos(0, 0)),
                new[] { intrinsicId },
                Array.Empty<string>());
        }

        private static SkillRuleDef CreateRule(string id, string skillId)
        {
            return new SkillRuleDef(
                id,
                skillId,
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.AddFlat,
                SkillScope.Self,
                0,
                new[] { 1f },
                Array.Empty<string>());
        }
    }
}
