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
            Sprite sprite = LoadSprite($"{ItemIconRoot}/{resourceName}");
            if (sprite != null)
            {
                return sprite;
            }

            string alias = AliasFor(item.Id);
            return string.IsNullOrEmpty(alias) ? null : LoadSprite($"{ItemIconRoot}/{alias}");
        }

        private static string AliasFor(string itemId)
        {
            switch (itemId)
            {
                case "item_shop_restock_active":
                case "item_shop_restock_passive":
                    return "shop_restock";
                case "item_skip_reward_node":
                    return "skip_node";
                case "item_double_daily_cost_repeat_node":
                    return "extra_day";
                case "item_timeline_stop_chance":
                    return "timeline_random";
                default:
                    return string.Empty;
            }
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
