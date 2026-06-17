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
    /// 系统事件「商店」：用金币购买被动道具，或卖出已有被动道具回血金币。
    /// 固定壳（遮罩/面板/标题/金币/分区标题/空提示/离开按钮/买卖容器）在 ShopForm.prefab，
    /// 进货与出售卡用 ShopBuyCardView / ShopSellCardView 子 prefab 按周确定性数据驱动实例化。
    /// </summary>
    public sealed class ShopForm : UGuiForm
    {
        private const int BuyPrice = 45;
        private const int SellPrice = 20;
        private const int MaxStock = 3;
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
        private readonly List<string> _stock = new();
        private readonly List<GameObject> _spawned = new();

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            _leaveButton.onClick.AddListener(Close);
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
            cfg.Tables tables = GameApp.Config.Tables;
            var pool = new List<string>();
            foreach (cfg.Item item in tables.TbItem.DataList)
            {
                if (item.Kind == cfg.ItemKind.Passive && !_run.ItemIds.Contains(item.Id))
                {
                    pool.Add(item.Id);
                }
            }

            IRandomStream rng = GameApp.Random.Stream($"shop_w{_run.WeekIndex}");
            rng.Shuffle(pool);
            int take = Mathf.Min(MaxStock, pool.Count);
            for (int i = 0; i < take; i++)
            {
                _stock.Add(pool[i]);
            }
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
                cfg.Item item = GameApp.Config.Tables.TbItem.GetOrDefault(_stock[i]);
                if (item == null)
                {
                    continue;
                }

                float minX = i * (cardW + gap);
                ShopBuyCardView card = Instantiate(_buyCardPrefab, _buyContainer);
                PlaceInContainer(card.transform, minX, minX + cardW);

                string captured = item.Id;
                card.Bind(item, BuyPrice, _run.Gold >= BuyPrice, () => OnBuy(captured));
                _spawned.Add(card.gameObject);
            }
        }

        private void BuildSellSection()
        {
            cfg.Tables tables = GameApp.Config.Tables;
            var owned = new List<string>();
            foreach (string id in _run.ItemIds)
            {
                cfg.Item item = tables.TbItem.GetOrDefault(id);
                if (item != null && item.Kind == cfg.ItemKind.Passive)
                {
                    owned.Add(id);
                }
            }

            bool empty = owned.Count == 0;
            _sellEmptyText.gameObject.SetActive(empty);
            _sellContainer.gameObject.SetActive(!empty);
            if (empty)
            {
                return;
            }

            int shown = Mathf.Min(owned.Count, MaxSellShown);
            float gap = 0.02f / 0.9f;
            float cardW = (1f - gap * (shown - 1)) / shown;
            for (int i = 0; i < shown; i++)
            {
                cfg.Item item = tables.TbItem.GetOrDefault(owned[i]);
                float minX = i * (cardW + gap);
                ShopSellCardView card = Instantiate(_sellCardPrefab, _sellContainer);
                PlaceInContainer(card.transform, minX, minX + cardW);

                string captured = item.Id;
                card.Bind(item, SellPrice, () => OnSell(captured));
                _spawned.Add(card.gameObject);
            }
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

        private void OnBuy(string itemId)
        {
            if (_run.Gold < BuyPrice || _run.ItemIds.Contains(itemId))
            {
                return;
            }

            _run.Gold -= BuyPrice;
            _run.ItemIds.Add(itemId);
            _stock.Remove(itemId);
            RunPersistence.Save(_run);
            Rebuild();
        }

        private void OnSell(string itemId)
        {
            if (!_run.ItemIds.Remove(itemId))
            {
                return;
            }

            _run.Gold += SellPrice;
            RunPersistence.Save(_run);
            Rebuild();
        }

        private void Close()
        {
            GameApp.UI.CloseUIForm(UIForm);
        }
    }
}
