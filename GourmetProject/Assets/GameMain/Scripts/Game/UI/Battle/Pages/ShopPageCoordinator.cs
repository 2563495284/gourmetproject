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
    /// <summary>
    /// 商品槽会在购买结算时同步刷新；先保留动画所需的视觉快照，再由结算结果决定播放或释放。
    /// </summary>
    internal sealed class PreparedShopPurchaseAnimation
    {
        private Action _play;
        private Action _cancel;
        private bool _resolved;

        public PreparedShopPurchaseAnimation(Action play, Action cancel = null)
        {
            _play = play;
            _cancel = cancel;
        }

        public void Play()
        {
            if (_resolved)
            {
                return;
            }

            _resolved = true;
            Action play = _play;
            _play = null;
            _cancel = null;
            play?.Invoke();
        }

        public void Cancel()
        {
            if (_resolved)
            {
                return;
            }

            _resolved = true;
            Action cancel = _cancel;
            _play = null;
            _cancel = null;
            cancel?.Invoke();
        }
    }

    internal interface IShopPageHost
    {
        GameRun Run { get; }

        ShopForm ShopPanel { get; }

        bool ShouldRefreshItemsAfterShopChange { get; }

        void OnShopClosed();

        void WhenPassivePresentationsIdle(Action onIdle);

        void RefreshPersistent(bool refreshItems = true);

        void OpenDeleteDish();

        void OpenTableEdit(Action onShown = null);

        void OpenRecipeInspect(int bookIndex);

        PreparedShopPurchaseAnimation PrepareShopPurchaseAnimation(
            ShopEntry entry,
            ShopBuyItemViewBase sourceCard);
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
            // ShopSession.Purchase 会同步清空/补货槽位并触发卡片重绑；食物预览 RT 必须在此前复制。
            PreparedShopPurchaseAnimation purchaseAnimation =
                entry.Kind != ShopEntryKind.Fragment
                    ? _host.PrepareShopPurchaseAnimation(purchasedVisual, card)
                    : null;
            ArchetypeVector archetype = ArchetypeService.Capture(run);
            ShopPurchaseResult result = _session?.Purchase(entry);
            if (result?.Success != true)
            {
                purchaseAnimation?.Cancel();
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

            purchaseAnimation?.Play();

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

            _host.WhenPassivePresentationsIdle(_host.OnShopClosed);
        }
    }
}
