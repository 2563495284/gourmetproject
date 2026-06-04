using System.Collections.Generic;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Roguelike
{
    /// <summary>
    /// 技能图鉴：把 Luban <see cref="cfg.TbSkill"/> 转换为玩法层 <see cref="SkillDef"/> 列表并按 Tier 分桶。
    /// 抽取算法只面向 <see cref="SkillDef"/>，本类是 Config → 逻辑层的唯一转换边界。
    /// </summary>
    public sealed class SkillCatalog
    {
        private static readonly IReadOnlyList<SkillDef> EmptyPool = new SkillDef[0];

        private readonly List<SkillDef> _all = new List<SkillDef>();
        private readonly Dictionary<int, List<SkillDef>> _byTier = new Dictionary<int, List<SkillDef>>();
        private readonly Dictionary<string, SkillDef> _byId = new Dictionary<string, SkillDef>();

        /// <summary>全部技能（只读）。</summary>
        public IReadOnlyList<SkillDef> All => _all;

        /// <summary>从已加载的全局配置构建。须在 <c>GameApp.Config.LoadAll()</c> 之后调用。</summary>
        public static SkillCatalog FromConfig()
        {
            var catalog = new SkillCatalog();
            cfg.TbSkill table = GameApp.Config.Tables.TbSkill;
            IReadOnlyList<cfg.Skill> rows = table.DataList;
            for (int i = 0; i < rows.Count; i++)
            {
                catalog.Add(ToDef(rows[i]));
            }
            return catalog;
        }

        /// <summary>由外部技能集合构建（测试或自定义数据源）。</summary>
        public static SkillCatalog FromDefs(IEnumerable<SkillDef> defs)
        {
            var catalog = new SkillCatalog();
            if (defs != null)
            {
                foreach (SkillDef def in defs) catalog.Add(def);
            }
            return catalog;
        }

        /// <summary>取指定 Tier 的技能池（不存在返回空）。</summary>
        public IReadOnlyList<SkillDef> PoolOf(int tier)
            => _byTier.TryGetValue(tier, out List<SkillDef> list) ? list : EmptyPool;

        /// <summary>按 id 取技能定义（不存在返回 null）。</summary>
        public SkillDef Get(string id)
            => id != null && _byId.TryGetValue(id, out SkillDef def) ? def : null;

        private void Add(SkillDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.Id) || _byId.ContainsKey(def.Id)) return;

            _all.Add(def);
            _byId.Add(def.Id, def);

            if (!_byTier.TryGetValue(def.Tier, out List<SkillDef> list))
            {
                list = new List<SkillDef>();
                _byTier.Add(def.Tier, list);
            }
            list.Add(def);
        }

        private static SkillDef ToDef(cfg.Skill row)
        {
            return new SkillDef
            {
                Id = row.Id,
                Tier = row.Tier,
                Tag = (SkillTag)(int)row.Tag,
                Name = row.Name,
                Desc = row.Desc,
                Weight = row.Weight,
                IsMovement = row.IsMovement,
                MaxStacks = row.MaxStacks,
                PrereqAny = row.PrereqAny ?? (IReadOnlyList<string>)new string[0],
                Mutex = row.Mutex ?? (IReadOnlyList<string>)new string[0],
            };
        }
    }
}
