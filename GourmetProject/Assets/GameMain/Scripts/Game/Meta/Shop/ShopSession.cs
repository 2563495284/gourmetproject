using System.Collections.Generic;
using System.Text;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 一次商店访问的正式状态机：pending 库存、命名随机、购买与自动补货均在此完成。
    /// UI 和 Balance 自动玩家只决定买哪一件。
    /// </summary>
    public sealed class ShopSession
    {
        private readonly GameRun _run;
        private readonly List<ShopEntry> _stock = new List<ShopEntry>();
        private readonly Dictionary<string, int> _restockIndexes = new Dictionary<string, int>();

        public ShopSession(GameRun run)
        {
            _run = run;
            ShopKey = run != null
                ? GameRun.BuildShopKey(run.WeekIndex, run.CurrentDay)
                : string.Empty;
            LoadOrRollStock();
        }

        public string ShopKey { get; }

        public IReadOnlyList<ShopEntry> Stock => _stock;

        public ShopPurchaseResult Purchase(ShopEntry entry)
        {
            var result = new ShopPurchaseResult();
            if (_run == null || entry == null || !_stock.Contains(entry) || !entry.IsStocked)
            {
                result.Error = "商品无效或已售罄";
                return result;
            }

            result.Kind = entry.Kind;
            result.Id = entry.Id;
            result.Name = entry.Name;
            result.Price = ShopService.CurrentPrice(_run, entry);
            result.GoldBefore = _run.Gold;
            if (!ShopService.Purchase(_run, entry))
            {
                result.Error = "金币不足、槽位已满或商品当前不可购买";
                return result;
            }

            result.GoldAfter = _run.Gold;
            result.Success = true;
            ShopEntry replacement = TryAutoRestock(entry);
            if (replacement != null)
            {
                entry.RestockFrom(replacement);
                string indexKey = RestockIndexKey(entry);
                _restockIndexes[indexKey] = RestockIndex(entry) + 1;
                result.Restocked = true;
            }
            else
            {
                entry.ClearStock();
            }

            RefreshAndCache();
            return result;
        }

        public int RestockIndex(ShopEntry entry)
        {
            return _restockIndexes.TryGetValue(RestockIndexKey(entry), out int value) ? value : 0;
        }

        public void RefreshAndCache()
        {
            if (_run == null)
            {
                return;
            }

            ShopService.RefreshStockPrices(_run, _stock);
            _run.SetPendingShopStock(ShopKey, _stock);
        }

        private void LoadOrRollStock()
        {
            _stock.Clear();
            if (_run == null)
            {
                return;
            }

            if (_run.HasPendingShopStock(ShopKey))
            {
                _stock.AddRange(_run.GetPendingShopStock(ShopKey));
                RefreshAndCache();
                return;
            }

            IRandomStream rng = _run.Random.DomainStream(SeedDomains.Shop, ShopKey);
            IRandomStream lootRng = _run.Random.DomainStream(SeedDomains.Loot, $"shop_{ShopKey}");
            _stock.AddRange(ShopService.RollStock(_run.Tables, _run, rng, lootRng));
            RefreshAndCache();
        }

        private ShopEntry TryAutoRestock(ShopEntry purchasedEntry)
        {
            if (_run == null || purchasedEntry == null
                || !new ItemRuntime(_run).AutoRestock(purchasedEntry.Kind))
            {
                return null;
            }

            string restockKey = BuildRestockKey(purchasedEntry);
            IRandomStream rng = _run.Random.DomainStream(SeedDomains.Shop, restockKey);
            IRandomStream lootRng = _run.Random.DomainStream(SeedDomains.Loot, $"loot_{restockKey}");
            var remaining = new List<ShopEntry>();
            foreach (ShopEntry stockEntry in _stock)
            {
                if (stockEntry != null && !ReferenceEquals(stockEntry, purchasedEntry))
                {
                    remaining.Add(stockEntry);
                }
            }

            ShopEntry replacement = ShopService.RollRestockEntry(
                _run.Tables,
                _run,
                purchasedEntry.Kind,
                rng,
                lootRng,
                remaining);
            if (replacement != null)
            {
                new ItemRuntime(_run).FlashTriggered(m => m.AutoRestock(purchasedEntry.Kind));
            }

            return replacement;
        }

        private string BuildRestockKey(ShopEntry purchasedEntry)
        {
            var builder = new StringBuilder();
            builder.Append("restock_");
            builder.Append(ShopKey);
            builder.Append('_');
            builder.Append(purchasedEntry.Kind);
            builder.Append('_');
            builder.Append(purchasedEntry.Id);
            foreach (ShopEntry entry in _stock)
            {
                if (entry == null || ReferenceEquals(entry, purchasedEntry))
                {
                    continue;
                }

                builder.Append('|');
                builder.Append(entry.Kind);
                builder.Append(':');
                builder.Append(entry.SlotIndex);
                builder.Append(':');
                builder.Append(entry.Id);
            }

            return builder.ToString();
        }

        private static string RestockIndexKey(ShopEntry entry)
        {
            return $"{entry?.Kind}:{entry?.SlotIndex}";
        }
    }

    public sealed class ShopPurchaseResult
    {
        public bool Success;
        public string Error = string.Empty;
        public ShopEntryKind Kind;
        public string Id = string.Empty;
        public string Name = string.Empty;
        public int Price;
        public int GoldBefore;
        public int GoldAfter;
        public bool Restocked;
    }
}
