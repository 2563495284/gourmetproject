using System;
using System.Collections.Generic;
using System.Linq;

namespace GourmetProject.Game.DevConsole
{
    /// <summary>
    /// 控制台命令基类（参考 STS2 <c>AbstractConsoleCmd</c>）。
    /// 每个玩法命令继承本类，由 <see cref="DevConsole"/> 反射扫描本程序集自动注册。
    /// </summary>
    public abstract class ConsoleCommand
    {
        /// <summary>命令名（小写，唯一），如 "gold"。</summary>
        public abstract string CmdName { get; }

        /// <summary>参数用法提示，如 "&lt;amount:int&gt;"。</summary>
        public abstract string Args { get; }

        /// <summary>命令说明，用于 help 列表。</summary>
        public abstract string Description { get; }

        /// <summary>执行命令。<paramref name="args"/> 是命令名之后的参数（已去空）。</summary>
        public abstract CmdResult Execute(string[] args);

        /// <summary>
        /// 为正在输入的参数提供补全候选。<paramref name="args"/> 的最后一项是「正在输入的部分参数」，
        /// 前面各项是「已输入完整的参数」。默认无补全。
        /// </summary>
        public virtual IReadOnlyList<string> GetCompletions(string[] args) => Array.Empty<string>();

        /// <summary>
        /// 前缀优先、其次子串匹配的候选过滤（参考 STS2 <c>CompleteArgument</c> 的匹配思路）。
        /// </summary>
        protected static IReadOnlyList<string> Match(IEnumerable<string> candidates, string partial)
        {
            if (candidates == null)
            {
                return Array.Empty<string>();
            }

            IEnumerable<string> distinct = candidates.Where(c => !string.IsNullOrEmpty(c)).Distinct();
            if (string.IsNullOrEmpty(partial))
            {
                return distinct.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
            }

            var startsWith = new List<string>();
            var contains = new List<string>();
            foreach (string candidate in distinct)
            {
                if (candidate.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                {
                    startsWith.Add(candidate);
                }
                else if (candidate.IndexOf(partial, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    contains.Add(candidate);
                }
            }

            startsWith.Sort(StringComparer.OrdinalIgnoreCase);
            contains.Sort(StringComparer.OrdinalIgnoreCase);
            startsWith.AddRange(contains);
            return startsWith;
        }
    }
}
