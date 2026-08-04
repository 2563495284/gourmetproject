using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Scoring;

namespace GourmetProject.Game.Balance
{
    [Serializable]
    public sealed class BalanceSampleResult
    {
        public int Seed;
        public bool IsValid;
        public string FailureReason = string.Empty;
        public int TotalScore;
        public int EffectiveRequiredScore;
        public float GoldDelta;
        public List<BalanceDishContribution> Dishes = new List<BalanceDishContribution>();
        [NonSerialized] public ScoreResult ScoreResult;
    }

    [Serializable]
    public sealed class BalanceDishContribution
    {
        public string DishId = string.Empty;
        public float Score;
    }

    [Serializable]
    public sealed class BalanceComponentContribution
    {
        public string ComponentId = string.Empty;
        public float MeanDelta;
        public float MeanRatio;
    }

    [Serializable]
    public sealed class BalanceStatistics
    {
        public string ScenarioName = string.Empty;
        public string CheckpointName = string.Empty;
        public int Week;
        public float Day;
        public int SampleCount;
        public int ValidCount;
        public float ValidRate;
        public float Mean;
        public float StandardDeviation;
        public float CoefficientOfVariation;
        public float P10;
        public float P25;
        public float P50;
        public float P75;
        public float P90;
        public float Max;
        public int CurrentRequiredScore;
        public float CurrentPassRate;
        public int SuggestedNormal;
        public int SuggestedHard;
        public int SuggestedChallenge;
        public List<string> Warnings = new List<string>();
        public List<BalanceComponentContribution> ComponentContributions = new List<BalanceComponentContribution>();
    }
}
