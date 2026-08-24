using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BreakInfinity;
using UnityEngine;

namespace GourmetProject.Game.Balance
{
    public enum AutoRunFailureKind
    {
        BalanceFailure,
        RuntimeError,
        UnsupportedMechanic,
        UserCancelled,
    }

    public enum AutoRunOutcomeKind
    {
        Completed,
        NormalDefeat,
        RuntimeError,
        UnsupportedMechanic,
        UserCancelled,
    }

    [Serializable]
    public sealed class AutoRunReportRequest
    {
        public string GeneratedUtc = string.Empty;
        public string StartedUtc = string.Empty;
        public string FinishedUtc = string.Empty;
        public string ApplicationVersion = string.Empty;
        public string UnityVersion = string.Empty;
        public string ConfigContentHash = string.Empty;
        public int ConfigFileCount;
        public string ConfigLoadedUtc = string.Empty;
        public bool ConfigStale;
        public string CharacterId = string.Empty;
        public string CharacterName = string.Empty;
        public int TotalWeeks;
        public int BaseSeed;
        public int RequestedRunsPerLevel;
        public bool Cancelled;
        public string UnlockProfileId = AutoRunReport.AllUnlockedProfileId;
        public string UnlockProfileHash = string.Empty;
        public string MetaAffinityProfileId = string.Empty;
        public string MetaAffinityProfileHash = string.Empty;
        public string PolicyVersion = AutoRunPolicySeed.Version;
        public AutoPlayerPolicy Policy = new AutoPlayerPolicy();
        public List<AutoPlayerLevel> Levels = new List<AutoPlayerLevel>();
    }

    [Serializable]
    public sealed class AutoRunReport
    {
        public const string AllUnlockedProfileId = "all-unlocked-v1";
        public const string DefaultMetaAffinityProfileId = MetaAffinityCatalog.DefaultProfileId;

        public int SchemaVersion = 4;
        public string GeneratedUtc = string.Empty;
        public string StartedUtc = string.Empty;
        public string FinishedUtc = string.Empty;
        public string ApplicationVersion = string.Empty;
        public string UnityVersion = string.Empty;
        public string ConfigContentHash = string.Empty;
        public int ConfigFileCount;
        public string ConfigLoadedUtc = string.Empty;
        public bool ConfigStale;
        public string CharacterId = string.Empty;
        public string CharacterName = string.Empty;
        public int TotalWeeks;
        public int BaseSeed;
        public int RequestedRunsPerLevel;
        public int RequestedTraceCount;
        public int ActualTraceCount;
        public bool Cancelled;
        public bool Partial;
        public bool SamplingComplete;
        public bool HasRuntimeErrors;
        public bool HasUnsupportedMechanics;
        public bool PolicyCalibrationValid;
        public bool IsValid;
        public string UnlockProfileId = AllUnlockedProfileId;
        public string UnlockProfileHash = string.Empty;
        public string MetaAffinityProfileId = string.Empty;
        public string MetaAffinityProfileHash = string.Empty;
        public string PolicyVersion = AutoRunPolicySeed.Version;
        public AutoPlayerPolicy Policy = new AutoPlayerPolicy();
        public List<AutoPlayerLevel> Levels = new List<AutoPlayerLevel>();
        public List<AutoRunLevelSummary> LevelSummaries = new List<AutoRunLevelSummary>();
        public List<AutoRunWeekSummary> WeekSummaries = new List<AutoRunWeekSummary>();
        public List<AutoRunFailureRecord> Failures = new List<AutoRunFailureRecord>();
        public List<AutoRunTrace> Traces = new List<AutoRunTrace>();
    }

    [Serializable]
    public sealed class AutoRunLevelSummary
    {
        public AutoPlayerLevel PlayerLevel;
        public int RequestedRuns;
        public int ActualRuns;
        public int CompletedRuns;
        public int NormalDefeats;
        public int BalanceFailures;
        public int RuntimeErrors;
        public int UnsupportedMechanics;
        public int UserCancelled;
        public bool PolicyCalibrationValid;
        public float CompletionRate;
        public float AverageArchetypeChanges;
        public int FragmentChoiceRuns;
        public float FragmentChoiceRunShare;
        public List<int> BalanceFailureSeeds = new List<int>();
        public List<int> RuntimeErrorSeeds = new List<int>();
        public List<int> UnsupportedMechanicSeeds = new List<int>();
        public List<int> UserCancelledSeeds = new List<int>();
    }

    [Serializable]
    public sealed class AutoRunScoreQuantiles
    {
        public BigDouble P10;
        public BigDouble P30;
        public BigDouble P50;
        public BigDouble P90;
    }

    [Serializable]
    public sealed class AutoRunWeekSummary
    {
        public AutoPlayerLevel PlayerLevel;
        public int Week;
        public int ActualRuns;
        public int CompletedRuns;
        public int BalanceFailures;
        public int RuntimeErrors;
        public int StageReached;
        public float StageReachRate;
        public int BossReached;
        public int BossPassed;
        public float BossReachRate;
        public float BossPassRate;
        public bool BossMetricsValid;
        public bool SuggestionValid;
        public AutoRunScoreQuantiles BossScores;
        /// <summary>
        /// Schema v3 的正式 Boss 统计。每项只包含同一 Day + EncounterKey，禁止跨 Boss 混样。
        /// 上面的单值字段保留给旧 UI/旧 JSON，并映射为最晚一次结构化 Boss。
        /// </summary>
        public List<AutoRunBossEncounterSummary> BossEncounters = new List<AutoRunBossEncounterSummary>();
        public bool UsesLegacyBossTrace;
        public int MealBattles;
        public int MealPasses;
        public float MealPassRate;
        public float MeanGoldBalance;
        public float SolverTruncatedRate;
        public float PlacementCandidateLimitedRate;
        public float NoLegalPlacementRate;
        public float ActiveItemsUsedPerRun;
        public float SweetTransferShare;
        public float CountShare;
        public float CakeShare;
        public float NormalRouteShare;
        public float EventRouteShare;
        public float InterestRouteShare;
        public float ShopRouteShare;
        public List<string> WarningCodes = new List<string>();
    }

