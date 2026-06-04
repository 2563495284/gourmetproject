using System.Collections.Generic;
using GourmetProject.Core.Rng;

namespace GourmetProject.Game.Roguelike
{
    /// <summary>
    /// 本局存档数据（玩法层 DTO，经 Newtonsoft 序列化写入存档槽）。
    /// 含可复现地图的种子、随机快照、已选技能与已激活检查点数，用于「继续游戏」恢复。
    /// </summary>
    public sealed class RunSaveData
    {
        /// <summary>主种子文本：继续游戏时用它 Init 随机系统以复现同一张地图。</summary>
        public string SeedText;

        /// <summary>随机流快照（保留以备精确续档/展示；当前恢复主要走 <see cref="SeedText"/>）。</summary>
        public RandomSnapshot Rng;

        /// <summary>按选取顺序记录的技能 id（含重复层）。</summary>
        public List<string> PickedSkillIds = new List<string>();

        /// <summary>已激活的普通检查点数量（决定下次抽取的段位与重生点）。</summary>
        public int ActivatedCheckpointCount;
    }
}
