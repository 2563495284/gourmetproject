using System;
namespace GourmetProject.Game.Run
{
    /// <summary>
    /// 单次运行中的「一份」道具持有条目。静态定义来自被动/主动道具配置表，这里只保存会随运行变化的数据。
    /// 道具生命周期只有「存在 / 不存在」：被动道具同一 id 唯一一条且不升级；
    /// 主动道具同一 id 可以有多条，每条代表一份独立实例，使用后整条移除（不存在数量消耗的中间态）。
    /// </summary>
    [Serializable]
    public sealed class RunItemState
    {
        public RunItemState(string itemId, int level)
        {
            ItemId = itemId;
            Level = 1;
        }

        public string ItemId { get; }

        /// <summary>旧存档兼容字段。被动道具已无等级设计，新状态恒为 1。</summary>
        public int Level { get; }

        public RunItemSaveData ToSaveData()
        {
            return new RunItemSaveData
            {
                ItemId = ItemId,
                Level = Level,
            };
        }

        public static RunItemState FromSaveData(RunItemSaveData data)
        {
            return new RunItemState(data.ItemId, data.Level);
        }
    }
}