    [Serializable]
    public sealed class AutoRunBossEncounterSummary
    {
        public AutoPlayerLevel PlayerLevel;
        public int Week;
        public float Day;
        public string EncounterKey = string.Empty;
        public string ActionId = string.Empty;
        public string BossId = string.Empty;
        public int ActualRuns;
        public int Reached;
        public int Passed;
        public float ReachRate;
        public float PassRate;
        public bool MetricsValid;
        public bool IdentityValid;
        public bool SuggestionValid;
        public AutoRunScoreQuantiles Scores;
        public List<string> WarningCodes = new List<string>();
    }

    [Serializable]
    public sealed class AutoRunFailureRecord
    {
        public AutoPlayerLevel PlayerLevel;
        public int Seed;
        public int LastWeek;
        public AutoRunFailureKind Kind;
        public AutoRunOutcomeKind Outcome;
        public string Reason = string.Empty;
    }

    public static class AutoRunReportBuilder
    {
        private const string HeartsExhaustedReason = "红心耗尽";
        private const string EmptyTraceArrayJson = "\"Traces\":[]";
        private const int JsonStreamBufferSize = 64 * 1024;
        private static readonly FieldInfo[] ReportSerializationFields = typeof(AutoRunReport)
            .GetFields(BindingFlags.Instance | BindingFlags.Public);

        private sealed class LegacyBossSample
        {
            public LegacyBossSample(AutoRunTrace run, AutoRunStageTrace stage)
            {
                Run = run;
                Stage = stage;
            }

            public AutoRunTrace Run { get; }
            public AutoRunStageTrace Stage { get; }
        }

        private sealed class BossBattleSample
        {
            public BossBattleSample(AutoRunTrace run, AutoRunBattleTrace battle)
            {
                Run = run;
                Battle = battle;
            }

            public AutoRunTrace Run { get; }
            public AutoRunBattleTrace Battle { get; }
        }

