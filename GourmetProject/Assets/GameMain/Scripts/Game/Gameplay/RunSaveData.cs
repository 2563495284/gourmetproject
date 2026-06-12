using System;
using System.Collections.Generic;

namespace GourmetProject.Game.Gameplay
{
    /// <summary>
    /// 一次肉鸽运行的存档数据（可被 JsonSaveService 序列化）。随机以「种子 + 周编号命名流」复现，
    /// 因此只需存种子与进度，无需存完整随机快照。
    /// </summary>
    [Serializable]
    public sealed class RunSaveData
    {
        public string CharacterId;
        public string SeedText;
        public int WeekIndex;
        public int Gold;
        public List<string> ItemIds = new List<string>();
    }
}
