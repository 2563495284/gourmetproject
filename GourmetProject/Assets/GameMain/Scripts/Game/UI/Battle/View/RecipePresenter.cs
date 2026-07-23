using System;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
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

        /// <summary>战斗态固定菜谱：只显示剩余数量，点击卡片查看剩余食物。</summary>
        public void BuildBattle(BattleSession session, Action<int> onInspect = null)
        {
            if (_recipe == null || session == null || session.Slots.Count == 0)
            {
                return;
            }

            const int slotIndex = 0;
            RecipeSlot slot = session.Slots[slotIndex];
            _recipe.Bind(
                $"{slot.Count}",
                onInspect != null,
                onInspect == null ? null : () => onInspect.Invoke(slotIndex),
                onInspect == null ? null : () => onInspect.Invoke(slotIndex));
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
                $"{slot.Count}",
                onInspect != null,
                onInspect == null ? null : () => onInspect.Invoke(slotIndex),
                onInspect == null ? null : () => onInspect.Invoke(slotIndex));
            _recipe.SetTargetHighlight(selectedBookIndex == slotIndex, true);
        }
    }
}
