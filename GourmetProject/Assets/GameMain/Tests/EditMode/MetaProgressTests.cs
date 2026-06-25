using System;
using System.Collections.Generic;
using System.IO;
using GourmetProject.Core.Rng;
using GourmetProject.Core.Save;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;

namespace GourmetProject.Tests
{
    public class MetaProgressTests
    {
        [Test]
        public void RunEndUnlock_AllowsLossWhenConditionSatisfied()
        {
            cfg.Tables tables = LoadTablesWithUnlocks(
                RuleJson(("unlock_item_reroll", "item_reroll", 10)),
                ConditionJson(
                    ("cond_loss", "unlock_item_reroll", "default", cfg.UnlockConditionType.RunLost, 0, string.Empty, cfg.UnlockTargetType.Item, string.Empty),
                    ("cond_week", "unlock_item_reroll", "default", cfg.UnlockConditionType.MinWeek, 2, string.Empty, cfg.UnlockTargetType.Item, string.Empty)));
            GameRun run = NewRun(tables, week: 2);

            MetaProgressUpdate update = MetaProgressService.EvaluateRunEnd(tables, run, won: false, lastTotal: 80, lastTarget: 100, new MetaProgressSaveData());

            Assert.IsTrue(HasUnlock(update, "item_reroll"));
            Assert.IsTrue(update.Progress.IsTargetUnlocked(cfg.UnlockTargetType.Item, "item_reroll"));
            Assert.AreEqual(1, update.Progress.CompletedRunCount);
            Assert.AreEqual(1, update.Progress.LostRunCount);
        }

        [Test]
        public void RunEndUnlock_DoesNotRepeatAlreadyUnlockedItems()
        {
            cfg.Tables tables = LoadTablesWithUnlocks(
                RuleJson(("unlock_item_reroll", "item_reroll", 10)),
                ConditionJson(("cond_always", "unlock_item_reroll", "default", cfg.UnlockConditionType.Always, 0, string.Empty, cfg.UnlockTargetType.Item, string.Empty)));
            GameRun run = NewRun(tables, week: 1);
            var progress = new MetaProgressSaveData();
            progress.AddUnlockedTarget(cfg.UnlockTargetType.Item, "item_reroll");

            MetaProgressUpdate update = MetaProgressService.EvaluateRunEnd(tables, run, won: true, lastTotal: 120, lastTarget: 100, progress);

            Assert.IsFalse(HasUnlock(update, "item_reroll"));
            Assert.AreEqual(1, CountItem(update.Progress.UnlockedTargetKeys, MetaProgressSaveData.TargetKey(cfg.UnlockTargetType.Item, "item_reroll")));
            Assert.AreEqual(1, update.Progress.CompletedRunCount);
            Assert.AreEqual(1, update.Progress.WonRunCount);
        }

        [Test]
        public void RunEndUnlock_UsesBossAndRunActionConditions()
        {
            cfg.Tables tables = LoadTablesWithUnlocks(
                RuleJson(("unlock_item_reroll", "item_reroll", 10)),
                ConditionJson(
                    ("cond_boss", "unlock_item_reroll", "default", cfg.UnlockConditionType.CompletedBoss, 0, "boss_glutton", cfg.UnlockTargetType.Item, string.Empty),
                    ("cond_actions", "unlock_item_reroll", "default", cfg.UnlockConditionType.MinRunActions, 3, string.Empty, cfg.UnlockTargetType.Item, string.Empty)));
            GameRun run = NewRun(tables, week: 4);
            run.MarkBossCompleted("boss_glutton");
            run.RestoreRunActionStepIndex(3);

            MetaProgressUpdate update = MetaProgressService.EvaluateRunEnd(tables, run, won: false, lastTotal: 90, lastTarget: 100, new MetaProgressSaveData());

            Assert.IsTrue(HasUnlock(update, "item_reroll"));
            Assert.IsTrue(update.Progress.DefeatedBossIds.Contains("boss_glutton"));
            Assert.AreEqual(3, update.Progress.HighestRunActionStepIndex);
        }

