using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta;

namespace GourmetProject.Game.Run
{
    public interface IRunSaveSink
    {
        bool Enabled { get; }

        void Save(GameRun run);
    }

    public interface IRunPresentationSink
    {
        bool Enabled { get; }

        void Present(Action action);

        T Read<T>(Func<T> read, T fallback = default);
    }

    public interface IRunTelemetrySink
    {
        bool Enabled { get; }

        void Track(Action action);
    }

    /// <summary>
    /// A run-scoped boundary for deterministic random state and external side effects.
    /// Gameplay rules must use this boundary instead of process-wide GameApp services.
    /// </summary>
    public interface IRunExecutionEnvironment
    {
        RandomService Random { get; }

        MetaProgressSaveData MetaProgress { get; }

        string ProfileId { get; }

        IRunSaveSink Saves { get; }

        IRunPresentationSink Presentation { get; }

        IRunTelemetrySink Telemetry { get; }

        bool AllowsExternalSideEffects { get; }

        void Save(GameRun run);
    }

    /// <summary>Factory for production and isolated run environments.</summary>
    public static class RunExecutionEnvironment
    {
        public const string LiveProfileId = "live-profile";
        public const string AllUnlockedProfileId = "all-unlocked-v1";

        public static IRunExecutionEnvironment CreateLive(
            RandomService random,
            MetaProgressSaveData metaProgress)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            if (!random.IsInitialized)
            {
                throw new InvalidOperationException("Live run random service must be initialized first.");
            }

            return new LiveRunExecutionEnvironment(
                random,
                CloneMetaProgress(metaProgress),
                LiveProfileId);
        }

        public static IRunExecutionEnvironment CreateIsolated(
            cfg.Tables tables,
            string seedText,
            RandomSnapshot snapshot = null,
            MetaProgressSaveData metaProgress = null,
            string profileId = null)
        {
            var random = new RandomService();
            if (snapshot != null)
            {
                random.Restore(snapshot);
            }
            else
            {
                random.Init(seedText ?? string.Empty);
            }

            MetaProgressSaveData progress = metaProgress != null
                ? CloneMetaProgress(metaProgress)
                : BuildAllUnlockedMetaProgress(tables);
            return new IsolatedRunExecutionEnvironment(
                random,
                progress,
                string.IsNullOrWhiteSpace(profileId) ? AllUnlockedProfileId : profileId);
        }

        private static MetaProgressSaveData BuildAllUnlockedMetaProgress(cfg.Tables tables)
        {
            var progress = new MetaProgressSaveData();
            if (tables?.TbUnlockRule?.DataList != null)
            {
                foreach (cfg.UnlockRule rule in tables.TbUnlockRule.DataList)
                {
                    if (rule == null || !rule.Enabled || string.IsNullOrWhiteSpace(rule.TargetId))
                    {
                        continue;
                    }

                    progress.AddUnlockedTarget(rule.TargetType, rule.TargetId);
                }
            }

            progress.Normalize();
            return progress;
        }

