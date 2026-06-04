using System.Collections.Generic;

namespace GourmetProject.Game.Roguelike
{
    /// <summary>
    /// 局外成长存档（设计文档 13.10，独立于单局 <see cref="RunSaveData"/>，写入独立槽位）。
    /// 横向扩展为主：光之碎片货币、已解锁技能进池、初始能量上限等级，以及历史统计。
    /// </summary>
    public sealed class MetaSaveData
    {
        /// <summary>当前可用光之碎片。</summary>
        public int Shards;

        /// <summary>累计获得的光之碎片（统计展示用）。</summary>
        public int LifetimeShards;

        /// <summary>初始能量上限等级（0..MetaEnergyCapMaxLevel），每级 +10。</summary>
        public int EnergyCapLevel;

        /// <summary>已解锁、加入抽取池的技能 id（默认锁定的进阶/终极技）。</summary>
        public List<string> UnlockedSkillIds = new List<string>();

        /// <summary>历史最高进度（0..1），用于结算"到达更高 → 获得碎片"。</summary>
        public float BestProgress;

        /// <summary>累计开局数。</summary>
        public int RunCount;

        /// <summary>累计通关数。</summary>
        public int Victories;
    }
}
