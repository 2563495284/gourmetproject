using UnityEngine;
using GourmetProject.Game.Platformer;
using GourmetProject.Runtime;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.Roguelike
{
    /// <summary>
    /// 局外成长档案（设计文档 13.10）：持久化光之碎片、技能解锁、初始能量上限等级，
    /// 提供对局结束结算与碎片消费。独立存档槽，与单局存档隔离。进程内单例。
    /// </summary>
    public sealed class MetaProfile
    {
        private const string Tag = "Meta";
        public const string Slot = "meta1";

        // 结算系数：本局达到的新高度按比例给碎片，通关额外奖励。
        private const int ShardsPerProgressUnit = 20;  // floor(progress*20) 的每点对应 1 碎片
        private const int VictoryBonus = 50;

        private static MetaProfile _current;
        public static MetaProfile Current => _current ??= Load();

        private readonly MetaSaveData _data;
        public MetaSaveData Data => _data;

        private MetaProfile(MetaSaveData data) => _data = data;

        public int Shards => _data.Shards;
        public int EnergyCapLevel => _data.EnergyCapLevel;
        public float BestProgress => _data.BestProgress;
        public int RunCount => _data.RunCount;
        public int Victories => _data.Victories;

        /// <summary>由局外成长提供的初始能量上限加成（世界单位无关，直接是能量百分点）。</summary>
        public float EnergyCapBonus => _data.EnergyCapLevel * GameConst.MetaEnergyCapPerLevel;

        public static MetaProfile Load()
        {
            if (GameApp.Save.TryLoad(Slot, out MetaSaveData d) && d != null)
            {
                d.UnlockedSkillIds ??= new System.Collections.Generic.List<string>();
                return new MetaProfile(d);
            }
            return new MetaProfile(new MetaSaveData());
        }

        public void Save() => GameApp.Save.Save(Slot, _data);

        // —— 技能解锁 ——

        /// <summary>该技能当前是否在抽取池内（非默认锁定，或已解锁）。</summary>
        public bool IsSkillAvailable(string id)
            => !MetaCatalog.IsLockedByDefault(id) || _data.UnlockedSkillIds.Contains(id);

        public bool IsSkillUnlocked(string id) => _data.UnlockedSkillIds.Contains(id);

        public bool TryUnlockSkill(string id)
        {
            if (!MetaCatalog.IsLockedByDefault(id) || IsSkillUnlocked(id)) return false;
            if (_data.Shards < MetaCatalog.SkillUnlockCost) return false;
            _data.Shards -= MetaCatalog.SkillUnlockCost;
            _data.UnlockedSkillIds.Add(id);
            Save();
            Log.Info($"Unlocked skill '{id}' for {MetaCatalog.SkillUnlockCost} shards.", Tag);
            return true;
        }

        // —— 能量上限 ——

        public bool IsEnergyCapMaxed => _data.EnergyCapLevel >= GameConst.MetaEnergyCapMaxLevel;

        /// <summary>下一级能量上限升级花费（随等级递增）。</summary>
        public int EnergyCapCost => 30 * (_data.EnergyCapLevel + 1);

        public bool TryUpgradeEnergyCap()
        {
            if (IsEnergyCapMaxed) return false;
            int cost = EnergyCapCost;
            if (_data.Shards < cost) return false;
            _data.Shards -= cost;
            _data.EnergyCapLevel++;
            Save();
            Log.Info($"Energy cap upgraded to level {_data.EnergyCapLevel} for {cost} shards.", Tag);
            return true;
        }

        // —— 对局结束结算 ——

        /// <summary>对局结束（返回菜单/通关）时结算：达到新高度给碎片，通关额外奖励。</summary>
        public void OnRunEnded(float maxProgress, bool victory)
        {
            _data.RunCount++;
            if (victory) _data.Victories++;

            int newHeightShards = Mathf.Max(0,
                Mathf.FloorToInt(maxProgress * ShardsPerProgressUnit)
                - Mathf.FloorToInt(_data.BestProgress * ShardsPerProgressUnit));
            int award = newHeightShards + (victory ? VictoryBonus : 0);

            if (award > 0)
            {
                _data.Shards += award;
                _data.LifetimeShards += award;
            }

            if (maxProgress > _data.BestProgress) _data.BestProgress = maxProgress;

            Save();
            Log.Info($"Run ended: progress={maxProgress:P0} victory={victory} award={award} shards={_data.Shards}.", Tag);
        }
    }
}
