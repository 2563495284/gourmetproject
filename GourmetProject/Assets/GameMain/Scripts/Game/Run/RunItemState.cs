using System;
using GourmetProject.Game.Meta.Passives;

namespace GourmetProject.Game.Run
{
    /// <summary>
    /// 单次运行中的「一份」装饰品和消耗品持有条目。静态定义来自被动/消耗品配置表，这里只保存会随运行变化的数据。
    /// 装饰品和消耗品生命周期只有「存在 / 不存在」：装饰品同一 id 唯一一条且不升级；
    /// 消耗品同一 id 可以有多条，每条代表一份独立实例，使用后整条移除（不存在数量消耗的中间态）。
    /// 装饰品持有时挂一个 <see cref="Passives.PassiveItemModel"/>（行为 + per-instance 状态）；由 GameRun 负责构建/绑定。
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

        /// <summary>旧存档兼容字段。装饰品已无等级设计，新状态恒为 1。</summary>
        public int Level { get; }

        /// <summary>装饰品行为模型（消耗品为 null）；由 GameRun 构建绑定，携带 per-instance 运行时状态。</summary>
        public PassiveItemModel Model { get; set; }

        public RunItemSaveData ToSaveData()
        {
            return new RunItemSaveData
            {
                ItemId = ItemId,
                Level = Level,
                StateJson = Model != null ? Model.CaptureState() : string.Empty,
            };
        }

        public static RunItemState FromSaveData(RunItemSaveData data)
        {
            return new RunItemState(data.ItemId, data.Level);
        }
    }
}
