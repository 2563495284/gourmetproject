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
    /// 进货列表按周确定性生成；买卖即时存档。可从周地图进入，离开后回到周地图。
    /// </summary>
    public sealed class ShopForm : UGuiForm
    {
        private const int BuyPrice = 45;
        private const int SellPrice = 20;

        private static readonly Color Dim = new(0f, 0f, 0f, 0.8f);
        private static readonly Color Box = new(0.18f, 0.16f, 0.12f, 1f);
        private static readonly Color CardColor = new(0.3f, 0.26f, 0.18f, 1f);

        private GameRun _run;
        private readonly List<string> _stock = new();
        private RectTransform _content;
        private Text _goldText;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            UiBuilder.AddImage(CachedTransform, "Dim", Dim, 0f, 0f, 1f, 1f);
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
            int take = Mathf.Min(3, pool.Count);
            for (int i = 0; i < take; i++)
            {
                _stock.Add(pool[i]);
            }
        }

        private void Rebuild()
        {
            if (_content != null)
            {
                Destroy(_content.gameObject);
                _content = null;
            }

            _content = UiBuilder.NewRect("Content", CachedTransform);
            UiBuilder.Anchor(_content, 0.14f, 0.14f, 0.86f, 0.86f);
            UiBuilder.AddImage(_content, "Box", Box, 0f, 0f, 1f, 1f);

            UiBuilder.AddText(_content, "Title", "商店", 34, Color.white, 0.05f, 0.88f, 0.6f, 0.98f, TextAnchor.MiddleLeft);
            _goldText = UiBuilder.AddText(_content, "Gold", $"金币 {_run.Gold}", 26, new Color(1f, 0.9f, 0.5f, 1f),
                0.5f, 0.88f, 0.95f, 0.98f, TextAnchor.MiddleRight);

            BuildBuySection();
            BuildSellSection();

            UiBuilder.AddButton(_content, "Leave", "离开商店", new Color(0.35f, 0.35f, 0.4f, 1f),
                0.38f, 0.03f, 0.62f, 0.11f, Close, 24);
        }

        private void BuildBuySection()
        {
            UiBuilder.AddText(_content, "BuyTitle", "进货（购买被动道具）", 20, new Color(0.9f, 0.9f, 0.9f, 1f),
                0.05f, 0.78f, 0.95f, 0.86f, TextAnchor.MiddleLeft);

            if (_stock.Count == 0)
            {
                UiBuilder.AddText(_content, "BuyEmpty", "今日已无新货。", 18, new Color(1f, 1f, 1f, 0.6f),
                    0.05f, 0.56f, 0.95f, 0.76f, TextAnchor.UpperLeft);
                return;
            }

            int n = _stock.Count;
            float gap = 0.03f;
            float cardW = (0.9f - gap * (n - 1)) / n;
            for (int i = 0; i < n; i++)
            {
                cfg.Item item = GameApp.Config.Tables.TbItem.GetOrDefault(_stock[i]);
                if (item == null)
                {
                    continue;
                }

                float minX = 0.05f + i * (cardW + gap);
                float maxX = minX + cardW;
                RectTransform card = UiBuilder.NewRect($"Buy_{item.Id}", _content);
                UiBuilder.Anchor(card, minX, 0.42f, maxX, 0.76f);
                UiBuilder.AddImage(card, "Bg", CardColor, 0f, 0f, 1f, 1f, 5f);
                UiBuilder.AddText(card, "Name", item.Name, 20, Color.white, 0.06f, 0.78f, 0.94f, 0.96f);
                UiBuilder.AddText(card, "Desc", item.Desc, 15, new Color(0.9f, 0.9f, 0.9f, 1f),
                    0.08f, 0.34f, 0.92f, 0.76f, TextAnchor.UpperLeft);

                string captured = item.Id;
                Button buy = UiBuilder.AddButton(card, "Buy", $"购买 {BuyPrice}", new Color(0.9f, 0.6f, 0.2f, 1f),
                    0.1f, 0.05f, 0.9f, 0.28f, () => OnBuy(captured), 18);
                buy.interactable = _run.Gold >= BuyPrice;
            }
        }

        private void BuildSellSection()
        {
            UiBuilder.AddText(_content, "SellTitle", "出售（持有的被动道具）", 20, new Color(0.9f, 0.9f, 0.9f, 1f),
                0.05f, 0.32f, 0.95f, 0.40f, TextAnchor.MiddleLeft);

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

            if (owned.Count == 0)
            {
                UiBuilder.AddText(_content, "SellEmpty", "暂无可出售的被动道具。", 16, new Color(1f, 1f, 1f, 0.6f),
                    0.05f, 0.14f, 0.95f, 0.30f, TextAnchor.UpperLeft);
                return;
            }

            int shown = Mathf.Min(owned.Count, 4);
            float gap = 0.02f;
            float cardW = (0.9f - gap * (shown - 1)) / shown;
            for (int i = 0; i < shown; i++)
            {
                cfg.Item item = tables.TbItem.GetOrDefault(owned[i]);
                float minX = 0.05f + i * (cardW + gap);
                float maxX = minX + cardW;
                RectTransform card = UiBuilder.NewRect($"Sell_{item.Id}_{i}", _content);
                UiBuilder.Anchor(card, minX, 0.14f, maxX, 0.30f);
                UiBuilder.AddImage(card, "Bg", new Color(0.25f, 0.22f, 0.28f, 1f), 0f, 0f, 1f, 1f, 4f);
                UiBuilder.AddText(card, "Name", item.Name, 15, Color.white, 0.05f, 0.55f, 0.95f, 0.95f);

                string captured = item.Id;
                UiBuilder.AddButton(card, "Sell", $"卖 +{SellPrice}", new Color(0.4f, 0.55f, 0.4f, 1f),
                    0.1f, 0.06f, 0.9f, 0.5f, () => OnSell(captured), 15);
            }
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
