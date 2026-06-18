using System;

namespace GourmetProject.Game.Gameplay
{
    /// <summary>
    /// 单次运行中的道具持有状态。静态定义仍来自 cfg.Item，这里只保存会随运行变化的数据。
    /// </summary>
    [Serializable]
    public sealed class RunItemState
    {
        public RunItemState(string itemId, int level, int count, int totalAcquired)
        {
            ItemId = itemId;
            Level = Math.Max(1, level);
            Count = Math.Max(0, count);
            TotalAcquired = Math.Max(0, totalAcquired);
        }

        public string ItemId { get; }

        public int Level { get; private set; }

        public int Count { get; private set; }

        public int TotalAcquired { get; private set; }

        public int RunUseCount { get; private set; }

        public int BattleUseCount { get; private set; }

        public bool IsEmpty => Count <= 0;

        public void IncreaseLevel(int maxLevel)
        {
            Level = Math.Min(Math.Max(1, maxLevel), Level + 1);
            TotalAcquired++;
        }

        public void AddCount(int amount)
        {
            Count = Math.Max(0, Count + Math.Max(0, amount));
            TotalAcquired += Math.Max(0, amount);
        }

        public bool ConsumeOne()
        {
            if (Count <= 0)
            {
                return false;
            }

            Count--;
            RecordUse();
            return true;
        }

        public void RecordUse()
        {
            RunUseCount++;
            BattleUseCount++;
        }

        public void ResetBattleUseCount()
        {
            BattleUseCount = 0;
        }

        public RunItemSaveData ToSaveData()
        {
            return new RunItemSaveData
            {
                ItemId = ItemId,
                Level = Level,
                Count = Count,
                TotalAcquired = TotalAcquired,
                RunUseCount = RunUseCount,
            };
        }

        public static RunItemState FromSaveData(RunItemSaveData data)
        {
            var state = new RunItemState(data.ItemId, data.Level, data.Count, data.TotalAcquired);
            state.RunUseCount = Math.Max(0, data.RunUseCount);
            return state;
        }
    }
}
