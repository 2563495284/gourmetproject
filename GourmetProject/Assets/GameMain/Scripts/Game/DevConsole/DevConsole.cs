using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GourmetProject.Game.DevConsole
{
    /// <summary>
    /// 开发者控制台核心：命令注册表 + 解析执行 + Tab 补全 + 历史记录。
    /// 参考 STS2 <c>DevConsole</c>，去掉联机队列与文件历史持久化（单机、内存历史即可）。
    /// 命令通过反射扫描本程序集里所有非抽象 <see cref="ConsoleCommand"/> 自动注册，
    /// 无需像 STS2 那样维护手写的子类型列表。
    /// </summary>
    public sealed class DevConsole
    {
        private const int HistoryLimit = 40;
        private const string HelpCommand = "help";

        private readonly Dictionary<string, ConsoleCommand> _commands =
            new Dictionary<string, ConsoleCommand>(StringComparer.OrdinalIgnoreCase);

        // 最新的在前（index 0 = 最近一条），供上下键翻阅。
        private readonly List<string> _history = new List<string>();

        public DevConsole()
        {
            RegisterCommands();
        }

        public IReadOnlyList<string> History => _history;

        public IReadOnlyList<ConsoleCommand> Commands =>
            _commands.Values.OrderBy(c => c.CmdName, StringComparer.OrdinalIgnoreCase).ToList();

        private void RegisterCommands()
        {
            foreach (Type type in typeof(DevConsole).Assembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(ConsoleCommand).IsAssignableFrom(type))
                {
                    continue;
                }

                try
                {
                    var command = (ConsoleCommand)Activator.CreateInstance(type);
                    _commands[command.CmdName] = command;
                }
                catch
                {
                    // 忽略无法实例化的命令（如缺少无参构造），不影响其余命令注册。
                }
            }
        }

        /// <summary>解析并执行一行输入，返回展示结果。会记入历史。</summary>
        public CmdResult ProcessCommand(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return CmdResult.Fail(string.Empty);
            }

            input = input.Trim();
            RecordHistory(input);

            string[] tokens = Tokenize(input);
            string name = tokens[0];
            string[] args = tokens.Skip(1).ToArray();

            if (name.Equals(HelpCommand, StringComparison.OrdinalIgnoreCase))
            {
                return Help(args);
            }

            if (_commands.TryGetValue(name, out ConsoleCommand command))
            {
                try
                {
                    return command.Execute(args);
                }
                catch (Exception e)
                {
                    return CmdResult.Fail($"命令 '{name}' 执行异常：{e.Message}");
                }
            }

            return CmdResult.Fail($"未知命令 '{name}'。输入 help 查看所有命令。");
        }

        /// <summary>
        /// 返回当前正在输入的最后一个 token 的补全候选。
        /// 由 UI 层决定如何替换（单候选直接补全，多候选展示 + 补最长公共前缀）。
        /// </summary>
        public IReadOnlyList<string> GetCompletions(string input)
        {
            input ??= string.Empty;
            bool trailingSpace = input.Length > 0 && char.IsWhiteSpace(input[input.Length - 1]);
            string[] tokens = Tokenize(input);

            // 尚未输入任何内容：列出全部命令名。
            if (tokens.Length == 0)
            {
                return CommandNames();
            }

            // 正在输入命令名（只有一个 token 且没有尾部空格）。
            if (tokens.Length == 1 && !trailingSpace)
            {
                return ConsoleCommand_Match(CommandNames(), tokens[0]);
            }

            // 正在输入参数：委托给对应命令。
            if (!_commands.TryGetValue(tokens[0], out ConsoleCommand command))
            {
                return Array.Empty<string>();
            }

            string[] argTokens = tokens.Skip(1).ToArray();
            // 尾部有空格表示开始输入新参数，补一个空的「部分参数」占位。
            string[] argsForCompletion = trailingSpace
                ? argTokens.Append(string.Empty).ToArray()
                : argTokens;

            try
            {
                return command.GetCompletions(argsForCompletion) ?? Array.Empty<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private CmdResult Help(string[] args)
        {
            if (args.Length > 0)
            {
                string target = args[0];
                if (target.Equals(HelpCommand, StringComparison.OrdinalIgnoreCase))
                {
                    return CmdResult.Ok("help [命令]\n    列出所有命令，或查看指定命令的用法。");
                }

                if (_commands.TryGetValue(target, out ConsoleCommand command))
                {
                    return CmdResult.Ok($"{command.CmdName} {command.Args}\n    {command.Description}");
                }

                return CmdResult.Fail($"没有名为 '{target}' 的命令。");
            }

            List<ConsoleCommand> all = Commands.ToList();
            int width = HelpCommand.Length;
            foreach (ConsoleCommand command in all)
            {
                width = Math.Max(width, command.CmdName.Length);
            }

            var sb = new StringBuilder("可用命令：\n");
            sb.Append("  ").Append(HelpCommand.PadRight(width)).Append("  - 列出命令 / 查看单个命令用法\n");
            foreach (ConsoleCommand command in all)
            {
                sb.Append("  ").Append(command.CmdName.PadRight(width)).Append("  - ").Append(command.Description).Append('\n');
            }

            sb.Append("输入 help <命令> 查看具体用法。");
            return CmdResult.Ok(sb.ToString());
        }

        private void RecordHistory(string input)
        {
            // 与上一条相同则不重复记录。
            if (_history.Count > 0 && string.Equals(_history[0], input, StringComparison.Ordinal))
            {
                return;
            }

            _history.Insert(0, input);
            if (_history.Count > HistoryLimit)
            {
                _history.RemoveAt(_history.Count - 1);
            }
        }

        private IReadOnlyList<string> CommandNames()
        {
            var names = new List<string> { HelpCommand };
            names.AddRange(_commands.Keys);
            return names.Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string[] Tokenize(string input)
        {
            return input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        // 复用命令基类的匹配逻辑（前缀优先、子串兜底），用于命令名补全。
        private static IReadOnlyList<string> ConsoleCommand_Match(IEnumerable<string> candidates, string partial)
        {
            if (string.IsNullOrEmpty(partial))
            {
                return candidates.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
            }

            return candidates
                .Where(c => c.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
