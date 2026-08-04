using System;
using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.Balance
{
    [CreateAssetMenu(fileName = "BalanceScenario", menuName = "Gourmet/Balance Scenario")]
    public sealed class BalanceScenario : ScriptableObject
    {
        public string BuildName = "新 Build";
        public List<BuildCheckpoint> Checkpoints = new List<BuildCheckpoint>();
    }

    [Serializable]
    public sealed class BuildCheckpoint
    {
        public string Name = "阶段";
        public string CharacterId = string.Empty;
        [Min(1)] public int Week = 1;
        [Min(0f)] public float Day;
        [Min(0)] public int Gold;
        public bool UseConfiguredRequiredScore = true;
        [Min(1)] public int RequiredScore = 1;
        public string BossDebuffId = string.Empty;
        [Min(0)] public int HappyCakeLayers;
        public List<BalanceItemEntry> Items = new List<BalanceItemEntry>();
        public List<string> TableFragmentIds = new List<string>();
        public List<BalanceFragmentPlacement> FragmentPlacements = new List<BalanceFragmentPlacement>();
        public List<BalanceCellMaterial> CellMaterials = new List<BalanceCellMaterial>();
        public List<BuildReplayStep> Dishes = new List<BuildReplayStep>();
        public BalancePerturbation Perturbation = new BalancePerturbation();
    }

    [Serializable]
    public sealed class BalanceItemEntry
    {
        public string ItemId = string.Empty;
        [Min(1)] public int Level = 1;
        public bool AnalyzeContribution;
    }

    [Serializable]
    public sealed class BuildReplayStep
    {
        public string DishId = string.Empty;
        public int X;
        public int Y;
        [Range(0, 3)] public int Rotation;
        public List<string> ExtraSkillIds = new List<string>();
        public List<string> ExtraFlavorIds = new List<string>();
        public float PermanentFlat;
        [Min(0.0001f)] public float PermanentMultiplier = 1f;
        public bool AnalyzeContribution;
        [Range(0f, 1f)] public float RetainProbability = 1f;
        public List<WeightedDishReplacement> Replacements = new List<WeightedDishReplacement>();
    }

    [Serializable]
    public sealed class WeightedDishReplacement
    {
        public string DishId = string.Empty;
        [Min(0f)] public float Weight = 1f;
    }

    [Serializable]
    public sealed class BalancePerturbation
    {
        public bool Enabled;
        [Min(0f)] public float PermanentFlatMinFactor = 1f;
        [Min(0f)] public float PermanentFlatMaxFactor = 1f;
        [Min(0.0001f)] public float PermanentMultiplierMinFactor = 1f;
        [Min(0.0001f)] public float PermanentMultiplierMaxFactor = 1f;
    }

    [Serializable]
    public sealed class BalanceFragmentPlacement
    {
        public string FragmentId = string.Empty;
        [Range(0, 3)] public int Rotation;
        public int X;
        public int Y;
    }

    [Serializable]
    public sealed class BalanceCellMaterial
    {
        public int X;
        public int Y;
        public string MaterialId = string.Empty;
    }
}
