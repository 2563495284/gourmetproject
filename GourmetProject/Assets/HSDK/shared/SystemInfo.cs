using System;
using UnityEngine;

namespace Hortor
{
    internal static class ClientSystemInfo
    {
        public static string Version { get; private set; } = string.Empty;
        public static string Platform { get; private set; } = string.Empty;
        public static string GraphicsMemorySize { get; private set; } = string.Empty;
        public static string GraphicsDeviceName { get; private set; } = string.Empty;
        public static string ProcessorType { get; private set; } = string.Empty;
        public static int ProcessorCount { get; private set; }
        public static string SystemMemorySize { get; private set; } = string.Empty;
        public static string ProcessorFrequency { get; private set; } = string.Empty;

        public static void Init()
        {
            OperatingSystem operatingSystem = Environment.OSVersion;
            Version = operatingSystem.Version.ToString();
            Platform = operatingSystem.Platform.ToString();
            GraphicsDeviceName = UnityEngine.SystemInfo.graphicsDeviceName ?? string.Empty;
            GraphicsMemorySize = $"{UnityEngine.SystemInfo.graphicsMemorySize}MB";
            SystemMemorySize = $"{UnityEngine.SystemInfo.systemMemorySize}MB";
            ProcessorType = UnityEngine.SystemInfo.processorType ?? string.Empty;
            ProcessorCount = UnityEngine.SystemInfo.processorCount;
            ProcessorFrequency = $"{UnityEngine.SystemInfo.processorFrequency}MHz";
        }
    }
}
