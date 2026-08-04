using GourmetProject.Game.Run;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Runtime;

namespace GourmetProject.Game.DevConsole.Commands
{
    /// <summary>
    /// 重新生成并打开当前进度的过关奖励界面。
    /// </summary>
    public sealed class RewardCommand : ConsoleCommand
    {
        public override string CmdName => "reward";

        public override string Args => string.Empty;

        public override string Description => "重新随机并打开过关奖励界面。";

        public override CmdResult Execute(string[] args)
        {
            if (!GameRunContext.HasRun)
            {
                return CmdResult.Fail("当前没有进行中的对局。");
            }

            if (BattleForm.Active == null)
            {
                return CmdResult.Fail("需要在经营挑战界面内使用。");
            }

            if (GameApp.UI.HasUIForm(UIForms.Reward))
            {
                return CmdResult.Fail("奖励界面已打开。");
            }

            // 清掉 pending offer，RewardForm.OnOpen 会按当前周重新 GenerateOffer。
            GameRunContext.Current.ClearPendingBattleReward();
            GameApp.UI.OpenUIForm(UIForms.Reward, UIForms.GroupDialog);
            return CmdResult.Ok("已打开奖励界面（已重新随机）。");
        }
    }
}
