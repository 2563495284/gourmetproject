using System;
using System.Collections.Generic;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Model;
using GourmetProject.Game.UI.Hud;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 底部菜谱抽屉（<see cref="RecipeView"/>）的渲染器：负责局外菜谱与战斗菜谱的展示。
    /// </summary>
    internal sealed class RecipeBooksPresenter
    {
        private readonly RecipeView _recipeView;

        public RecipeBooksPresenter(RecipeView recipeView)
        {
            _recipeView = recipeView;
        }

        /// <summary>底部菜谱条：展示唯一菜谱及已放数量。</summary>
        public void BuildPersistent(GameRun run, Action<int> onInspect = null, int selectedBookIndex = -1)
        {
            if (_recipeView == null || run == null)
            {
                return;
            }

            var books = new List<RecipeView.BookEntry>
            {
                new RecipeView.BookEntry(
                    "菜谱",
                    $"{run.RecipeDishes.Count}",
                    onInspect != null,
                    onInspect == null ? null : () => onInspect.Invoke(0)),
            };
            _recipeView.SetBooks(books, selectedBookIndex: selectedBookIndex);
        }

        /// <summary>商店态菜谱条：展示唯一菜谱。</summary>
        public void BuildShop(GameRun run, Action<int> onInspect = null)
        {
            BuildPersistent(run, onInspect);
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
                    "菜谱",
                    $"剩 {slot.Count}",
                    onInspect != null,
                    onInspect == null ? null : () => onInspect.Invoke(slotIndex),
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
                    "菜谱",
                    $"剩 {slot.Count}",
                    onInspect != null,
                    onInspect == null ? null : () => onInspect.Invoke(slotIndex),
                    onInspect == null ? null : () => onInspect.Invoke(slotIndex)));
            }

            _recipeView.SetBooks(books, selectedBookIndex: selectedBookIndex);
        }

        public void SetState(RecipeView.RecipeState state)
        {
            _recipeView?.SetState(state);
        }
    }
}
