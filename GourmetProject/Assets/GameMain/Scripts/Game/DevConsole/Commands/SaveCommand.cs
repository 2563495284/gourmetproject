using System;
using System.Collections.Generic;
using GourmetProject.Game.Run;
using GourmetProject.Game.Save;

namespace GourmetProject.Game.DevConsole.Commands
{
    /// <summary>按分区管理玩家总档。</summary>
    public sealed class SaveCommand : ConsoleCommand
    {
        private static readonly string[] Subcommands = { "clear", "clear-all" };
        private readonly Action _deleteRunSave;
        private readonly Action _deleteAllSave;

        public SaveCommand()
            : this(RunPersistence.Delete, GameSavePersistence.DeleteAll)
        {
        }

        internal SaveCommand(Action deleteRunSave, Action deleteAllSave)
        {
            _deleteRunSave = deleteRunSave ?? throw new ArgumentNullException(nameof(deleteRunSave));
            _deleteAllSave = deleteAllSave ?? throw new ArgumentNullException(nameof(deleteAllSave));
        }

        public override string CmdName => "save";

        public override string Args => "<clear|clear-all>";

        public override string Description => "清除单局存档，或清除包含局外/引导进度的整份总档。";

        public override CmdResult Execute(string[] args)
        {
            if (args.Length != 1)
            {
                return CmdResult.Fail("用法：save <clear|clear-all>");
            }

            if (args[0].Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                _deleteRunSave();
                return CmdResult.Ok("已清除单局运行存档；局外和引导进度保持不变。");
            }

            if (args[0].Equals("clear-all", StringComparison.OrdinalIgnoreCase))
            {
                _deleteAllSave();
                return CmdResult.Ok("已清除整份玩家总档（含单局、局外和引导进度）。");
            }

            return CmdResult.Fail("用法：save <clear|clear-all>");
        }

        public override IReadOnlyList<string> GetCompletions(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return Subcommands;
            }

            return args.Length == 1 ? Match(Subcommands, args[0]) : Array.Empty<string>();
        }
    }
}
