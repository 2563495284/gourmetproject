using System;
using System.Collections.Generic;
using BreakInfinity;
using UnityEngine;

namespace GourmetProject.Game.Balance
{
    public enum AutoPlayerLevel { Normal, Expert }
    public enum BuildArchetype { SweetTransfer, Count, Cake }
    public enum MetaRoute { Normal, Event, Interest, Shop }

    [Serializable]
    public sealed class AutoPlayerPolicy
    {
        [Range(0.1f, 5f)] public float SoftmaxTemperature = 1f;
        [Range(1, 128)] public int NormalBeamWidth = 24;
        [Range(1, 128)] public int ExpertBeamWidth = 64;
        [Min(100)] public int PlacementNodeBudget = 8000;
        [Range(0f, 1f)] public float DualArchetypeThreshold = 0.15f;
        [Min(0)] public int InterestReserve = 50;
        public int MaxActionsPerWeek = 32;

        public int BeamWidth(AutoPlayerLevel level) => level == AutoPlayerLevel.Expert ? ExpertBeamWidth : NormalBeamWidth;
    }

    [Serializable]
    public sealed class AutoRunRequest
    {
        public string CharacterId;
        public AutoPlayerLevel PlayerLevel;
        public int Seed = 1001;
        public AutoPlayerPolicy Policy = new AutoPlayerPolicy();
        public MetaAffinityCatalog MetaAffinity;
    }

    [Serializable]
    public sealed class ArchetypeScore
    {
        public BuildArchetype Archetype;
        public float Score;
    }

    [Serializable]
    public sealed class ArchetypeClassification
    {
        public List<ArchetypeScore> Scores = new List<ArchetypeScore>();
        public List<BuildArchetype> Active = new List<BuildArchetype>();
        public BuildArchetype Primary;
    }

    [Serializable]
    public sealed class AutoRunStageTrace
    {
        public int Week;
        public BigDouble Score;
        public int RequiredScore;
        public bool Passed;
        public int MealBattles;
        public int MealPasses;
        public BigDouble BossScore;
        public int BossRequiredScore;
        public bool BossReached;
        public bool BossPassed;
        public int GoldEarned;
        public int GoldSpent;
        public int GoldBalance;
        public BuildArchetype Archetype;
        public MetaRoute MetaRoute;
        public bool ArchetypeChanged;
        public bool SolverTruncated;
        public bool NoLegalPlacement;
        public int SolverNodes;
        public List<string> Actions = new List<string>();
        public List<string> Rewards = new List<string>();
        public List<string> Purchases = new List<string>();
        public List<string> ActiveItemsUsed = new List<string>();
    }

    [Serializable]
    public sealed class AutoRunTrace
    {
        public int Seed;
        public string CharacterId;
        public AutoPlayerLevel PlayerLevel;
        public bool Completed;
        public string FailureReason;
        public int ArchetypeChanges;
        public List<AutoRunStageTrace> Stages = new List<AutoRunStageTrace>();
    }
}
