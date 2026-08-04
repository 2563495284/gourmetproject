using System;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class RewardSlotPresentationTests
    {
        private static readonly object[] MaterialRewardCases =
        {
            new object[]
            {
                "lay_wood",
                new[] { "item_active_lay_cherry", "item_active_lay_walnut" },
            },
            new object[]
            {
                "lay_stone",
                new[] { "item_active_lay_marble", "item_active_lay_obsidian", "item_active_lay_emerald" },
            },
            new object[]
            {
                "lay_metal",
                new[] { "item_active_lay_gold", "item_active_lay_silver" },
            },
        };

        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void EveryRewardSlot_HasValidPresentationConfig()
        {
            foreach (cfg.RewardSlot slot in _tables.TbRewardSlot.DataList)
            {
                Assert.That(slot.Name, Is.Not.Empty, slot.Id);
                Assert.That(slot.RuleTemplate, Is.Not.Empty, slot.Id);

                string withoutKnownPlaceholders = slot.RuleTemplate
                    .Replace("{choiceCount}", string.Empty)
                    .Replace("{requiredPickCount}", string.Empty);
                Assert.That(withoutKnownPlaceholders, Does.Not.Contain("{"), slot.Id);
                Assert.That(withoutKnownPlaceholders, Does.Not.Contain("}"), slot.Id);
            }
        }

        [Test]
        public void RolledGroup_SnapshotsChosenSlotAndUsesFinalCandidateCount()
        {
            GameRun run = CreateRun();
            run.AddEventChoiceCountPenalty(1, -1);

            RewardChoiceGroup group = RewardGranter.BuildConfigChoiceGroup(
                run,
                new Xoshiro256SS(42UL),
                "passive_choice_3",
                "调用方标题不应覆盖配置");

            Assert.That(group, Is.Not.Null);
            Assert.That(group.SourceSlotId, Is.EqualTo("passive_choice_3"));
            Assert.That(group.Title, Is.EqualTo("装饰品选择"));
            Assert.That(group.Description, Is.Empty);
            Assert.That(group.Choices, Has.Count.EqualTo(2));
            Assert.That(group.RuleText, Does.Contain("2 个装饰品"));
            Assert.That(group.RuleText, Does.Not.Contain("{choiceCount}"));
            Assert.That(group.Choices.All(choice => !string.IsNullOrWhiteSpace(choice.Description)), Is.True);
        }

        [Test]
        public void PendingOffer_NewPresentationFieldsRoundTrip()
        {
            GameRun run = CreateRun();
            RewardChoice choice = RewardChoice.Gold(12, "测试金币");
            var group = new RewardChoiceGroup(
                "配置名称",
                new[] { choice },
                description: "配置描述",
                ruleText: "配置规则",
                sourceSlotId: "slot_test");
            run.SetPendingRewardOffer(
                "reward",
                new RewardOffer(0, new[] { group }, null, baseGoldClaimed: true));

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            RewardChoiceGroup actual = restored.GetPendingRewardOffer("reward").MainGroup;

            Assert.That(actual.Title, Is.EqualTo("配置名称"));
            Assert.That(actual.Description, Is.EqualTo("配置描述"));
            Assert.That(actual.RuleText, Is.EqualTo("配置规则"));
            Assert.That(actual.SourceSlotId, Is.EqualTo("slot_test"));
            Assert.That(actual.Choices[0].Id, Is.EqualTo(choice.Id));
        }

        [Test]
        public void PendingOffer_LegacyGroupKeepsOriginalCandidatesWithoutReroll()
        {
            GameRun run = CreateRun();
            run.SetPendingRewardOffer(
                "reward",
                new RewardOffer(
                    0,
                    new[]
                    {
                        new RewardChoiceGroup(
                            "旧奖励",
                            new[] { new RewardChoice(cfg.RewardKind.PassiveItemChoice, "legacy_item", "旧装饰品和消耗品", "旧描述") }),
                    },
                    null,
                    baseGoldClaimed: true));
            RunSaveData save = run.ToSaveData();
            save.PendingRewardOffer.FixedGroups[0].Description = null;
            save.PendingRewardOffer.FixedGroups[0].RuleText = null;
            save.PendingRewardOffer.FixedGroups[0].SourceSlotId = null;

            GameRun restored = GameRun.FromSaveData(_tables, _database, save);
            RewardChoiceGroup actual = restored.GetPendingRewardOffer("reward").MainGroup;

            Assert.That(actual.Title, Is.EqualTo("旧奖励"));
            Assert.That(actual.Description, Is.Empty);
            Assert.That(actual.RuleText, Is.Empty);
            Assert.That(actual.SourceSlotId, Is.Empty);
            Assert.That(actual.Choices.Single().Id, Is.EqualTo("legacy_item"));
        }

        [TestCaseSource(nameof(MaterialRewardCases))]
        public void MaterialRewardSlot_OnlyRollsItemsWithMatchingMaterialTag(
            string slotGroupId,
            string[] expectedItemIds)
        {
            GameRun run = CreateRun();

            for (ulong seed = 1; seed <= 64; seed++)
            {
                RewardChoiceGroup group = RewardGranter.BuildConfigChoiceGroup(
                    run,
                    new Xoshiro256SS(seed),
                    slotGroupId,
                    "材料奖励");

                Assert.That(group, Is.Not.Null, $"seed={seed}");
                Assert.That(group.Choices, Has.Count.EqualTo(1), $"seed={seed}");
                Assert.That(expectedItemIds, Does.Contain(group.Choices[0].Id), $"seed={seed}");
            }
        }

        [Test]
        public void CarpenterMetalOption_EnqueuesOnlyMetalMaterialRewards()
        {
            cfg.EventOption option = _tables.TbEventOption.Get("opt_carpenter_metal");
            string[] expectedItemIds = { "item_active_lay_gold", "item_active_lay_silver" };

            for (ulong seed = 1; seed <= 32; seed++)
            {
                GameRun run = CreateRun();
                EventService.ResolveOption(run, option, new Xoshiro256SS(seed));

                Assert.That(
                    run.TryPeekPendingGenericReward(out _, out _, out RewardOffer offer),
                    Is.True,
                    $"seed={seed}");
                Assert.That(offer.MainChoices, Has.Count.EqualTo(1), $"seed={seed}");
                Assert.That(expectedItemIds, Does.Contain(offer.MainChoices[0].Id), $"seed={seed}");
            }
        }

        private GameRun CreateRun()
        {
            string characterId = _tables.TbCharacter.DataList.First().Id;
            return new GameRun(_tables, _database, characterId, "reward-slot-presentation-tests");
        }
    }
}
