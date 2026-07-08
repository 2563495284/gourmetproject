using GourmetProject.Gameplay.Model;
using UnityEngine;

namespace GourmetProject.Game
{
    /// <summary>按内容名称约定加载 Resources 图标。</summary>
    public static class ContentIconLoader
    {
        private const string DishIconRoot = "Sprites/Dishes";
        private const string ItemIconRoot = "Sprites/Items";

        public static Sprite LoadDish(DishDef dish)
        {
            return dish == null || string.IsNullOrEmpty(dish.Name)
                ? null
                : Resources.Load<Sprite>($"{DishIconRoot}/{dish.Name}");
        }

        public static Sprite LoadItem(cfg.Item item)
        {
            return item == null || string.IsNullOrEmpty(item.Name)
                ? null
                : Resources.Load<Sprite>($"{ItemIconRoot}/{item.Name}");
        }
    }
}
