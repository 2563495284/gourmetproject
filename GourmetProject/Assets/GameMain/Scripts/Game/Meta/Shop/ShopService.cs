using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>商店一件商品的归一化描述（被动道具 / 主动道具 / 菜品 / 胃部碎片）。</summary>
    public enum ShopEntryKind
    {
        PassiveItem,
        ActiveItem,
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
    /// 商店服务：根据当前进度「隐藏分」刷新商品（道具 + 菜品 + 胃部碎片），并处理购买、出售、删菜。
    /// 隐藏分来自 <see cref="GameRun.RewardHiddenScore"/>，与奖励系统共用同一尺度。
    /// </summary>
    public static class ShopService
    {
        public const int PassiveItemPrice = 45;
        public const int ActiveItemPrice = 35;
        public const int ItemSellPrice = 20;
        public const int DeleteDishCost = 15;
        public const int EmptyRecipeBookPrice = 20;

        private const int PassiveCount = 2;
        private const int ActiveCount = 1;
        private const int DishCount = 2;
        private const int FragmentCount = 1;

        /// <summary>
        /// 按隐藏分刷新一批商品。道具（被动/主动）走 <paramref name="lootRng"/>，
        /// 菜品/碎片走 <paramref name="rng"/>，两者隔离：调整道具数量不会污染菜品/碎片序列。
        /// </summary>
        public static List<ShopEntry> RollStock(cfg.Tables tables, GameRun run, IRandomStream rng, IRandomStream lootRng)
        {
            var stock = new List<ShopEntry>();
            int dishHidden = HiddenScoreService.DishHiddenScore(run, run.LastActionContext);
            int passiveHidden = HiddenScoreService.PassiveItemHiddenScore(run, run.LastActionContext);
            int fragmentHidden = HiddenScoreService.FragmentHiddenScore(run, run.LastActionContext);

            foreach (string itemId in ItemPoolService.Roll(tables, run, cfg.ItemKind.Passive, lootRng, PassiveCount, passiveHidden, distanceFloor: 5))
            {
                cfg.Item item = tables.TbItem.GetOrDefault(itemId);
                if (item != null)
                {
                    stock.Add(new ShopEntry(ShopEntryKind.PassiveItem, item.Id, item.Name, item.Desc, PassiveItemPrice));
                }
            }

            int activeHidden = HiddenScoreService.ActiveItemHiddenScore(run, run.LastActionContext);
            foreach (string itemId in ItemPoolService.Roll(tables, run, cfg.ItemKind.Active, lootRng, ActiveCount, activeHidden, distanceFloor: 5))
            {
                cfg.Item item = tables.TbItem.GetOrDefault(itemId);
                if (item != null)
                {
                    stock.Add(new ShopEntry(ShopEntryKind.ActiveItem, item.Id, item.Name, item.Desc, ActiveItemPrice));
                }
            }

            foreach (cfg.DishVariant variant in RollDishVariants(tables, run, dishHidden, rng, DishCount))
            {
                cfg.DishBase baseDish = tables.TbDishBase.GetOrDefault(variant.BaseId);
                string name = baseDish != null ? baseDish.Name : variant.Id;
                int price = variant.Price > 0 ? variant.Price : 30;
                stock.Add(new ShopEntry(ShopEntryKind.Dish, variant.Id, name, "加入菜谱池的菜品", price));
            }

            foreach (cfg.StomachFragment fragment in RollFragments(tables, run, fragmentHidden, rng, FragmentCount))
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
                case ShopEntryKind.ActiveItem:
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

        /// <summary>出售一份道具实例（被动整条移除；主动移除一份），返还金币。</summary>
        public static bool SellItem(GameRun run, string itemId)
        {
            if (run == null || !run.RemoveItem(itemId))
            {
                return false;
            }

            run.Gold += ItemSellPrice;
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

        public static bool DeleteDishAt(GameRun run, int bookIndex, int dishIndex)
        {
            if (run == null || run.Gold < DeleteDishCost || !run.RemoveBonusDishAt(bookIndex, dishIndex))
            {
                return false;
            }

            run.Gold -= DeleteDishCost;
            return true;
        }

        public static bool PurchaseRecipeBook(GameRun run)
        {
            if (run == null || run.Gold < EmptyRecipeBookPrice || !run.CanAddRecipeBook)
            {
                return false;
            }

            if (!run.AddRecipeBook())
            {
                return false;
            }

            run.Gold -= EmptyRecipeBookPrice;
            return true;
        }

        public static bool MoveDish(GameRun run, int fromBookIndex, int dishIndex, int toBookIndex)
        {
            return run != null && run.MoveBonusDish(fromBookIndex, dishIndex, toBookIndex);
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

            return WeightedTake(candidates, v => RewardPoolService.HiddenScoreWeight(v.BaseWeight, HiddenMean(v.HiddenRange), hidden, 5), count, rng);
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

            return WeightedTake(candidates, f => RewardPoolService.HiddenScoreWeight(f.BaseWeight, HiddenMean(f.HiddenRange), hidden, 5), count, rng);
        }

        private static float HiddenMean(cfg.HiddenRange range)
        {
            if (range.Min == 0 && range.Max == 0)
            {
                return 0f;
            }

            return (range.Min + range.Max) * 0.5f;
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
