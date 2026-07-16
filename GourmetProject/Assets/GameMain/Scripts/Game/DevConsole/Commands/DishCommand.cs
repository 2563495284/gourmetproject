using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Runtime;

namespace GourmetProject.Game.DevConsole.Commands
{
    /// <summary>给当前对局的菜谱本添加一道菜（按菜品变体 id）。</summary>
    public sealed class DishCommand : ConsoleCommand
    {
        public override string CmdName => "dish";

        public override string Args => "<dish-id:string>";

        public override string Description => "向第一本菜谱末尾添加一道菜（按变体 id）。";

        public override CmdResult Execute(string[] args)
        {
            if (!GameRunContext.HasRun)
            {
                return CmdResult.Fail("当前没有进行中的对局。");
            }

            if (args.Length < 1)
            {
                return CmdResult.Fail("用法：dish <dish-id>");
            }

            string dishId = args[0];
            GameRun run = GameRunContext.Current;
            if (!run.AddBonusDish(dishId))
            {
                return CmdResult.Fail($"添加失败：找不到菜品 '{dishId}' 或菜谱本已满。");
            }

            BattleForm.Active?.RefreshPersistentHud();
            return CmdResult.Ok($"已添加菜品 '{dishId}' 到第一本菜谱。");
        }

        public override IReadOnlyList<string> GetCompletions(string[] args)
        {
            IReadOnlyList<string> ids = AllDishIds();
            return args.Length == 0 ? ids : Match(ids, args[args.Length - 1]);
        }

        private static IReadOnlyList<string> AllDishIds()
        {
            cfg.Tables tables = GameApp.Config.Tables;
            if (tables == null)
            {
                return Array.Empty<string>();
            }

            return tables.TbDishVariant.DataList.Select(v => v.Id).ToList();
        }
    }
}
