using System;
using System.Collections.Generic;
using BreakInfinity;

namespace GourmetProject.Gameplay.Battle
{
    /// <summary>
    /// 经营挑战食谱中一个独立条目的当前展示状态。
    /// 每个条目在任意时刻只会处于其中一种状态。
    /// </summary>
    public enum BattleRecipeEntryStatus
    {
        Normal,
        CannotPlace,
        WaitingForPlacement,
        Served,
        Discarded,
        Removed,
    }

    /// <summary>
    /// 经营挑战食谱只读快照。即使条目已从待出菜列表移除，仍保留在完整食谱中供 UI 展示。
    /// </summary>
    public sealed class BattleRecipeEntrySnapshot
    {
        public BattleRecipeEntrySnapshot(
            int entryId,
            int slotIndex,
            string dishId,
            IReadOnlyList<string> extraFlavorIds,
            IReadOnlyList<string> extraSkillIds,
            BigDouble scoreMultiplier,
            BigDouble scoreFlatBonus,
            bool skillsDisabled,
            bool excludedFromScore,
            bool isTemporaryCopy,
            BattleRecipeEntryStatus status)
        {
            EntryId = entryId;
            SlotIndex = slotIndex;
            DishId = dishId ?? string.Empty;
            ExtraFlavorIds = extraFlavorIds ?? Array.Empty<string>();
            ExtraSkillIds = extraSkillIds ?? Array.Empty<string>();
            ScoreMultiplier = scoreMultiplier > BigDouble.Zero ? scoreMultiplier : BigDouble.One;
            ScoreFlatBonus = scoreFlatBonus;
            SkillsDisabled = skillsDisabled;
            ExcludedFromScore = excludedFromScore;
            IsTemporaryCopy = isTemporaryCopy;
            Status = status;
        }

        public int EntryId { get; }

        public int SlotIndex { get; }

        public string DishId { get; }

        public IReadOnlyList<string> ExtraFlavorIds { get; }

        public IReadOnlyList<string> ExtraSkillIds { get; }

        public BigDouble ScoreMultiplier { get; }

        public BigDouble ScoreFlatBonus { get; }

        public bool SkillsDisabled { get; }

        public bool ExcludedFromScore { get; }

        /// <summary>是否为本场经营挑战临时复制到食谱中的条目。</summary>
        public bool IsTemporaryCopy { get; }

        public BattleRecipeEntryStatus Status { get; }
    }
}
