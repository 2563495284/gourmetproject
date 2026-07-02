using System.Collections;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Flow;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 系统节点「商店」：Prefab 中固定摆好四个商品区、底部菜谱条和编辑菜谱页。
    /// 运行时只向这些容器绑定商品卡/菜谱卡数据，布局由 Prefab 上的 LayoutGroup 交给设计师调。
    /// </summary>
    public sealed class ShopForm : UGuiForm
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
        private bool _notifiedClosed;
        private string _shopKey;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

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

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            _run = GameRunContext.Current;
            _notifiedClosed = false;
            if (_run == null)
            {
                Close();
                return;
            }

            RollStock();
            ShowShopPanel();
            Rebuild();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            ClearSpawned();
            _pendingEditorRebuild = null;
            NotifyClosedOnce();
            base.OnClose(isShutdown, userData);
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
            _run.SetPendingShopStock(_shopKey, _stock);
            RunPersistence.Save(_run);
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
            BuildRecipeEditor();
        }

        private void CloseRecipeEditor()
        {
            ShowShopPanel();
            Rebuild();
        }

        private void ShowShopPanel()
        {
            if (_shopPanel != null)
            {
                _shopPanel.SetActive(true);
            }

            if (_recipeEditPanel != null)
            {
                _recipeEditPanel.SetActive(false);
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
                RunPersistence.Save(_run);
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
                RunPersistence.Save(_run);
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
                RunPersistence.Save(_run);
                Rebuild();
            }
        }

        private void OnBuy(ShopEntry entry)
        {
            if (ShopService.Purchase(_run, entry))
            {
                _stock.Remove(entry);
                _run.SetPendingShopStock(_shopKey, _stock);
                RunPersistence.Save(_run);
                Rebuild();
            }
        }

        private void OnLeaveClicked()
        {
            if (_run != null)
            {
                RunPersistence.Save(_run);
            }

            Close();
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

        private void Close()
        {
            GameApp.UI.CloseUIForm(UIForm);
        }

        private void NotifyClosedOnce()
        {
            if (_notifiedClosed)
            {
                return;
            }

            _notifiedClosed = true;
            BattleForm.Active?.OnShopClosed();
        }
    }
}
