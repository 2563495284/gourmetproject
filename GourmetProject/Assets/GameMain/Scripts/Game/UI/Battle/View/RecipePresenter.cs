using System;
using System.Collections.Generic;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Model;
using GourmetProject.Game.UI.Hud;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>BattleForm 固定菜谱的渲染器：负责局外与战斗数据展示。</summary>
    internal sealed class RecipePresenter
    {
        private readonly RecipeCardView _recipe;

        public RecipePresenter(RecipeCardView recipe)
        {
            _recipe = recipe;
        }

        /// <summary>展示唯一菜谱及已放数量。</summary>
        public void BuildPersistent(GameRun run, Action<int> onInspect = null, int selectedBookIndex = -1)
        {
            if (_recipe == null || run == null)
            {
                return;
            }

            _recipe.Bind(
                $"{run.RecipeDishes.Count}",
                onInspect != null,
                onInspect == null ? null : () => onInspect.Invoke(0));
            _recipe.SetTargetHighlight(selectedBookIndex == 0, true);
        }

        /// <summary>商店态展示唯一菜谱。</summary>
        public void BuildShop(GameRun run, Action<int> onInspect = null)
        {
            BuildPersistent(run, onInspect);
        }

        /// <summary>战斗态固定菜谱：点击卡片查看详情，独立上餐铃负责随机上菜。</summary>
        public void BuildBattle(BattleSession session, Action<int> onServe, Action<int> onInspect = null)
        {
            if (_recipe == null || session == null || session.Slots.Count == 0)
            {
                return;
            }

            const int slotIndex = 0;
            RecipeSlot slot = session.Slots[slotIndex];
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
            _recipe.Bind(
                $"剩 {slot.Count}",
                onInspect != null,
                onInspect == null ? null : () => onInspect.Invoke(slotIndex),
                onInspect == null ? null : () => onInspect.Invoke(slotIndex),
                showBattleContent: true,
                serveInteractable: serveInteractable,
                onServe: onServe == null ? null : () => onServe.Invoke(slotIndex),
                dishes: dishes);
            _recipe.SetTargetHighlight(false, true);
        }

        /// <summary>战斗菜谱查看态：同步剩余数量，点击只打开唯一菜谱，不执行上菜。</summary>
        public void BuildBattleInspect(BattleSession session, Action<int> onInspect, int selectedBookIndex = -1)
        {
            if (_recipe == null || session == null || session.Slots.Count == 0)
            {
                return;
            }

            const int slotIndex = 0;
            RecipeSlot slot = session.Slots[slotIndex];
            _recipe.Bind(
                $"剩 {slot.Count}",
                onInspect != null,
                onInspect == null ? null : () => onInspect.Invoke(slotIndex),
                onInspect == null ? null : () => onInspect.Invoke(slotIndex));
            _recipe.SetTargetHighlight(selectedBookIndex == slotIndex, true);
        }
    }
}
