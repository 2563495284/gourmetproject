using System.Collections.Generic;

namespace GourmetProject.Game.Run
{
    /// <summary>
    /// 菜谱一格条目：dishId + 玩家用「调味小票」永久附加的额外风味。
    /// 额外风味绑定在条目对象上，随移动/删除一起走，避免与 dishId 列表错位。
    /// </summary>
    public sealed class RecipeBookSlot
    {
        private readonly List<string> _extraFlavorIds = new List<string>();

        public RecipeBookSlot(string dishId)
        {
            DishId = dishId ?? string.Empty;
        }

        public string DishId { get; }

        /// <summary>玩家永久附加的额外风味 id（可叠加，与菜谱变体自带风味叠加）。</summary>
        public IReadOnlyList<string> ExtraFlavorIds => _extraFlavorIds;

        public bool HasExtraFlavors => _extraFlavorIds.Count > 0;

        public void AddFlavor(string flavorId)
        {
            if (!string.IsNullOrEmpty(flavorId))
            {
                _extraFlavorIds.Add(flavorId);
            }
        }

        public RecipeBookSlot Clone()
        {
            var copy = new RecipeBookSlot(DishId);
            copy._extraFlavorIds.AddRange(_extraFlavorIds);
            return copy;
        }
    }
}
