using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle;

namespace GourmetProject.Game.DevConsole.Commands
{
    /// <summary>立即进入指定事件，便于逐个调试事件内容与分支。</summary>
    public sealed class EventCommand : ConsoleCommand
    {
        public override string CmdName => "event";

        public override string Args => "<event-id:string>";

        public override string Description => "立即执行指定事件（按 id，不消耗天数或行动次数）。";

        public override CmdResult Execute(string[] args)
        {
            if (!GameRunContext.HasRun)
            {
                return CmdResult.Fail("当前没有进行中的对局。");
            }

            if (args.Length < 1)
            {
                return CmdResult.Fail("用法：event <event-id>");
            }

            string eventId = args[0];
            cfg.GameEvent ev = GameRunContext.Current.Tables?.TbEvent.GetOrDefault(eventId);
            if (ev == null)
            {
                return CmdResult.Fail($"找不到事件 '{eventId}'。");
            }

            BattleForm battle = BattleForm.Active;
            if (battle?.ActiveLoop == null)
            {
                return CmdResult.Fail("需要在经营挑战界面内使用。");
            }

            if (!battle.IsDailyActionSelectionActive)
            {
                return CmdResult.Fail("请先返回日常行动选择页，再执行事件命令。");
            }

            if (!battle.ActiveLoop.ExecuteEventImmediately(ev.Id))
            {
                return CmdResult.Fail($"事件 '{eventId}' 执行失败。");
            }

            return CmdResult.Ok($"已立即进入事件 '{ev.Id}'（{ev.Name}）。");
        }

        public override IReadOnlyList<string> GetCompletions(string[] args)
        {
            IReadOnlyList<string> ids = AllEventIds();
            return args.Length == 0 ? ids : Match(ids, args[args.Length - 1]);
        }

        private static IReadOnlyList<string> AllEventIds()
        {
            cfg.Tables tables = GameRunContext.HasRun
                ? GameRunContext.Current.Tables
                : null;
            if (tables == null)
            {
                return Array.Empty<string>();
            }

            return tables.TbEvent.DataList.Select(ev => ev.Id).ToList();
        }
    }
}
