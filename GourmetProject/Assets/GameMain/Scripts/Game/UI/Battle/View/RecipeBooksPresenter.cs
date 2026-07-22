using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Model;
using GourmetProject.Game.UI.Hud;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 底部扇形菜谱抽屉（<see cref="RecipeView"/>）的渲染器：把「持有菜谱本 / 商店购买 / 战斗上菜」三种模式的
    /// 卡片构建从 BattleForm 里剥离出来，只负责喂数据，购买/上菜等副作用由外层回调处理。
    /// </summary>
    internal sealed class RecipeBooksPresenter
    {
        private readonly RecipeView _recipeView;

        public RecipeBooksPresenter(RecipeView recipeView)
        {
            _recipeView = recipeView;
        }

        /// <summary>底部扇形菜谱条：按持有的菜谱本铺卡，显示已放数量。showAdd 时末尾追加购买空菜谱卡。</summary>
        public void BuildPersistent(GameRun run, bool showAdd, Action onAdd, Action<int> onInspect = null, int selectedBookIndex = -1)
        {
            if (_recipeView == null || run == null)
            {
                return;
            }

            var books = new List<RecipeView.BookEntry>();
            for (int i = 0; i < run.RecipeBookCount; i++)
            {
                int count = run.GetRecipeBookDishes(i).Count;
                int bookIndex = i;
                books.Add(new RecipeView.BookEntry(
                    $"菜谱{i + 1}",
                    $"{count}",
                    onInspect != null,
                    onInspect == null ? null : () => onInspect.Invoke(bookIndex),
                    true));
            }

            string addCost = showAdd ? $"+ {ShopService.RecipeBookCost(run)}" : null;
            _recipeView.SetBooks(books, showAdd, onAdd, addCost, selectedBookIndex);
        }

        /// <summary>商店态菜谱条：展示持有菜谱本；未满上限时末尾追加唯一的「购买空菜谱」卡（买得起才可点）。</summary>
        public void BuildShop(GameRun run, Action onBuy, Action<int> onInspect = null)
        {
            if (run == null)
            {
                return;
            }

            bool showAdd = run.RecipeBookCount < run.RecipeBookMaxCount;
            bool canBuy = showAdd && run.Gold >= ShopService.RecipeBookCost(run);
            BuildPersistent(run, showAdd, canBuy ? onBuy : null, onInspect);
        }

        /// <summary>战斗态扇形菜谱条：点击卡片查看详情，独立上餐铃负责随机上菜。</summary>
        public void BuildBattle(BattleSession session, Action<int> onServe, Action<int> onInspect = null)
        {
            if (_recipeView == null || session == null)
            {
                return;
            }

            var books = new List<RecipeView.BookEntry>();
            for (int i = 0; i < session.Slots.Count; i++)
            {
                RecipeSlot slot = session.Slots[i];
                int slotIndex = i;
                var dishes = new List<RecipeDishDisplayData>(slot.Count);
                int placeableCount = 0;

                // TODO: 确认战斗菜谱内剩余食物的最终显示排序规则。
                for (int entryIndex = 0; entryIndex < slot.Entries.Count; entryIndex++)
                {
                    RecipeSlotEntry entry = slot.Entries[entryIndex];
                    var dish = session.Database.GetDish(entry.DishId);
                    bool canPlace = session.CanFitRecipeEntry(slotIndex, entryIndex);
                    if (canPlace)
                    {
                        placeableCount++;
                    }

                    dishes.Add(new RecipeDishDisplayData(dish?.Name ?? entry.DishId, canPlace));
                }

                bool serveLimitReached = session.MaxServes >= 0 && session.ServesUsed >= session.MaxServes;
                bool serveInteractable = !session.IsSettled && !serveLimitReached && placeableCount > 0;
                books.Add(new RecipeView.BookEntry(
                    $"菜谱{i + 1}",
                    $"剩 {slot.Count}",
                    onInspect != null,
                    onInspect == null ? null : () => onInspect.Invoke(slotIndex),
                    true,
                    onInspect == null ? null : () => onInspect.Invoke(slotIndex),
                    showBattleContent: true,
                    serveInteractable: serveInteractable,
                    onServe: onServe == null ? null : () => onServe.Invoke(slotIndex),
                    dishes: dishes));
            }

            _recipeView.SetBooks(books);
        }

        /// <summary>战斗菜谱查看态：同步战斗槽剩余数量，点击只切换查看的菜谱，不执行上菜。</summary>
        public void BuildBattleInspect(BattleSession session, Action<int> onInspect, int selectedBookIndex = -1)
        {
            if (_recipeView == null || session == null)
            {
                return;
            }

            var books = new List<RecipeView.BookEntry>();
            for (int i = 0; i < session.Slots.Count; i++)
            {
                RecipeSlot slot = session.Slots[i];
                int slotIndex = i;
                books.Add(new RecipeView.BookEntry(
                    $"菜谱{i + 1}",
                    $"剩 {slot.Count}",
                    onInspect != null,
                    onInspect == null ? null : () => onInspect.Invoke(slotIndex),
                    true,
                    onInspect == null ? null : () => onInspect.Invoke(slotIndex)));
            }

            _recipeView.SetBooks(books, selectedBookIndex: selectedBookIndex);
        }

        public void RemoveAddCard()
        {
            _recipeView?.RemoveAddCard();
        }

        public void SetState(RecipeView.RecipeState state)
        {
            _recipeView?.SetState(state);
        }
    }
}
