using System;
using System.Collections.Generic;
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
        private ShopSession _session;
        private string _shopKey;

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
            _session = new ShopSession(run);
            _shopKey = _session.ShopKey;
            _stock.AddRange(_session.Stock);
        }

        private void RefreshStock()
        {
            GameRun run = _host.Run;
            if (run == null)
            {
                return;
            }

            _session?.RefreshAndCache();
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

            var purchasedVisual = new ShopEntry(
                entry.Kind,
                entry.Id,
                entry.Name,
                entry.Desc,
                entry.BasePrice,
                entry.Price,
                entry.SlotIndex);
            ArchetypeVector archetype = ArchetypeService.Capture(run);
            ShopPurchaseResult result = _session?.Purchase(entry);
            if (result?.Success != true)
            {
                return false;
            }

            GameAnalyticsService.TrackShopPurchase(
                run,
                _shopKey,
                AnalyticsCategory(result.Kind),
                result.Id,
                result.Price,
                result.GoldBefore,
                result.GoldAfter,
                archetype);

            if (result.Kind != ShopEntryKind.Fragment)
            {
                _host.PlayShopPurchaseAnimation(purchasedVisual, card);
            }

            RefreshPanel();
            ReportStockShown(run);
            if (result.Kind == ShopEntryKind.Fragment && run.PendingFragmentPack.Count > 0)
            {
                _host.OpenTableEdit();
                return true;
            }

            if (run.HasPendingGenericRewards && !GameApp.UI.HasUIForm(UIForms.Reward))
            {
                GameApp.UI.OpenUIForm(UIForms.Reward, UIForms.GroupDialog, RewardFormOpenArgs.GenericQueue());
            }

            return true;
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
            return _session?.RestockIndex(entry) ?? 0;
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
