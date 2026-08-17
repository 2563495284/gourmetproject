using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using GourmetProject.Core.Rng;
using GourmetProject.Core.Save;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Balance;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using GourmetProject.Runtime;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class RunExecutionEnvironmentIsolationTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;
        private string _characterId;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            string configDirectory = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name =>
                JSON.Parse(File.ReadAllText(Path.Combine(configDirectory, name + ".json"))));
            _database = GameplayContentBuilder.BuildDatabase(_tables);
            _characterId = _tables.TbCharacter.DataList.First().Id;
        }

        [Test]
        public void Seed1001_NormalAndExpert_ProduceWeekDataWithoutGameAppServices()
        {
            using (new GameAppServiceScope(random: null, save: null, config: null))
            {
                GameRun previousRun = GameRunContext.Current;
                GameRunContext.Clear();
                try
                {
                    AutoRunTrace normal = Simulate(AutoPlayerLevel.Normal);
                    AutoRunTrace expert = Simulate(AutoPlayerLevel.Expert);

                    AssertValidWeekData(normal);
                    AssertValidWeekData(expert);
                    Assert.That(GameRunContext.Current, Is.Null);
                    Assert.That(GameApp.Random, Is.Null);
                    Assert.That(GameApp.Save, Is.Null);
                    Assert.That(GameApp.Config, Is.Null);
                }
                finally
                {
                    RestoreRunContext(previousRun);
                }
            }
        }

        [Test]
        public void Simulation_PreservesGlobalRandomSaveAndContext_AndIsBatchOrderIndependent()
        {
            var globalRandom = new RandomService();
            globalRandom.Init("global-random-sentinel");
            globalRandom.DomainStream(SeedDomains.Action, "already-in-use").NextULong();
            RandomSnapshot randomBefore = globalRandom.Capture();
            var save = new CountingSaveService();
            GameRun previousRun = GameRunContext.Current;
            GameRun contextSentinel = CreateIsolatedRun("context-sentinel");

            using (new GameAppServiceScope(globalRandom, save, config: null))
            {
                GameRunContext.Set(contextSentinel);
                try
                {
                    var forwardSimulator = new HeadlessRunSimulator(_tables, _database);
                    AutoRunTrace normalForward = Simulate(forwardSimulator, AutoPlayerLevel.Normal);
                    AutoRunTrace expertForward = Simulate(forwardSimulator, AutoPlayerLevel.Expert);

                    var reverseSimulator = new HeadlessRunSimulator(_tables, _database);
                    AutoRunTrace expertReverse = Simulate(reverseSimulator, AutoPlayerLevel.Expert);
                    AutoRunTrace normalReverse = Simulate(reverseSimulator, AutoPlayerLevel.Normal);

                    Assert.That(Fingerprint(normalReverse), Is.EqualTo(Fingerprint(normalForward)));
                    Assert.That(Fingerprint(expertReverse), Is.EqualTo(Fingerprint(expertForward)));
                    Assert.That(GameRunContext.Current, Is.SameAs(contextSentinel));
                    AssertRandomSnapshotEqual(randomBefore, globalRandom.Capture());
                    Assert.That(save.TotalCalls, Is.Zero, "isolated simulation must not read, write or delete player saves");
                }
                finally
                {
                    RestoreRunContext(previousRun);
                }
            }
        }

        [Test]
        [Category("BalanceLabCommitGate")]
        public void ProductionConfig_200SeedsPerLevel_HasNoInfrastructureFailuresAndStaysWithinFrameBudget()
        {
            var invalid = new List<string>();
            var stepMilliseconds = new List<double>();
            var simulator = new HeadlessRunSimulator(_tables, _database);
            foreach (AutoPlayerLevel level in new[] { AutoPlayerLevel.Normal, AutoPlayerLevel.Expert })
            {
                for (int seed = 1001; seed < 1201; seed++)
                {
                    HeadlessRunSession session = simulator.StartSession(new AutoRunRequest
                    {
                        CharacterId = _characterId,
                        PlayerLevel = level,
                        Seed = seed,
                        Policy = new AutoPlayerPolicy(),
                    });
                    while (!session.IsCompleted)
                    {
                        Stopwatch watch = Stopwatch.StartNew();
                        session.Step(1);
                        watch.Stop();
                        stepMilliseconds.Add(watch.Elapsed.TotalMilliseconds);
                    }

                    AutoRunTrace trace = session.Trace;
                    if (trace.Stages.Count == 0 || !IsExpectedBalanceOutcome(trace.Termination))
                        invalid.Add($"{level}/Seed {seed}: {trace.Termination} - {trace.FailureReason}");
                }
            }

            stepMilliseconds.Sort();
            double p95 = Percentile(stepMilliseconds, 0.95d);
            double maximum = stepMilliseconds.Count > 0 ? stepMilliseconds[stepMilliseconds.Count - 1] : 0d;
            Assert.That(invalid, Is.Empty, string.Join("\n", invalid.Take(10)));
            Assert.That(p95, Is.LessThanOrEqualTo(100d), $"P95 editor time slice was {p95:0.###}ms");
            Assert.That(maximum, Is.LessThanOrEqualTo(250d), $"maximum editor time slice was {maximum:0.###}ms");
        }

        [Test]
        [Timeout(600000)]
        [Explicit("Nightly Balance Lab soak and deterministic reverse replay: 1000 seeds per player level.")]
        [Category("BalanceLabNightly")]
        public void ProductionConfig_Nightly1000SeedsPerLevel_HasNoInfrastructureFailuresAndReplaysDeterministically()
        {
            var invalid = new List<string>();
            var fingerprints = new Dictionary<string, string>();
            var simulator = new HeadlessRunSimulator(_tables, _database);
            foreach (AutoPlayerLevel level in new[] { AutoPlayerLevel.Normal, AutoPlayerLevel.Expert })
            {
                for (int seed = 1001; seed < 2001; seed++)
                {
                    AutoRunTrace trace = simulator.Run(new AutoRunRequest
                    {
                        CharacterId = _characterId,
                        PlayerLevel = level,
                        Seed = seed,
                        Policy = new AutoPlayerPolicy(),
                    });
                    if (trace.Stages.Count == 0 || !IsExpectedBalanceOutcome(trace.Termination))
                        invalid.Add($"{level}/Seed {seed}: {trace.Termination} - {trace.FailureReason}");
                    fingerprints[$"{level}:{seed}"] = Fingerprint(trace);
                }
            }

            var replaySimulator = new HeadlessRunSimulator(_tables, _database);
            foreach (AutoPlayerLevel level in new[] { AutoPlayerLevel.Expert, AutoPlayerLevel.Normal })
            {
                for (int seed = 2000; seed >= 1001; seed--)
                {
                    AutoRunTrace replay = replaySimulator.Run(new AutoRunRequest
                    {
                        CharacterId = _characterId,
                        PlayerLevel = level,
                        Seed = seed,
                        Policy = new AutoPlayerPolicy(),
                    });
                    if (!string.Equals(
                            fingerprints[$"{level}:{seed}"],
                            Fingerprint(replay),
                            StringComparison.Ordinal))
                    {
                        invalid.Add($"{level}/Seed {seed}: deterministic reverse replay mismatch");
                    }
                }
            }

            Assert.That(invalid, Is.Empty, string.Join("\n", invalid.Take(10)));
        }

        [Test]
        [Timeout(600000)]
        [Explicit("Nightly Balance Lab performance gate: 5000 total runs on the approved editor baseline.")]
        [Category("BalanceLabPerformance")]
        public void ProductionConfig_Nightly5000Runs_StaysWithinApprovedPerformanceBaseline()
        {
            const int runsPerLevel = 2500;
            const double approvedMillisecondsPerRun = 75d;
            const double allowedRegression = 0.20d;
            var invalid = new List<string>();
            var simulator = new HeadlessRunSimulator(_tables, _database);
            Stopwatch watch = Stopwatch.StartNew();
            foreach (AutoPlayerLevel level in new[] { AutoPlayerLevel.Normal, AutoPlayerLevel.Expert })
            {
                for (int index = 0; index < runsPerLevel; index++)
                {
                    int seed = 1001 + index;
                    AutoRunTrace trace = simulator.Run(new AutoRunRequest
                    {
                        CharacterId = _characterId,
                        PlayerLevel = level,
                        Seed = seed,
                        Policy = new AutoPlayerPolicy(),
                    });
                    if (trace.Stages.Count == 0 || !IsExpectedBalanceOutcome(trace.Termination))
                        invalid.Add($"{level}/Seed {seed}: {trace.Termination} - {trace.FailureReason}");
                }
            }
            watch.Stop();

            double millisecondsPerRun = watch.Elapsed.TotalMilliseconds / (runsPerLevel * 2d);
            TestContext.WriteLine($"Balance Lab: {millisecondsPerRun:0.###} ms/run across {runsPerLevel * 2} runs");
            Assert.That(invalid, Is.Empty, string.Join("\n", invalid.Take(10)));
            Assert.That(
                millisecondsPerRun,
                Is.LessThanOrEqualTo(approvedMillisecondsPerRun * (1d + allowedRegression)),
                $"{millisecondsPerRun:0.###} ms/run exceeds the approved {approvedMillisecondsPerRun:0.###} ms baseline by more than 20%");
        }

        [Test]
        public void Session_CancelStopsOnNextStepAndPreservesPartialTrace()
        {
            var simulator = new HeadlessRunSimulator(_tables, _database);
            HeadlessRunSession session = simulator.StartSession(new AutoRunRequest
            {
                CharacterId = _characterId,
                PlayerLevel = AutoPlayerLevel.Normal,
                Seed = 1001,
                Policy = new AutoPlayerPolicy(),
            });

            session.Step(1);
            int stagesBeforeCancel = session.Trace.Stages.Count;
            session.Cancel();
            session.Step(1);

            Assert.That(session.IsCompleted, Is.True);
            Assert.That(session.Trace.Termination, Is.EqualTo(AutoRunTerminationKind.Cancelled));
            Assert.That(session.Trace.Stages.Count, Is.GreaterThanOrEqualTo(stagesBeforeCancel));
        }

        [Test]
        public void IsolatedProfile_UnlocksEveryEnabledTargetAndStartsWithZeroStatistics()
        {
            IRunExecutionEnvironment execution = RunExecutionEnvironment.CreateIsolated(
                _tables,
                "all-unlocked-profile-test");

            Assert.That(execution.ProfileId, Is.EqualTo(RunExecutionEnvironment.AllUnlockedProfileId));
            Assert.That(execution.AllowsExternalSideEffects, Is.False);
            foreach (cfg.UnlockRule rule in _tables.TbUnlockRule.DataList.Where(v => v.Enabled))
            {
                Assert.That(
                    execution.MetaProgress.IsTargetUnlocked(rule.TargetType, rule.TargetId),
                    Is.True,
                    $"enabled target was not unlocked: {rule.TargetType}:{rule.TargetId}");
            }

            Assert.That(execution.MetaProgress.CompletedRunCount, Is.Zero);
            Assert.That(execution.MetaProgress.WonRunCount, Is.Zero);
            Assert.That(execution.MetaProgress.LostRunCount, Is.Zero);
            Assert.That(execution.MetaProgress.HighestWeekIndex, Is.Zero);
            Assert.That(execution.MetaProgress.TotalBossDefeats, Is.Zero);
            Assert.That(execution.MetaProgress.DefeatedBossIds, Is.Empty);
            Assert.That(execution.MetaProgress.SeenEventIds, Is.Empty);
        }

        [Test]
        public void IsolatedEnvironment_UsesNullSavePresentationAndTelemetrySinks()
        {
            IRunExecutionEnvironment execution = RunExecutionEnvironment.CreateIsolated(
                _tables,
                "null-sinks-test");
            int presentationCalls = 0;
            int telemetryCalls = 0;

            execution.Saves.Save(null);
            execution.Presentation.Present(() => presentationCalls++);
            int read = execution.Presentation.Read(() => ++presentationCalls, fallback: 17);
            execution.Telemetry.Track(() => telemetryCalls++);

            Assert.That(execution.Saves.Enabled, Is.False);
            Assert.That(execution.Presentation.Enabled, Is.False);
            Assert.That(execution.Telemetry.Enabled, Is.False);
            Assert.That(presentationCalls, Is.Zero);
            Assert.That(telemetryCalls, Is.Zero);
            Assert.That(read, Is.EqualTo(17));
        }

        [Test]
        public void IsolatedProfile_ClonesWithoutNormalizingOrMutatingCallerData()
        {
            var source = new MetaProgressSaveData
            {
                Version = 0,
                UnlockedTargetKeys = new List<string> { "Item:item_a", "Item:item_a" },
                UnlockedItemIds = null,
                DefeatedBossIds = new List<string> { "boss_a", "boss_a" },
                SeenEventIds = null,
                CompletedRunCount = 9,
            };

            IRunExecutionEnvironment execution = RunExecutionEnvironment.CreateIsolated(
                _tables,
                "profile-clone-test",
                metaProgress: source,
                profileId: "custom-profile");

            Assert.That(source.Version, Is.Zero);
            Assert.That(source.UnlockedTargetKeys, Has.Count.EqualTo(2));
            Assert.That(source.UnlockedItemIds, Is.Null);
            Assert.That(source.DefeatedBossIds, Has.Count.EqualTo(2));
            Assert.That(source.SeenEventIds, Is.Null);
            Assert.That(execution.MetaProgress, Is.Not.SameAs(source));
            Assert.That(execution.MetaProgress.Version, Is.EqualTo(1));
            Assert.That(execution.MetaProgress.UnlockedTargetKeys, Has.Count.EqualTo(1));
            Assert.That(execution.MetaProgress.UnlockedItemIds, Is.Empty);
            Assert.That(execution.MetaProgress.DefeatedBossIds, Has.Count.EqualTo(1));
            Assert.That(execution.MetaProgress.SeenEventIds, Is.Empty);
            Assert.That(execution.MetaProgress.CompletedRunCount, Is.EqualTo(9));
        }

        private AutoRunTrace Simulate(AutoPlayerLevel level)
        {
            return Simulate(new HeadlessRunSimulator(_tables, _database), level);
        }

        private AutoRunTrace Simulate(HeadlessRunSimulator simulator, AutoPlayerLevel level)
        {
            return simulator.Run(new AutoRunRequest
            {
                CharacterId = _characterId,
                PlayerLevel = level,
                Seed = 1001,
                Policy = new AutoPlayerPolicy(),
            });
        }

        private GameRun CreateIsolatedRun(string seed)
        {
            return new GameRun(
                _tables,
                _database,
                _characterId,
                seed,
                execution: RunExecutionEnvironment.CreateIsolated(_tables, seed));
        }

        private static void AssertValidWeekData(AutoRunTrace trace)
        {
            Assert.That(trace, Is.Not.Null);
            Assert.That(trace.Stages, Is.Not.Null.And.Not.Empty, trace.FailureReason);
            Assert.That(trace.Termination, Is.Not.EqualTo(AutoRunTerminationKind.InfrastructureError), trace.FailureReason);
            Assert.That(trace.Termination, Is.Not.EqualTo(AutoRunTerminationKind.UnsupportedMechanic), trace.FailureReason);
            Assert.That(trace.Warnings.Any(v => v.Kind == AutoRunWarningKind.UnsupportedMechanic), Is.False);
            Assert.That(trace.Stages.All(v => v.Week > 0), Is.True);
        }

        private static bool IsExpectedBalanceOutcome(AutoRunTerminationKind termination)
        {
            return termination == AutoRunTerminationKind.Completed
                   || termination == AutoRunTerminationKind.HeartsDepleted
                   || termination == AutoRunTerminationKind.GameOver;
        }

        private static double Percentile(IReadOnlyList<double> sorted, double probability)
        {
            if (sorted == null || sorted.Count == 0) return 0d;
            int index = Math.Max(0, Math.Min(sorted.Count - 1,
                (int)Math.Ceiling(sorted.Count * probability) - 1));
            return sorted[index];
        }

        private static string Fingerprint(AutoRunTrace trace)
        {
            return JsonUtility.ToJson(trace, prettyPrint: false);
        }

        private static void AssertRandomSnapshotEqual(RandomSnapshot expected, RandomSnapshot actual)
        {
            Assert.That(actual.SeedText, Is.EqualTo(expected.SeedText));
            Assert.That(actual.MasterSeed, Is.EqualTo(expected.MasterSeed));
            Assert.That(actual.Streams.Keys, Is.EquivalentTo(expected.Streams.Keys));
            foreach (KeyValuePair<string, RngState> pair in expected.Streams)
            {
                Assert.That(actual.Streams[pair.Key], Is.EqualTo(pair.Value), pair.Key);
            }
        }

        private static void RestoreRunContext(GameRun previous)
        {
            if (previous == null)
            {
                GameRunContext.Clear();
            }
            else
            {
                GameRunContext.Set(previous);
            }
        }

        private sealed class CountingSaveService : ISaveService
        {
            public int TotalCalls { get; private set; }

            public void Save<T>(string slot, T data) => TotalCalls++;

            public bool TryLoad<T>(string slot, out T data)
            {
                TotalCalls++;
                data = default;
                return false;
            }

            public bool Has(string slot)
            {
                TotalCalls++;
                return false;
            }

            public void Delete(string slot) => TotalCalls++;

            public IEnumerable<string> ListSlots()
            {
                TotalCalls++;
                return Array.Empty<string>();
            }
        }

        private sealed class GameAppServiceScope : IDisposable
        {
            private readonly object _previousRandom;
            private readonly object _previousSave;
            private readonly object _previousConfig;

            public GameAppServiceScope(RandomService random, ISaveService save, object config)
            {
                _previousRandom = GetStaticAutoProperty("Random");
                _previousSave = GetStaticAutoProperty("Save");
                _previousConfig = GetStaticAutoProperty("Config");
                SetStaticAutoProperty("Random", random);
                SetStaticAutoProperty("Save", save);
                SetStaticAutoProperty("Config", config);
            }

            public void Dispose()
            {
                SetStaticAutoProperty("Random", _previousRandom);
                SetStaticAutoProperty("Save", _previousSave);
                SetStaticAutoProperty("Config", _previousConfig);
            }

            private static object GetStaticAutoProperty(string propertyName)
            {
                return BackingField(propertyName).GetValue(null);
            }

            private static void SetStaticAutoProperty(string propertyName, object value)
            {
                BackingField(propertyName).SetValue(null, value);
            }

            private static FieldInfo BackingField(string propertyName)
            {
                FieldInfo field = typeof(GameApp).GetField(
                    $"<{propertyName}>k__BackingField",
                    BindingFlags.Static | BindingFlags.NonPublic);
                return field ?? throw new InvalidOperationException($"GameApp.{propertyName} backing field not found.");
            }
        }
    }
}
