using GourmetProject.Core.Diagnostics;
using UnityEngine;

namespace GourmetProject.Runtime.Diagnostics
{
    /// <summary>
    /// 把 Core 的引擎无关日志门面转发到 UnityEngine.Debug，从而统一出现在 Unity 控制台
    /// 以及 GameFramework 的 Debugger 窗口。GameApp 启动时注入。
    /// </summary>
    public sealed class UnityLogSink : ILogSink
    {
        public void Log(LogLevel level, string tag, string message)
        {
            string line = string.IsNullOrEmpty(tag) ? message : $"<color=#8FBCBB>[{tag}]</color> {message}";
            switch (level)
            {
                case LogLevel.Warning:
                    Debug.LogWarning(line);
                    break;
                case LogLevel.Error:
                    Debug.LogError(line);
                    break;
                default:
                    Debug.Log(line);
                    break;
            }
        }
    }
}
