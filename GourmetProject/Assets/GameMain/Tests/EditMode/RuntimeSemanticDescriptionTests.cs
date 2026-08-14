using System;
using System.Collections.Generic;
using System.IO;
using BreakInfinity;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Game.UI.Tooltips;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class RuntimeSemanticDescriptionTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            string configDirectory = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name =>
                JSON.Parse(File.ReadAllText(Path.Combine(configDirectory, name + ".json"))));
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void ServingBuildDishes_CountEffectiveServingsAndDisplayServingUnit()
        {
            string[] skillIds =
            {
                "sk_eggtart",
                "sk_mousse",
                "sk_choco_bar",
                "sk_mochi",
                "sk_eggroll",
                "sk_hawthorn_cake",
                "sk_brown_sugar_steamed_cake",
                "sk_mango_sago",
            };

            foreach (string skillId in skillIds)
            {
                SkillDef skill = _database.GetSkill(skillId);
                Assert.That(skill, Is.Not.Null, skillId);

                SkillRuleDef countingRule = null;
                foreach (SkillRuleDef rule in skill.Rules)
                {
                    if (rule.CondType == SkillConditionType.DishCount
                        || rule.CondType == SkillConditionType.DishSize)
                    {
                        countingRule = rule;
                        break;
                    }
                }

                Assert.That(countingRule, Is.Not.Null, $"{skillId} 缺少食物份数条件");
                Assert.That(countingRule.CondUnit, Is.EqualTo(CountUnit.Instances), skillId);
                Assert.That(skill.Desc, Does.Contain("份"), skillId);
            }
        }

        [TestCase(1, "1份")]
        [TestCase(3, "3份")]
        [TestCase(8, "8份")]
        public void FoodPreview_CountBadgeUsesServingUnit(int countAs, string expected)
        {
            Assert.That(FoodSummaryTipsView.FormatCountAs(countAs), Is.EqualTo(expected));
        }

        [Test]
        public void SlotPaidOption_ProducesSemanticGoldTokens()
        {
            cfg.EventOption option = cfg.EventOption.DeserializeEventOption(JSON.Parse(@"{
                ""id"": ""opt_slot_machine_spin"",
                ""eventId"": ""ev_slot_machine"",
                ""parentId"": """",
                ""text"": ""{slotCostText}（{slotStatus}）"",
                ""resultText"": """",
                ""condition"": """",
                ""conditionText"": """",
                ""effectTypes"": [],
                ""effectValues"": [],
                ""effectParams"": [],
                ""autoEnd"": false,
                ""branchWeight"": 0,
                ""branchPageText"": """"
            }"));
            var config = new SlotMachineConfig(
                null,
                0f,
                0,
                25,
                3,
                Array.Empty<cfg.RewardSlot>(),
                Array.Empty<float>(),
                Array.Empty<cfg.EventOption>());

            string text = SlotService.FormatOptionText(
                null,
                config,
                option,
                spinsUsed: 0,
                canAfford: false);

            Assert.That(
                text,
                Is.EqualTo("投入 [gold]25 金币[/gold]（[gold]金币[/gold]不足）"));
        }

        [Test]
        public void SettlementSummary_IncludesSemanticCategoriesAndUnlockDescription()
        {
            var progressUpdate = new MetaProgressUpdate
            {
                Statistics = new RunStatistics
                {
                    Won = true,
                    WeekIndex = 4,
                    CurrentDay = 28,
                    LastTotal = new BigDouble(1234),
                    LastTarget = 1000,
                    Gold = 321,
                    OwnedItemCount = 7,
                    CompletedBossIds = new List<string>(),
                },
                NewUnlocks = new List<UnlockEntry>
                {
                    new UnlockEntry(
                        "item_chef_knife",
                        "主厨刀",
                        "装饰品",
                        "[term]装饰品[/term]：主厨刀"),
                },
            };

            SettlementSummary summary = SettlementService.Build(
                null,
                true,
                BigDouble.Zero,
                0,
                progressUpdate);

            StringAssert.Contains("[gold]金币：321[/gold]", summary.Body);
            StringAssert.Contains(
                "持有[term]装饰品[/term]和[term]消耗品[/term]：7 个",
                summary.Body);
            StringAssert.Contains("- [term]装饰品[/term]：主厨刀", summary.Body);
        }

        [Test]
        public void GoldLossFeedback_StylesOnlyGoldTokens()
        {
            var run = new GameRun(
                _tables,
                _database,
                "glutton_dog",
                "runtime-semantic-gold-loss");
            run.Gold = 200;

            string fixedLoss = EffectResolver.Apply(
                run,
                cfg.EffectType.GainGold,
                -80f,
                string.Empty,
                null);
            Assert.That(fixedLoss, Is.EqualTo("失去[gold]80 金币[/gold]。"));

            string allLoss = EffectResolver.Apply(
                run,
                cfg.EffectType.LoseAllGold,
                0f,
                string.Empty,
                null);
            Assert.That(allLoss, Is.EqualTo("失去所有[gold]金币（-120）[/gold]。"));
        }

        [Test]
        public void PermanentScoreFeedback_StrongsTheKeywordWithoutChangingItsColor()
        {
            var run = new GameRun(
                _tables,
                _database,
                "glutton_dog",
                "runtime-semantic-permanent-score");

            string result = EffectResolver.Apply(
                run,
                cfg.EffectType.AddAllRecipeScoreFlat,
                20f,
                string.Empty,
                null);

            StringAssert.Contains("[strong]分数[/strong]", result);
            StringAssert.DoesNotContain("[term]分数[/term]", result);
            if (result.Contains("+20"))
            {
                StringAssert.Contains("[score]+20[/score]", result);
            }
        }

        [Test]
        public void UpdatedCandyAndPositionDescriptions_UseFinalRulesAndSemanticTags()
        {
            SkillDef popping = _database.GetSkill("sk_popping_candy");
            SkillDef gummy = _database.GetSkill("sk_gummy");
            SkillDef roll = _database.GetSkill("sk_soybean_glutinous_roll");
            SkillDef iceCream = _database.GetSkill("sk_ice_cream");
            SkillDef coconutJelly = _database.GetSkill("sk_coconut_milk_jelly");
            SkillDef lollipop = _database.GetSkill("sk_big_lollipop");

            Assert.That(popping.Desc, Is.EqualTo(
                "[context]结算[/context]后\n[context]本列[/context]获得[term]甜蜜传递[/term]时\n[strong]分数[/strong] [score]+40[/score]"));
            Assert.That(popping.Rules[0].ActionParam,
                Does.Contain("when:receive-transfer;resultscope:BuffTargets"));

            Assert.That(gummy.Desc, Is.EqualTo(
                "[context]结算[/context]后\n[context]本列[/context]发动[term]甜蜜传递[/term]时\n[strong]倍率[/strong] [multadd]+0.7[/multadd]"));
            Assert.That(gummy.Rules[0].ActionParam,
                Does.Contain("when:transfer;resultscope:BuffTargets"));

            Assert.That(roll.Desc, Is.EqualTo(
                "[context]周围及自身[/context]\n[strong]倍率[/strong] [multmul]×1[/multmul]（每有1份食物[multadd]+0.15[/multadd]）"));
            Assert.That(roll.Rules[0].ActionParam, Is.EqualTo("linear"));

            Assert.That(iceCream.Desc, Does.Contain("[context]处于边缘[/context]"));
            Assert.That(iceCream.Desc, Does.Contain("[context]边缘[/context]"));
            Assert.That(coconutJelly.Desc, Does.Contain("[context]处于非边缘[/context]"));
            Assert.That(coconutJelly.Desc, Does.Contain("[context]非边缘[/context]"));

            Assert.That(lollipop.Rules, Has.Count.EqualTo(1));
            Assert.That(lollipop.Rules[0].Id, Is.EqualTo("sk_big_lollipop#0"));
            Assert.That(lollipop.Rules[0].ActionType, Is.EqualTo(SkillActionType.TriggerSweetTransfer));
            Assert.That(lollipop.Rules[0].ActionScope, Is.EqualTo(SkillScope.RowAndColumn));
            Assert.That(lollipop.Rules[0].ActionParam, Is.EqualTo("skilltype:TransferSkills"));
            Assert.That(lollipop.Desc, Does.Contain("[context]同行同列[/context]"));
            Assert.That(lollipop.Desc, Does.Not.Contain("每有1个技能"));
        }

    }
}
