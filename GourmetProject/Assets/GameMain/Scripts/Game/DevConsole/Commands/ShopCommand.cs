using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle;

namespace GourmetProject.Game.DevConsole.Commands
{
    /// <summary>清空当前商店库存缓存并（若正在商店内）重新随机上架。</summary>
    public sealed class ShopCommand : ConsoleCommand
    {
        public override string CmdName => "refresh_shop";

        public override string Args => string.Empty;

        public override string Description => "刷新商店库存（清空 pending 缓存，重新随机）。";

        public override CmdResult Execute(string[] args)
        {
            if (!GameRunContext.HasRun)
            {
                return CmdResult.Fail("当前没有进行中的对局。");
            }

            GameRun run = GameRunContext.Current;
            // 清空同一天复用的库存快照，下次进入商店会用新随机流重掷。
            run.ClearPendingShopStock();

            BattleForm battle = BattleForm.Active;
            if (battle != null && battle.CurrentView == GameplayView.Shop)
            {
                // 正在商店态：重进商店态即重掷（ShopPageCoordinator 在无 pending 时会重新 RollStock）。
                battle.OpenShop();
                return CmdResult.Ok("商店已重新随机。");
            }

            return CmdResult.Ok("已清空商店库存缓存，下次进入商店会重新随机。");
        }
    }
}
