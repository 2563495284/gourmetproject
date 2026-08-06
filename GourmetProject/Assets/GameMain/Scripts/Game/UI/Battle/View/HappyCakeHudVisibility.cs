using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 「欢乐蛋糕」HUD 显隐规则：玩家食谱或当前页面可见候选中存在蛋糕时显示。
    /// 页面候选必须按当前页面过滤，避免离开商店/奖励页后残留数据继续触发显示。
    /// </summary>
    internal static class HappyCakeHudVisibility
    {
        private const string CakeCategory = "cake";

        public static bool ShouldShow(
            GameRun run,
            GameplayView currentView,
            bool rewardDishPackVisible,
            IReadOnlyList<RewardChoice> rewardChoices,
            IReadOnlyList<ShopEntry> shopStock)
        {
            if (run?.Database == null)
            {
                return false;
            }

            if (RecipeContainsCake(run))
            {
                return true;
            }

            if (rewardDishPackVisible
                && RewardChoicesContainCake(run, rewardChoices))
            {
                return true;
            }

            return currentView == GameplayView.Shop
                && ShopStockContainsCake(run, shopStock);
        }

        private static bool RecipeContainsCake(GameRun run)
        {
            IReadOnlyList<RecipeBookSlot> recipe = run.RecipeEntries;
            if (recipe == null)
            {
                return false;
            }

            for (int i = 0; i < recipe.Count; i++)
            {
                if (IsCake(run, recipe[i]?.DishId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RewardChoicesContainCake(
            GameRun run,
            IReadOnlyList<RewardChoice> choices)
        {
            if (choices == null)
            {
                return false;
            }

            for (int i = 0; i < choices.Count; i++)
            {
                RewardChoice choice = choices[i];
                if (choice != null
                    && choice.Kind == cfg.RewardKind.DishChoice
                    && IsCake(run, choice.Id))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShopStockContainsCake(
            GameRun run,
            IReadOnlyList<ShopEntry> stock)
        {
            if (stock == null)
            {
                return false;
            }

            for (int i = 0; i < stock.Count; i++)
            {
                ShopEntry entry = stock[i];
                if (entry != null
                    && entry.Kind == ShopEntryKind.Dish
                    && entry.IsStocked
                    && IsCake(run, entry.Id))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCake(GameRun run, string dishId)
        {
            return !string.IsNullOrWhiteSpace(dishId)
                && run.Database.GetDish(dishId)?.IsCategory(CakeCategory) == true;
        }
    }
}
