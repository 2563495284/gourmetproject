using System;
using System.Collections.Generic;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.DevConsole.Commands
{
    /// <summary>管理本地单局运行存档。</summary>
    public sealed class SaveCommand : ConsoleCommand
    {
        private static readonly string[] Subcommands = { "clear" };
        private readonly Action _deleteRunSave;

        public SaveCommand()
            : this(RunPersistence.Delete)
        {
        }

        internal SaveCommand(Action deleteRunSave)
        {
            _deleteRunSave = deleteRunSave ?? throw new ArgumentNullException(nameof(deleteRunSave));
        }

        public override string CmdName => "save";

        public override string Args => "clear";

        public override string Description => "清除单局运行存档（保留开场动画完成记录）。";

        public override CmdResult Execute(string[] args)
        {
            if (args.Length != 1 || !args[0].Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                return CmdResult.Fail("用法：save clear");
            }

            _deleteRunSave();
            return CmdResult.Ok("已清除单局运行存档；开场动画完成记录保持不变。");
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