        [Test]
        public void ItemAndRewardPools_FilterLockedItemsUntilProgressUnlocks()
        {
            cfg.Tables tables = LoadTablesWithUnlocks(
                RuleJson(("unlock_item_reroll", "item_reroll", 10)),
                ConditionJson(("cond_week", "unlock_item_reroll", "default", cfg.UnlockConditionType.MinWeek, 1, string.Empty, cfg.UnlockTargetType.Item, string.Empty)));
            GameRun run = NewRun(tables, week: 1);
            cfg.RewardSlot activeSlot = tables.TbRewardSlot.Get("slot_main_active");

            var lockedProgress = new MetaProgressSaveData();
            List<string> lockedRoll = ItemPoolService.Roll(tables, run, cfg.ItemKind.Active, new MaxWeightRandomStream(), 2, hidden: 0, distanceFloor: 5, progress: lockedProgress);
            Assert.IsFalse(lockedRoll.Contains("item_reroll"));

            List<RewardChoice> lockedChoices = RewardPoolService.RollChoices(
                new RewardContext(tables, run, run.CurrentWeek, run.CurrentRewardPackage, new MaxWeightRandomStream(), progress: lockedProgress),
                activeSlot);
            Assert.IsFalse(lockedChoices.Exists(choice => choice.Id == "item_reroll"));

            var unlockedProgress = new MetaProgressSaveData();
            unlockedProgress.AddUnlockedTarget(cfg.UnlockTargetType.Item, "item_reroll");
            List<string> unlockedRoll = ItemPoolService.Roll(tables, run, cfg.ItemKind.Active, new MaxWeightRandomStream(), 1, hidden: 0, distanceFloor: 5, progress: unlockedProgress);
            Assert.AreEqual("item_reroll", unlockedRoll[0]);

            List<RewardChoice> unlockedChoices = RewardPoolService.RollChoices(
                new RewardContext(tables, run, run.CurrentWeek, run.CurrentRewardPackage, new MaxWeightRandomStream(), progress: unlockedProgress),
                activeSlot);
            Assert.AreEqual("item_reroll", unlockedChoices[0].Id);
        }

        [Test]
        public void RunEndUnlock_UsesOrGroups()
        {
            cfg.Tables tables = LoadTablesWithUnlocks(
                RuleJson(("unlock_item_extra_serve", "item_extra_serve", 10)),
                ConditionJson(
                    ("cond_win", "unlock_item_extra_serve", "win_path", cfg.UnlockConditionType.RunWon, 0, string.Empty, cfg.UnlockTargetType.Item, string.Empty),
                    ("cond_late", "unlock_item_extra_serve", "win_path", cfg.UnlockConditionType.MinWeek, 2, string.Empty, cfg.UnlockTargetType.Item, string.Empty),
                    ("cond_boss", "unlock_item_extra_serve", "boss_path", cfg.UnlockConditionType.CompletedBoss, 0, "boss_glutton", cfg.UnlockTargetType.Item, string.Empty)));
            GameRun run = NewRun(tables, week: 1);
            run.MarkBossCompleted("boss_glutton");

            MetaProgressUpdate update = MetaProgressService.EvaluateRunEnd(tables, run, won: false, lastTotal: 90, lastTarget: 100, new MetaProgressSaveData());

            Assert.IsTrue(HasUnlock(update, "item_extra_serve"));
        }

