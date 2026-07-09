using System.Collections.Generic;
using System.Linq;

namespace GourmetProject.Gameplay.Battle
{
    /// <summary>
    /// 一个「菜谱」槽位（image1 的菜谱1/菜谱2）：持有一组待上菜的菜品 id，点击「上菜」时从中随机取出一道。
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

    /// <summary>菜谱槽内的一条具体食物记录，可被 Boss Debuff 标记后随上菜传给实例。</summary>
    public sealed class RecipeSlotEntry
    {
        public RecipeSlotEntry(string dishId)
        {
            DishId = dishId ?? string.Empty;
        }

        public string DishId { get; }

        public bool DisableSkills { get; private set; }

        public bool ExcludeFromScore { get; private set; }

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
            var clone = new RecipeSlotEntry(DishId);
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
}
