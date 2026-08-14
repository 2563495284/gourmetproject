using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BreakInfinity;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Balance;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BalanceLabReportTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            string configDirectory = Path.Combine(
                Directory.GetCurrentDirectory(), "Assets", "StreamingAssets", "Config");
            if (!Directory.Exists(configDirectory))
                configDirectory = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name =>
                JSON.Parse(File.ReadAllText(Path.Combine(configDirectory, name + ".json"))));
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void ReportDoesNotInventBossPercentilesWhenNobodyReachedBoss()
        {
            AutoRunReport report = AutoRunReportBuilder.Build(Request(2, 1), new[]
            {
                Trace(1001, completed: true, failure: string.Empty, bossReached: false),
                Trace(1002, completed: false, failure: "红心耗尽", bossReached: false),
            });

            AutoRunWeekSummary week = report.WeekSummaries[0];
            Assert.That(week.BossMetricsValid, Is.False);
            Assert.That(week.SuggestionValid, Is.False);
            Assert.That(week.BossScores, Is.Null);
            Assert.That(week.WarningCodes, Does.Contain("no-boss-reached"));
            Assert.That(report.LevelSummaries[0].BalanceFailures, Is.EqualTo(1));
            Assert.That(report.LevelSummaries[0].RuntimeErrors, Is.Zero);
        }

        [Test]
        public void LowBossReachKeepsObservedPercentilesButInvalidatesSuggestion()
        {
            var traces = new List<AutoRunTrace>();
            for (int i = 0; i < 4; i++)
                traces.Add(Trace(2000 + i, completed: true, failure: string.Empty, bossReached: i == 0));

            AutoRunWeekSummary week = AutoRunReportBuilder.Build(Request(4, 1), traces).WeekSummaries[0];

            Assert.That(week.BossMetricsValid, Is.True);
            Assert.That(week.BossScores, Is.Not.Null);
            Assert.That(week.BossReachRate, Is.EqualTo(0.25f));
            Assert.That(week.SuggestionValid, Is.False);
            Assert.That(week.WarningCodes, Does.Contain("boss-survivor-bias"));
        }

        [Test]
        public void SuggestionRequiresThirtyReachedBossSamples()
        {
            List<AutoRunTrace> twentyNine = Enumerable.Range(0, 29)
                .Select(i => Trace(2100 + i, completed: true, failure: string.Empty, bossReached: true))
                .ToList();
            AutoRunWeekSummary below = AutoRunReportBuilder.Build(Request(29, 1), twentyNine).WeekSummaries[0];
            Assert.That(below.SuggestionValid, Is.False);
            Assert.That(below.WarningCodes, Does.Contain("boss-samples-below-30"));

            List<AutoRunTrace> thirty = Enumerable.Range(0, 30)
                .Select(i => Trace(2200 + i, completed: true, failure: string.Empty, bossReached: true))
                .ToList();
            AutoRunWeekSummary enough = AutoRunReportBuilder.Build(Request(30, 1), thirty).WeekSummaries[0];
            Assert.That(enough.SuggestionValid, Is.True);
            Assert.That(enough.BossScores.P30, Is.EqualTo(new BigDouble(1234)));
        }

        [Test]
        public void SuggestionAllowsOnePercentBudgetTruncationButRejectsMore()
        {
            List<AutoRunTrace> onePercent = Enumerable.Range(0, 100)
                .Select(i => Trace(2300 + i, completed: true, failure: string.Empty, bossReached: true,
                    budgetTruncated: i == 0))
                .ToList();
            AutoRunWeekSummary allowed = AutoRunReportBuilder.Build(Request(100, 1), onePercent).WeekSummaries[0];
            Assert.That(allowed.SolverTruncatedRate, Is.EqualTo(0.01f).Within(0.0001f));
            Assert.That(allowed.SuggestionValid, Is.True);

            onePercent[1].Stages[0].Termination = AutoRunTerminationKind.PlacementBudgetExhausted;
            AutoRunWeekSummary rejected = AutoRunReportBuilder.Build(Request(100, 1), onePercent).WeekSummaries[0];
            Assert.That(rejected.SolverTruncatedRate, Is.EqualTo(0.02f).Within(0.0001f));
            Assert.That(rejected.SuggestionValid, Is.False);
            Assert.That(rejected.WarningCodes, Does.Contain("solver-truncated-over-limit"));
        }

        [Test]
        public void UnsupportedMechanicAndCancellationAreClassifiedSeparately()
        {
            AutoRunTrace unsupported = Trace(2401, false, "not implemented", false);
            unsupported.Termination = AutoRunTerminationKind.UnsupportedMechanic;
            AutoRunTrace cancelled = Trace(2402, false, "cancelled", false);
            cancelled.Termination = AutoRunTerminationKind.Cancelled;
            AutoRunReportRequest request = Request(2, 1);
            request.Cancelled = true;

            AutoRunReport report = AutoRunReportBuilder.Build(request, new[] { unsupported, cancelled });

            Assert.That(report.LevelSummaries[0].UnsupportedMechanics, Is.EqualTo(1));
            Assert.That(report.LevelSummaries[0].UserCancelled, Is.EqualTo(1));
            Assert.That(report.Failures.Select(failure => failure.Outcome), Is.EquivalentTo(new[]
            {
                AutoRunOutcomeKind.UnsupportedMechanic,
                AutoRunOutcomeKind.UserCancelled,
            }));
            Assert.That(report.HasUnsupportedMechanics, Is.True);
            Assert.That(report.IsValid, Is.False);
        }

        [Test]
        public void StaleConfigDisablesSuggestionWithoutDeletingPercentiles()
        {
            List<AutoRunTrace> traces = Enumerable.Range(0, 30)
                .Select(i => Trace(2500 + i, completed: true, failure: string.Empty, bossReached: true))
                .ToList();
            AutoRunReport report = AutoRunReportBuilder.Build(Request(30, 1), traces);
            Assert.That(report.WeekSummaries[0].SuggestionValid, Is.True);

            AutoRunReportBuilder.RefreshValidity(report, configStale: true);

            Assert.That(report.ConfigStale, Is.True);
            Assert.That(report.WeekSummaries[0].SuggestionValid, Is.False);
            Assert.That(report.WeekSummaries[0].BossScores, Is.Not.Null);
            Assert.That(report.WeekSummaries[0].WarningCodes, Does.Contain("config-stale"));
        }

        [Test]
        public void RuntimeFailureMakesCompleteReportInvalidAndPreservesSeed()
        {
            AutoRunReport report = AutoRunReportBuilder.Build(Request(1, 1), new[]
            {
                Trace(3001, completed: false, failure: "System.InvalidOperationException: bad", bossReached: false),
            });

            Assert.That(report.SamplingComplete, Is.True);
            Assert.That(report.HasRuntimeErrors, Is.True);
            Assert.That(report.IsValid, Is.False);
            Assert.That(report.Failures[0].Kind, Is.EqualTo(AutoRunFailureKind.RuntimeError));
            Assert.That(report.Failures[0].Seed, Is.EqualTo(3001));
        }

        [Test]
        public void CancelledReportIsPartialAndStillExportsCsv()
        {
            AutoRunReportRequest request = Request(10, 1);
            request.Cancelled = true;
            AutoRunReport report = AutoRunReportBuilder.Build(request, new[]
            {
                Trace(4001, completed: true, failure: string.Empty, bossReached: true),
            });

            string csv = AutoRunReportBuilder.ToSummaryCsv(report);

            Assert.That(report.Partial, Is.True);
            Assert.That(report.SamplingComplete, Is.False);
            Assert.That(csv, Does.Contain("configHash"));
            Assert.That(csv, Does.Contain("all-unlocked-v1"));
        }

        [Test]
        public void CancelledReportCountsOnlyTracedCancellationAndKeepsUnstartedRunsPartial()
        {
            AutoRunReportRequest request = Request(10, 1);
            request.Cancelled = true;
            AutoRunTrace cancelled = Trace(4010, completed: false, failure: "用户取消", bossReached: false);
            cancelled.Termination = AutoRunTerminationKind.Cancelled;

            AutoRunReport report = AutoRunReportBuilder.Build(request, new[] { cancelled });

            Assert.That(report.LevelSummaries[0].ActualRuns, Is.EqualTo(1));
            Assert.That(report.LevelSummaries[0].UserCancelled, Is.EqualTo(1));
            Assert.That(report.LevelSummaries[0].UserCancelledSeeds, Is.EqualTo(new[] { 4010 }));
            Assert.That(report.RequestedTraceCount - report.ActualTraceCount, Is.EqualTo(9));
            Assert.That(report.Partial, Is.True);
        }

        [Test]
        public void CsvExportsEffectiveCandidateLimitsRatherThanLegacyConfiguredValues()
        {
            AutoRunReportRequest request = Request(1, 1);
            request.Policy.NormalBeamWidth = 128;
            request.Policy.ExpertBeamWidth = 128;
            request.Levels = new List<AutoPlayerLevel>
            {
                AutoPlayerLevel.Normal,
                AutoPlayerLevel.Expert,
            };

            AutoRunTrace normal = Trace(4020, completed: true, failure: string.Empty, bossReached: true);
            AutoRunTrace expert = Trace(4021, completed: true, failure: string.Empty, bossReached: true);
            expert.PlayerLevel = AutoPlayerLevel.Expert;
            string csv = AutoRunReportBuilder.ToSummaryCsv(
                AutoRunReportBuilder.Build(request, new[] { normal, expert }));
            string dataLine = csv.Split('\n').First(line => line.Contains("\"Normal\""));
            string[] columns = dataLine.Split(',');

            Assert.That(columns[16], Is.EqualTo("24"));
            Assert.That(columns[17], Is.EqualTo("64"));
        }

        [Test]
        public void ConfigFingerprintChangesOnlyWhenContentChanges()
        {
            string directory = Path.Combine(Path.GetTempPath(), "balance-fingerprint-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(Path.Combine(directory, "a.json"), "[]");
                BalanceConfigFingerprint first = BalanceConfigFingerprint.Compute(directory);
                File.SetLastWriteTimeUtc(Path.Combine(directory, "a.json"), DateTime.UtcNow.AddMinutes(1));
                BalanceConfigFingerprint touched = BalanceConfigFingerprint.Compute(directory);
                File.WriteAllText(Path.Combine(directory, "a.json"), "[1]");
                BalanceConfigFingerprint changed = BalanceConfigFingerprint.Compute(directory);

                Assert.That(first.IsValid, Is.True);
                Assert.That(first.HasSameContent(touched), Is.True);
                Assert.That(first.HasSameContent(changed), Is.False);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void BalanceItemStateJsonRoundTripsAndOldAssetsDefaultToEmpty()
        {
            var current = new BalanceItemEntry { ItemId = "item", Level = 2, StateJson = "count:7" };
            Assert.That(BuildCheckpointRuntimeFactory.ToRunItemSaveData(current).StateJson, Is.EqualTo("count:7"));

            BalanceItemEntry legacy = JsonUtility.FromJson<BalanceItemEntry>("{\"ItemId\":\"item\",\"Level\":2}");
            Assert.That(BuildCheckpointRuntimeFactory.ToRunItemSaveData(legacy).StateJson, Is.EqualTo(string.Empty));
        }

        [Test]
        public void CheckpointStateJsonSurvivesActualRuntimeModelRoundTrip()
        {
            var checkpoint = new BuildCheckpoint
            {
                Name = "state-roundtrip",
                CharacterId = _tables.TbCharacter.DataList[0].Id,
                Items = new List<BalanceItemEntry>
                {
                    new BalanceItemEntry
                    {
                        ItemId = "item_extra_food_choice",
                        Level = 1,
                        StateJson = "count:4",
                    },
                },
            };

            BalanceRuntime runtime = BuildCheckpointRuntimeFactory.Create(
                _tables, _database, checkpoint, seed: 1001, applyPerturbation: false);
            string restored = runtime.Run.ToSaveData().Items
                .Single(item => item.ItemId == "item_extra_food_choice")
                .StateJson;

            Assert.That(restored, Does.Contain("count:4"));
        }

        [Test]
        public void RepositoryDefaultAffinityCoversCurrentConfigAndHasEveryRoute()
        {
            const string path = "Assets/GameMain/Content/Balance/DefaultMetaAffinityCatalog.asset";
            MetaAffinityCatalog catalog = AssetDatabase.LoadAssetAtPath<MetaAffinityCatalog>(path);

            Assert.That(catalog, Is.Not.Null, path);
            Assert.That(catalog.ProfileId, Is.EqualTo(MetaAffinityCatalog.DefaultProfileId));
            Assert.That(catalog.Validate(_tables, requireAllConfigured: true), Is.Empty);
            Assert.That(catalog.Entries.Any(entry => entry.Event > entry.Normal), Is.True);
            Assert.That(catalog.Entries.Any(entry => entry.Interest > entry.Normal), Is.True);
            Assert.That(catalog.Entries.Any(entry => entry.Shop > entry.Normal), Is.True);
            Assert.That(catalog.ComputeContentHash(), Has.Length.EqualTo(64));
        }

        [Test]
        public void RepositoryExampleScenarioPassesCheckpointValidation()
        {
            const string path = "Assets/GameMain/Content/Balance/ExampleBalanceScenario.asset";
            BalanceScenario scenario = AssetDatabase.LoadAssetAtPath<BalanceScenario>(path);

            Assert.That(scenario, Is.Not.Null, path);
            Assert.That(scenario.Checkpoints, Is.Not.Empty);
            foreach (BuildCheckpoint checkpoint in scenario.Checkpoints)
                Assert.That(BuildCheckpointRuntimeFactory.Validate(_tables, _database, checkpoint), Is.Empty);
        }

        [Test]
        public void CsvAndJsonCarrySameMetadataStatisticsAndFullTrace()
        {
            AutoRunReportRequest request = Request(30, 1);
            request.UnlockProfileHash = "unlock-hash";
            request.MetaAffinityProfileId = MetaAffinityCatalog.DefaultProfileId;
            request.MetaAffinityProfileHash = "affinity-hash";
            request.PolicyVersion = AutoRunPolicySeed.Version;
            List<AutoRunTrace> traces = Enumerable.Range(0, 30)
                .Select(i => Trace(2600 + i, completed: true, failure: string.Empty, bossReached: true))
                .ToList();
            AutoRunReport report = AutoRunReportBuilder.Build(request, traces);

            string csv = AutoRunReportBuilder.ToSummaryCsv(report);
            string json = AutoRunReportBuilder.ToFullJson(report, prettyPrint: false);
            AutoRunReport restored = JsonUtility.FromJson<AutoRunReport>(json);

            Assert.That(csv, Does.Contain("unlockProfileHash"));
            Assert.That(csv, Does.Contain("unlock-hash"));
            Assert.That(csv, Does.Contain("affinity-hash"));
            Assert.That(csv, Does.Contain(AutoRunPolicySeed.Version));
            BigDouble p30 = report.WeekSummaries[0].BossScores.P30;
            string p30Text = p30.Mantissa.ToString("R", CultureInfo.InvariantCulture)
                             + "e"
                             + p30.Exponent.ToString(CultureInfo.InvariantCulture);
            Assert.That(csv, Does.Contain(p30Text));
            Assert.That(restored.ConfigContentHash, Is.EqualTo(report.ConfigContentHash));
            Assert.That(restored.UnlockProfileHash, Is.EqualTo(report.UnlockProfileHash));
            Assert.That(restored.MetaAffinityProfileHash, Is.EqualTo(report.MetaAffinityProfileHash));
            Assert.That(restored.PolicyVersion, Is.EqualTo(report.PolicyVersion));
            Assert.That(restored.WeekSummaries[0].BossReached, Is.EqualTo(report.WeekSummaries[0].BossReached));
            Assert.That(restored.Traces.Count, Is.EqualTo(30));
            Assert.That(restored.Traces[0].Seed, Is.EqualTo(2600));
        }

        private static AutoRunReportRequest Request(int requested, int weeks)
        {
            return new AutoRunReportRequest
            {
                GeneratedUtc = "2026-08-13T00:00:00Z",
                ConfigContentHash = "abc123",
                CharacterId = "character",
                CharacterName = "Character",
                RequestedRunsPerLevel = requested,
                TotalWeeks = weeks,
                Levels = new List<AutoPlayerLevel> { AutoPlayerLevel.Normal },
            };
        }

        private static AutoRunTrace Trace(
            int seed,
            bool completed,
            string failure,
            bool bossReached,
            bool budgetTruncated = false)
        {
            return new AutoRunTrace
            {
                Seed = seed,
                CharacterId = "character",
                PlayerLevel = AutoPlayerLevel.Normal,
                Completed = completed,
                FailureReason = failure,
                Stages = new List<AutoRunStageTrace>
                {
                    new AutoRunStageTrace
                    {
                        Week = 1,
                        BossReached = bossReached,
                        BossPassed = bossReached,
                        BossScore = new BigDouble(1234),
                        MealBattles = 1,
                        MealPasses = 1,
                        SolverTruncated = budgetTruncated,
                        Termination = budgetTruncated
                            ? AutoRunTerminationKind.PlacementBudgetExhausted
                            : AutoRunTerminationKind.None,
                    },
                },
            };
        }
    }
}
