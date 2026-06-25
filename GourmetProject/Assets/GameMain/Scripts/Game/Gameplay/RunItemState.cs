using System;

namespace GourmetProject.Game.Gameplay
{
    /// <summary>
    /// 单次运行中的「一份」道具持有条目。静态定义仍来自 cfg.Item，这里只保存会随运行变化的数据。
    /// 道具生命周期只有「存在 / 不存在」：被动道具同一 id 唯一一条（用 Level 表示升级）；
    /// 主动道具同一 id 可以有多条，每条代表一份独立实例，使用后整条移除（不存在数量消耗的中间态）。
    /// </summary>
    [Serializable]
    public sealed class RunItemState
    {
        public RunItemState(string itemId, int level)
        {
            ItemId = itemId;
            Level = Math.Max(1, level);
        }

        public string ItemId { get; }

        public int Level { get; private set; }

        public void IncreaseLevel(int maxLevel)
        {
            Level = Math.Min(Math.Max(1, maxLevel), Level + 1);
        }

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
