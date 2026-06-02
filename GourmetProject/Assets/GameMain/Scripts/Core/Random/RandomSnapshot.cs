using System;
using System.Collections.Generic;

namespace GourmetProject.Core.Rng
{
    /// <summary>
    /// 一份随机系统的完整快照：主种子 + 所有命名流的当前状态。
    /// 直接嵌入存档即可在读档后让所有随机序列无缝续上。
    /// </summary>
    [Serializable]
    public sealed class RandomSnapshot
    {
        /// <summary>玩家可见/可输入的原始种子字符串（可能为空，表示随机生成）。</summary>
        public string SeedText;

        /// <summary>由种子字符串解析得到的 64 位主种子。</summary>
        public ulong MasterSeed;

        /// <summary>各命名流名称 -> 当前状态。</summary>
        public Dictionary<string, RngState> Streams = new Dictionary<string, RngState>();
    }
}
