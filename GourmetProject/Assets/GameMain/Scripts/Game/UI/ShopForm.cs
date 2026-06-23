using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Gameplay;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 系统节点「商店」：按隐藏分刷新商品（被动道具 / 菜品 / 胃部碎片），可购买、出售被动道具、花金币删菜。
    /// 商品逻辑集中在 <see cref="ShopService"/>；本界面只负责按周+天确定性刷新与卡片渲染。
    /// 固定壳在 ShopForm.prefab，买/卖卡用 ShopBuyCardView / ShopSellCardView 数据驱动。
    /// </summary>
    public sealed class ShopForm : UGuiForm
    {
        private const int MaxSellShown = 4;

        [SerializeField] private Text _goldText;
        [SerializeField] private Text _buyEmptyText;
        [SerializeField] private Text _sellEmptyText;
        [SerializeField] private RectTransform _buyContainer;
        [SerializeField] private RectTransform _sellContainer;
        [SerializeField] private Button _leaveButton;
        [SerializeField] private ShopBuyCardView _buyCardPrefab;
        [SerializeField] private ShopSellCardView _sellCardPrefab;

        private GameRun _run;
        private readonly List<ShopEntry> _stock = new();
        private readonly List<GameObject> _spawned = new();

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            _leaveButton.onClick.AddListener(OnLeaveClicked);
        }

        /// <summary>玩家点「离开」：关闭商店并通知编排层继续（区别于返回菜单时的强制关闭）。</summary>
        private void OnLeaveClicked()
        {
            Close();
            BattleForm.Active?.OnShopClosed();
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            _run = GameRunContext.Current;
            if (_run == null)
            {
                Close();
                return;
            }

            RollStock();
            Rebuild();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            ClearSpawned();
            base.OnClose(isShutdown, userData);
        }

        private void RollStock()
        {
            _stock.Clear();
            IRandomStream rng = GameApp.Random.Stream($"shop_w{_run.WeekIndex}_d{_run.CurrentDay}");
            _stock.AddRange(ShopService.RollStock(GameApp.Config.Tables, _run, rng));
        }

        private void Rebuild()
        {
            ClearSpawned();

            _goldText.text = $"金币 {_run.Gold}";
            BuildBuySection();
            BuildSellSection();
        }

        private void BuildBuySection()
        {
            bool empty = _stock.Count == 0;
            _buyEmptyText.gameObject.SetActive(empty);
            _buyContainer.gameObject.SetActive(!empty);
            if (empty)
            {
                return;
            }

            int n = _stock.Count;
            float gap = 0.03f / 0.9f;
            float cardW = (1f - gap * (n - 1)) / n;
            for (int i = 0; i < n; i++)
            {
                ShopEntry entry = _stock[i];
                float minX = i * (cardW + gap);
                ShopBuyCardView card = Instantiate(_buyCardPrefab, _buyContainer);
                PlaceInContainer(card.transform, minX, minX + cardW);

                ShopEntry captured = entry;
                card.Bind(entry.Name, entry.Desc, entry.Price, _run.Gold >= entry.Price, () => OnBuy(captured));
                _spawned.Add(card.gameObject);
            }
        }

        private void BuildSellSection()
        {
            cfg.Tables tables = GameApp.Config.Tables;

            // 出售被动道具 + 删除菜谱池菜品，合并展示在同一区。
            var entries = new List<SellEntry>();
            foreach (RunItemState state in _run.Items)
            {
                cfg.Item item = tables.TbItem.GetOrDefault(state.ItemId);
                if (item != null && item.Kind == cfg.ItemKind.Passive && !state.IsEmpty)
                {
                    entries.Add(SellEntry.Sell(item.Id, item.Name));
                }
            }

            foreach (string dishId in _run.BonusDishIds)
            {
                entries.Add(SellEntry.Delete(dishId, DishName(tables, dishId)));
            }

            bool empty = entries.Count == 0;
            _sellEmptyText.gameObject.SetActive(empty);
            _sellContainer.gameObject.SetActive(!empty);
            if (empty)
            {
                return;
            }

            int shown = Mathf.Min(entries.Count, MaxSellShown);
            float gap = 0.02f / 0.9f;
            float cardW = (1f - gap * (shown - 1)) / shown;
            for (int i = 0; i < shown; i++)
            {
                SellEntry entry = entries[i];
                float minX = i * (cardW + gap);
                ShopSellCardView card = Instantiate(_sellCardPrefab, _sellContainer);
                PlaceInContainer(card.transform, minX, minX + cardW);

                SellEntry captured = entry;
                if (entry.IsDelete)
                {
                    card.Bind(entry.Name, $"删除 -{ShopService.DeleteDishCost}", () => OnDeleteDish(captured.Id));
                }
                else
                {
                    card.Bind(entry.Name, $"卖 +{ShopService.PassiveSellPrice}", () => OnSell(captured.Id));
                }

                _spawned.Add(card.gameObject);
            }
        }

        private static string DishName(cfg.Tables tables, string dishId)
        {
            cfg.DishVariant variant = tables.TbDishVariant.GetOrDefault(dishId);
            if (variant != null)
            {
                cfg.DishBase baseDish = tables.TbDishBase.GetOrDefault(variant.BaseId);
                if (baseDish != null)
                {
                    return baseDish.Name;
                }
            }

            return dishId;
        }

        private static void PlaceInContainer(Transform card, float minX, float maxX)
        {
            var rect = (RectTransform)card;
            rect.anchorMin = new Vector2(minX, 0f);
            rect.anchorMax = new Vector2(maxX, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private void ClearSpawned()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }

            _spawned.Clear();
        }

        private void OnBuy(ShopEntry entry)
        {
            if (ShopService.Purchase(_run, entry))
            {
                _stock.Remove(entry);
                RunPersistence.Save(_run);
                Rebuild();
            }
        }

        private void OnSell(string itemId)
        {
            if (ShopService.SellPassive(_run, itemId))
            {
                RunPersistence.Save(_run);
                Rebuild();
            }
        }

        private void OnDeleteDish(string dishId)
        {
            if (ShopService.DeleteDish(_run, dishId))
            {
                RunPersistence.Save(_run);
                Rebuild();
            }
        }

        private void Close()
        {
            GameApp.UI.CloseUIForm(UIForm);
        }

        private readonly struct SellEntry
        {
            private SellEntry(string id, string name, bool isDelete)
            {
                Id = id;
                Name = name;
                IsDelete = isDelete;
            }

            public string Id { get; }
            public string Name { get; }
            public bool IsDelete { get; }

            public static SellEntry Sell(string id, string name) => new SellEntry(id, name, false);
            public static SellEntry Delete(string id, string name) => new SellEntry(id, name, true);
        }
    }
}
