using System;
using System.Collections;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 「编辑菜谱态」独立面板：作为 <c>BattleForm</c> 常驻壳中部内容区的一个状态（不再内嵌在 ShopForm 里）。
    /// 全屏（盖住中部与行动轴，左右壳仍常驻），支持跨菜谱拖拽移动、拖入垃圾桶删除（花金币）、离开编辑返回商店。
    /// 面板显隐与切换动画由 BattleForm 的状态机驱动，本组件只负责编辑区内容的构建与拖拽交互。
    /// </summary>
    public sealed class RecipeEditPanel : MonoBehaviour
    {
        private const float DesiredBookGap = 24f;
        private const float MinBookScale = 0.1f;

        [Header("Books")]
        [SerializeField] private RectTransform _editBooksContainer;
        [SerializeField] private RecipeEditBookView _editBookPrefab;
        [SerializeField] private RecipeEditDishView _editDishPrefab;

        [Header("Trash / Exit")]
        [SerializeField] private RecipeTrashDropZone _trashZone;
        [SerializeField] private Text _trashPriceText;
        [SerializeField] private Button _exitEditButton;

        private readonly List<GameObject> _spawned = new();
        private GameRun _run;
        private Coroutine _pendingRebuild;
        private Action _onExit;
        private Action _onChanged;
        private bool _wired;

        private void Awake()
        {
            EnsureWired();
        }

        private void OnDisable()
        {
            ClearSpawned();
            if (_pendingRebuild != null)
            {
                StopCoroutine(_pendingRebuild);
                _pendingRebuild = null;
            }
        }

        /// <summary>由 BattleForm 进入编辑菜谱态时调用：绑定运行数据与回调并铺出编辑区。</summary>
        /// <param name="run">当前肉鸽运行。</param>
        /// <param name="onExit">点「离开编辑」按钮时回调（BattleForm 返回商店态）。</param>
        /// <param name="onChanged">编辑（移动 / 删除）后回调，用于刷新常驻壳金币与底部菜谱条。</param>
        public void Open(GameRun run, Action onExit, Action onChanged)
        {
            EnsureWired();
            _run = run;
            _onExit = onExit;
            _onChanged = onChanged;
            Rebuild();
        }

        /// <summary>供外部（如金币变化）请求重建编辑区。</summary>
        public void Refresh()
        {
            if (_run != null && isActiveAndEnabled)
            {
                Rebuild();
            }
        }

        private void EnsureWired()
        {
            if (_wired)
            {
                return;
            }

            _wired = true;
            if (_exitEditButton != null)
            {
                _exitEditButton.onClick.RemoveAllListeners();
                _exitEditButton.onClick.AddListener(() => _onExit?.Invoke());
            }
        }

        private void Rebuild()
        {
            ClearSpawned();
            if (_run == null || _editBooksContainer == null || _editBookPrefab == null || _editDishPrefab == null)
            {
                return;
            }

            ConfigureBooksLayoutGroup();

            if (_trashZone != null)
            {
                _trashZone.Bind(OnDishDroppedToTrash);
            }

            SetText(_trashPriceText, $"-{ShopService.DeleteDishCost}");

            var books = new List<RecipeEditBookView>(_run.RecipeBookCount);
            for (int i = 0; i < _run.RecipeBookCount; i++)
            {
                IReadOnlyList<string> dishes = _run.GetRecipeBookDishes(i);
                RecipeEditBookView book = Instantiate(_editBookPrefab, _editBooksContainer);
                book.gameObject.name = $"RecipeEditBook_{i + 1}";
                book.Bind(i, $"菜谱{i + 1}", $"{dishes.Count}/{GameRun.RecipeBookCapacity}", OnDishDroppedToBook);
                _spawned.Add(book.gameObject);
                books.Add(book);

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

            FitBooksToContainer(books);
            _onChanged?.Invoke();
        }

        private void ConfigureBooksLayoutGroup()
        {
            HorizontalLayoutGroup layout = _editBooksContainer.GetComponent<HorizontalLayoutGroup>();
            if (layout == null)
            {
                return;
            }

            layout.enabled = false;
        }

        private void FitBooksToContainer(IReadOnlyList<RecipeEditBookView> books)
        {
            if (books == null || books.Count == 0)
            {
                return;
            }

            RectTransform firstBookRect = (RectTransform)books[0].transform;
            Vector2 bookSize = firstBookRect.sizeDelta;
            if (bookSize.x <= 0f || bookSize.y <= 0f)
            {
                bookSize = firstBookRect.rect.size;
            }

            if (bookSize.x <= 0f || bookSize.y <= 0f)
            {
                return;
            }

            float availableWidth = _editBooksContainer.rect.width;
            float availableHeight = _editBooksContainer.rect.height;
            if (availableWidth <= 0f || availableHeight <= 0f)
            {
                return;
            }

            float totalDesiredGap = DesiredBookGap * (books.Count + 1);
            float widthScale = (availableWidth - totalDesiredGap) / (bookSize.x * books.Count);
            float heightScale = availableHeight / bookSize.y;
            float scale = Mathf.Clamp(Mathf.Min(widthScale, heightScale, 1f), MinBookScale, 1f);
            float scaledBookWidth = bookSize.x * scale;
            float gap = books.Count == 1
                ? (availableWidth - scaledBookWidth) * 0.5f
                : (availableWidth - scaledBookWidth * books.Count) / (books.Count + 1);
            gap = Mathf.Max(0f, gap);
            float x = -availableWidth * 0.5f + gap + scaledBookWidth * 0.5f;

            foreach (RecipeEditBookView book in books)
            {
                RectTransform rect = (RectTransform)book.transform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = bookSize;
                rect.localScale = new Vector3(scale, scale, 1f);
                rect.anchoredPosition = new Vector2(x, 0f);
                x += scaledBookWidth + gap;
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
                QueueRebuild();
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
                QueueRebuild();
            }
        }

        private void QueueRebuild()
        {
            if (_pendingRebuild != null)
            {
                StopCoroutine(_pendingRebuild);
            }

            _pendingRebuild = StartCoroutine(RebuildNextFrame());
        }

        private IEnumerator RebuildNextFrame()
        {
            yield return null;
            _pendingRebuild = null;
            Rebuild();
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

        private static void SetText(Text text, string value)
        {
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }
    }
}
