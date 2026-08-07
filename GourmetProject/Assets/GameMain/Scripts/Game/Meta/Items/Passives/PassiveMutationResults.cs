using System.Collections.Generic;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.Meta.Passives
{
    public sealed class RecipeMutationResult
    {
        public string SourceItemId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public List<RecipeMutationEntry> Entries { get; } = new List<RecipeMutationEntry>();

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

        public float ScoreFlatBonus { get; set; }

        public float ScoreMultiplier { get; set; } = 1f;
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
