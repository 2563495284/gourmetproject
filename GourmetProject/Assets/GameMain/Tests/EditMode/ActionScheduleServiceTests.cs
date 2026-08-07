using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ActionScheduleServiceTests
    {
        private string _configDirectory;
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            _configDirectory = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name =>
                JSON.Parse(File.ReadAllText(Path.Combine(_configDirectory, name + ".json"))));
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void ChoiceCountBag_UsesTwoTwoThreeTickets_AndReloadsAfterEmpty()
        {
            GameRun run = CreateRun();
            var rng = new Xoshiro256SS(101UL);

            int[] firstBag = Enumerable.Range(0, 3)
                .Select(_ => ActionScheduleService.GenerateChoices(run, rng).Count)
                .OrderBy(count => count)
                .ToArray();

            Assert.That(firstBag, Is.EqualTo(new[] { 2, 2, 3 }));
            Assert.That(run.ToSaveData().ActionRandomState.RemainingChoiceCounts, Is.Empty);

            List<ActionChoice> fourth = ActionScheduleService.GenerateChoices(run, rng);
            Assert.That(fourth.Count, Is.EqualTo(2).Or.EqualTo(3));
            Assert.That(run.ToSaveData().ActionRandomState.RemainingChoiceCounts, Has.Count.EqualTo(2));
        }

        [Test]
        public void Reroll_ConsumesNewBagTicket_AndCountsEveryNewCard()
        {
            GameRun run = CreateRun();
            var rng = new Xoshiro256SS(202UL);

            List<ActionChoice> first = ActionScheduleService.GenerateChoices(run, rng);
            List<ActionChoice> rerolled = ActionScheduleService.RerollChoices(run, rng);
            ActionRandomStateSaveData state = run.ToSaveData().ActionRandomState;

            Assert.That(state.GroupSerial, Is.EqualTo(2));
            Assert.That(state.CandidateIndex, Is.EqualTo(first.Count + rerolled.Count));
            Assert.That(state.RemainingChoiceCounts, Has.Count.EqualTo(1));
            Assert.That(rerolled.All(choice => choice.ActionGroupId == "action_w1_g2"), Is.True);
        }

        [Test]
        public void WeekChange_ResetsBagCountsCandidateIndexAndGroupSerial()
        {
            GameRun run = CreateRun();
            var rng = new Xoshiro256SS(303UL);
            ActionScheduleService.GenerateChoices(run, rng);

            run.SetWeekIndex(2);
            List<ActionChoice> weekTwo = ActionScheduleService.GenerateChoices(run, rng);
            ActionRandomStateSaveData state = run.ToSaveData().ActionRandomState;

            Assert.That(state.WeekIndex, Is.EqualTo(2));
            Assert.That(state.CandidateIndex, Is.EqualTo(weekTwo.Count));
            Assert.That(state.GroupSerial, Is.EqualTo(1));
            Assert.That(state.RemainingChoiceCounts, Has.Count.EqualTo(2));
            Assert.That(weekTwo.All(choice => choice.ActionGroupId == "action_w2_g1"), Is.True);
        }

        [Test]
        public void Group_MasksSecondEventAndSameCategoryReward_ButAllowsCrossCategoryReward()
        {
            GameRun run = CreateRun();
            var rng = new Xoshiro256SS(404UL);
            bool sawCrossCategoryDuplicate = false;

            for (int groupIndex = 0; groupIndex < 600; groupIndex++)
            {
                List<ActionChoice> choices = ActionScheduleService.GenerateChoices(run, rng);
                var events = choices.Where(choice => choice.Action.Behavior == cfg.ActionBehavior.Event).ToList();
                Assert.That(events, Has.Count.LessThanOrEqualTo(1));

                var rewardsByCategory = new Dictionary<cfg.ActionRandomCategory, HashSet<cfg.RewardKind>>();
                var categoryByReward = new Dictionary<cfg.RewardKind, cfg.ActionRandomCategory>();
                foreach (ActionChoice choice in choices)
                {
                    if (!TryResolveFoodChoice(choice, out cfg.ActionRandomCategory category, out cfg.RewardKind reward))
                    {
                        continue;
                    }

                    if (!rewardsByCategory.TryGetValue(category, out HashSet<cfg.RewardKind> rewards))
                    {
                        rewards = new HashSet<cfg.RewardKind>();
                        rewardsByCategory[category] = rewards;
                    }

                    Assert.That(rewards.Add(reward), Is.True, $"{category} 在同组重复了 {reward}");
                    if (categoryByReward.TryGetValue(reward, out cfg.ActionRandomCategory previousCategory)
                        && previousCategory != category)
                    {
                        sawCrossCategoryDuplicate = true;
                    }
                    else
                    {
                        categoryByReward[reward] = category;
                    }
                }
            }

            Assert.That(sawCrossCategoryDuplicate, Is.True, "跨类别相同奖励应保持合法");
        }

        [Test]
        public void FoodKindAndRewardKind_MapToExactlyOneAction()
        {
            cfg.RewardKind[] rewards =
            {
                cfg.RewardKind.PassiveItemChoice,
                cfg.RewardKind.FragmentChoice,
                cfg.RewardKind.ActiveItemStrengthen,
                cfg.RewardKind.ActiveItemAdjust,
                cfg.RewardKind.Gold,
            };

            foreach (cfg.FoodActionKind kind in new[] { cfg.FoodActionKind.Normal, cfg.FoodActionKind.Super })
            {
                foreach (cfg.RewardKind reward in rewards)
                {
                    int count = _tables.TbAction.DataList.Count(action =>
                    {
                        cfg.Food food = FoodService.Resolve(_tables, action);
                        return food != null && food.ActionKind == kind && food.RewardKind == reward;
                    });
                    Assert.That(count, Is.EqualTo(1), $"{kind} + {reward}");
                }
            }

            Assert.That(
                _tables.TbAction.DataList.Count(action => action.Behavior == cfg.ActionBehavior.Event),
                Is.EqualTo(1));
        }

        [Test]
        public void GuaranteeBounds_UseCandidateAndWeekIndex_AndOutOfRangeMeansUnlimited()
        {
            IReadOnlyList<List<int>> bounds = new List<List<int>>
            {
                new List<int> { 0, 2, -1 },
                new List<int> { 3 },
            };

            Assert.That(ActionScheduleService.TryGetGuarantee(bounds, 1, 0, out int w1c1), Is.True);
            Assert.That(w1c1, Is.EqualTo(0));
            Assert.That(ActionScheduleService.TryGetGuarantee(bounds, 1, 1, out int w1c2), Is.True);
            Assert.That(w1c2, Is.EqualTo(2));
            Assert.That(ActionScheduleService.TryGetGuarantee(bounds, 1, 2, out _), Is.False);
            Assert.That(ActionScheduleService.TryGetGuarantee(bounds, 1, 3, out _), Is.False);
            Assert.That(ActionScheduleService.TryGetGuarantee(bounds, 2, 0, out int w2c1), Is.True);
            Assert.That(w2c1, Is.EqualTo(3));
            Assert.That(ActionScheduleService.TryGetGuarantee(bounds, 3, 0, out _), Is.False);
            Assert.That(ActionScheduleService.TryGetGuarantee(bounds, -1, -1, out _), Is.False);
        }

        [Test]
        public void MinimumGuarantee_PrecedesMaximum_ForCategoryAndReward()
        {
            cfg.Tables tables = CreateTables((name, root) =>
            {
                if (name == "tbactioncategoryrule")
                {
                    root[0]["minGuaranteeCounts"] = JSON.Parse("[[1],[-1],[-1],[-1]]");
                    root[1]["maxGuaranteeCounts"] = JSON.Parse("[[1],[-1],[-1],[-1]]");
                }
                else if (name == "tbactionrewardrule")
                {
                    root[0]["maxGuaranteeCounts"] = JSON.Parse("[[1],[-1],[-1],[-1]]");
                    root[2]["minGuaranteeCounts"] = JSON.Parse("[[1],[-1],[-1],[-1]]");
                }
            });
            GameRun run = CreateRun(tables);

            ActionChoice first = ActionScheduleService.GenerateChoices(run, new Xoshiro256SS(808UL))[0];

            Assert.That(TryResolveFoodChoice(tables, first, out cfg.ActionRandomCategory category, out cfg.RewardKind reward), Is.True);
            Assert.That(category, Is.EqualTo(cfg.ActionRandomCategory.Daily));
            Assert.That(reward, Is.EqualTo(cfg.RewardKind.PassiveItemChoice));
        }

        [Test]
        public void GroupMask_PrecedesGuarantee_AndUnmetGuaranteeDefersToNextCard()
        {
            cfg.Tables tables = CreateTables((name, root) =>
            {
                if (name == "tbactionchoicecountrule")
                {
                    root[0]["weeklyWeights"] = JSON.Parse("[1]");
                    root[1]["weeklyWeights"] = JSON.Parse("[0]");
                }
                else if (name == "tbactioncategoryrule")
                {
                    root[0]["fallbackWeights"] = JSON.Parse("[1]");
                    root[1]["fallbackWeights"] = JSON.Parse("[0]");
                    root[2]["fallbackWeights"] = JSON.Parse("[0]");
                }
                else if (name == "tbactionrewardrule")
                {
                    root[2]["minGuaranteeCounts"] = JSON.Parse("[[1,2,2]]");
                }
            });
            GameRun run = CreateRun(tables);
            var rng = new Xoshiro256SS(909UL);

            List<ActionChoice> firstGroup = ActionScheduleService.GenerateChoices(run, rng);
            List<ActionChoice> secondGroup = ActionScheduleService.GenerateChoices(run, rng);

            Assert.That(firstGroup, Has.Count.EqualTo(2));
            Assert.That(ResolveReward(tables, firstGroup[0]), Is.EqualTo(cfg.RewardKind.PassiveItemChoice));
            Assert.That(ResolveReward(tables, firstGroup[1]), Is.Not.EqualTo(cfg.RewardKind.PassiveItemChoice));
            Assert.That(ResolveReward(tables, secondGroup[0]), Is.EqualTo(cfg.RewardKind.PassiveItemChoice));
        }

        [Test]
        public void CategoryDecorationBonus_MultipliesWeightAndRoundsGuaranteeAwayFromZero()
        {
            Assert.That(ActionScheduleService.ApplyCategoryWeightBonus(20f, 0.25f), Is.EqualTo(25f));
            Assert.That(ActionScheduleService.ApplyCategoryGuaranteeBonus(2, 0.25f), Is.EqualTo(3));
            Assert.That(ActionScheduleService.ApplyCategoryGuaranteeBonus(3, 0.5f), Is.EqualTo(5));
            Assert.That(ActionScheduleService.ApplyCategoryGuaranteeBonus(-1, 3f), Is.EqualTo(-1));
        }

        [Test]
        public void PendingChoices_ReopenDoesNotAdvanceRandomCounts()
        {
            GameRun run = CreateRun();
            var rng = new Xoshiro256SS(505UL);
            List<ActionChoice> choices = ActionScheduleService.GenerateChoices(run, rng);
            string key = GameRun.BuildActionChoiceKey(0, 1, 0f, 0);
            run.SetPendingActionChoices(key, choices);
            int before = run.ToSaveData().ActionRandomState.CandidateIndex;

            Assert.That(run.GetPendingActionChoices(key), Has.Count.EqualTo(choices.Count));
            Assert.That(run.GetPendingActionChoices(key), Has.Count.EqualTo(choices.Count));
            Assert.That(run.ToSaveData().ActionRandomState.CandidateIndex, Is.EqualTo(before));
        }

        [Test]
        public void SameSeedProducesSameGroups()
        {
            GameRun left = CreateRun();
            GameRun right = CreateRun();
            var leftRng = new Xoshiro256SS(606UL);
            var rightRng = new Xoshiro256SS(606UL);

            for (int groupIndex = 0; groupIndex < 12; groupIndex++)
            {
                Assert.That(
                    Signatures(ActionScheduleService.GenerateChoices(left, leftRng)),
                    Is.EqualTo(Signatures(ActionScheduleService.GenerateChoices(right, rightRng))));
            }
        }

        [Test]
        public void SaveRestoreContinuesBagCountsAndRandomSequence()
        {
            GameRun live = CreateRun();
            var liveRng = new Xoshiro256SS(707UL);
            ActionScheduleService.GenerateChoices(live, liveRng);
            RunSaveData checkpoint = live.ToSaveData();
            RngState checkpointRng = liveRng.State;

            List<string> expected = Signatures(ActionScheduleService.GenerateChoices(live, liveRng));
            GameRun restored = GameRun.FromSaveData(_tables, _database, checkpoint);
            var restoredRng = new Xoshiro256SS(checkpointRng);
            List<string> actual = Signatures(ActionScheduleService.GenerateChoices(restored, restoredRng));

            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(restored.ToSaveData().ActionRandomState.CandidateIndex,
                Is.EqualTo(live.ToSaveData().ActionRandomState.CandidateIndex));
        }

        [Test]
        public void SaveVersion_RejectsLegacyZeroAndAcceptsCurrent()
        {
            Assert.That(RunPersistence.IsActionRandomRuleVersionCompatible(0), Is.False);
            Assert.That(
                RunPersistence.IsActionRandomRuleVersionCompatible(RunPersistence.CurrentActionRandomRuleVersion),
                Is.True);
            Assert.That(CreateRun().ToSaveData().ActionRandomRuleVersion,
                Is.EqualTo(RunPersistence.CurrentActionRandomRuleVersion));
        }

        private GameRun CreateRun()
        {
            return new GameRun(_tables, _database, "glutton_dog", "action-schedule-test", 1);
        }

        private GameRun CreateRun(cfg.Tables tables)
        {
            return new GameRun(
                tables,
                GameplayContentBuilder.BuildDatabase(tables),
                "glutton_dog",
                "action-schedule-test",
                1);
        }

        private cfg.Tables CreateTables(Action<string, JSONNode> configure)
        {
            return new cfg.Tables(name =>
            {
                JSONNode root = JSON.Parse(File.ReadAllText(Path.Combine(_configDirectory, name + ".json")));
                configure?.Invoke(name, root);
                return root;
            });
        }

        private bool TryResolveFoodChoice(
            ActionChoice choice,
            out cfg.ActionRandomCategory category,
            out cfg.RewardKind reward)
        {
            return TryResolveFoodChoice(_tables, choice, out category, out reward);
        }

        private static bool TryResolveFoodChoice(
            cfg.Tables tables,
            ActionChoice choice,
            out cfg.ActionRandomCategory category,
            out cfg.RewardKind reward)
        {
            cfg.Food food = FoodService.Resolve(tables, choice.Action);
            if (food == null || food.ActionKind == cfg.FoodActionKind.Feast)
            {
                category = default;
                reward = default;
                return false;
            }

            category = food.ActionKind == cfg.FoodActionKind.Super
                ? cfg.ActionRandomCategory.Hot
                : cfg.ActionRandomCategory.Daily;
            reward = food.RewardKind;
            return true;
        }

        private static cfg.RewardKind ResolveReward(cfg.Tables tables, ActionChoice choice)
        {
            Assert.That(
                TryResolveFoodChoice(tables, choice, out _, out cfg.RewardKind reward),
                Is.True);
            return reward;
        }

        private static List<string> Signatures(IReadOnlyList<ActionChoice> choices)
        {
            return choices.Select(choice =>
                $"{choice.Action.Id}|{choice.ActionGroupId}|{choice.CostDays:0.0}").ToList();
        }
    }
}
