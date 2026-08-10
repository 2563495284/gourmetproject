using System;
using System.Collections.Generic;
using System.Text;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Analytics;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Runtime;

namespace GourmetProject.Game.UI.Battle.Pages
{
    internal interface IShopPageHost
    {
        GameRun Run { get; }

        ShopForm ShopPanel { get; }

        bool ShouldRefreshItemsAfterShopChange { get; }

        void OnShopClosed();

        void RefreshPersistent(bool refreshItems = true);

        void OpenDeleteDish();

        void OpenTableEdit(Action onShown = null);

        void OpenRecipeInspect(int bookIndex);

        void PlayShopPurchaseAnimation(ShopEntry entry, ShopBuyItemViewBase sourceCard);
    }

    internal sealed class ShopPageCoordinator
    {
        private readonly IShopPageHost _host;
        private readonly List<ShopEntry> _stock = new();
        private string _shopKey;
        private readonly Dictionary<string, int> _restockIndexes = new();

        public ShopPageCoordinator(IShopPageHost host)
        {
            _host = host;
        }

        public void OpenPanel()
        {
            GameRun run = _host.Run;
            if (run == null)
            {
                _host.OnShopClosed();
                return;
            }

            EnsureStock(run);
            ReportStockShown(run);
            _host.ShopPanel?.Open(
                run,
                _stock,
                OnLeave,
                _host.OpenDeleteDish,
                BuyImmediate);
            RefreshPersistent();
        }

        public void RefreshPersistent()
        {
            RefreshStock();
            _host.RefreshPersistent(_host.ShouldRefreshItemsAfterShopChange);
        }

        private void EnsureStock(GameRun run)
        {
            _stock.Clear();
            _shopKey = GameRun.BuildShopKey(run.WeekIndex, run.CurrentDay);
            if (run.HasPendingShopStock(_shopKey))
            {
                _stock.AddRange(run.GetPendingShopStock(_shopKey));
                RefreshStock();
                return;
            }

            IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Shop, _shopKey);
            IRandomStream lootRng = GameApp.Random.DomainStream(SeedDomains.Loot, $"shop_{_shopKey}");
            _stock.AddRange(ShopService.RollStock(GameApp.Config.Tables, run, rng, lootRng));
            // 掷库存只写内存 pending（同一天重开商店复用同一份）；不存档，退出商店结算时才统一存。
            run.SetPendingShopStock(_shopKey, _stock);
        }

        private void RefreshStock()
        {
            GameRun run = _host.Run;
            if (run == null)
            {
                return;
            }

            ShopService.RefreshStockPrices(run, _stock);
            if (!string.IsNullOrEmpty(_shopKey))
            {
                run.SetPendingShopStock(_shopKey, _stock);
            }
        }

        private void RefreshPanel()
        {
            RefreshStock();
            _host.ShopPanel?.RefreshShop(_host.Run, _stock);
            RefreshPersistent();
        }

        private bool BuyImmediate(ShopEntry entry, ShopBuyItemViewBase card)
        {
            GameRun run = _host.Run;
            if (run == null || entry == null)
            {
                return false;
            }

            string contentId = entry.Id;
            string category = AnalyticsCategory(entry.Kind);
            int price = ShopService.CurrentPrice(run, entry);
            int goldBefore = run.Gold;
            ArchetypeVector archetype = ArchetypeService.Capture(run);
            if (!ShopService.Purchase(run, entry))
            {
                return false;
            }

            GameAnalyticsService.TrackShopPurchase(
                run,
                _shopKey,
                category,
                contentId,
                price,
                goldBefore,
                run.Gold,
                archetype);

            if (entry.Kind != ShopEntryKind.Fragment)
            {
                _host.PlayShopPurchaseAnimation(entry, card);
            }

            FinishPurchasedEntry(entry);
            return true;
        }

        private void FinishPurchasedEntry(ShopEntry entry)
        {
            GameRun run = _host.Run;
            if (run == null || entry == null)
            {
                return;
            }

            ShopEntry restock = TryAutoRestock(entry);
            if (restock != null)
            {
                entry.RestockFrom(restock);
                string indexKey = RestockIndexKey(entry);
                _restockIndexes[indexKey] = RestockIndex(entry) + 1;
            }
            else
            {
                entry.ClearStock();
            }

            RefreshPanel();
            ReportStockShown(run);

            // 碎片包：购买后进入餐桌编辑页手动拼贴（金币已扣，待开包状态已置）。
            if (entry.Kind == ShopEntryKind.Fragment && run.PendingFragmentPack.Count > 0)
            {
                _host.OpenTableEdit();
                return;
            }

            if (run.HasPendingGenericRewards && !GameApp.UI.HasUIForm(UIForms.Reward))
            {
                GameApp.UI.OpenUIForm(UIForms.Reward, UIForms.GroupDialog, RewardFormOpenArgs.GenericQueue());
            }
        }

        private void ReportStockShown(GameRun run)
        {
            if (run == null)
            {
                return;
            }

            foreach (ShopEntry entry in _stock)
            {
                if (entry == null || !entry.IsStocked)
                {
                    continue;
                }

                int price = ShopService.CurrentPrice(run, entry);
                GameAnalyticsService.TrackShopItemShown(
                    run,
                    _shopKey,
                    AnalyticsCategory(entry.Kind),
                    entry.Id,
                    entry.SlotIndex,
                    price,
                    run.Gold >= price,
                    RestockIndex(entry));
            }
        }

        private int RestockIndex(ShopEntry entry)
        {
            return _restockIndexes.TryGetValue(RestockIndexKey(entry), out int value) ? value : 0;
        }

        private static string RestockIndexKey(ShopEntry entry)
        {
            return $"{entry?.Kind}:{entry?.SlotIndex}";
        }

        private static string AnalyticsCategory(ShopEntryKind kind)
        {
            return kind switch
            {
                ShopEntryKind.Dish => "dish",
                ShopEntryKind.PassiveItem => "passive_item",
                ShopEntryKind.ActiveItem => "active_item",
                ShopEntryKind.Fragment => "fragment",
                _ => "unknown",
            };
        }

        private ShopEntry TryAutoRestock(ShopEntry purchasedEntry)
        {
            GameRun run = _host.Run;
            if (run == null || purchasedEntry == null || !new ItemRuntime(run).AutoRestock(purchasedEntry.Kind))
            {
                return null;
            }

            string restockKey = BuildRestockKey(purchasedEntry);
            IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Shop, restockKey);
            IRandomStream lootRng = GameApp.Random.DomainStream(SeedDomains.Loot, $"loot_{restockKey}");
            var remainingStock = new List<ShopEntry>(_stock.Count);
            foreach (ShopEntry entry in _stock)
            {
                if (entry != null && !ReferenceEquals(entry, purchasedEntry))
                {
                    remainingStock.Add(entry);
                }
            }

            ShopEntry restock = ShopService.RollRestockEntry(
                GameApp.Config.Tables,
                run,
                purchasedEntry.Kind,
                rng,
                lootRng,
                remainingStock);
            if (restock == null)
            {
                return null;
            }

            new ItemRuntime(run).FlashTriggered(m => m.AutoRestock(purchasedEntry.Kind));
            return restock;
        }

        private string BuildRestockKey(ShopEntry purchasedEntry)
        {
            var builder = new StringBuilder();
            builder.Append("restock_");
            builder.Append(_shopKey);
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

        private void OnLeave()
        {
            if (_host.ShopPanel != null)
            {
                _host.ShopPanel.gameObject.SetActive(false);
            }

            _host.OnShopClosed();
        }
    }
}
