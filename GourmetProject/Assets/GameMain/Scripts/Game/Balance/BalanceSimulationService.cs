using System;
using System.Collections.Generic;
using BreakInfinity;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Scoring;

namespace GourmetProject.Game.Balance
{
    public sealed class BalanceSimulationService
    {
        private readonly cfg.Tables _tables;
        private readonly GameplayDatabase _database;

        public BalanceSimulationService(cfg.Tables tables, GameplayDatabase database)
        {
            _tables = tables ?? throw new ArgumentNullException(nameof(tables));
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        public BalanceSampleResult RunSample(BuildCheckpoint checkpoint, int seed, string excludedComponentId = null)
        {
            var sample = new BalanceSampleResult { Seed = seed };
            try
            {
                BalanceRuntime runtime = BuildCheckpointRuntimeFactory.Create(_tables, _database, checkpoint, seed, excludedComponentId);
                var random = new Xoshiro256SS(unchecked((ulong)(uint)seed ^ 0x9E3779B97F4A7C15UL));
                int requiredScore = checkpoint.UseConfiguredRequiredScore ? runtime.Run.RequiredScore : Math.Max(1, checkpoint.RequiredScore);
                var session = BattleSessionFactory.BuildBalancePreview(runtime.Run, runtime.Board, requiredScore, checkpoint.BossDebuffId, random, checkpoint.HappyCakeLayers);
                ScoreResult score = session.PreviewScore(captureDiagnostics: false);
                sample.IsValid = true;
                sample.TotalScore = score.Total;
                sample.EffectiveRequiredScore = requiredScore;
                sample.GoldDelta = score.GoldDelta;
                sample.ScoreResult = score;
                foreach (DishScore dish in score.DishScores)
                    sample.Dishes.Add(new BalanceDishContribution { DishId = dish.DishId, Score = dish.Contribution });
            }
            catch (Exception e)
            {
                sample.IsValid = false;
                sample.FailureReason = e.Message;
            }
            return sample;
        }

        public List<BalanceSampleResult> Run(BuildCheckpoint checkpoint, int sampleCount, int baseSeed, Func<bool> shouldCancel = null, Action<int, int> progress = null)
        {
            var results = new List<BalanceSampleResult>(Math.Max(0, sampleCount));
            for (int i = 0; i < sampleCount; i++)
            {
                if (shouldCancel?.Invoke() == true) break;
                results.Add(RunSample(checkpoint, unchecked(baseSeed + i)));
                progress?.Invoke(i + 1, sampleCount);
            }
            return results;
        }

        public List<BalanceComponentContribution> CalculateComponentContributions(BuildCheckpoint checkpoint, IReadOnlyList<BalanceSampleResult> baseline, int maxSamples = 1000)
        {
            var result = new List<BalanceComponentContribution>();
            if (baseline == null || baseline.Count == 0) return result;
            BalanceRuntime template = BuildCheckpointRuntimeFactory.Create(_tables, _database, checkpoint, baseline[0].Seed);
            foreach (string componentId in template.ComponentIds)
            {
                BigDouble delta = BigDouble.Zero;
                double ratio = 0d;
                int count = 0;
                int stride = Math.Max(1, baseline.Count / Math.Max(1, maxSamples));
                for (int sampleIndex = 0; sampleIndex < baseline.Count && count < maxSamples; sampleIndex += stride)
                {
                    BalanceSampleResult original = baseline[sampleIndex];
                    if (!original.IsValid) continue;
                    BalanceSampleResult removed = RunSample(checkpoint, original.Seed, componentId);
                    if (!removed.IsValid) continue;
                    BigDouble d = original.TotalScore - removed.TotalScore;
                    delta += d;
                    ratio += original.TotalScore != 0 ? (d / original.TotalScore).ToDouble() : 0d;
                    count++;
                }
                if (count > 0)
                    result.Add(new BalanceComponentContribution { ComponentId = componentId, MeanDelta = delta / count, MeanRatio = (float)(ratio / count) });
            }
            return result;
        }
    }
}
