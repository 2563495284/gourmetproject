using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle;

namespace GourmetProject.Game.DevConsole.Commands
{
    /// <summary>跳到指定周并重开该周行动轴（参考 STS2 <c>ActConsoleCmd</c> 的跳关思路）。</summary>
    public sealed class WeekCommand : ConsoleCommand
    {
        public override string CmdName => "week";

        public override string Args => "<week:int>";

        public override string Description => "跳到指定周并重新随机该周行动轴。";

        public override CmdResult Execute(string[] args)
        {
            if (!GameRunContext.HasRun)
            {
                return CmdResult.Fail("当前没有进行中的对局。");
            }

            if (args.Length < 1)
            {
                return CmdResult.Fail("用法：week <week>");
            }

            if (!int.TryParse(args[0], out int week))
            {
                return CmdResult.Fail("week 必须是整数。");
            }

            GameRun run = GameRunContext.Current;
            run.SetWeekIndex(week);

            BattleForm battle = BattleForm.Active;
            if (battle == null)
            {
                return CmdResult.Ok($"已设置周序号为 {run.WeekIndex}（未在战斗界面，进入后生效）。");
            }

            battle.BeginWeek();
            return CmdResult.Ok($"已跳到第 {run.WeekIndex} 周并重开行动轴。");
        }
    }
}
