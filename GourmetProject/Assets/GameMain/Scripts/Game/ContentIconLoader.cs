using GourmetProject.Game.Meta;
using GourmetProject.Gameplay.Model;
using UnityEngine;

namespace GourmetProject.Game
{
    /// <summary>按内容名称约定加载 Resources 图标。</summary>
    public static class ContentIconLoader
    {
        private const string DishIconRoot = "Sprites/Dishes";
        private const string ItemIconRoot = "Sprites/Items";
        private const string ItemIdPrefix = "item_";

        public static Sprite LoadDish(DishDef dish)
        {
            if (dish == null)
            {
                return null;
            }

            return LoadSprite($"{DishIconRoot}/{dish.BaseId}")
                ?? LoadSprite($"{DishIconRoot}/{dish.Id}");
        }

        public static Sprite LoadItem(ItemDefinition item)
        {
            if (item == null || string.IsNullOrEmpty(item.Id))
            {
                return null;
            }

            string resourceName = item.Id.StartsWith(ItemIdPrefix, System.StringComparison.Ordinal)
                ? item.Id.Substring(ItemIdPrefix.Length)
                : item.Id;
            return LoadSprite($"{ItemIconRoot}/{resourceName}");
        }

        private static Sprite LoadSprite(string path)
        {
            Sprite sprite = Resources.Load<Sprite>(path);
            if (sprite != null)
            {
                return sprite;
            }

            Sprite[] sprites = Resources.LoadAll<Sprite>(path);
            return sprites != null && sprites.Length > 0 ? sprites[0] : null;
        }
    }
}
