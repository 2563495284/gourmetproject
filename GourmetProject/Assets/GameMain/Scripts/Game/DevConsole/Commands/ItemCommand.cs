using System.Collections.Generic;
using System.Linq;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Runtime;

namespace GourmetProject.Game.DevConsole.Commands
{
    /// <summary>给当前一局游戏添加装饰品或消耗品（参考 STS2 <c>RelicConsoleCmd</c> 的 id 补全）。</summary>
    public sealed class ItemCommand : ConsoleCommand
    {
        public override string CmdName => "item";

        public override string Args => "<item-id:string>";

        public override string Description => "给玩家添加 1 件装饰品或消耗品（按 id）。";

        public override CmdResult Execute(string[] args)
        {
            if (!GameRunContext.HasRun)
            {
                return CmdResult.Fail("当前没有进行中的一局游戏。");
            }

            if (args.Length < 1)
            {
                return CmdResult.Fail("用法：item <item-id>");
            }

            string itemId = args[0];
            cfg.Tables tables = GameApp.Config.Tables;
            ItemDefinition def = ItemDefinition.Get(tables, itemId);
            if (def == null)
            {
                return CmdResult.Fail($"找不到装饰品或消耗品 '{itemId}'。");
            }

            GameRun run = GameRunContext.Current;
            ItemAcquireResult result = run.AcquireItem(def.Id, fallbackGold: 0);
            BattleForm.Active?.RefreshPersistentHud();

            string kind = def.IsPassive ? "装饰品" : "消耗品";
            return CmdResult.Ok($"已添加{kind} '{def.Id}'（{def.Name}）。结果：{result.Outcome}");
        }

        public override IReadOnlyList<string> GetCompletions(string[] args)
        {
            if (args.Length == 0)
            {
                return AllItemIds();
            }

            return Match(AllItemIds(), args[args.Length - 1]);
        }

        private static IReadOnlyList<string> AllItemIds()
        {
            cfg.Tables tables = GameApp.Config.Tables;
            if (tables == null)
            {
                return System.Array.Empty<string>();
            }

            IEnumerable<string> passive = ItemDefinition.All(tables, cfg.ItemKind.Passive).Select(i => i.Id);
            IEnumerable<string> active = ItemDefinition.All(tables, cfg.ItemKind.Active).Select(i => i.Id);
            return passive.Concat(active).ToList();
        }
    }
}
