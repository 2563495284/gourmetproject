using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle;

namespace GourmetProject.Game.Meta.Passives
{
    internal static class PassiveMutationPresenter
    {
        public static void ShowRecipe(GameRun run, RecipeMutationResult result)
        {
            if (result == null || !result.HasChanges)
            {
                return;
            }

            BattleForm.Active?.ShowPassiveRecipeMutation(result);
        }

        public static void ShowCells(GameRun run, CellMutationResult result)
        {
            if (result == null || !result.HasChanges)
            {
                return;
            }

            BattleForm.Active?.ShowPassiveCellMutation(result);
        }

        public static void ShowTimeline(GameRun run, TimelineMutationResult result)
        {
            if (result == null || !result.Changed)
            {
                return;
            }

            BattleForm.Active?.ShowPassiveTimelineMutation(result);
        }
    }
}
