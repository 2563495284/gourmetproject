#if UNITY_EDITOR || DEVELOPMENT_BUILD
using GourmetProject.Game.UI;
using GourmetProject.Runtime;

namespace GourmetProject.Game.DevConsole.Commands
{
    /// <summary>打开不读取或修改对局的多风味 Sprite 表现实验室。</summary>
    public sealed class FlavorLabCommand : ConsoleCommand
    {
        public override string CmdName => "flavor-lab";

        public override string Args => string.Empty;

        public override string Description => "打开多风味 Sprite 三方案实验室（仅 Editor / Development Build）。";

        public override CmdResult Execute(string[] args)
        {
            if (GameApp.UI.HasUIForm(UIForms.FlavorLab))
            {
                return CmdResult.Fail("多风味 Sprite 表现实验室已经打开。");
            }

            GameApp.UI.OpenUIForm(UIForms.FlavorLab, UIForms.GroupDialog);
            return CmdResult.Ok("已打开多风味 Sprite 表现实验室。");
        }
    }
}
#endif
