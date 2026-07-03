using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 商店「中部态」面板：作为 <c>BattleForm</c> 常驻壳的中部内容之一（不再是独立弹层）。
    /// 常驻壳（左列信息 / 行动轴 / 右列道具 / 底部扇形菜谱条）由 BattleForm 提供，本面板只负责中部四区
    /// （食物 / 碎片包 / 被动 / 主动）与「编辑菜谱」入口。编辑菜谱已抽出为独立状态
    /// <see cref="RecipeEditPanel"/>，点入口时通过回调交回 BattleForm 状态机切换。
    /// </summary>
    public sealed class ShopForm : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private GameObject _shopPanel;

        [Header("Top")]
        [SerializeField] private Text _goldText;
        [SerializeField] private Button _leaveButton;

        [Header("Shop Sections")]
        [SerializeField] private RectTransform _foodContainer;
        [SerializeField] private RectTransform _fragmentContainer;
        [SerializeField] private RectTransform _passiveContainer;
        [SerializeField] private RectTransform _activeContainer;
        [SerializeField] private Text _foodEmptyText;
        [SerializeField] private Text _fragmentEmptyText;
        [SerializeField] private Text _passiveEmptyText;
        [SerializeField] private Text _activeEmptyText;
        [SerializeField] private ShopBuyCardView _buyCardPrefab;

        [Header("Recipe Entry")]
        [SerializeField] private Button _editRecipeButton;
        [SerializeField] private Text _recipeLimitText;

        private readonly List<ShopEntry> _stock = new();
        private readonly List<GameObject> _spawned = new();
        private GameRun _run;
        private string _shopKey;
        private bool _wired;

        private Action _onLeave;
        private Action _onChanged;
        private Action _onOpenRecipeEdit;
        private Action _onOpenBoardEdit;

        private void Awake()
        {
            EnsureWired();
        }

        private void OnDisable()
        {
            ClearSpawned();
        }

        /// <summary>由 BattleForm 进入商店态时调用：刷新库存并展示商店购买区。</summary>
        /// <param name="onLeave">点「离开商店」时回调（BattleForm 继续周循环编排）。</param>
        /// <param name="onChanged">商店内数据变化（买卖）后回调，用于刷新常驻壳金币/道具与底部菜谱条。</param>
        /// <param name="onOpenRecipeEdit">点「编辑菜谱」时回调：BattleForm 切到编辑菜谱态（独立状态）。</param>
        /// <param name="onOpenBoardEdit">购买碎片包后回调：BattleForm 切到棋盘编辑页手动拼贴。</param>
        public void Open(Action onLeave, Action onChanged, Action onOpenRecipeEdit = null, Action onOpenBoardEdit = null)
        {
            EnsureWired();
            _onLeave = onLeave;
            _onChanged = onChanged;
            _onOpenRecipeEdit = onOpenRecipeEdit;
            _onOpenBoardEdit = onOpenBoardEdit;

            _run = GameRunContext.Current;
            if (_run == null)
            {
                _onLeave?.Invoke();
                return;
            }

            if (_shopPanel != null)
            {
                _shopPanel.SetActive(true);
            }

            RollStock();
            Rebuild();
        }

        private void EnsureWired()
        {
            if (_wired)
            {
                return;
            }

            _wired = true;

            if (_leaveButton != null)
            {
                _leaveButton.onClick.RemoveAllListeners();
                _leaveButton.onClick.AddListener(OnLeaveClicked);
            }

            if (_editRecipeButton != null)
            {
                _editRecipeButton.onClick.RemoveAllListeners();
                _editRecipeButton.onClick.AddListener(() => _onOpenRecipeEdit?.Invoke());
            }
        }

        private void RollStock()
        {
            _stock.Clear();
            _shopKey = GameRun.BuildShopKey(_run.WeekIndex, _run.CurrentDay);
            if (_run.HasPendingShopStock(_shopKey))
            {
                _stock.AddRange(_run.GetPendingShopStock(_shopKey));
                return;
            }

            IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Shop, _shopKey);
            IRandomStream lootRng = GameApp.Random.DomainStream(SeedDomains.Loot, $"shop_{_shopKey}");
            _stock.AddRange(ShopService.RollStock(GameApp.Config.Tables, _run, rng, lootRng));
            // 掷库存只写内存 pending（同一天重开商店复用同一份）；不存档，退出商店结算时才统一存。
            _run.SetPendingShopStock(_shopKey, _stock);
        }

        private void Rebuild()
        {
            ClearSpawned();

            SetText(_goldText, $"金币 {_run.Gold}");
            BuildBuySection(ShopEntryKind.Dish, _foodContainer, _foodEmptyText, "暂无食物");
            BuildBuySection(ShopEntryKind.Fragment, _fragmentContainer, _fragmentEmptyText, "暂无碎片包");
            BuildBuySection(ShopEntryKind.PassiveItem, _passiveContainer, _passiveEmptyText, "暂无被动道具");
            BuildBuySection(ShopEntryKind.ActiveItem, _activeContainer, _activeEmptyText, "暂无主动道具");
            SetText(_recipeLimitText, $"{_run.RecipeBookCount}/{GameRun.MaxRecipeBookCount}");

            _onChanged?.Invoke();
        }

        private void BuildBuySection(ShopEntryKind kind, RectTransform container, Text emptyText, string emptyMessage)
        {
            if (container == null || _buyCardPrefab == null)
            {
                SetEmpty(emptyText, true, emptyMessage);
                return;
            }

            int count = 0;
            foreach (ShopEntry entry in _stock)
            {
                if (entry.Kind != kind)
                {
                    continue;
                }

                ShopBuyCardView card = Instantiate(_buyCardPrefab, container);
                card.gameObject.name = $"ShopBuy_{kind}_{count}";
                ShopEntry captured = entry;
                card.Bind(entry.Name, entry.Desc, entry.Price, _run.Gold >= entry.Price, () => OnBuy(captured));
                _spawned.Add(card.gameObject);
                count++;
            }

            SetEmpty(emptyText, count == 0, emptyMessage);
            container.gameObject.SetActive(count > 0);
        }

        /// <summary>供 BattleForm 在底部扇形「购买空菜谱」后回调：重建商店购买区与上限文本。</summary>
        public void RefreshShop()
        {
            if (_run != null)
            {
                Rebuild();
            }
        }

        private void OnBuy(ShopEntry entry)
        {
            if (!ShopService.Purchase(_run, entry))
            {
                return;
            }

            _stock.Remove(entry);
            _run.SetPendingShopStock(_shopKey, _stock);
            Rebuild();

            // 碎片包：购买后进入棋盘编辑页手动拼贴（金币已扣，待开包状态已置）。
            if (entry.Kind == ShopEntryKind.Fragment && _run.HasPendingFragmentPack && _onOpenBoardEdit != null)
            {
                _onOpenBoardEdit.Invoke();
            }
        }

        private void OnLeaveClicked()
        {
            // 商店内买卖只改内存，不即时存档；离开商店 = 结算，由编排层（WeekLoopController）
            // 在 OnShopClosed 续接里 Commit（推进步数）并统一存最终态。这里只负责继续编排。
            _onLeave?.Invoke();
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

        private static void SetEmpty(Text emptyText, bool visible, string message)
        {
            if (emptyText == null)
            {
                return;
            }

            emptyText.gameObject.SetActive(visible);
            emptyText.text = message ?? string.Empty;
        }

        private static void SetText(Text text, string value)
        {
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }
    }
}