        [Test]
        public void MetaProgressPersistence_RoundTripsUnlockedItemsAndStats()
        {
            string dir = Path.Combine(Path.GetTempPath(), "gourmet-meta-progress-tests-" + Guid.NewGuid().ToString("N"));
            try
            {
                var save = new JsonSaveService(dir, new SaveServiceOptions { CurrentVersion = 1, EnableChecksum = true, Indented = false });
                var data = new MetaProgressSaveData
                {
                    CompletedRunCount = 2,
                    WonRunCount = 1,
                    LostRunCount = 1,
                    HighestWeekIndex = 4,
                };
                data.AddUnlockedTarget(cfg.UnlockTargetType.Item, "item_reroll");
                data.DefeatedBossIds.Add("boss_glutton");

                MetaProgressPersistence.Save(save, data);
                MetaProgressSaveData loaded = MetaProgressPersistence.Load(save);

                Assert.IsTrue(loaded.IsTargetUnlocked(cfg.UnlockTargetType.Item, "item_reroll"));
                Assert.AreEqual(2, loaded.CompletedRunCount);
                Assert.AreEqual(4, loaded.HighestWeekIndex);
                Assert.IsTrue(loaded.DefeatedBossIds.Contains("boss_glutton"));
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }

        private static GameRun NewRun(cfg.Tables tables, int week)
        {
            GameplayDatabase database = GameplayContentBuilder.BuildDatabase(tables);
            return new GameRun(tables, database, "glutton_dog", "meta-progress-test", week);
        }

        private static bool HasUnlock(MetaProgressUpdate update, string itemId)
        {
            return update.NewUnlocks.Exists(entry => entry.TargetType == cfg.UnlockTargetType.Item && entry.TargetId == itemId);
        }

        private static int CountItem(IReadOnlyList<string> values, string itemId)
        {
            int count = 0;
            foreach (string value in values)
            {
                if (value == itemId)
                {
                    count++;
                }
            }

            return count;
        }

        private static cfg.Tables LoadTablesWithUnlocks(string rulesJson, string conditionsJson)
        {
            return LoadTables(new Dictionary<string, string>
            {
                ["tbunlockrule"] = rulesJson,
                ["tbunlockcondition"] = conditionsJson,
            });
        }

        private static cfg.Tables LoadTables(IReadOnlyDictionary<string, string> overrides = null)
        {
            string root = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "StreamingAssets", "Config");
            return new cfg.Tables(name =>
            {
                if (overrides != null && overrides.TryGetValue(name, out string json))
                {
                    return JSON.Parse(json);
                }

                string path = Path.Combine(root, name + ".json");
                return JSON.Parse(File.ReadAllText(path));
            });
        }

        private static string RuleJson(params (string id, string targetId, int sortOrder)[] rules)
        {
            var parts = new List<string>();
            foreach ((string id, string targetId, int sortOrder) rule in rules)
            {
                parts.Add(
                    "{" +
                    $"\"id\":\"{rule.id}\"," +
                    $"\"targetType\":{(int)cfg.UnlockTargetType.Item}," +
                    $"\"targetId\":\"{rule.targetId}\"," +
                    "\"title\":\"\"," +
                    "\"desc\":\"\"," +
                    "\"enabled\":true," +
                    $"\"sortOrder\":{rule.sortOrder}" +
                    "}");
            }

            return "[" + string.Join(",", parts) + "]";
        }

        private static string ConditionJson(params (string id, string ruleId, string groupId, cfg.UnlockConditionType type, int intParam, string stringParam, cfg.UnlockTargetType targetTypeParam, string targetIdParam)[] conditions)
        {
            var parts = new List<string>();
            foreach ((string id, string ruleId, string groupId, cfg.UnlockConditionType type, int intParam, string stringParam, cfg.UnlockTargetType targetTypeParam, string targetIdParam) condition in conditions)
            {
                parts.Add(
                    "{" +
                    $"\"id\":\"{condition.id}\"," +
                    $"\"ruleId\":\"{condition.ruleId}\"," +
                    $"\"groupId\":\"{condition.groupId}\"," +
                    $"\"type\":{(int)condition.type}," +
                    $"\"intParam\":{condition.intParam}," +
                    $"\"stringParam\":\"{condition.stringParam}\"," +
                    $"\"targetTypeParam\":{(int)condition.targetTypeParam}," +
                    $"\"targetIdParam\":\"{condition.targetIdParam}\"" +
                    "}");
            }

            return "[" + string.Join(",", parts) + "]";
        }

        private sealed class MaxWeightRandomStream : IRandomStream
        {
            public RngState State { get; set; }

            public uint NextUInt() => throw new NotSupportedException();

            public ulong NextULong() => throw new NotSupportedException();

            public int Range(int minInclusive, int maxExclusive) => minInclusive;

            public float Range(float minInclusive, float maxExclusive) => minInclusive;

            public float NextFloat() => throw new NotSupportedException();

            public double NextDouble() => throw new NotSupportedException();

            public bool NextBool(double probability = 0.5) => throw new NotSupportedException();

            public void Shuffle<T>(IList<T> list) => throw new NotSupportedException();

            public T Pick<T>(IReadOnlyList<T> list) => throw new NotSupportedException("Tests should use weighted selection.");

            public int WeightedPickIndex(IReadOnlyList<float> weights)
            {
                int bestIndex = 0;
                float bestWeight = float.MinValue;
                for (int i = 0; i < weights.Count; i++)
                {
                    if (weights[i] > bestWeight)
                    {
                        bestWeight = weights[i];
                        bestIndex = i;
                    }
                }

                return bestIndex;
            }
        }
    }
}
