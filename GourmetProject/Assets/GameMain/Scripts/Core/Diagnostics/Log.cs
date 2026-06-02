using System;

namespace GourmetProject.Core.Diagnostics
{
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        None = 4,
    }

    /// <summary>
    /// 日志输出汇。Core 保持引擎无关，由上层（Runtime）注入一个把日志转发到
    /// UnityEngine.Debug / GameFramework 的实现；未注入时默认输出到 Console。
    /// </summary>
    public interface ILogSink
    {
        void Log(LogLevel level, string tag, string message);
    }

    /// <summary>
    /// 全局日志门面。支持等级过滤与 tag。Runtime 启动时调用 <see cref="SetSink"/> 接入引擎日志。
    /// </summary>
    public static class Log
    {
        private sealed class ConsoleSink : ILogSink
        {
            public void Log(LogLevel level, string tag, string message)
            {
                string line = string.IsNullOrEmpty(tag) ? message : $"[{tag}] {message}";
                if (level >= LogLevel.Error)
                {
                    Console.Error.WriteLine(line);
                }
                else
                {
                    Console.WriteLine(line);
                }
            }
        }

        private static ILogSink _sink = new ConsoleSink();

        /// <summary>低于该等级的日志被丢弃。</summary>
        public static LogLevel MinLevel = LogLevel.Debug;

        public static void SetSink(ILogSink sink)
        {
            _sink = sink ?? new ConsoleSink();
        }

        public static void Debug(string message, string tag = null) => Emit(LogLevel.Debug, tag, message);
        public static void Info(string message, string tag = null) => Emit(LogLevel.Info, tag, message);
        public static void Warning(string message, string tag = null) => Emit(LogLevel.Warning, tag, message);
        public static void Error(string message, string tag = null) => Emit(LogLevel.Error, tag, message);

        public static void Exception(Exception ex, string tag = null)
        {
            Emit(LogLevel.Error, tag, ex == null ? "null exception" : ex.ToString());
        }

        private static void Emit(LogLevel level, string tag, string message)
        {
            if (level < MinLevel)
            {
                return;
            }

            _sink.Log(level, tag, message);
        }
    }
}
