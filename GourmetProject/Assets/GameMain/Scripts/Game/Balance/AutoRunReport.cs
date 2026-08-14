using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

        public int SchemaVersion = 2;
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
        public float CompletionRate;
        public float AverageArchetypeChanges;
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
            text.AppendLine("schemaVersion,generatedUtc,applicationVersion,unityVersion,configHash,configLoadedUtc,configStale,unlockProfile,unlockProfileHash,metaAffinityProfile,metaAffinityProfileHash,policyVersion,characterId,characterName,baseSeed,requestedRunsPerLevel,normalCandidateLimit,expertCandidateLimit,placementNodeBudget,interestReserve,softmaxTemperature,dualArchetypeThreshold,maxActionsPerWeek,samplingComplete,cancelled,reportValid,playerLevel,week,actualRuns,completedRuns,normalDefeats,runtimeErrors,unsupportedMechanics,userCancelled,stageReached,stageReachRate,bossReached,bossPassed,bossReachRate,bossPassRate,bossMetricsValid,suggestionValid,p10,p30,p50,p90,suggestedRequirement,mealBattles,mealPasses,mealPassRate,meanGoldBalance,solverTruncatedRate,placementCandidateLimitedRate,noLegalPlacementRate,activeItemsUsedPerRun,sweetTransferShare,countShare,cakeShare,normalRouteShare,eventRouteShare,interestRouteShare,shopRouteShare,balanceFailureSeeds,runtimeErrorSeeds,unsupportedMechanicSeeds,userCancelledSeeds,warningCodes");
            foreach (AutoRunWeekSummary week in report.WeekSummaries)
            {
                AutoRunLevelSummary level = report.LevelSummaries.First(summary => summary.PlayerLevel == week.PlayerLevel);
                AutoRunScoreQuantiles scores = week.BossScores;
                string suggested = week.SuggestionValid && scores != null ? Big(scores.P30) : string.Empty;
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
                    .Append(Number(report.Policy.DualArchetypeThreshold)).Append(',')
                    .Append(report.Policy.MaxActionsPerWeek).Append(',')
                    .Append(Bool(report.SamplingComplete)).Append(',')
                    .Append(Bool(report.Cancelled)).Append(',')
                    .Append(Bool(report.IsValid)).Append(',')
                    .Append(Csv(week.PlayerLevel.ToString())).Append(',')
                    .Append(week.Week).Append(',')
                    .Append(week.ActualRuns).Append(',')
                    .Append(week.CompletedRuns).Append(',')
                    .Append(level.NormalDefeats).Append(',')
                    .Append(week.RuntimeErrors).Append(',')
                    .Append(level.UnsupportedMechanics).Append(',')
                    .Append(level.UserCancelled).Append(',')
                    .Append(week.StageReached).Append(',')
                    .Append(Number(week.StageReachRate)).Append(',')
                    .Append(week.BossReached).Append(',')
                    .Append(week.BossPassed).Append(',')
                    .Append(Number(week.BossReachRate)).Append(',')
                    .Append(Number(week.BossPassRate)).Append(',')
                    .Append(Bool(week.BossMetricsValid)).Append(',')
                    .Append(Bool(week.SuggestionValid)).Append(',')
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
                    .Append(Csv(string.Join("|", week.WarningCodes)))
                    .Append('\n');
            }
            return text.ToString();
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
            return result;
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
            List<AutoRunStageTrace> bosses = stages.Where(stage => stage.BossReached).ToList();
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
                BossReached = bosses.Count,
                BossPassed = bosses.Count(stage => stage.BossPassed),
                BossReachRate = Rate(bosses.Count, level.ActualRuns),
                BossPassRate = Rate(bosses.Count(stage => stage.BossPassed), bosses.Count),
                BossMetricsValid = bosses.Count > 0,
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
            if (bosses.Count > 0)
            {
                BigDouble[] scores = bosses.Select(stage => stage.BossScore).OrderBy(score => score).ToArray();
                result.BossScores = new AutoRunScoreQuantiles
                {
                    P10 = LowerQuantile(scores, 0.10f),
                    P30 = LowerQuantile(scores, 0.30f),
                    P50 = LowerQuantile(scores, 0.50f),
                    P90 = UpperQuantile(scores, 0.90f),
                };
            }

            if (stages.Count == 0) result.WarningCodes.Add("no-stage-samples");
            if (bosses.Count == 0) result.WarningCodes.Add("no-boss-reached");
            else if (result.BossReachRate < 0.5f) result.WarningCodes.Add("boss-survivor-bias");
            if (bosses.Count > 0 && bosses.Count < 30) result.WarningCodes.Add("boss-samples-below-30");
            if (result.MealBattles > 0 && result.MealPassRate < 0.3f) result.WarningCodes.Add("low-meal-pass-rate");
            if (result.SolverTruncatedRate > 0f) result.WarningCodes.Add("solver-truncated");
            if (result.PlacementCandidateLimitedRate > 0f) result.WarningCodes.Add("placement-candidate-limited");
            if (result.NoLegalPlacementRate > 0f) result.WarningCodes.Add("no-legal-placement");
            return result;
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
            foreach (AutoRunWeekSummary week in report.WeekSummaries)
            {
                AutoRunLevelSummary level = report.LevelSummaries.FirstOrDefault(
                    summary => summary.PlayerLevel == week.PlayerLevel);
                RemoveDynamicWarnings(week.WarningCodes);
                bool runtimeClean = level != null && level.RuntimeErrors == 0;
                bool supported = level != null && level.UnsupportedMechanics == 0;
                bool truncationAcceptable = week.SolverTruncatedRate <= 0.01f;
                week.SuggestionValid = week.BossReached >= 30
                                       && runtimeClean
                                       && supported
                                       && truncationAcceptable
                                       && !report.ConfigStale
                                       && report.SamplingComplete
                                       && !report.Cancelled;

                if (!runtimeClean) week.WarningCodes.Add("runtime-errors");
                if (!supported) week.WarningCodes.Add("unsupported-mechanics");
                if (!truncationAcceptable) week.WarningCodes.Add("solver-truncated-over-limit");
                if (report.ConfigStale) week.WarningCodes.Add("config-stale");
                if (!report.SamplingComplete) week.WarningCodes.Add("sampling-incomplete");
                if (report.Cancelled) week.WarningCodes.Add("user-cancelled");
            }

            report.IsValid = report.SamplingComplete
                             && !report.HasRuntimeErrors
                             && !report.HasUnsupportedMechanics
                             && !report.ConfigStale
                             && report.ActualTraceCount > 0
                             && report.WeekSummaries.All(week => week.SolverTruncatedRate <= 0.01f);
        }

        public static string ToFullJson(AutoRunReport report, bool prettyPrint = true)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            return JsonUtility.ToJson(report, prettyPrint);
        }

        private static void RemoveDynamicWarnings(List<string> warnings)
        {
            if (warnings == null) return;
            warnings.RemoveAll(code => code == "runtime-errors"
                                       || code == "unsupported-mechanics"
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
