using System.Collections.Generic;
using BreakInfinity;
using System.Linq;

namespace GourmetProject.Gameplay.Battle
{
    /// <summary>
    /// 一个「食谱」槽位（image1 的食谱1/食谱2）：持有一组待上菜的食物 id，点击「上菜」时从中随机取出一道。
    /// </summary>
    public sealed class RecipeSlot
    {
        private readonly List<RecipeSlotEntry> _entries;

        public RecipeSlot(string id, IEnumerable<string> dishIds)
        {
            Id = id;
            _entries = new List<RecipeSlotEntry>();
            if (dishIds == null)
            {
                return;
            }

            foreach (string dishId in dishIds)
            {
                _entries.Add(new RecipeSlotEntry(dishId));
            }
        }

        public RecipeSlot(string id, IEnumerable<RecipeSlotEntry> entries)
        {
            Id = id;
            _entries = new List<RecipeSlotEntry>();
            if (entries == null)
            {
                return;
            }

            foreach (RecipeSlotEntry entry in entries)
            {
                AddEntry(entry);
            }
        }

        public string Id { get; }

        public IReadOnlyList<string> Remaining => _entries.Select(e => e.DishId).ToList();

        public IReadOnlyList<RecipeSlotEntry> Entries => _entries;

        public int Count => _entries.Count;

        public bool IsEmpty => _entries.Count == 0;

        public string RemoveAt(int index)
        {
            string dishId = RemoveEntryAt(index).DishId;
            return dishId;
        }

        public RecipeSlotEntry RemoveEntryAt(int index)
        {
            RecipeSlotEntry entry = _entries[index];
            _entries.RemoveAt(index);
            return entry;
        }

        public void AddEntry(RecipeSlotEntry entry)
        {
            if (entry != null && !string.IsNullOrEmpty(entry.DishId))
            {
                _entries.Add(entry);
            }
        }

        public void ReplaceEntries(IEnumerable<RecipeSlotEntry> entries)
        {
            _entries.Clear();
            if (entries == null)
            {
                return;
            }

            foreach (RecipeSlotEntry entry in entries)
            {
                AddEntry(entry?.Clone());
            }
        }
    }

    /// <summary>食谱槽内的一条具体食物记录，可被 Boss Debuff 标记后随上菜传给实例。</summary>
    public sealed class RecipeSlotEntry
    {
        private readonly List<string> _extraFlavorIds;
        private readonly List<string> _extraSkillIds;

        public RecipeSlotEntry(string dishId)
            : this(dishId, null, null, 1f, 0f)
        {
        }

        public RecipeSlotEntry(string dishId, IEnumerable<string> extraFlavorIds)
            : this(dishId, extraFlavorIds, null, 1f, 0f)
        {
        }

        public RecipeSlotEntry(
            string dishId,
            IEnumerable<string> extraFlavorIds,
            IEnumerable<string> extraSkillIds,
            BigDouble scoreMultiplier,
            BigDouble scoreFlatBonus = default,
            int sourceBookIndex = -1,
            int sourceDishIndex = -1)
        {
            DishId = dishId ?? string.Empty;
            _extraFlavorIds = extraFlavorIds != null ? new List<string>(extraFlavorIds) : new List<string>();
            _extraSkillIds = extraSkillIds != null ? new List<string>(extraSkillIds) : new List<string>();
            ScoreMultiplier = scoreMultiplier > BigDouble.Zero ? scoreMultiplier : BigDouble.One;
            ScoreFlatBonus = scoreFlatBonus;
            SourceBookIndex = sourceBookIndex;
            SourceDishIndex = sourceDishIndex;
        }

        public string DishId { get; }

        public bool DisableSkills { get; private set; }

        public bool ExcludeFromScore { get; private set; }

        /// <summary>玩家用调味小票为该食谱条目永久附加的额外风味（上菜时与变体自带风味合并）。</summary>
        public IReadOnlyList<string> ExtraFlavorIds => _extraFlavorIds;

        public IReadOnlyList<string> ExtraSkillIds => _extraSkillIds;

        public BigDouble ScoreMultiplier { get; }

        public BigDouble ScoreFlatBonus { get; }

        public int SourceBookIndex { get; }

        public int SourceDishIndex { get; }

        public void MarkSkillsDisabled()
        {
            DisableSkills = true;
        }

        public void MarkExcludedFromScore()
        {
            ExcludeFromScore = true;
        }

        public RecipeSlotEntry Clone()
        {
            var clone = new RecipeSlotEntry(
                DishId,
                _extraFlavorIds,
                _extraSkillIds,
                ScoreMultiplier,
                ScoreFlatBonus,
                SourceBookIndex,
                SourceDishIndex);
            if (DisableSkills)
            {
                clone.MarkSkillsDisabled();
            }

            if (ExcludeFromScore)
            {
                clone.MarkExcludedFromScore();
            }

            return clone;
        }
    }

    public readonly struct RecipeScoreFlatDelta
    {
        public RecipeScoreFlatDelta(int bookIndex, int dishIndex, BigDouble delta)
        {
            BookIndex = bookIndex;
            DishIndex = dishIndex;
            Delta = delta;
        }

        public int BookIndex { get; }

        public int DishIndex { get; }

        public BigDouble Delta { get; }
    }

    public readonly struct RecipeScoreMultiplierDelta
    {
        public RecipeScoreMultiplierDelta(int bookIndex, int dishIndex, BigDouble multiplier)
        {
            BookIndex = bookIndex;
            DishIndex = dishIndex;
            Multiplier = multiplier;
        }

        public int BookIndex { get; }

        public int DishIndex { get; }

        public BigDouble Multiplier { get; }
    }
}
