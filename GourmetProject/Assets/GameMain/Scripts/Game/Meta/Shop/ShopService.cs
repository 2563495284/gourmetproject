using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>商店一件商品的归一化描述（装饰品 / 消耗品 / 食物 / 餐桌格包）。</summary>
    public enum ShopEntryKind
    {
        PassiveItem,
        ActiveItem,
        Dish,

        /// <summary>餐桌格包：购买后开出三种碎片形状，进入餐桌编辑页手动拼贴一块。</summary>
        Fragment,
    }

    public sealed class ShopEntry
    {
        public ShopEntry(
            ShopEntryKind kind,
            string id,
            string name,
            string desc,
            int basePrice,
            int price = -1,
            int slotIndex = -1)
        {
            Kind = kind;
            SlotIndex = slotIndex;
            SetContent(id, name, desc, basePrice, price);
        }

        public ShopEntryKind Kind { get; }

        /// <summary>同一商品类型内的稳定槽位编号；兼容临时旧调用时可为 -1。</summary>
        public int SlotIndex { get; }

        public bool IsStocked => !string.IsNullOrEmpty(Id);

        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Desc { get; private set; }
        public int BasePrice { get; private set; }
        public int Price { get; private set; }

        /// <summary>槽位被清空或在原位补货时触发；槽位身份不会改变。</summary>
        public event Action<ShopEntry> StockChanged;

        public static ShopEntry CreateEmpty(ShopEntryKind kind, int slotIndex)
        {
            return new ShopEntry(kind, string.Empty, string.Empty, string.Empty, 0, 0, slotIndex);
        }

        public void SetPrice(int price)
        {
            Price = IsStocked ? System.Math.Max(1, price) : 0;
        }

        public void ClearStock()
        {
            if (!IsStocked)
            {
                return;
            }

            SetContent(string.Empty, string.Empty, string.Empty, 0, 0);
            StockChanged?.Invoke(this);
        }

        public void RestockFrom(ShopEntry replacement)
        {
            if (replacement == null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }

            if (replacement.Kind != Kind)
            {
                throw new ArgumentException(
                    $"Cannot restock {Kind} slot with {replacement.Kind}.",
                    nameof(replacement));
            }

            if (!replacement.IsStocked)
            {
                throw new ArgumentException("Replacement entry must be stocked.", nameof(replacement));
            }

            SetContent(
                replacement.Id,
                replacement.Name,
                replacement.Desc,
                replacement.BasePrice,
                replacement.Price);
            StockChanged?.Invoke(this);
        }

        private void SetContent(
            string id,
            string name,
            string desc,
            int basePrice,
            int price)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            Desc = desc ?? string.Empty;
            if (!IsStocked)
            {
                BasePrice = 0;
                Price = 0;
                return;
            }

            BasePrice = System.Math.Max(1, basePrice);
            Price = price > 0 ? price : BasePrice;
        }
    }

    /// <summary>
    /// 商店服务：根据当前进度「隐藏分」刷新商品（装饰品和消耗品 + 食物 + 餐桌格），并处理购买、出售、删菜。
    /// 隐藏分由 <see cref="HiddenScoreService"/> 统一计算，与奖励系统共用同一尺度。
    /// </summary>
    public static class ShopService
    {
        private const string FragmentPackRewardSlotId = "fragment_choice_3";

        /// <summary>
        /// 按隐藏分刷新一批商品。装饰品和消耗品（被动/主动）走 <paramref name="lootRng"/>，
        /// 食物/碎片走 <paramref name="rng"/>，两者隔离：调整装饰品和消耗品数量不会污染食物/碎片序列。
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
            int distanceFloor = HiddenScoreDistanceFloor(tables);
            int passiveSlotIndex = 0;
            int activeSlotIndex = 0;
            int dishSlotIndex = 0;

            foreach (string itemId in ItemPoolService.Roll(tables, run, cfg.ItemKind.Passive, lootRng, passiveCount, passiveHidden, distanceFloor))
            {
                ItemDefinition item = ItemDefinition.Get(tables, itemId, cfg.ItemKind.Passive);
                if (item != null)
                {
                    int basePrice = item.Price;
                    stock.Add(CreateEntry(
                        run,
                        ShopEntryKind.PassiveItem,
                        item.Id,
                        item.Name,
                        item.Desc,
                        FluctuatePrice(tables, basePrice, lootRng),
                        passiveSlotIndex++));
                }
            }

            foreach (string itemId in ItemPoolService.Roll(tables, run, cfg.ItemKind.Active, lootRng, activeCount, passiveHidden, distanceFloor))
            {
                ItemDefinition item = ItemDefinition.Get(tables, itemId, cfg.ItemKind.Active);
                if (item != null)
                {
                    int basePrice = item.Price;
                    stock.Add(CreateEntry(
                        run,
                        ShopEntryKind.ActiveItem,
                        item.Id,
                        item.Name,
                        item.Desc,
                        FluctuatePrice(tables, basePrice, lootRng),
                        activeSlotIndex++));
                }
            }

            foreach (cfg.DishVariant variant in RollDishVariants(tables, run, dishHidden, rng, dishCount))
            {
                cfg.DishBase baseDish = tables.TbDishBase.GetOrDefault(variant.BaseId);
                string name = baseDish != null ? baseDish.Name : variant.Id;
                int price = variant.Price > 0 ? variant.Price : 30;
                stock.Add(CreateEntry(
                    run,
                    ShopEntryKind.Dish,
                    variant.Id,
                    name,
                    "加入食谱的食物",
                    FluctuatePrice(tables, price, rng),
                    dishSlotIndex++));
            }

            // 碎片包：仅当存在「可拼入当前餐桌」的候选碎片时才上架（避免买了无处可放）。
            if (BuildFragmentCandidates(tables, run, fragmentHidden).Count > 0)
            {
                stock.Add(CreateEntry(
                    run,
                    ShopEntryKind.Fragment,
                    "fragment_pack",
                    "碎片包",
                    FragmentPackDesc(tables),
                    FragmentPackCost(run),
                    0));
            }

            return stock;
        }

        /// <summary>为自动补货按指定商品类型补 1 个新商品；不重掷现有库存。</summary>
        public static ShopEntry RollRestockEntry(
            cfg.Tables tables,
            GameRun run,
            ShopEntryKind kind,
            IRandomStream rng,
            IRandomStream lootRng,
            IReadOnlyList<ShopEntry> existingStock = null)
        {
            tables ??= GameApp.Config.Tables;
            if (run == null)
            {
                return null;
            }

            switch (kind)
            {
                case ShopEntryKind.PassiveItem:
                    {
                        int hidden = HiddenScoreService.PassiveItemHiddenScore(run, run.LastActionContext);
                        foreach (string itemId in ItemPoolService.Roll(
                                     tables,
                                     run,
                                     cfg.ItemKind.Passive,
                                     lootRng,
                                     ExistingStockedCount(existingStock, kind) + 1,
                                     hidden,
                                     HiddenScoreDistanceFloor(tables)))
                        {
                            if (ContainsEntryId(existingStock, kind, itemId))
                            {
                                continue;
                            }

                            ItemDefinition item = ItemDefinition.Get(tables, itemId, cfg.ItemKind.Passive);
                            if (item == null)
                            {
                                continue;
                            }

                            int basePrice = item.Price;
                            return CreateEntry(run, kind, item.Id, item.Name, item.Desc, FluctuatePrice(tables, basePrice, lootRng));
                        }

                        return null;
                    }
                case ShopEntryKind.ActiveItem:
                    {
                        int hidden = HiddenScoreService.PassiveItemHiddenScore(run, run.LastActionContext);
                        foreach (string itemId in ItemPoolService.Roll(
                                     tables,
                                     run,
                                     cfg.ItemKind.Active,
                                     lootRng,
                                     ExistingStockedCount(existingStock, kind) + 1,
                                     hidden,
                                     HiddenScoreDistanceFloor(tables)))
                        {
                            if (ContainsEntryId(existingStock, kind, itemId))
                            {
                                continue;
                            }

                            ItemDefinition item = ItemDefinition.Get(tables, itemId, cfg.ItemKind.Active);
                            if (item == null)
                            {
                                continue;
                            }

                            int basePrice = item.Price;
                            return CreateEntry(run, kind, item.Id, item.Name, item.Desc, FluctuatePrice(tables, basePrice, lootRng));
                        }

                        return null;
                    }
                case ShopEntryKind.Dish:
                    {
                        int hidden = HiddenScoreService.DishHiddenScore(run, run.LastActionContext);
                        foreach (cfg.DishVariant variant in RollDishVariants(
                                     tables,
                                     run,
                                     hidden,
                                     rng,
                                     ExistingStockedCount(existingStock, kind) + 1))
                        {
                            if (ContainsEntryId(existingStock, kind, variant.Id))
                            {
                                continue;
                            }

                            cfg.DishBase baseDish = tables.TbDishBase.GetOrDefault(variant.BaseId);
                            string name = baseDish != null ? baseDish.Name : variant.Id;
                            int price = variant.Price > 0 ? variant.Price : 30;
                            return CreateEntry(run, kind, variant.Id, name, "加入食谱的食物", FluctuatePrice(tables, price, rng));
                        }

                        return null;
                    }
                case ShopEntryKind.Fragment:
                    {
                        if (ContainsEntryId(existingStock, kind, "fragment_pack"))
                        {
                            return null;
                        }

                        int hidden = HiddenScoreService.FragmentHiddenScore(run, run.LastActionContext);
                        if (BuildFragmentCandidates(tables, run, hidden).Count <= 0)
                        {
                            return null;
                        }

                        return CreateEntry(
                            run,
                            kind,
                            "fragment_pack",
                            "碎片包",
                            FragmentPackDesc(tables),
                            FragmentPackCost(run));
                    }
                default:
                    return null;
            }
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
            if (entry == null || !entry.IsStocked)
            {
                return 0;
            }

            if (run == null)
            {
                return entry.BasePrice;
            }

            int basePrice = entry.Kind == ShopEntryKind.Fragment ? FragmentPackCost(run) : entry.BasePrice;
            int itemPrice = new ItemRuntime(run).ModifyShopPrice(entry.Kind, entry.Id, basePrice);
            return run.ModifyEventShopPrice(itemPrice);
        }

        private static ShopEntry CreateEntry(
            GameRun run,
            ShopEntryKind kind,
            string id,
            string name,
            string desc,
            int basePrice,
            int slotIndex = -1)
        {
            var entry = new ShopEntry(kind, id, name, desc, basePrice, slotIndex: slotIndex);
            RefreshPrice(run, entry);
            return entry;
        }

        private static void RefreshPrice(GameRun run, ShopEntry entry)
        {
            if (entry != null)
            {
                entry.SetPrice(CurrentPrice(run, entry));
            }
        }

        private static int ConfiguredSlotCount(int value)
        {
            return System.Math.Max(0, value);
        }

        private static int ExistingStockedCount(
            IReadOnlyList<ShopEntry> stock,
            ShopEntryKind kind)
        {
            if (stock == null)
            {
                return 0;
            }

            int count = 0;
            foreach (ShopEntry entry in stock)
            {
                if (entry != null && entry.IsStocked && entry.Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool ContainsEntryId(IReadOnlyList<ShopEntry> stock, ShopEntryKind kind, string id)
        {
            if (stock == null || string.IsNullOrEmpty(id))
            {
                return false;
            }

            foreach (ShopEntry entry in stock)
            {
                if (entry != null
                    && entry.IsStocked
                    && entry.Kind == kind
                    && entry.Id == id)
                {
                    return true;
                }
            }

            return false;
        }

        private static int FluctuatePrice(cfg.Tables tables, int basePrice, IRandomStream rng)
        {
            int safeBase = System.Math.Max(1, basePrice);
            float pct = tables?.TbGameBase?.ShopPriceFluctuationPct ?? 0.2f;
            pct = System.Math.Max(0f, pct);
            if (pct <= 0f)
            {
                return safeBase;
            }

            float factor = rng != null ? rng.Range(1f - pct, 1f + pct) : 1f;
            int price = (int)System.Math.Round(safeBase * factor, System.MidpointRounding.AwayFromZero);
            return System.Math.Max(1, price);
        }

        private static int FragmentPackCost(GameRun run)
        {
            cfg.GameBase gameBase = run?.Tables?.TbGameBase?.Data;
            return ProgressivePrice(gameBase?.FragmentPackPrices, run?.FragmentPackPurchaseCount ?? 0);
        }

        private static int DeleteDishCostBase(GameRun run)
        {
            cfg.GameBase gameBase = run?.Tables?.TbGameBase?.Data;
            return ProgressivePrice(gameBase?.DeleteDishPrices, run?.DeleteDishCount ?? 0);
        }

        private static int ProgressivePrice(IReadOnlyList<int> prices, int completedCount)
        {
            if (prices == null || prices.Count == 0)
            {
                return 0;
            }

            int index = System.Math.Max(0, completedCount);
            if (index >= prices.Count)
            {
                index = prices.Count - 1;
            }

            return System.Math.Max(1, prices[index]);
        }

        /// <summary>购买一件商品：扣金并结算到运行状态。返回是否成功。</summary>
        public static bool Purchase(GameRun run, ShopEntry entry)
        {
            if (run == null || entry == null || !entry.IsStocked)
            {
                return false;
            }

            if (entry.Kind == ShopEntryKind.Fragment && !CanPurchaseFragmentPack(run))
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
                    ItemAcquireResult acquireResult = run.AcquireItem(entry.Id, 0);
                    applied = acquireResult.Outcome == ItemAcquireOutcome.Added
                        || acquireResult.Outcome == ItemAcquireOutcome.Stacked;
                    break;
                case ShopEntryKind.Dish:
                    applied = run.AddBonusDish(entry.Id);
                    break;
                case ShopEntryKind.Fragment:
                    {
                        // 碎片包：按奖励槽配置开出候选碎片置为待拼贴状态；实际拼入餐桌在餐桌编辑页完成。
                        List<string> pack = RollFragmentPack(run);
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
            if (entry.Kind == ShopEntryKind.Fragment)
            {
                run.RecordFragmentPackPurchased();
            }

            return true;
        }

        /// <summary>每次进入商店可购买碎片包次数；0 表示不限。</summary>
        public static int FragmentPackPurchaseLimit(GameRun run)
        {
            return System.Math.Max(0, run?.Tables?.TbGameBase?.ShopFragmentPackPurchaseLimit ?? 0);
        }

        /// <summary>当前商店剩余可购买碎片包次数；不限时返回 int.MaxValue。</summary>
        public static int FragmentPackPurchaseRemaining(GameRun run)
        {
            if (run == null)
            {
                return 0;
            }

            int limit = FragmentPackPurchaseLimit(run);
            return limit <= 0
                ? int.MaxValue
                : System.Math.Max(0, limit - run.CurrentShopFragmentPackPurchaseCount);
        }

        public static bool CanPurchaseFragmentPack(GameRun run)
        {
            return run != null
                && run.PendingFragmentPack.Count == 0
                && FragmentPackPurchaseRemaining(run) > 0;
        }

        /// <summary>当前删牌花费（含装饰品和消耗品折扣/固定价/涨价修正）。</summary>
        public static int DeleteCost(GameRun run)
        {
            if (run == null)
            {
                return 0;
            }

            return run.ModifyEventShopPrice(new ItemRuntime(run).ModifyDeletePrice(DeleteDishCostBase(run)));
        }

        /// <summary>每次商店可删除食物次数；0 表示不限。</summary>
        public static int DeleteDishLimit(GameRun run)
        {
            return System.Math.Max(0, run?.Tables?.TbGameBase?.ShopDeleteDishLimit ?? 0);
        }

        /// <summary>当前商店剩余可删除次数；不限时返回 int.MaxValue。</summary>
        public static int DeleteDishRemaining(GameRun run)
        {
            if (run == null)
            {
                return 0;
            }

            int limit = DeleteDishLimit(run);
            return limit <= 0
                ? int.MaxValue
                : System.Math.Max(0, limit - run.CurrentShopDeleteDishCount);
        }

        public static bool CanDeleteDish(GameRun run)
        {
            return run != null
                && DeleteDishRemaining(run) > 0
                && run.RecipeEntries.Count > 0
                && run.Gold >= DeleteCost(run)
                && !new ItemRuntime(run).BlockRemoveDish();
        }

        /// <summary>删除食谱中的1 个食物，花费金币。持有「囤积癖」时禁止删除。</summary>
        public static bool DeleteDish(GameRun run, string dishId)
        {
            if (!CanDeleteDish(run))
            {
                return false;
            }

            int cost = DeleteCost(run);
            if (!run.RemoveBonusDish(dishId))
            {
                return false;
            }

            run.Gold -= cost;
            run.RecordDishDeleted();
            return true;
        }

        public static bool DeleteDishAt(GameRun run, int dishIndex)
        {
            if (!CanDeleteDish(run))
            {
                return false;
            }

            int cost = DeleteCost(run);
            if (!run.RemoveBonusDishAt(dishIndex))
            {
                return false;
            }

            run.Gold -= cost;
            run.RecordDishDeleted();
            return true;
        }

        public static bool MoveDish(GameRun run, int dishIndex, int toDishIndex)
        {
            return run != null && run.MoveBonusDish(dishIndex, toDishIndex);
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

            int distanceFloor = HiddenScoreDistanceFloor(tables);
            return WeightedTake(candidates, v => RewardPoolService.HiddenScoreWeight(v.BaseWeight, HiddenMean(v.HiddenRange), hidden, distanceFloor), count, rng, DefaultRandomWeight(tables));
        }

        /// <summary>开一份商店碎片包：候选数量读取 reward_slot(fragment_choice_3).choiceCount。</summary>
        public static List<string> RollFragmentPack(GameRun run)
        {
            return RollFragmentPack(run, FragmentPackSize(run?.Tables));
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
            int distanceFloor = HiddenScoreDistanceFloor(run.Tables);
            foreach (cfg.TableFragment fragment in WeightedTake(
                candidates,
                f => RewardPoolService.HiddenScoreWeight(f.BaseWeight, HiddenMean(f.HiddenRange), hidden, distanceFloor),
                count,
                rng,
                DefaultRandomWeight(run.Tables)))
            {
                ids.Add(fragment.Id);
            }

            return ids;
        }

        private static string FragmentPackDesc(cfg.Tables tables)
        {
            return $"开出{FragmentPackSize(tables)}种碎片，选一块拼入餐桌";
        }

        private static int FragmentPackSize(cfg.Tables tables)
        {
            cfg.RewardSlot slot = tables?.TbRewardSlot?.GetOrDefault(FragmentPackRewardSlotId);
            return slot.ChoiceCount;
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

        private static int HiddenScoreDistanceFloor(cfg.Tables tables)
        {
            tables ??= GameApp.Config.Tables;
            return System.Math.Max(1, tables.TbGameBase.HiddenScoreDistanceFloor);
        }

        private static float DefaultRandomWeight(cfg.Tables tables)
        {
            tables ??= GameApp.Config.Tables;
            return System.Math.Max(float.Epsilon, tables.TbGameBase.DefaultRandomWeight);
        }

        private static List<T> WeightedTake<T>(List<T> candidates, System.Func<T, float> weightOf, int count, IRandomStream rng, float defaultWeight)
        {
            var result = new List<T>();
            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                var weights = new List<float>(candidates.Count);
                foreach (T c in candidates)
                {
                    float w = weightOf(c);
                    weights.Add(w > 0f ? w : defaultWeight);
                }

                int index = rng.WeightedPickIndex(weights);
                result.Add(candidates[index]);
                candidates.RemoveAt(index);
            }

            return result;
        }
    }
}
