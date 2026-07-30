using System;
using System.Collections.Generic;

namespace GourmetProject.Gameplay.Battle
{
    /// <summary>
    /// 战斗菜谱中一个独立条目的当前展示状态。
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
    /// 战斗菜谱只读快照。即使条目已从待出餐列表移除，仍保留在完整菜谱中供 UI 展示。
    /// </summary>
    public sealed class BattleRecipeEntrySnapshot
    {
        public BattleRecipeEntrySnapshot(
            int entryId,
            int slotIndex,
            string dishId,
            IReadOnlyList<string> extraFlavorIds,
            IReadOnlyList<string> extraSkillIds,
            float scoreMultiplier,
            float scoreFlatBonus,
            bool skillsDisabled,
            bool excludedFromScore,
            BattleRecipeEntryStatus status)
        {
            EntryId = entryId;
            SlotIndex = slotIndex;
            DishId = dishId ?? string.Empty;
            ExtraFlavorIds = extraFlavorIds ?? Array.Empty<string>();
            ExtraSkillIds = extraSkillIds ?? Array.Empty<string>();
            ScoreMultiplier = scoreMultiplier > 0f ? scoreMultiplier : 1f;
            ScoreFlatBonus = scoreFlatBonus;
            SkillsDisabled = skillsDisabled;
            ExcludedFromScore = excludedFromScore;
            Status = status;
        }

        public int EntryId { get; }

        public int SlotIndex { get; }

        public string DishId { get; }

        public IReadOnlyList<string> ExtraFlavorIds { get; }

        public IReadOnlyList<string> ExtraSkillIds { get; }

        public float ScoreMultiplier { get; }

        public float ScoreFlatBonus { get; }

        public bool SkillsDisabled { get; }

        public bool ExcludedFromScore { get; }

        public BattleRecipeEntryStatus Status { get; }
    }
}
