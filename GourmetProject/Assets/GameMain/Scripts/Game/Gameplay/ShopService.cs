using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Gameplay
{
    /// <summary>商店一件商品的归一化描述（被动道具 / 菜品 / 胃部碎片）。</summary>
    public enum ShopEntryKind
    {
        PassiveItem,
        Dish,
        Fragment,
    }

    public sealed class ShopEntry
    {
        public ShopEntry(ShopEntryKind kind, string id, string name, string desc, int price)
        {
            Kind = kind;
            Id = id;
            Name = name;
            Desc = desc;
            Price = price;
        }

        public ShopEntryKind Kind { get; }
        public string Id { get; }
        public string Name { get; }
        public string Desc { get; }
        public int Price { get; }
    }

    /// <summary>
    /// 商店服务：根据当前进度「隐藏分」刷新商品（被动道具 + 菜品 + 胃部碎片），并处理购买、出售、删菜。
    /// 隐藏分来自 <see cref="GameRun.RewardHiddenScore"/>，与奖励系统共用同一尺度。
    /// </summary>
    public static class ShopService
    {
        public const int PassiveItemPrice = 45;
        public const int PassiveSellPrice = 20;
        public const int DeleteDishCost = 15;

        private const int PassiveCount = 2;
        private const int DishCount = 2;
        private const int FragmentCount = 1;

        /// <summary>按隐藏分刷新一批商品。</summary>
        public static List<ShopEntry> RollStock(cfg.Tables tables, GameRun run, IRandomStream rng)
        {
            var stock = new List<ShopEntry>();
            int hidden = run.RewardHiddenScore;

            foreach (string itemId in ItemPoolService.Roll(tables, run, cfg.ItemKind.Passive, rng, PassiveCount))
            {
                cfg.Item item = tables.TbItem.GetOrDefault(itemId);
                if (item != null)
                {
                    stock.Add(new ShopEntry(ShopEntryKind.PassiveItem, item.Id, item.Name, item.Desc, PassiveItemPrice));
                }
            }

            foreach (cfg.DishVariant variant in RollDishVariants(tables, run, hidden, rng, DishCount))
            {
                cfg.DishBase baseDish = tables.TbDishBase.GetOrDefault(variant.BaseId);
                string name = baseDish != null ? baseDish.Name : variant.Id;
                int price = variant.Price > 0 ? variant.Price : 30;
                stock.Add(new ShopEntry(ShopEntryKind.Dish, variant.Id, name, "加入菜谱池的菜品", price));
            }

            foreach (cfg.StomachFragment fragment in RollFragments(tables, run, hidden, rng, FragmentCount))
            {
                int price = fragment.Price > 0 ? fragment.Price : 40;
                stock.Add(new ShopEntry(ShopEntryKind.Fragment, fragment.Id, "胃部碎片", "扩展胃部棋盘", price));
            }

            return stock;
        }

        /// <summary>购买一件商品：扣金并结算到运行状态。返回是否成功。</summary>
        public static bool Purchase(GameRun run, ShopEntry entry)
        {
            if (run == null || entry == null || run.Gold < entry.Price)
            {
                return false;
            }

            bool applied;
            switch (entry.Kind)
            {
                case ShopEntryKind.PassiveItem:
                    run.AcquireItem(entry.Id, 0);
                    applied = true;
                    break;
                case ShopEntryKind.Dish:
                    applied = run.AddBonusDish(entry.Id);
                    break;
                case ShopEntryKind.Fragment:
                    applied = run.AddStomachFragment(entry.Id);
                    break;
                default:
                    applied = false;
                    break;
            }

            if (!applied)
            {
                return false;
            }

            run.Gold -= entry.Price;
            return true;
        }

        /// <summary>出售一件被动道具，回血金币。</summary>
        public static bool SellPassive(GameRun run, string itemId)
        {
            if (run == null || !run.RemoveItem(itemId))
            {
                return false;
            }

            run.Gold += PassiveSellPrice;
            return true;
        }

        /// <summary>删除菜谱池中的一道菜，花费金币。</summary>
        public static bool DeleteDish(GameRun run, string dishId)
        {
            if (run == null || run.Gold < DeleteDishCost || !run.RemoveBonusDish(dishId))
            {
                return false;
            }

            run.Gold -= DeleteDishCost;
            return true;
        }

        private static List<cfg.DishVariant> RollDishVariants(cfg.Tables tables, GameRun run, int hidden, IRandomStream rng, int count)
        {
            var candidates = new List<cfg.DishVariant>();
            foreach (cfg.DishVariant variant in tables.TbDishVariant.DataList)
            {
                if (hidden >= variant.HiddenRange.Min && hidden <= variant.HiddenRange.Max)
                {
                    candidates.Add(variant);
                }
            }

            return WeightedTake(candidates, v => v.BaseWeight, count, rng);
        }

        private static List<cfg.StomachFragment> RollFragments(cfg.Tables tables, GameRun run, int hidden, IRandomStream rng, int count)
        {
            var candidates = new List<cfg.StomachFragment>();
            foreach (cfg.StomachFragment fragment in tables.TbStomachFragment.DataList)
            {
                if (fragment.HiddenRange.Min == 0 && fragment.HiddenRange.Max == 0)
                {
                    continue; // 初始胃等不入随机池。
                }

                if (hidden < fragment.HiddenRange.Min || hidden > fragment.HiddenRange.Max)
                {
                    continue;
                }

                StomachFragmentDef def = run.Database.GetFragment(fragment.Id);
                if (def != null && run.CanAttachStomachFragment(def))
                {
                    candidates.Add(fragment);
                }
            }

            return WeightedTake(candidates, f => f.BaseWeight, count, rng);
        }

        private static List<T> WeightedTake<T>(List<T> candidates, System.Func<T, float> weightOf, int count, IRandomStream rng)
        {
            var result = new List<T>();
            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                var weights = new List<float>(candidates.Count);
                foreach (T c in candidates)
                {
                    float w = weightOf(c);
                    weights.Add(w > 0f ? w : 1f);
                }

                int index = rng.WeightedPickIndex(weights);
                result.Add(candidates[index]);
                candidates.RemoveAt(index);
            }

            return result;
        }
    }
}
