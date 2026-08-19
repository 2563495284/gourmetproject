using GourmetProject.Game.UI;
using GourmetProject.Runtime;

namespace GourmetProject.Game.DevConsole.Commands
{
    /// <summary>打开仅使用演示快照的时间轴表现实验室。</summary>
    public sealed class TimelineLabCommand : ConsoleCommand
    {
        public override string CmdName => "timeline-lab";

        public override string Args => string.Empty;

        public override string Description => "打开时间轴表现实验室。";

        public override CmdResult Execute(string[] args)
        {
            if (GameApp.UI.HasUIForm(UIForms.TimelineLab))
            {
                return CmdResult.Fail("时间轴表现实验室已经打开。");
            }

            GameApp.UI.OpenUIForm(UIForms.TimelineLab, UIForms.GroupDialog);
            return CmdResult.Ok("已打开时间轴表现实验室。");
        }
    }
}
