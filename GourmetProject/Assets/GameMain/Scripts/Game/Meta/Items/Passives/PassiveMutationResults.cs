using System.Collections.Generic;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.Meta.Passives
{
    public sealed class RecipeMutationResult
    {
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

        public float ScoreMultiplier { get; set; } = 1f;
    }

    public sealed class CellMutationResult
    {
        public string Title { get; set; } = string.Empty;

        public List<CellMutationEntry> Entries { get; } = new List<CellMutationEntry>();

        public bool HasChanges => Entries.Count > 0;
    }

    public sealed class CellMutationEntry
    {
        public GridPos Pos { get; set; }

        public string MaterialId { get; set; } = string.Empty;
    }

    public sealed class TimelineMutationResult
    {
        public string Title { get; set; } = string.Empty;

        public List<RuntimeTimelineNodeSnapshot> Before { get; } = new List<RuntimeTimelineNodeSnapshot>();

        public List<RuntimeTimelineNodeSnapshot> After { get; } = new List<RuntimeTimelineNodeSnapshot>();

        public bool Changed { get; set; }
    }

    public sealed class RuntimeTimelineNodeSnapshot
    {
        public string Id { get; set; } = string.Empty;

        public int Day { get; set; }

        public string ActionId { get; set; } = string.Empty;
    }
}
