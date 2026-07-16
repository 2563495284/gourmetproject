using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>商店一件商品的归一化描述（被动道具 / 主动道具 / 菜品 / 餐桌碎片包）。</summary>
    public enum ShopEntryKind
    {
        PassiveItem,
        ActiveItem,
        Dish,

        /// <summary>餐桌碎片包：购买后开出三种碎片形状，进入餐桌编辑页手动拼贴一块。</summary>
        Fragment,
    }

    public sealed class ShopEntry
    {
        public ShopEntry(ShopEntryKind kind, string id, string name, string desc, int basePrice, int price = -1)
        {
            Kind = kind;
            Id = id;
            Name = name;
            Desc = desc;
            BasePrice = System.Math.Max(1, basePrice);
            Price = price > 0 ? price : BasePrice;
        }

        public ShopEntryKind Kind { get; }
        public string Id { get; }
        public string Name { get; }
        public string Desc { get; }
        public int BasePrice { get; }
        public int Price { get; private set; }

        public void SetPrice(int price)
        {
            Price = System.Math.Max(1, price);
        }
    }

    /// <summary>
    /// 商店服务：根据当前进度「隐藏分」刷新商品（道具 + 菜品 + 餐桌碎片），并处理购买、出售、删菜。
    /// 隐藏分来自 <see cref="GameRun.RewardHiddenScore"/>，与奖励系统共用同一尺度。
    /// </summary>
    public static class ShopService
    {
        public const int PassiveItemPrice = 45;
        public const int ActiveItemPrice = 35;
        public const int ItemSellPrice = 20;
        public const int DeleteDishCost = 15;
        public const int EmptyRecipeBookPrice = 20;

        /// <summary>碎片包售价（固定）。</summary>
        public const int FragmentPackPrice = 60;

        /// <summary>碎片包开出的候选碎片数量（三选一）。</summary>
        public const int FragmentPackSize = 3;

        /// <summary>
        /// 按隐藏分刷新一批商品。道具（被动/主动）走 <paramref name="lootRng"/>，
        /// 菜品/碎片走 <paramref name="rng"/>，两者隔离：调整道具数量不会污染菜品/碎片序列。
        /// </summary>
        public static List<ShopEntry> RollStock(cfg.Tables tables, GameRun run, IRandomStream rng, IRandomStream lootRng)
        {
            tables ??= GameApp.Config.Tables;
            var stock = new List<ShopEntry>();
            int dishHidden = HiddenScoreService.DishHiddenScore(run, run.LastActionContext);
            int passiveHidden = HiddenScoreService.PassiveItemHiddenScore(run, run.LastActionContext);
            int fragmentHidden = HiddenScoreService.FragmentHiddenScore(run, run.LastActionContext);
            int passiveCount = ConfiguredSlotCount(tables.TbGameBase.ShopPassiveItemSaleSlotCount);
            int activeCount = ConfiguredSlotCount(tables.TbGameBase.ShopActiveItemSaleSlotCount);
            int dishCount = ConfiguredSlotCount(tables.TbGameBase.ShopFoodSaleSlotCount);

            foreach (string itemId in ItemPoolService.Roll(tables, run, cfg.ItemKind.Passive, lootRng, passiveCount, passiveHidden, distanceFloor: 5))
            {
                ItemDefinition item = ItemDefinition.Get(tables, itemId, cfg.ItemKind.Passive);
                if (item != null)
                {
                    stock.Add(CreateEntry(run, ShopEntryKind.PassiveItem, item.Id, item.Name, item.Desc, PassiveItemPrice));
                }
            }

            foreach (string itemId in ItemPoolService.Roll(tables, run, cfg.ItemKind.Active, lootRng, activeCount, passiveHidden, distanceFloor: 5))
            {
                ItemDefinition item = ItemDefinition.Get(tables, itemId, cfg.ItemKind.Active);
                if (item != null)
                {
                    stock.Add(CreateEntry(run, ShopEntryKind.ActiveItem, item.Id, item.Name, item.Desc, ActiveItemPrice));
                }
            }

            foreach (cfg.DishVariant variant in RollDishVariants(tables, run, dishHidden, rng, dishCount))
            {
                cfg.DishBase baseDish = tables.TbDishBase.GetOrDefault(variant.BaseId);
                string name = baseDish != null ? baseDish.Name : variant.Id;
                int price = variant.Price > 0 ? variant.Price : 30;
                stock.Add(CreateEntry(run, ShopEntryKind.Dish, variant.Id, name, "加入菜谱池的菜品", price));
            }

            // 碎片包：仅当存在「可拼入当前餐桌」的候选碎片时才上架（避免买了无处可放）。
            if (BuildFragmentCandidates(tables, run, fragmentHidden).Count > 0)
            {
                stock.Add(CreateEntry(
                    run,
                    ShopEntryKind.Fragment,
                    "fragment_pack",
                    "碎片包",
                    "开出三种碎片，选一块拼入餐桌",
                    FragmentPackPrice));
            }

            // TODO(passive-item): AutoRestock（自动补货）需商店购买循环支持「卖出后回填槽位」，属 UI/流程交互，
            //   数值判定 ItemRuntime.AutoRestock() 已就绪，待 ShopForm 购买流程接入。

            return stock;
        }

        public static void RefreshStockPrices(GameRun run, IReadOnlyList<ShopEntry> stock)
        {
            if (stock == null)
            {
                return;
            }

            foreach (ShopEntry entry in stock)
            {
                RefreshPrice(run, entry);
            }
        }

        public static int CurrentPrice(GameRun run, ShopEntry entry)
        {
            if (entry == null)
            {
                return 0;
            }

            if (run == null)
            {
                return entry.BasePrice;
            }

            int itemPrice = new ItemRuntime(run).ModifyShopPrice(entry.Kind, entry.BasePrice);
            return run.ModifyEventShopPrice(itemPrice);
        }

        private static ShopEntry CreateEntry(GameRun run, ShopEntryKind kind, string id, string name, string desc, int basePrice)
        {
            var entry = new ShopEntry(kind, id, name, desc, basePrice);
            RefreshPrice(run, entry);
            return entry;
        }

        private static void RefreshPrice(GameRun run, ShopEntry entry)
        {
            entry?.SetPrice(CurrentPrice(run, entry));
        }

        private static int ConfiguredSlotCount(int value)
        {
            return System.Math.Max(0, value);
        }

        /// <summary>购买一件商品：扣金并结算到运行状态。返回是否成功。</summary>
        public static bool Purchase(GameRun run, ShopEntry entry)
        {
            if (run == null || entry == null)
            {
                return false;
            }

            int price = CurrentPrice(run, entry);
            entry.SetPrice(price);
            if (run.Gold < price)
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
                {
                    // 碎片包：开出三种候选碎片置为待拼贴状态；实际拼入餐桌在餐桌编辑页完成。
                    List<string> pack = RollFragmentPack(run, FragmentPackSize);
                    if (pack.Count == 0)
                    {
                        return false;
                    }

                    run.SetPendingFragmentPack(pack);
                    applied = true;
                    break;
                }
                default:
                    applied = false;
                    break;
            }

            if (!applied)
            {
                return false;
            }

            run.Gold -= price;
            return true;
        }

        public static bool PurchaseDishToBook(GameRun run, ShopEntry entry, int bookIndex)
        {
            if (run == null || entry == null || entry.Kind != ShopEntryKind.Dish)
            {
                return false;
            }

            int price = CurrentPrice(run, entry);
            entry.SetPrice(price);
            if (run.Gold < price)
            {
                return false;
            }

            if (!run.AddBonusDishToBook(entry.Id, bookIndex))
            {
                return false;
            }

            run.Gold -= price;
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

        /// <summary>当前删牌花费（含道具折扣/固定价/涨价修正）。</summary>
        public static int DeleteCost(GameRun run)
        {
            if (run == null)
            {
                return DeleteDishCost;
            }

            return run.ModifyEventShopPrice(new ItemRuntime(run).ModifyDeletePrice(DeleteDishCost));
        }

        public static int RecipeBookCost(GameRun run)
        {
            if (run == null)
            {
                return EmptyRecipeBookPrice;
            }

            return run.ModifyEventShopPrice(new ItemRuntime(run).ModifyRecipeBookPrice(EmptyRecipeBookPrice));
        }

        /// <summary>删除菜谱池中的一道菜，花费金币。持有「囤积癖」时禁止删除。</summary>
        public static bool DeleteDish(GameRun run, string dishId)
        {
            if (run == null || new ItemRuntime(run).BlockRemoveDish())
            {
                return false;
            }

            int cost = DeleteCost(run);
            if (run.Gold < cost || !run.RemoveBonusDish(dishId))
            {
                return false;
            }

            run.Gold -= cost;
            return true;
        }

        public static bool DeleteDishAt(GameRun run, int bookIndex, int dishIndex)
        {
            if (run == null || new ItemRuntime(run).BlockRemoveDish())
            {
                return false;
            }

            int cost = DeleteCost(run);
            if (run.Gold < cost || !run.RemoveBonusDishAt(bookIndex, dishIndex))
            {
                return false;
            }

            run.Gold -= cost;
            return true;
        }

        public static bool PurchaseRecipeBook(GameRun run)
        {
            if (run == null || !run.CanAddRecipeBook)
            {
                return false;
            }

            int price = RecipeBookCost(run);
            if (run.Gold < price || !run.AddRecipeBook())
            {
                return false;
            }

            run.Gold -= price;
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

        /// <summary>开一份碎片包：按当前隐藏分加权 roll 出 <paramref name="count"/> 个可拼入当前餐桌的候选碎片 id。</summary>
        public static List<string> RollFragmentPack(GameRun run, int count)
        {
            var ids = new List<string>();
            if (run == null || count <= 0)
            {
                return ids;
            }

            int hidden = HiddenScoreService.FragmentHiddenScore(run, run.LastActionContext);
            List<cfg.TableFragment> candidates = BuildFragmentCandidates(run.Tables, run, hidden);
            if (candidates.Count == 0)
            {
                return ids;
            }

            IRandomStream rng = GameApp.Random.DomainStream(
                SeedDomains.Shop, $"pack_{run.WeekIndex}_{run.CurrentDay}_{run.FragmentPlacements.Count}");
            foreach (cfg.TableFragment fragment in WeightedTake(
                candidates, f => RewardPoolService.HiddenScoreWeight(f.BaseWeight, HiddenMean(f.HiddenRange), hidden, 5), count, rng))
            {
                ids.Add(fragment.Id);
            }

            return ids;
        }

        /// <summary>筛选可拼入当前餐桌、且隐藏分覆盖的碎片候选（不消耗随机流）。</summary>
        private static List<cfg.TableFragment> BuildFragmentCandidates(cfg.Tables tables, GameRun run, int hidden)
        {
            var candidates = new List<cfg.TableFragment>();
            foreach (cfg.TableFragment fragment in tables.TbTableFragment.DataList)
            {
                if (fragment.HiddenRange.Min == 0 && fragment.HiddenRange.Max == 0)
                {
                    continue; // 初始胃等不入随机池。
                }

                if (hidden < fragment.HiddenRange.Min || hidden > fragment.HiddenRange.Max)
                {
                    continue;
                }

                TableFragmentDef def = run.Database.GetFragment(fragment.Id);
                if (def != null && run.CanAttachTableFragment(def))
                {
                    candidates.Add(fragment);
                }
            }

            return candidates;
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
