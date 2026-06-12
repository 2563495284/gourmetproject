using System.Collections.Generic;

namespace GourmetProject.Gameplay.Battle
{
    /// <summary>
    /// 一个「菜谱」槽位（image1 的菜谱1/菜谱2）：持有一组待上菜的菜品 id，点击「上菜」时从中随机取出一道。
    /// </summary>
    public sealed class RecipeSlot
    {
        private readonly List<string> _remaining;

        public RecipeSlot(string id, IEnumerable<string> dishIds)
        {
            Id = id;
            _remaining = new List<string>(dishIds);
        }

        public string Id { get; }

        public IReadOnlyList<string> Remaining => _remaining;

        public int Count => _remaining.Count;

        public bool IsEmpty => _remaining.Count == 0;

        public string RemoveAt(int index)
        {
            string dishId = _remaining[index];
            _remaining.RemoveAt(index);
            return dishId;
        }
    }
}
