using System.Collections.Generic;

namespace GourmetProject.Game.Roguelike
{
    /// <summary>局外成长解锁项类型。</summary>
    public enum MetaUnlockKind
    {
        EnergyCap,    // 提升初始能量上限（可重复，至上限等级）
        SkillUnlock,  // 解锁一个默认锁定的技能进入抽取池
    }

    /// <summary>单个局外成长解锁项（碎片消费对象）。</summary>
    public sealed class MetaUnlock
    {
        public MetaUnlockKind Kind;
        public string SkillId;   // SkillUnlock 用：对应技能 id（显示名/描述由 SkillCatalog 解析）
        public int Cost;         // EnergyCap 项的 Cost 为基准值，实际按等级递增由 MetaProfile 计算
    }

    /// <summary>
    /// 局外成长目录：默认锁定的进阶/终极技集合，以及可购买的解锁项列表（代码内定义，
    /// 与 Luban 技能表通过 id 关联；横向扩展不产生数值碾压）。设计文档 13.10。
    /// </summary>
    public static class MetaCatalog
    {
        public const string EnergyCapId = "meta_energy_cap";
        public const int SkillUnlockCost = 40;

        /// <summary>默认锁定、需用碎片解锁后才会进入抽取池的技能 id（进阶/终极向）。</summary>
        private static readonly HashSet<string> LockedSkills = new HashSet<string>
        {
            SkillIds.DashInvuln,
            SkillIds.Aura30,
            SkillIds.Cost30,
            SkillIds.PulseCd3,
            SkillIds.Revive,
            SkillIds.InvulnExt,
            SkillIds.LightBurst,
            SkillIds.ShadowRepelAura,
        };

        /// <summary>该技能是否默认锁定（未解锁则不进抽取池）。</summary>
        public static bool IsLockedByDefault(string id) => id != null && LockedSkills.Contains(id);

        /// <summary>可解锁技能 id 列表（用于成长面板逐项展示）。</summary>
        public static IReadOnlyCollection<string> LockableSkillIds => LockedSkills;
    }
}
