using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle;

namespace GourmetProject.Game.Meta.Passives
{
    internal static class PassiveMutationPresenter
    {
        public static void ShowRecipe(GameRun run, RecipeMutationResult result)
        {
            if (run == null
                || result == null
                || !result.HasChanges)
            {
                return;
            }

            run.Execution.Presentation.Present(
                () => BattleForm.Active?.ShowPassiveRecipeMutation(result));
        }

        public static void ShowCells(GameRun run, CellMutationResult result)
        {
            if (run == null
                || result == null
                || !result.HasChanges)
            {
                return;
            }

            run.Execution.Presentation.Present(
                () => BattleForm.Active?.ShowPassiveCellMutation(result));
        }

        public static void ShowTimeline(GameRun run, TimelineMutationResult result)
        {
            if (run == null
                || result == null
                || !result.Changed)
            {
                return;
            }

            run.Execution.Presentation.Present(
                () => BattleForm.Active?.ShowPassiveTimelineMutation(result));
        }
    }
}
