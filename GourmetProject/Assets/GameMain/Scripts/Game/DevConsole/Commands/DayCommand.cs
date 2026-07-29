using System.Globalization;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle;

namespace GourmetProject.Game.DevConsole.Commands
{
    /// <summary>设置当前周行动轴已经消耗的天数，便于调试不同时间点的流程。</summary>
    public sealed class DayCommand : ConsoleCommand
    {
        public override string CmdName => "day";

        public override string Args => "<days:float>";

        public override string Description => "设置当前行动轴已消耗的天数。";

        public override CmdResult Execute(string[] args)
        {
            if (!GameRunContext.HasRun)
            {
                return CmdResult.Fail("当前没有进行中的对局。");
            }

            if (args.Length < 1)
            {
                return CmdResult.Fail("用法：day <days>");
            }

            if (!float.TryParse(
                    args[0],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float days)
                || float.IsNaN(days)
                || float.IsInfinity(days))
            {
                return CmdResult.Fail("days 必须是有效数字。");
            }

            GameRun run = GameRunContext.Current;
            if (days < 0f || days > run.TimelineLengthDays)
            {
                return CmdResult.Fail(
                    $"days 必须在 0 到 {FormatDays(run.TimelineLengthDays)} 之间。");
            }

            float quantizedDays = TimelineMath.Quantize(days);
            float previousDay = run.CurrentDay;
            run.CurrentDay = quantizedDays;

            BattleForm battle = BattleForm.Active;
            battle?.RefreshPersistentHud();
            if (battle != null && battle.IsDailyActionSelectionActive)
            {
                // 复用现有周循环：向前跳时补结算跨过的节点，并重建行动轴显示。
                battle.PromptNextAction();
            }

            return CmdResult.Ok(
                $"行动轴已消耗天数：{FormatDays(previousDay)} -> {FormatDays(run.CurrentDay)}。");
        }

        private static string FormatDays(float days)
        {
            return days.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
