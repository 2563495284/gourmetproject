using System.Collections.Generic;
using System.Linq;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Runtime;

namespace GourmetProject.Game.DevConsole.Commands
{
    /// <summary>指定本周 Boss Debuff，便于调试星级评鉴机制。</summary>
    public sealed class BossDebuffCommand : ConsoleCommand
    {
        private const string ClearArg = "clear";

        public override string CmdName => "bossdebuff";

        public override string Args => "<debuff-id:string|clear>";

        public override string Description => "指定本周 Boss 的 Debuff（或 clear 恢复随机）。";

        public override CmdResult Execute(string[] args)
        {
            if (!GameRunContext.HasRun)
            {
                return CmdResult.Fail("当前没有进行中的对局。");
            }

            if (args.Length < 1)
            {
                return CmdResult.Fail("用法：bossdebuff <debuff-id|clear>");
            }

            GameRun run = GameRunContext.Current;
            string debuffId = args[0];
            if (debuffId == ClearArg)
            {
                run.ClearForcedBossDebuff();
                return CmdResult.Ok("已清除本周 Boss Debuff 指定，恢复随机抽取。");
            }

            cfg.Tables tables = GameApp.Config.Tables;
            cfg.BossDebuff debuff = tables?.TbBossDebuff.GetOrDefault(debuffId);
            if (debuff == null)
            {
                return CmdResult.Fail($"找不到 Boss Debuff '{debuffId}'。");
            }

            if (!PreconditionEvaluator.IsSatisfied(run, debuff.UnlockCondition))
            {
                return CmdResult.Fail($"Boss Debuff '{debuff.Id}'（{debuff.Name}）当前不满足解锁条件。");
            }

            run.ForceBossDebuffForCurrentWeek(debuff.Id);
            run.ResetBossDebuffRollHistory();
            return CmdResult.Ok($"本周 Boss Debuff 已指定为 '{debuff.Id}'（{debuff.Name}）。");
        }

        public override IReadOnlyList<string> GetCompletions(string[] args)
        {
            if (args.Length == 0)
            {
                return AllDebuffIds();
            }

            return Match(AllDebuffIds(), args[args.Length - 1]);
        }

        private static IReadOnlyList<string> AllDebuffIds()
        {
            cfg.Tables tables = GameApp.Config.Tables;
            if (tables == null)
            {
                return new[] { ClearArg };
            }

            return tables.TbBossDebuff.DataList
                .Select(d => d.Id)
                .Concat(new[] { ClearArg })
                .ToList();
        }
    }
}
