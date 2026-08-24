using System;
using System.Collections.Generic;
using BreakInfinity;
using UnityEngine;

namespace GourmetProject.Game.Balance
{
    public enum AutoPlayerLevel { Normal, Expert }
    public enum BuildArchetype { SweetTransfer, Count, Cake }
    public enum MetaRoute { Normal, Event, Interest, Shop }
    public enum AutoActionRewardPriority
    {
        Balanced,
        FragmentChoice,
        PassiveItemChoice,
        ActiveItemStrengthen,
        ActiveItemAdjust,
        Gold,
    }

    /// <summary>自动局的结构化终止原因；与面向人的 <see cref="AutoRunTrace.FailureReason"/> 分离。</summary>
    public enum AutoRunTerminationKind
    {
        None,
        Completed,
        HeartsDepleted,
        GameOver,
        Cancelled,
        NoLegalPlacement,
        PlacementBudgetExhausted,
        ActionLimitReached,
        UnsupportedMechanic,
        InfrastructureError,
    }

    /// <summary>不一定让样本失效、但必须由报告显式披露的结构化告警。</summary>
    public enum AutoRunWarningKind
    {
        None,
        PlacementCandidateLimitApplied,
        PlacementBudgetExhausted,
        NoLegalPlacement,
        UnsupportedMechanic,
    }

    public enum AutoPlacementTerminationKind
    {
        None,
        Completed,
        InvalidSession,
        MissingPolicyRandom,
        PrepareFailed,
        NoLegalPlacement,
        NodeBudgetExhausted,
        CommitFailed,
    }

    public enum AutoPlacementWarningKind
    {
        CandidateLimitApplied,
        NodeBudgetExhausted,
    }

    public enum PlacementSelectionKind
    {
        RankSoftmax,
        BestScore,
    }

    [Serializable]
    public sealed class AutoRunWarning
    {
        public AutoRunWarningKind Kind;
        public string Code = string.Empty;
        public string Message = string.Empty;
        public int Week;
    }

    [Serializable]
    public sealed class AutoPlacementWarning
    {
        public AutoPlacementWarningKind Kind;
        public int ServeIndex;
        public int LegalPlacementCount;
        public int EvaluatedPlacementCount;
        public string Message = string.Empty;
    }

    /// <summary>一次自动摆盘决定的可复现审计记录。</summary>
    [Serializable]
    public sealed class PlacementDecisionTrace
    {
        public int ServeIndex;
        public string DishId = string.Empty;
        public AutoPlayerLevel PlayerLevel;
        public PlacementSelectionKind SelectionKind;
        public float RankSoftmaxTemperature;
        public int LegalPlacementCount;
        public int CandidateLimit;
        public int EvaluatedPlacementCount;
        public int SelectedCandidateIndex = -1;
        public int SelectedRank = -1;
        public int RotationIndex;
        public int OriginX;
        public int OriginY;
        public BigDouble PreviewScore;
        public BigDouble PlanningScore;
        public double DirectionalFuturePotential;
        public int FutureOpenSpace;
        public int EmptyRegionCount;
        public string SelectionReason = string.Empty;
        public bool CandidateLimitApplied;
        public int SearchNodesAfterDecision;
    }

    /// <summary>
    /// 一次正式战斗的无损审计记录。周级 Score/BossScore 字段仅为旧报告兼容；
    /// 新报告必须按 EncounterKey + Day 使用本列表，禁止把同周多个 Boss 混为一个样本。
    /// </summary>
    [Serializable]
    public sealed class AutoRunBattleTrace
    {
        public string BattleKey = string.Empty;
        public string EncounterKey = string.Empty;
        public int Week;
        public float Day;
        public string SourceKey = string.Empty;
        public string ActionId = string.Empty;
        public string FoodId = string.Empty;
        public bool IsBoss;
        public string BossId = string.Empty;
        public string BossDebuffId = string.Empty;
        public int RequiredScore;
        public BigDouble Score;
        public bool TargetHit;
        public int HeartsBefore;
        public int HeartsAfter;
        public int HeartsLost;
        public bool Survived;
        public bool TerminalDeath;
        public bool UndyingPrevented;
        public int PlacementDecisionStart;
        public int PlacementDecisionCount;
        public int SolverNodes;
        public bool SolverTruncated;
        public bool NoLegalPlacement;
    }

    /// <summary>一次正式行动候选展示及自动玩家选择的结构化审计记录。</summary>
    [Serializable]
    public sealed class AutoRunActionDecisionTrace
    {
        public int Week;
        public float Day;
        public int RunStepIndex;
        public string OfferKey = string.Empty;
        public int Revision;
        public List<string> CandidateActionIds = new List<string>();
        public List<cfg.RewardKind> CandidateRewardKinds = new List<cfg.RewardKind>();
        public List<float> CandidateCosts = new List<float>();
        public List<float> CandidatePolicyWeights = new List<float>();
        public string SelectedActionId = string.Empty;
        public cfg.RewardKind SelectedRewardKind;
        public int SelectedIndex = -1;
        public string SelectionReason = string.Empty;
        public bool Rerolled;
    }

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
        public AutoActionRewardPriority ActionRewardPriority = AutoActionRewardPriority.Balanced;

        public int PlacementCandidateLimit(AutoPlayerLevel level)
        {
            int configured = level == AutoPlayerLevel.Expert ? ExpertBeamWidth : NormalBeamWidth;
            int maximum = level == AutoPlayerLevel.Expert ? 64 : 24;
            return Mathf.Clamp(configured, 1, maximum);
        }

        [Obsolete("Use PlacementCandidateLimit; placement is bounded candidate evaluation, not a search beam.")]
        public int BeamWidth(AutoPlayerLevel level) => PlacementCandidateLimit(level);
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

    /// <summary>规则随机与自动玩家决策随机分离时使用的稳定 seed 派生。</summary>
    public static class AutoRunPolicySeed
    {
        public const string Version = "bounded-policy-v6";

        public static ulong Derive(int seed, AutoPlayerLevel level, string scope)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                string text = $"{Version}|{seed}|{level}|{scope ?? string.Empty}";
                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= 1099511628211UL;
                }

                return hash;
            }
        }
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
        public AutoRunTerminationKind Termination;
        public List<AutoRunWarning> Warnings = new List<AutoRunWarning>();
        public List<PlacementDecisionTrace> PlacementDecisions = new List<PlacementDecisionTrace>();
        public List<AutoRunBattleTrace> Battles = new List<AutoRunBattleTrace>();
        public List<AutoRunActionDecisionTrace> ActionDecisions = new List<AutoRunActionDecisionTrace>();
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
        public AutoRunTerminationKind Termination;
        public List<AutoRunWarning> Warnings = new List<AutoRunWarning>();
        public int ArchetypeChanges;
        public List<AutoRunStageTrace> Stages = new List<AutoRunStageTrace>();
    }
}
