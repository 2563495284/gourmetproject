using System;
using System.Collections;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 商店「中部态」面板：作为 <c>BattleForm</c> 常驻壳的中部内容之一（不再是独立弹层）。
    /// 常驻壳（左列信息 / 行动轴 / 右列道具 / 底部抽屉）由 BattleForm 提供，本面板只负责中部四区、
    /// 底部菜谱条与全屏编辑菜谱页。由 BattleForm 通过 <see cref="Open"/> / SetActive 驱动显隐。
    /// </summary>
    public sealed class ShopForm : MonoBehaviour
    {
        [Header("Root States")]
        [SerializeField] private GameObject _shopPanel;
        [SerializeField] private GameObject _recipeEditPanel;

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

        [Header("Recipe Strip")]
        [SerializeField] private RectTransform _recipeStripContainer;
        [SerializeField] private ShopRecipeBookView _recipeBookPrefab;
        [SerializeField] private Button _buyRecipeBookButton;
        [SerializeField] private Button _editRecipeButton;
        [SerializeField] private Text _recipeLimitText;

        [Header("Recipe Editor")]
        [SerializeField] private RectTransform _editBooksContainer;
        [SerializeField] private RecipeEditBookView _editBookPrefab;
        [SerializeField] private RecipeEditDishView _editDishPrefab;
        [SerializeField] private RecipeTrashDropZone _trashZone;
        [SerializeField] private Text _trashPriceText;
        [SerializeField] private Button _exitEditButton;

        private readonly List<ShopEntry> _stock = new();
        private readonly List<GameObject> _spawned = new();
        private GameRun _run;
        private Coroutine _pendingEditorRebuild;
        private string _shopKey;
        private bool _wired;

        private Action _onLeave;
        private Action _onChanged;
        private Action<bool> _onEditorToggled;
        private Action _onOpenBoardEdit;

        /// <summary>行动轴处于「编辑菜谱态」时为 true：此时应隐藏行动轴（由 BattleForm 查询）。</summary>
        public bool IsEditingRecipe => _recipeEditPanel != null && _recipeEditPanel.activeSelf;

        private void Awake()
        {
            EnsureWired();
        }

        private void OnDisable()
        {
            ClearSpawned();
            if (_pendingEditorRebuild != null)
            {
                StopCoroutine(_pendingEditorRebuild);
                _pendingEditorRebuild = null;
            }
        }

        /// <summary>由 BattleForm 进入商店态时调用：刷新库存并展示商店购买区。</summary>
        /// <param name="onLeave">点「离开商店」时回调（BattleForm 继续周循环编排）。</param>
        /// <param name="onChanged">商店内数据变化（买卖 / 删菜）后回调，用于刷新常驻壳金币/道具。</param>
        /// <param name="onEditorToggled">进入(true)/退出(false)全屏编辑菜谱态时回调（BattleForm 隐藏/恢复行动轴）。</param>
        /// <param name="onOpenBoardEdit">购买碎片包后回调：BattleForm 切到棋盘编辑页手动拼贴。</param>
        public void Open(Action onLeave, Action onChanged, Action<bool> onEditorToggled = null, Action onOpenBoardEdit = null)
        {
            EnsureWired();
            _onLeave = onLeave;
            _onChanged = onChanged;
            _onEditorToggled = onEditorToggled;
            _onOpenBoardEdit = onOpenBoardEdit;

            _run = GameRunContext.Current;
            if (_run == null)
            {
                _onLeave?.Invoke();
                return;
            }

            RollStock();
            ShowShopPanel();
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
                _editRecipeButton.onClick.AddListener(OpenRecipeEditor);
            }

            if (_buyRecipeBookButton != null)
            {
                _buyRecipeBookButton.onClick.RemoveAllListeners();
                _buyRecipeBookButton.onClick.AddListener(OnBuyRecipeBook);
            }

            if (_exitEditButton != null)
            {
                _exitEditButton.onClick.RemoveAllListeners();
                _exitEditButton.onClick.AddListener(CloseRecipeEditor);
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
            BuildRecipeStrip();

            if (_recipeEditPanel != null && _recipeEditPanel.activeSelf)
            {
                BuildRecipeEditor();
            }

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

        private void BuildRecipeStrip()
        {
            if (_recipeStripContainer != null && _recipeBookPrefab != null)
            {
                for (int i = 0; i < _run.RecipeBookCount; i++)
                {
                    IReadOnlyList<string> dishes = _run.GetRecipeBookDishes(i);
                    ShopRecipeBookView book = Instantiate(_recipeBookPrefab, _recipeStripContainer);
                    book.gameObject.name = $"RecipeBook_{i + 1}";
                    book.Bind($"菜谱{i + 1}", $"{dishes.Count}/{GameRun.RecipeBookCapacity}", false, null);
                    _spawned.Add(book.gameObject);
                }
            }

            bool canBuy = _run.CanAddRecipeBook && _run.Gold >= ShopService.EmptyRecipeBookPrice;
            if (_buyRecipeBookButton != null)
            {
                _buyRecipeBookButton.gameObject.SetActive(_run.CanAddRecipeBook);
                _buyRecipeBookButton.interactable = canBuy;
                SetButtonText(_buyRecipeBookButton, $"+ {ShopService.EmptyRecipeBookPrice}");
            }

            SetText(_recipeLimitText, $"{_run.RecipeBookCount}/{GameRun.MaxRecipeBookCount}");
        }

        private void OpenRecipeEditor()
        {
            if (_recipeEditPanel == null)
            {
                return;
            }

            if (_shopPanel != null)
            {
                _shopPanel.SetActive(false);
            }

            _recipeEditPanel.SetActive(true);
            _onEditorToggled?.Invoke(true);
            BuildRecipeEditor();
        }

        private void CloseRecipeEditor()
        {
            ShowShopPanel();
            Rebuild();
        }

        private void ShowShopPanel()
        {
            bool wasEditing = _recipeEditPanel != null && _recipeEditPanel.activeSelf;

            if (_shopPanel != null)
            {
                _shopPanel.SetActive(true);
            }

            if (_recipeEditPanel != null)
            {
                _recipeEditPanel.SetActive(false);
            }

            if (wasEditing)
            {
                _onEditorToggled?.Invoke(false);
            }
        }

        private void BuildRecipeEditor()
        {
            if (_editBooksContainer == null || _editBookPrefab == null || _editDishPrefab == null)
            {
                return;
            }

            ClearSpawned();
            if (_trashZone != null)
            {
                _trashZone.Bind(OnDishDroppedToTrash);
            }

            SetText(_trashPriceText, $"-{ShopService.DeleteDishCost}");

            for (int i = 0; i < _run.RecipeBookCount; i++)
            {
                IReadOnlyList<string> dishes = _run.GetRecipeBookDishes(i);
                RecipeEditBookView book = Instantiate(_editBookPrefab, _editBooksContainer);
                book.gameObject.name = $"RecipeEditBook_{i + 1}";
                book.Bind(i, $"菜谱{i + 1}", $"{dishes.Count}/{GameRun.RecipeBookCapacity}", OnDishDroppedToBook);
                _spawned.Add(book.gameObject);

                RectTransform dishContainer = book.DishContainer;
                if (dishContainer == null)
                {
                    continue;
                }

                for (int k = 0; k < dishes.Count; k++)
                {
                    string dishId = dishes[k];
                    RecipeEditDishView dish = Instantiate(_editDishPrefab, dishContainer);
                    dish.gameObject.name = $"RecipeDish_{i + 1}_{k + 1}";
                    dish.Bind(DishName(GameApp.Config.Tables, dishId), DishShapeText(dishId), i, k);
                    _spawned.Add(dish.gameObject);
                }
            }
        }

        private void OnDishDroppedToBook(RecipeEditDishView dish, int targetBookIndex)
        {
            if (dish == null)
            {
                return;
            }

            if (ShopService.MoveDish(_run, dish.BookIndex, dish.DishIndex, targetBookIndex))
            {
                QueueEditorRebuild();
            }
        }

        private void OnDishDroppedToTrash(RecipeEditDishView dish)
        {
            if (dish == null)
            {
                return;
            }

            if (ShopService.DeleteDishAt(_run, dish.BookIndex, dish.DishIndex))
            {
                QueueEditorRebuild();
            }
        }

        private void QueueEditorRebuild()
        {
            if (_pendingEditorRebuild != null)
            {
                StopCoroutine(_pendingEditorRebuild);
            }

            _pendingEditorRebuild = StartCoroutine(RebuildEditorNextFrame());
        }

        private IEnumerator RebuildEditorNextFrame()
        {
            yield return null;
            _pendingEditorRebuild = null;
            Rebuild();
        }

        private void OnBuyRecipeBook()
        {
            if (ShopService.PurchaseRecipeBook(_run))
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
            // 商店内买卖/删菜只改内存，不即时存档；离开商店 = 结算，由编排层（WeekLoopController）
            // 在 OnShopClosed 续接里 Commit（推进步数）并统一存最终态。这里只负责继续编排。
            _onLeave?.Invoke();
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

        private string DishShapeText(string dishId)
        {
            DishDef dish = _run?.Database.GetDish(dishId);
            return dish?.Shape == null ? string.Empty : $"{dish.Shape.Width}x{dish.Shape.Height}";
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

        private static void SetButtonText(Button button, string value)
        {
            if (button == null)
            {
                return;
            }

            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = value ?? string.Empty;
            }
        }
    }
}
