using System;
using System.Collections.Generic;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;

namespace GourmetProject.Game.Run
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
        public List<RunItemSaveData> Items = new List<RunItemSaveData>();
        public List<string> BonusDishIds = new List<string>();
        public List<string> StomachFragmentIds = new List<string>();

        // —— 行动轴状态（局外核心循环）——
        /// <summary>本周行动轴 id（用于读档时按配置重建节点）。</summary>
        public string CurrentTimelineId;

        /// <summary>本周行动轴长度（天）。</summary>
        public int TimelineLengthDays;

        /// <summary>当前天数游标（0..TimelineLengthDays）。</summary>
        public int CurrentDay;

        /// <summary>本周已执行行动次数。</summary>
        public int ActionStepIndex;

        /// <summary>本周要求分临时覆盖；小于 0 表示无覆盖。</summary>
        public int RequiredScoreOverride = -1;

        /// <summary>最近一次行动 id；用于读档后恢复奖励/商店隐藏分上下文。</summary>
        public string LastActionId;

        /// <summary>最近一次行动发生时的本周行动序号。</summary>
        public int LastActionStepIndex;

        /// <summary>本周已结算的节点 id。</summary>
        public List<string> TriggeredNodeIds = new List<string>();

        /// <summary>不可重复事件命中记录（整局）。</summary>
        public List<string> UsedEventIds = new List<string>();

        /// <summary>不可重复行动命中记录（本周内）。</summary>
        public List<string> UsedActionIds = new List<string>();

        /// <summary>已通关 Boss id（整局，含最终胜利判定）。</summary>
        public List<string> CompletedBossIds = new List<string>();

        /// <summary>旧存档兼容字段：曾经只保存道具 id，读档时会迁移为 Items。</summary>
        public List<string> ItemIds = new List<string>();
    }

    [Serializable]
    public sealed class RunItemSaveData
    {
        public string ItemId;
        public int Level = 1;

        /// <summary>旧存档兼容字段：曾经的主动道具持有数量。新档每份实例单独一条，恒为 1。</summary>
        public int Count = 1;
    }
}