        private static MetaProgressSaveData CloneMetaProgress(MetaProgressSaveData source)
        {
            var clone = new MetaProgressSaveData();
            if (source == null)
            {
                return clone;
            }

            clone.Version = source.Version;
            clone.UnlockedTargetKeys = source.UnlockedTargetKeys != null
                ? new List<string>(source.UnlockedTargetKeys)
                : new List<string>();
            clone.UnlockedItemIds = source.UnlockedItemIds != null
                ? new List<string>(source.UnlockedItemIds)
                : new List<string>();
            clone.CompletedRunCount = source.CompletedRunCount;
            clone.WonRunCount = source.WonRunCount;
            clone.LostRunCount = source.LostRunCount;
            clone.HighestWeekIndex = source.HighestWeekIndex;
            clone.HighestCurrentDay = source.HighestCurrentDay;
            clone.HighestRunActionStepIndex = source.HighestRunActionStepIndex;
            clone.TotalBossDefeats = source.TotalBossDefeats;
            clone.OpeningComicCompletedVersion = source.OpeningComicCompletedVersion;
            clone.DefeatedBossIds = source.DefeatedBossIds != null
                ? new List<string>(source.DefeatedBossIds)
                : new List<string>();
            clone.SeenEventIds = source.SeenEventIds != null
                ? new List<string>(source.SeenEventIds)
                : new List<string>();
            clone.Normalize();
            return clone;
        }
    }

    public sealed class LiveRunExecutionEnvironment : IRunExecutionEnvironment
    {
        internal LiveRunExecutionEnvironment(
            RandomService random,
            MetaProgressSaveData metaProgress,
            string profileId)
        {
            Random = random;
            MetaProgress = metaProgress ?? new MetaProgressSaveData();
            ProfileId = profileId ?? RunExecutionEnvironment.LiveProfileId;
        }

        public RandomService Random { get; }

        public MetaProgressSaveData MetaProgress { get; }

        public string ProfileId { get; }

        public bool AllowsExternalSideEffects => true;

        public IRunSaveSink Saves { get; } = LiveRunSaveSink.Instance;

        public IRunPresentationSink Presentation { get; } = LiveRunPresentationSink.Instance;

        public IRunTelemetrySink Telemetry { get; } = LiveRunTelemetrySink.Instance;

        public void Save(GameRun run)
        {
            Saves.Save(run);
        }
    }

    public sealed class IsolatedRunExecutionEnvironment : IRunExecutionEnvironment
    {
        internal IsolatedRunExecutionEnvironment(
            RandomService random,
            MetaProgressSaveData metaProgress,
            string profileId)
        {
            Random = random;
            MetaProgress = metaProgress ?? new MetaProgressSaveData();
            ProfileId = profileId ?? RunExecutionEnvironment.AllUnlockedProfileId;
        }

        public RandomService Random { get; }

        public MetaProgressSaveData MetaProgress { get; }

        public string ProfileId { get; }

        public bool AllowsExternalSideEffects => false;

        public IRunSaveSink Saves { get; } = NullRunSaveSink.Instance;

        public IRunPresentationSink Presentation { get; } = NullRunPresentationSink.Instance;

        public IRunTelemetrySink Telemetry { get; } = NullRunTelemetrySink.Instance;

        public void Save(GameRun run)
        {
            Saves.Save(run);
        }
    }

    internal sealed class LiveRunSaveSink : IRunSaveSink
    {
        public static readonly LiveRunSaveSink Instance = new LiveRunSaveSink();

        public bool Enabled => true;

        public void Save(GameRun run) => RunPersistence.Save(run);
    }

    internal sealed class NullRunSaveSink : IRunSaveSink
    {
        public static readonly NullRunSaveSink Instance = new NullRunSaveSink();

        public bool Enabled => false;

        public void Save(GameRun run) { }
    }

    internal sealed class LiveRunPresentationSink : IRunPresentationSink
    {
        public static readonly LiveRunPresentationSink Instance = new LiveRunPresentationSink();

        public bool Enabled => true;

        public void Present(Action action) => action?.Invoke();

        public T Read<T>(Func<T> read, T fallback = default) => read != null ? read() : fallback;
    }

    internal sealed class NullRunPresentationSink : IRunPresentationSink
    {
        public static readonly NullRunPresentationSink Instance = new NullRunPresentationSink();

        public bool Enabled => false;

        public void Present(Action action) { }

        public T Read<T>(Func<T> read, T fallback = default) => fallback;
    }

    internal sealed class LiveRunTelemetrySink : IRunTelemetrySink
    {
        public static readonly LiveRunTelemetrySink Instance = new LiveRunTelemetrySink();

        public bool Enabled => true;

        public void Track(Action action) => action?.Invoke();
    }

    internal sealed class NullRunTelemetrySink : IRunTelemetrySink
    {
        public static readonly NullRunTelemetrySink Instance = new NullRunTelemetrySink();

        public bool Enabled => false;

        public void Track(Action action) { }
    }
}
