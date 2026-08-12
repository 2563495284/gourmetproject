using System.Collections.Generic;
using BreakInfinity;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.Meta.Passives
{
    public sealed class RecipeMutationResult
    {
        public string SourceItemId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public List<RecipeMutationEntry> Entries { get; } = new List<RecipeMutationEntry>();

        /// <summary>涉及增删导致索引整体移动时使用的完整食谱前后快照。</summary>
        public List<RecipeDishSnapshot> BeforeRecipe { get; } = new List<RecipeDishSnapshot>();

        public List<RecipeDishSnapshot> AfterRecipe { get; } = new List<RecipeDishSnapshot>();

        public bool HasChanges => Entries.Count > 0;
    }

    public sealed class RecipeMutationEntry
    {
        public int BookIndex { get; set; }

        public int DishIndex { get; set; }

        public RecipeDishSnapshot Before { get; set; }

        public RecipeDishSnapshot After { get; set; }
    }

    public sealed class RecipeDishSnapshot
    {
        public string DishId { get; set; } = string.Empty;

        public IReadOnlyList<string> FlavorIds { get; set; } = System.Array.Empty<string>();

        public IReadOnlyList<string> SkillIds { get; set; } = System.Array.Empty<string>();

        public BigDouble ScoreFlatBonus { get; set; }

        public BigDouble ScoreMultiplier { get; set; } = BigDouble.One;
    }

    public sealed class CellMutationResult
    {
        public string SourceItemId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public List<CellMutationEntry> Entries { get; } = new List<CellMutationEntry>();

        public bool HasChanges => Entries.Count > 0;
    }

    public sealed class CellMutationEntry
    {
        public GridPos Pos { get; set; }

        public string MaterialId { get; set; } = string.Empty;

        public IReadOnlyList<string> BeforeMaterialIds { get; set; } = System.Array.Empty<string>();

        public IReadOnlyList<string> AfterMaterialIds { get; set; } = System.Array.Empty<string>();
    }

    public enum TimelineMutationCause
    {
        Unknown,
        Add,
        Replace,
        Move,
        Remove,
        Skip,
        Resize,
        WeekChange,
    }

    public sealed class TimelineMutationResult
    {
        public string Title { get; set; } = string.Empty;

        public TimelineMutationCause Cause { get; set; }

        public string TargetNodeId { get; set; } = string.Empty;

        public List<RuntimeTimelineNodeSnapshot> Before { get; } = new List<RuntimeTimelineNodeSnapshot>();

        public List<RuntimeTimelineNodeSnapshot> After { get; } = new List<RuntimeTimelineNodeSnapshot>();

        public float BeforeLengthDays { get; set; }

        public float AfterLengthDays { get; set; }

        public int BeforeWeekIndex { get; set; }

        public int AfterWeekIndex { get; set; }

        public bool Changed { get; set; }
    }

    public sealed class RuntimeTimelineNodeSnapshot
    {
        public string Id { get; set; } = string.Empty;

        public int Day { get; set; }

        public string ActionId { get; set; } = string.Empty;
    }
}