        private readonly struct EncounterIdentity : IEquatable<EncounterIdentity>
        {
            public EncounterIdentity(float day, string key)
            {
                Day = day;
                Key = key ?? string.Empty;
            }

            public float Day { get; }
            public string Key { get; }

            public bool Equals(EncounterIdentity other)
                => Day.Equals(other.Day) && string.Equals(Key, other.Key, StringComparison.Ordinal);

            public override bool Equals(object obj)
                => obj is EncounterIdentity other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Day.GetHashCode() * 397) ^ StringComparer.Ordinal.GetHashCode(Key);
                }
            }
        }

        public static AutoRunReport Build(AutoRunReportRequest request, IEnumerable<AutoRunTrace> traces)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            List<AutoRunTrace> traceList = traces?.Where(trace => trace != null).ToList()
                                                ?? new List<AutoRunTrace>();
            List<AutoPlayerLevel> levels = (request.Levels ?? new List<AutoPlayerLevel>())
                .Distinct()
                .ToList();
            var report = new AutoRunReport
            {
                GeneratedUtc = request.GeneratedUtc,
                StartedUtc = request.StartedUtc,
                FinishedUtc = request.FinishedUtc,
                ApplicationVersion = request.ApplicationVersion,
                UnityVersion = request.UnityVersion,
                ConfigContentHash = request.ConfigContentHash,
                ConfigFileCount = request.ConfigFileCount,
                ConfigLoadedUtc = request.ConfigLoadedUtc,
                ConfigStale = request.ConfigStale,
                CharacterId = request.CharacterId,
                CharacterName = request.CharacterName,
                TotalWeeks = Math.Max(0, request.TotalWeeks),
                BaseSeed = request.BaseSeed,
                RequestedRunsPerLevel = Math.Max(0, request.RequestedRunsPerLevel),
                RequestedTraceCount = Math.Max(0, request.RequestedRunsPerLevel) * levels.Count,
                ActualTraceCount = traceList.Count,
                Cancelled = request.Cancelled,
                UnlockProfileId = string.IsNullOrEmpty(request.UnlockProfileId)
                    ? AutoRunReport.AllUnlockedProfileId
                    : request.UnlockProfileId,
                UnlockProfileHash = request.UnlockProfileHash ?? string.Empty,
                MetaAffinityProfileId = request.MetaAffinityProfileId ?? string.Empty,
                MetaAffinityProfileHash = request.MetaAffinityProfileHash ?? string.Empty,
                PolicyVersion = string.IsNullOrEmpty(request.PolicyVersion)
                    ? AutoRunPolicySeed.Version
                    : request.PolicyVersion,
                Policy = ClonePolicy(request.Policy),
                Levels = levels,
                Traces = traceList,
            };
            report.SamplingComplete = !report.Cancelled
                                      && report.ActualTraceCount == report.RequestedTraceCount
                                      && levels.All(level => traceList.Count(trace => trace.PlayerLevel == level)
                                                             == report.RequestedRunsPerLevel);
            report.Partial = !report.SamplingComplete;

            foreach (AutoPlayerLevel level in levels)
            {
                List<AutoRunTrace> runs = traceList.Where(trace => trace.PlayerLevel == level).ToList();
                AutoRunLevelSummary levelSummary = BuildLevelSummary(
                    level,
                    report.RequestedRunsPerLevel,
                    runs,
                    report.Cancelled);
                report.LevelSummaries.Add(levelSummary);
                report.Failures.AddRange(BuildFailures(runs));
                int totalWeeks = report.TotalWeeks > 0
                    ? report.TotalWeeks
                    : runs.SelectMany(run => run.Stages ?? new List<AutoRunStageTrace>())
                          .Select(stage => stage.Week)
                          .DefaultIfEmpty(0)
                          .Max();
                for (int week = 1; week <= totalWeeks; week++)
                    report.WeekSummaries.Add(BuildWeekSummary(levelSummary, week, runs));
            }

            RefreshValidity(report, report.ConfigStale);
            return report;
        }

        public static AutoRunFailureKind ClassifyFailure(AutoRunTrace trace)
        {
            switch (ClassifyOutcome(trace))
            {
                case AutoRunOutcomeKind.NormalDefeat: return AutoRunFailureKind.BalanceFailure;
                case AutoRunOutcomeKind.UnsupportedMechanic: return AutoRunFailureKind.UnsupportedMechanic;
                case AutoRunOutcomeKind.UserCancelled: return AutoRunFailureKind.UserCancelled;
                default: return AutoRunFailureKind.RuntimeError;
            }
        }

        public static AutoRunOutcomeKind ClassifyOutcome(AutoRunTrace trace)
        {
            if (trace == null) return AutoRunOutcomeKind.RuntimeError;
            switch (trace.Termination)
            {
                case AutoRunTerminationKind.Completed:
                    return AutoRunOutcomeKind.Completed;
                case AutoRunTerminationKind.HeartsDepleted:
                case AutoRunTerminationKind.GameOver:
                    return AutoRunOutcomeKind.NormalDefeat;
                case AutoRunTerminationKind.UnsupportedMechanic:
                    return AutoRunOutcomeKind.UnsupportedMechanic;
                case AutoRunTerminationKind.Cancelled:
                    return AutoRunOutcomeKind.UserCancelled;
                case AutoRunTerminationKind.InfrastructureError:
                case AutoRunTerminationKind.NoLegalPlacement:
                case AutoRunTerminationKind.PlacementBudgetExhausted:
                case AutoRunTerminationKind.ActionLimitReached:
                    return AutoRunOutcomeKind.RuntimeError;
            }

            if (trace.Completed) return AutoRunOutcomeKind.Completed;
            return string.Equals(trace.FailureReason, HeartsExhaustedReason, StringComparison.Ordinal)
                ? AutoRunOutcomeKind.NormalDefeat
                : AutoRunOutcomeKind.RuntimeError;
        }

        public static string ToSummaryCsv(AutoRunReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var text = new StringBuilder();
            text.AppendLine("schemaVersion,generatedUtc,applicationVersion,unityVersion,configHash,configLoadedUtc,configStale,unlockProfile,unlockProfileHash,metaAffinityProfile,metaAffinityProfileHash,policyVersion,characterId,characterName,baseSeed,requestedRunsPerLevel,normalCandidateLimit,expertCandidateLimit,placementNodeBudget,interestReserve,softmaxTemperature,actionRewardPriority,dualArchetypeThreshold,maxActionsPerWeek,samplingComplete,cancelled,reportValid,playerLevel,week,bossDay,bossEncounterKey,bossActionId,bossId,bossIdentityValid,bossLegacyAmbiguous,actualRuns,completedRuns,normalDefeats,runtimeErrors,unsupportedMechanics,userCancelled,fragmentChoiceRuns,fragmentChoiceRunShare,stageReached,stageReachRate,bossReached,bossPassed,bossReachRate,bossPassRate,bossMetricsValid,suggestionValid,p10,p30,p50,p90,suggestedRequirement,mealBattles,mealPasses,mealPassRate,meanGoldBalance,solverTruncatedRate,placementCandidateLimitedRate,noLegalPlacementRate,activeItemsUsedPerRun,sweetTransferShare,countShare,cakeShare,normalRouteShare,eventRouteShare,interestRouteShare,shopRouteShare,balanceFailureSeeds,runtimeErrorSeeds,unsupportedMechanicSeeds,userCancelledSeeds,warningCodes");
            foreach (AutoRunWeekSummary week in report.WeekSummaries)
            {
                AutoRunLevelSummary level = report.LevelSummaries.First(summary => summary.PlayerLevel == week.PlayerLevel);
                List<AutoRunBossEncounterSummary> encounters = week.BossEncounters
                    ?? new List<AutoRunBossEncounterSummary>();
                if (encounters.Count == 0)
                {
                    AppendCsvRow(text, report, level, week, null);
                    continue;
                }

                foreach (AutoRunBossEncounterSummary encounter in encounters
                             .OrderBy(value => value.Day)
                             .ThenBy(value => value.EncounterKey, StringComparer.Ordinal))
                    AppendCsvRow(text, report, level, week, encounter);
            }
            return text.ToString();
        }

        private static void AppendCsvRow(
            StringBuilder text,
            AutoRunReport report,
            AutoRunLevelSummary level,
            AutoRunWeekSummary week,
            AutoRunBossEncounterSummary encounter)
        {
            AutoRunScoreQuantiles scores = encounter?.Scores ?? week.BossScores;
            bool legacyAmbiguous = week.UsesLegacyBossTrace
                                   || (encounter == null
                                       && (report.SchemaVersion < 3 || week.BossScores != null));
            bool suggestionValid = encounter?.SuggestionValid
                                   ?? (!legacyAmbiguous && week.SuggestionValid);
            int bossReached = encounter?.Reached ?? week.BossReached;
            int bossPassed = encounter?.Passed ?? week.BossPassed;
            float bossReachRate = encounter?.ReachRate ?? week.BossReachRate;
            float bossPassRate = encounter?.PassRate ?? week.BossPassRate;
            bool bossMetricsValid = encounter?.MetricsValid ?? week.BossMetricsValid;
            string suggested = suggestionValid && scores != null ? Big(scores.P30) : string.Empty;
            IEnumerable<string> warningCodes = (week.WarningCodes ?? new List<string>())
                .Concat(encounter?.WarningCodes ?? new List<string>())
                .Distinct(StringComparer.Ordinal);
            text.Append(report.SchemaVersion).Append(',')
                    .Append(Csv(report.GeneratedUtc)).Append(',')
                    .Append(Csv(report.ApplicationVersion)).Append(',')
                    .Append(Csv(report.UnityVersion)).Append(',')
                    .Append(Csv(report.ConfigContentHash)).Append(',')
                    .Append(Csv(report.ConfigLoadedUtc)).Append(',')
                    .Append(Bool(report.ConfigStale)).Append(',')
                    .Append(Csv(report.UnlockProfileId)).Append(',')
                    .Append(Csv(report.UnlockProfileHash)).Append(',')
                    .Append(Csv(report.MetaAffinityProfileId)).Append(',')
                    .Append(Csv(report.MetaAffinityProfileHash)).Append(',')
                    .Append(Csv(report.PolicyVersion)).Append(',')
                    .Append(Csv(report.CharacterId)).Append(',')
                    .Append(Csv(report.CharacterName)).Append(',')
                    .Append(report.BaseSeed).Append(',')
                    .Append(report.RequestedRunsPerLevel).Append(',')
                    .Append(report.Policy.PlacementCandidateLimit(AutoPlayerLevel.Normal)).Append(',')
                    .Append(report.Policy.PlacementCandidateLimit(AutoPlayerLevel.Expert)).Append(',')
                    .Append(report.Policy.PlacementNodeBudget).Append(',')
                    .Append(report.Policy.InterestReserve).Append(',')
                    .Append(Number(report.Policy.SoftmaxTemperature)).Append(',')
                    .Append(Csv(report.Policy.ActionRewardPriority.ToString())).Append(',')
                    .Append(Number(report.Policy.DualArchetypeThreshold)).Append(',')
                    .Append(report.Policy.MaxActionsPerWeek).Append(',')
                    .Append(Bool(report.SamplingComplete)).Append(',')
                    .Append(Bool(report.Cancelled)).Append(',')
                    .Append(Bool(report.IsValid)).Append(',')
                    .Append(Csv(week.PlayerLevel.ToString())).Append(',')
                    .Append(week.Week).Append(',')
                    .Append(encounter != null ? Number(encounter.Day) : string.Empty).Append(',')
                    .Append(Csv(encounter?.EncounterKey)).Append(',')
                    .Append(Csv(encounter?.ActionId)).Append(',')
                    .Append(Csv(encounter?.BossId)).Append(',')
                    .Append(Bool(encounter?.IdentityValid ?? false)).Append(',')
                    .Append(Bool(legacyAmbiguous)).Append(',')
                    .Append(week.ActualRuns).Append(',')
                    .Append(week.CompletedRuns).Append(',')
                    .Append(level.NormalDefeats).Append(',')
                    .Append(week.RuntimeErrors).Append(',')
                    .Append(level.UnsupportedMechanics).Append(',')
                    .Append(level.UserCancelled).Append(',')
                    .Append(level.FragmentChoiceRuns).Append(',')
                    .Append(Number(level.FragmentChoiceRunShare)).Append(',')
                    .Append(week.StageReached).Append(',')
                    .Append(Number(week.StageReachRate)).Append(',')
                    .Append(bossReached).Append(',')
                    .Append(bossPassed).Append(',')
                    .Append(Number(bossReachRate)).Append(',')
                    .Append(Number(bossPassRate)).Append(',')
                    .Append(Bool(bossMetricsValid)).Append(',')
                    .Append(Bool(suggestionValid)).Append(',')
                    .Append(scores != null ? Big(scores.P10) : string.Empty).Append(',')
                    .Append(scores != null ? Big(scores.P30) : string.Empty).Append(',')
                    .Append(scores != null ? Big(scores.P50) : string.Empty).Append(',')
                    .Append(scores != null ? Big(scores.P90) : string.Empty).Append(',')
                    .Append(suggested).Append(',')
                    .Append(week.MealBattles).Append(',')
                    .Append(week.MealPasses).Append(',')
                    .Append(Number(week.MealPassRate)).Append(',')
                    .Append(Number(week.MeanGoldBalance)).Append(',')
                    .Append(Number(week.SolverTruncatedRate)).Append(',')
                    .Append(Number(week.PlacementCandidateLimitedRate)).Append(',')
                    .Append(Number(week.NoLegalPlacementRate)).Append(',')
                    .Append(Number(week.ActiveItemsUsedPerRun)).Append(',')
                    .Append(Number(week.SweetTransferShare)).Append(',')
                    .Append(Number(week.CountShare)).Append(',')
                    .Append(Number(week.CakeShare)).Append(',')
                    .Append(Number(week.NormalRouteShare)).Append(',')
                    .Append(Number(week.EventRouteShare)).Append(',')
                    .Append(Number(week.InterestRouteShare)).Append(',')
                    .Append(Number(week.ShopRouteShare)).Append(',')
                    .Append(Csv(string.Join("|", level.BalanceFailureSeeds))).Append(',')
                    .Append(Csv(string.Join("|", level.RuntimeErrorSeeds))).Append(',')
                    .Append(Csv(string.Join("|", level.UnsupportedMechanicSeeds))).Append(',')
                    .Append(Csv(string.Join("|", level.UserCancelledSeeds))).Append(',')
                    .Append(Csv(string.Join("|", warningCodes)))
                    .Append('\n');
        }

        private static AutoRunLevelSummary BuildLevelSummary(
            AutoPlayerLevel level,
            int requestedRuns,
            List<AutoRunTrace> runs,
            bool reportCancelled)
        {
            var result = new AutoRunLevelSummary
            {
                PlayerLevel = level,
                RequestedRuns = Math.Max(0, requestedRuns),
                ActualRuns = runs.Count,
                CompletedRuns = runs.Count(run => ClassifyOutcome(run) == AutoRunOutcomeKind.Completed),
                AverageArchetypeChanges = runs.Count > 0 ? (float)runs.Average(run => run.ArchetypeChanges) : 0f,
                FragmentChoiceRuns = runs.Count(SelectedFragmentChoice),
            };
            foreach (AutoRunTrace run in runs)
            {
                switch (ClassifyOutcome(run))
                {
                    case AutoRunOutcomeKind.NormalDefeat:
                        result.NormalDefeats++;
                        result.BalanceFailures++;
                        result.BalanceFailureSeeds.Add(run.Seed);
                        break;
                    case AutoRunOutcomeKind.RuntimeError:
                        result.RuntimeErrors++;
                        result.RuntimeErrorSeeds.Add(run.Seed);
                        break;
                    case AutoRunOutcomeKind.UnsupportedMechanic:
                        result.UnsupportedMechanics++;
                        result.UnsupportedMechanicSeeds.Add(run.Seed);
                        break;
                    case AutoRunOutcomeKind.UserCancelled:
                        result.UserCancelled++;
                        result.UserCancelledSeeds.Add(run.Seed);
                        break;
                }
            }
            result.CompletionRate = result.ActualRuns > 0
                ? result.CompletedRuns / (float)result.ActualRuns
                : 0f;
            result.FragmentChoiceRunShare = Rate(result.FragmentChoiceRuns, result.ActualRuns);
            return result;
        }

        private static bool SelectedFragmentChoice(AutoRunTrace run)
        {
            return (run?.Stages ?? new List<AutoRunStageTrace>())
                .SelectMany(stage => stage?.ActionDecisions ?? new List<AutoRunActionDecisionTrace>())
                .Any(decision => decision != null
                                 && decision.SelectedRewardKind == cfg.RewardKind.FragmentChoice);
        }

        private static IEnumerable<AutoRunFailureRecord> BuildFailures(IEnumerable<AutoRunTrace> runs)
        {
            foreach (AutoRunTrace run in runs.Where(run => ClassifyOutcome(run) != AutoRunOutcomeKind.Completed))
            {
                AutoRunOutcomeKind outcome = ClassifyOutcome(run);
                yield return new AutoRunFailureRecord
                {
                    PlayerLevel = run.PlayerLevel,
                    Seed = run.Seed,
                    LastWeek = (run.Stages ?? new List<AutoRunStageTrace>())
                        .Select(stage => stage.Week)
                        .DefaultIfEmpty(0)
                        .Max(),
                    Kind = ClassifyFailure(run),
                    Outcome = outcome,
                    Reason = run.FailureReason ?? string.Empty,
                };
            }
        }

        private static AutoRunWeekSummary BuildWeekSummary(
            AutoRunLevelSummary level,
            int week,
            List<AutoRunTrace> runs)
        {
            List<AutoRunStageTrace> stages = runs
                .SelectMany(run => run.Stages ?? new List<AutoRunStageTrace>())
                .Where(stage => stage.Week == week)
                .ToList();
            List<LegacyBossSample> legacyBosses = runs
                .SelectMany(run => (run.Stages ?? new List<AutoRunStageTrace>())
                    .Where(stage => stage != null && stage.Week == week && stage.BossReached)
                    .Where(stage => !(stage.Battles ?? new List<AutoRunBattleTrace>())
                        .Any(battle => battle != null && battle.IsBoss))
                    .Select(stage => new LegacyBossSample(run, stage)))
                .ToList();
            List<BossBattleSample> structuredBosses = runs
                .SelectMany(run => (run.Stages ?? new List<AutoRunStageTrace>())
                    .Where(stage => stage != null && stage.Week == week)
                    .SelectMany(stage => (stage.Battles ?? new List<AutoRunBattleTrace>())
                        .Where(battle => battle != null
                                         && battle.IsBoss
                                         && (battle.Week <= 0 || battle.Week == week))
                        .Select(battle => new BossBattleSample(run, battle))))
                .ToList();
            var result = new AutoRunWeekSummary
            {
                PlayerLevel = level.PlayerLevel,
                Week = week,
                ActualRuns = level.ActualRuns,
                CompletedRuns = level.CompletedRuns,
                BalanceFailures = level.BalanceFailures,
                RuntimeErrors = level.RuntimeErrors,
                StageReached = stages.Count,
                StageReachRate = Rate(stages.Count, level.ActualRuns),
                UsesLegacyBossTrace = legacyBosses.Count > 0,
                MealBattles = stages.Sum(stage => stage.MealBattles),
                MealPasses = stages.Sum(stage => stage.MealPasses),
                MeanGoldBalance = stages.Count > 0 ? (float)stages.Average(stage => stage.GoldBalance) : 0f,
                SolverTruncatedRate = Rate(stages.Count(IsBudgetTruncated), stages.Count),
                PlacementCandidateLimitedRate = Rate(stages.Count(IsCandidateLimited), stages.Count),
                NoLegalPlacementRate = Rate(stages.Count(stage => stage.NoLegalPlacement), stages.Count),
                ActiveItemsUsedPerRun = level.ActualRuns > 0
                    ? stages.Sum(stage => stage.ActiveItemsUsed?.Count ?? 0) / (float)level.ActualRuns
                    : 0f,
                SweetTransferShare = Rate(stages.Count(stage => stage.Archetype == BuildArchetype.SweetTransfer), stages.Count),
                CountShare = Rate(stages.Count(stage => stage.Archetype == BuildArchetype.Count), stages.Count),
                CakeShare = Rate(stages.Count(stage => stage.Archetype == BuildArchetype.Cake), stages.Count),
                NormalRouteShare = Rate(stages.Count(stage => stage.MetaRoute == MetaRoute.Normal), stages.Count),
                EventRouteShare = Rate(stages.Count(stage => stage.MetaRoute == MetaRoute.Event), stages.Count),
                InterestRouteShare = Rate(stages.Count(stage => stage.MetaRoute == MetaRoute.Interest), stages.Count),
                ShopRouteShare = Rate(stages.Count(stage => stage.MetaRoute == MetaRoute.Shop), stages.Count),
            };
            result.MealPassRate = Rate(result.MealPasses, result.MealBattles);

            foreach (IGrouping<EncounterIdentity, BossBattleSample> group in structuredBosses
                         .GroupBy(sample => new EncounterIdentity(
                             sample.Battle.Day,
                             sample.Battle.EncounterKey ?? string.Empty))
                         .OrderBy(group => group.Key.Day)
                         .ThenBy(group => group.Key.Key, StringComparer.Ordinal))
            {
                result.BossEncounters.Add(BuildBossEncounterSummary(level, week, group.Key, group.ToList()));
            }

            if (result.BossEncounters.Count > 0)
            {
                AutoRunBossEncounterSummary primary = result.BossEncounters
                    .OrderBy(summary => summary.Day)
                    .ThenBy(summary => summary.EncounterKey, StringComparer.Ordinal)
                    .Last();
                CopyLegacyBossFields(result, primary);
            }
            else if (legacyBosses.Count > 0)
            {
                result.BossReached = legacyBosses.Count;
                result.BossPassed = legacyBosses.Count(sample => sample.Stage.BossPassed);
                result.BossReachRate = Rate(result.BossReached, level.ActualRuns);
                result.BossPassRate = Rate(result.BossPassed, result.BossReached);
                result.BossMetricsValid = true;
                result.BossScores = Quantiles(legacyBosses.Select(sample => sample.Stage.BossScore));
            }

            if (legacyBosses.Count > 0)
                result.WarningCodes.Add("legacy-boss-trace-ambiguous");

            if (stages.Count == 0) result.WarningCodes.Add("no-stage-samples");
            if (result.BossEncounters.Count == 0 && legacyBosses.Count == 0)
                result.WarningCodes.Add("no-boss-reached");
            else if (result.BossEncounters.Count == 0 && result.BossReachRate < 0.5f)
                result.WarningCodes.Add("boss-survivor-bias");
            if (result.BossEncounters.Count == 0 && legacyBosses.Count > 0 && legacyBosses.Count < 30)
                result.WarningCodes.Add("boss-samples-below-30");
            if (result.MealBattles > 0 && result.MealPassRate < 0.3f) result.WarningCodes.Add("low-meal-pass-rate");
            if (result.SolverTruncatedRate > 0f) result.WarningCodes.Add("solver-truncated");
            if (result.PlacementCandidateLimitedRate > 0f) result.WarningCodes.Add("placement-candidate-limited");
            if (result.NoLegalPlacementRate > 0f) result.WarningCodes.Add("no-legal-placement");
            return result;
        }

        private static AutoRunBossEncounterSummary BuildBossEncounterSummary(
            AutoRunLevelSummary level,
            int week,
            EncounterIdentity identity,
            List<BossBattleSample> samples)
        {
            List<IGrouping<AutoRunTrace, BossBattleSample>> byRun = samples
                .GroupBy(sample => sample.Run)
                .ToList();
            List<AutoRunBattleTrace> uniqueBattles = byRun
                .Select(group => group
                    .OrderBy(sample => sample.Battle.BattleKey, StringComparer.Ordinal)
                    .Select(sample => sample.Battle)
                    .First())
                .ToList();
            List<string> actionIds = uniqueBattles
                .Select(battle => battle.ActionId ?? string.Empty)
                .Where(value => !string.IsNullOrEmpty(value))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            List<string> bossIds = uniqueBattles
                .Select(battle => battle.BossId ?? string.Empty)
                .Where(value => !string.IsNullOrEmpty(value))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            bool duplicateInRun = byRun.Any(group => group.Count() > 1);
            bool identityValid = !string.IsNullOrWhiteSpace(identity.Key)
                                 && !duplicateInRun
                                 && actionIds.Count <= 1
                                 && bossIds.Count <= 1;
            var result = new AutoRunBossEncounterSummary
            {
                PlayerLevel = level.PlayerLevel,
                Week = week,
                Day = identity.Day,
                EncounterKey = identity.Key,
                ActionId = actionIds.FirstOrDefault() ?? string.Empty,
                BossId = bossIds.FirstOrDefault() ?? string.Empty,
                ActualRuns = level.ActualRuns,
                Reached = uniqueBattles.Count,
                Passed = uniqueBattles.Count(battle => battle.TargetHit),
                MetricsValid = uniqueBattles.Count > 0,
                IdentityValid = identityValid,
                Scores = uniqueBattles.Count > 0
                    ? Quantiles(uniqueBattles.Select(battle => battle.Score))
                    : null,
            };
            result.ReachRate = Rate(result.Reached, result.ActualRuns);
            result.PassRate = Rate(result.Passed, result.Reached);
            if (result.ReachRate < 0.5f) result.WarningCodes.Add("boss-survivor-bias");
            if (result.Reached < 30) result.WarningCodes.Add("boss-samples-below-30");
            if (string.IsNullOrWhiteSpace(identity.Key))
                result.WarningCodes.Add("boss-encounter-key-missing");
            if (duplicateInRun) result.WarningCodes.Add("boss-encounter-duplicate");
            if (actionIds.Count > 1 || bossIds.Count > 1)
                result.WarningCodes.Add("boss-encounter-identity-collision");
            return result;
        }

        private static AutoRunScoreQuantiles Quantiles(IEnumerable<BigDouble> values)
        {
            BigDouble[] sorted = values.OrderBy(value => value).ToArray();
            if (sorted.Length == 0) return null;
            return new AutoRunScoreQuantiles
            {
                P10 = LowerQuantile(sorted, 0.10f),
                P30 = LowerQuantile(sorted, 0.30f),
                P50 = LowerQuantile(sorted, 0.50f),
                P90 = UpperQuantile(sorted, 0.90f),
            };
        }

        private static void CopyLegacyBossFields(
            AutoRunWeekSummary week,
            AutoRunBossEncounterSummary encounter)
        {
            week.BossReached = encounter.Reached;
            week.BossPassed = encounter.Passed;
            week.BossReachRate = encounter.ReachRate;
            week.BossPassRate = encounter.PassRate;
            week.BossMetricsValid = encounter.MetricsValid;
            week.BossScores = encounter.Scores;
            week.SuggestionValid = encounter.SuggestionValid;
        }

        private static bool IsBudgetTruncated(AutoRunStageTrace stage)
        {
            if (stage == null) return false;
            if (stage.Termination == AutoRunTerminationKind.PlacementBudgetExhausted) return true;
            if ((stage.Warnings ?? new List<AutoRunWarning>())
                .Any(warning => warning != null && warning.Kind == AutoRunWarningKind.PlacementBudgetExhausted))
                return true;
            // 兼容旧 trace：只有一个布尔字段且没有明确的候选上限告警时，仍按预算截断处理。
            return stage.SolverTruncated && !IsCandidateLimited(stage);
        }

        private static bool IsCandidateLimited(AutoRunStageTrace stage)
            => stage != null && (stage.Warnings ?? new List<AutoRunWarning>())
                .Any(warning => warning != null && warning.Kind == AutoRunWarningKind.PlacementCandidateLimitApplied);

        /// <summary>配置变化后可在不重跑样本的情况下即时撤销或恢复报告的推荐资格。</summary>
        public static void RefreshValidity(AutoRunReport report, bool configStale)
        {
            if (report == null) return;
            report.ConfigStale = configStale;
            report.HasRuntimeErrors = report.LevelSummaries.Any(summary => summary.RuntimeErrors > 0);
            report.HasUnsupportedMechanics = report.LevelSummaries.Any(summary => summary.UnsupportedMechanics > 0);
            foreach (AutoRunLevelSummary level in report.LevelSummaries)
                level.PolicyCalibrationValid = level.CompletedRuns > 0;
            report.PolicyCalibrationValid = report.LevelSummaries.Any(level => level.PolicyCalibrationValid);
            foreach (AutoRunWeekSummary week in report.WeekSummaries)
            {
                AutoRunLevelSummary level = report.LevelSummaries.FirstOrDefault(
                    summary => summary.PlayerLevel == week.PlayerLevel);
                RemoveDynamicWarnings(week.WarningCodes);
                bool runtimeClean = level != null && level.RuntimeErrors == 0;
                bool supported = level != null && level.UnsupportedMechanics == 0;
                bool policyCalibrated = level?.PolicyCalibrationValid == true;
                bool truncationAcceptable = week.SolverTruncatedRate <= 0.01f;
                week.BossEncounters ??= new List<AutoRunBossEncounterSummary>();
                if (report.SchemaVersion < 3
                    || (week.BossEncounters.Count == 0
                        && week.BossMetricsValid
                        && week.BossScores != null))
                {
                    week.UsesLegacyBossTrace = true;
                    if (!week.WarningCodes.Contains("legacy-boss-trace-ambiguous"))
                        week.WarningCodes.Add("legacy-boss-trace-ambiguous");
                }
                foreach (AutoRunBossEncounterSummary encounter in week.BossEncounters)
                {
                    encounter.WarningCodes ??= new List<string>();
                    RemoveDynamicWarnings(encounter.WarningCodes);
                    encounter.SuggestionValid = encounter.Reached >= 30
                                                  && encounter.IdentityValid
                                                  && !week.UsesLegacyBossTrace
                                                  && runtimeClean
                                                  && supported
                                                  && policyCalibrated
                                                  && truncationAcceptable
                                                  && !report.ConfigStale
                                                  && report.SamplingComplete
                                                  && !report.Cancelled;
                    AddDynamicWarnings(
                        encounter.WarningCodes,
                        runtimeClean,
                        supported,
                        policyCalibrated,
                        truncationAcceptable,
                        report);
                }

                week.SuggestionValid = false;
                if (week.BossEncounters.Count > 0)
                {
                    AutoRunBossEncounterSummary primary = week.BossEncounters
                        .OrderBy(summary => summary.Day)
                        .ThenBy(summary => summary.EncounterKey, StringComparer.Ordinal)
                        .Last();
                    CopyLegacyBossFields(week, primary);
                }

                AddDynamicWarnings(
                    week.WarningCodes,
                    runtimeClean,
                    supported,
                    policyCalibrated,
                    truncationAcceptable,
                    report);
            }

            report.IsValid = report.SamplingComplete
                             && !report.HasRuntimeErrors
                             && !report.HasUnsupportedMechanics
                             && report.PolicyCalibrationValid
                             && !report.ConfigStale
                             && report.ActualTraceCount > 0
                             && report.WeekSummaries.All(week => week.SolverTruncatedRate <= 0.01f);
        }

        public static string ToFullJson(AutoRunReport report, bool prettyPrint = true)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            return JsonUtility.ToJson(report, prettyPrint);
        }

        /// <summary>
        /// 以与 <see cref="ToFullJson(AutoRunReport, bool)"/> compact 输出逐字节一致的格式写出完整报告。
        /// 顶层元数据只序列化一次，trace 则逐条交给 JsonUtility，避免为整份报告创建单体 UTF-16 字符串。
        /// </summary>
        public static void WriteFullJson(AutoRunReport report, TextWriter writer)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (writer == null) throw new ArgumentNullException(nameof(writer));

            AutoRunReport envelopeReport = CopyWithoutTraces(report);
            string envelopeJson = JsonUtility.ToJson(envelopeReport, prettyPrint: false);
            int markerIndex = envelopeJson.IndexOf(EmptyTraceArrayJson, StringComparison.Ordinal);
            if (markerIndex < 0
                || envelopeJson.IndexOf(
                    EmptyTraceArrayJson,
                    markerIndex + EmptyTraceArrayJson.Length,
                    StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException(
                    "AutoRunReport compact JSON did not contain exactly one empty Traces array.");
            }

            int arrayOffset = markerIndex + "\"Traces\":".Length;
            writer.Write(envelopeJson.Substring(0, arrayOffset));
            writer.Write('[');
            List<AutoRunTrace> traces = report.Traces ?? new List<AutoRunTrace>();
            for (int i = 0; i < traces.Count; i++)
            {
                if (i > 0) writer.Write(',');
                AutoRunTrace trace = traces[i];
                writer.Write(trace == null ? "null" : JsonUtility.ToJson(trace, prettyPrint: false));
            }

            writer.Write(']');
            writer.Write(envelopeJson.Substring(arrayOffset + 2));
        }

        /// <summary>
        /// 将完整 JSON 先写入同目录临时文件并刷盘，再原子替换目标，避免中断时留下半份报告。
        /// </summary>
        public static void WriteFullJsonFileAtomically(AutoRunReport report, string path)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path must not be empty", nameof(path));

            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException("path must have a parent directory", nameof(path));
            Directory.CreateDirectory(directory);

            string temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           JsonStreamBufferSize,
                           FileOptions.SequentialScan))
                using (var output = new StreamWriter(
                           stream,
                           new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                           JsonStreamBufferSize,
                           leaveOpen: true))
                {
                    WriteFullJson(report, output);
                    output.Flush();
                    stream.Flush(flushToDisk: true);
                }

                ReplaceCompletedFile(temporaryPath, fullPath);
            }
            finally
            {
                SafeDelete(temporaryPath);
            }
        }

        private static AutoRunReport CopyWithoutTraces(AutoRunReport source)
        {
            var copy = new AutoRunReport();
            foreach (FieldInfo field in ReportSerializationFields)
            {
                if (!string.Equals(field.Name, nameof(AutoRunReport.Traces), StringComparison.Ordinal))
                    field.SetValue(copy, field.GetValue(source));
            }

            copy.Traces = new List<AutoRunTrace>();
            return copy;
        }

        private static void ReplaceCompletedFile(string completedPath, string destinationPath)
        {
            if (!File.Exists(destinationPath))
            {
                File.Move(completedPath, destinationPath);
                return;
            }

            try
            {
                File.Replace(completedPath, destinationPath, destinationBackupFileName: null);
            }
            catch (PlatformNotSupportedException)
            {
                ReplaceByMoveWithRollback(completedPath, destinationPath);
            }
            catch (IOException)
            {
                ReplaceByMoveWithRollback(completedPath, destinationPath);
            }
        }

        private static void ReplaceByMoveWithRollback(string completedPath, string destinationPath)
        {
            string displacedPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".old";
            File.Move(destinationPath, displacedPath);
            bool displacedCanBeDeleted = false;
            try
            {
                File.Move(completedPath, destinationPath);
                displacedCanBeDeleted = true;
            }
            catch (Exception replacementException)
            {
                try
                {
                    if (!File.Exists(destinationPath) && File.Exists(displacedPath))
                        File.Move(displacedPath, destinationPath);
                    displacedCanBeDeleted = File.Exists(destinationPath);
                }
                catch (Exception rollbackException)
                {
                    throw new IOException(
                        $"Failed to replace '{destinationPath}' and could not restore the original file at '{displacedPath}'.",
                        new AggregateException(replacementException, rollbackException));
                }

                throw;
            }
            finally
            {
                if (displacedCanBeDeleted) SafeDelete(displacedPath);
            }
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // 临时文件清理失败不覆盖原始导出异常。
            }
        }

        private static void AddDynamicWarnings(
            List<string> warnings,
            bool runtimeClean,
            bool supported,
            bool policyCalibrated,
            bool truncationAcceptable,
            AutoRunReport report)
        {
            if (warnings == null || report == null) return;
            if (!runtimeClean) warnings.Add("runtime-errors");
            if (!supported) warnings.Add("unsupported-mechanics");
            if (!policyCalibrated) warnings.Add("policy-no-completed-runs");
            if (!truncationAcceptable) warnings.Add("solver-truncated-over-limit");
            if (report.ConfigStale) warnings.Add("config-stale");
            if (!report.SamplingComplete) warnings.Add("sampling-incomplete");
            if (report.Cancelled) warnings.Add("user-cancelled");
        }

        private static void RemoveDynamicWarnings(List<string> warnings)
        {
            if (warnings == null) return;
            warnings.RemoveAll(code => code == "runtime-errors"
                                       || code == "unsupported-mechanics"
                                       || code == "policy-no-completed-runs"
                                       || code == "solver-truncated-over-limit"
                                       || code == "config-stale"
                                       || code == "sampling-incomplete"
                                       || code == "user-cancelled");
        }

        private static AutoPlayerPolicy ClonePolicy(AutoPlayerPolicy source)
        {
            source ??= new AutoPlayerPolicy();
            return new AutoPlayerPolicy
            {
                SoftmaxTemperature = source.SoftmaxTemperature,
                NormalBeamWidth = source.NormalBeamWidth,
                ExpertBeamWidth = source.ExpertBeamWidth,
                PlacementNodeBudget = source.PlacementNodeBudget,
                DualArchetypeThreshold = source.DualArchetypeThreshold,
                InterestReserve = source.InterestReserve,
                MaxActionsPerWeek = source.MaxActionsPerWeek,
                ActionRewardPriority = source.ActionRewardPriority,
            };
        }

        private static BigDouble LowerQuantile(IReadOnlyList<BigDouble> sorted, float probability)
        {
            int index = Math.Max(0, Math.Min(sorted.Count - 1,
                (int)Math.Floor((sorted.Count - 1) * probability)));
            return sorted[index];
        }

        private static BigDouble UpperQuantile(IReadOnlyList<BigDouble> sorted, float probability)
        {
            int index = Math.Max(0, Math.Min(sorted.Count - 1,
                (int)Math.Ceiling((sorted.Count - 1) * probability)));
            return sorted[index];
        }

        private static float Rate(int numerator, int denominator)
            => denominator > 0 ? numerator / (float)denominator : 0f;

        private static string Csv(string value)
            => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

        private static string Bool(bool value) => value ? "true" : "false";

        private static string Number(float value)
            => value.ToString("0.######", CultureInfo.InvariantCulture);

        private static string Big(BigDouble value)
            => value.Mantissa.ToString("R", CultureInfo.InvariantCulture)
               + "e"
               + value.Exponent.ToString(CultureInfo.InvariantCulture);
    }

    public static class AutoRunProfileFingerprint
    {
        public static string ComputeAllUnlocked(cfg.Tables tables)
        {
            if (tables == null) return string.Empty;
            var canonical = new StringBuilder(AutoRunReport.AllUnlockedProfileId).Append('\n');
            foreach (cfg.UnlockRule rule in tables.TbUnlockRule.DataList
                         .Where(rule => rule != null && rule.Enabled)
                         .OrderBy(rule => rule.TargetType)
                         .ThenBy(rule => rule.TargetId, StringComparer.Ordinal)
                         .ThenBy(rule => rule.Id, StringComparer.Ordinal))
            {
                canonical.Append((int)rule.TargetType).Append('|')
                    .Append(rule.TargetId ?? string.Empty).Append('|')
                    .Append(rule.Id ?? string.Empty).Append('\n');
            }

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
