using System;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle;

namespace GourmetProject.Game.DevConsole.Commands
{
    /// <summary>增减当前对局金币（参考 STS2 <c>GoldConsoleCmd</c>）。</summary>
    public sealed class GoldCommand : ConsoleCommand
    {
        public override string CmdName => "gold";

        public override string Args => "<amount:int>";

        public override string Description => "增减当前对局金币（可为负）。";

        public override CmdResult Execute(string[] args)
        {
            if (!GameRunContext.HasRun)
            {
                return CmdResult.Fail("当前没有进行中的对局。");
            }

            if (args.Length < 1)
            {
                return CmdResult.Fail("用法：gold <amount>");
            }

            if (!int.TryParse(args[0], out int amount))
            {
                return CmdResult.Fail("amount 必须是整数。");
            }

            GameRun run = GameRunContext.Current;
            run.Gold = Math.Max(0, run.Gold + amount);
            BattleForm.Active?.RefreshPersistentHud();

            string sign = amount >= 0 ? "+" : string.Empty;
            return CmdResult.Ok($"金币 {sign}{amount}，当前 {run.Gold}。");
        }
    }
}
