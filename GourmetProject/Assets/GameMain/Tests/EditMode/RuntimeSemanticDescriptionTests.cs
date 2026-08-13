using System;
using System.Collections.Generic;
using System.IO;
using BreakInfinity;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
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

    }
}
