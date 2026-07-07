using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Board
{
    /// <summary>
    /// 棋盘上的一个菜品实例：引用菜品定义，记录其朝向、占格、技能集合与风味。
    /// 技能与风味在创建时由「初始技能列表 + 单槽风味」确定，运行时可被道具追加/替换。
    /// </summary>
    public sealed class DishInstance
    {
        private readonly List<GridPos> _occupiedCells;
        private readonly List<string> _skillIds;
        private readonly Dictionary<string, string> _skillSources = new Dictionary<string, string>();

        public DishInstance(int id, DishDef def, Placement placement, IReadOnlyList<string> skillIds, string flavorId)
        {
            Id = id;
            Def = def ?? throw new ArgumentNullException(nameof(def));
            Placement = placement;
            _skillIds = skillIds != null ? new List<string>(skillIds) : new List<string>();
            FlavorId = flavorId ?? string.Empty;

            _occupiedCells = placement.Orientation.Cells
                .Select(c => c.Offset(placement.Origin.X, placement.Origin.Y))
                .ToList();
        }

        /// <summary>棋盘内唯一序号，用于稳定排序与表现层映射。</summary>
        public int Id { get; }

        public DishDef Def { get; }

        public Placement Placement { get; private set; }

        /// <summary>
        /// 迁移到新的摆放（朝向 + 原点）并重算绝对占格，保留 Id/层数/技能/风味。
        /// 供「食物调整」态移动菜品：调用方需先 <see cref="Board.RemoveDish"/>，再 Relocate，最后 <see cref="Board.Place"/>。
        /// </summary>
        public void Relocate(Placement placement)
        {
            Placement = placement;
            _occupiedCells.Clear();
            foreach (GridPos cell in placement.Orientation.Cells)
            {
                _occupiedCells.Add(cell.Offset(placement.Origin.X, placement.Origin.Y));
            }
        }

        /// <summary>该实例的运行时技能 id 列表（数量无上限，可被技能传递追加）。</summary>
        public IReadOnlyList<string> SkillIds => _skillIds;

        /// <summary>该实例的最终风味 id（单槽，可空）。</summary>
        public string FlavorId { get; }

        /// <summary>运行时「视为食物数」加成（AddCountAs 副作用累加，跨结算持久）。</summary>
        public int RuntimeCountAsBonus { get; private set; }

        /// <summary>本实例最终「视为食物数」= 定义值 + 运行时加成（下限 1）。技能计数时按此累加。</summary>
        public int EffectiveCountAs => Math.Max(1, Def.CountAs + RuntimeCountAsBonus);

        /// <summary>运行时永久加法分（PermanentAddFlat 累加，计入基础分）。</summary>
        public float PermanentFlatBonus { get; private set; }

        /// <summary>运行时永久乘区（PermanentAddMult 累乘，计入乘区初值），初始 1。</summary>
        public float PermanentMultBonus { get; private set; } = 1f;

        /// <summary>累加「视为食物数」加成。</summary>
        public void AddCountAsBonus(int delta)
        {
            RuntimeCountAsBonus += delta;
        }

        /// <summary>累加永久加法分。</summary>
        public void AddPermanentFlat(float delta)
        {
            PermanentFlatBonus += delta;
        }

        /// <summary>累乘永久乘区（value 为倍数，如 1.2）。</summary>
        public void MultiplyPermanentMult(float value)
        {
            if (value > 0f)
            {
                PermanentMultBonus *= value;
            }
        }

        /// <summary>追加运行时技能（技能传递）。已存在则不重复。</summary>
        public void AddSkill(string skillId)
        {
            AddSkill(skillId, null);
        }

        /// <summary>
        /// 追加运行时技能并记录来源标签（如「马卡龙&lt;甜蜜传递&gt;」）。已存在则不重复，
        /// 但仍会补记来源标签（供明细/tips 显示技能是从别的菜获得）。
        /// </summary>
        public void AddSkill(string skillId, string sourceLabel)
        {
            if (string.IsNullOrEmpty(skillId))
            {
                return;
            }

            if (!_skillIds.Contains(skillId))
            {
                _skillIds.Add(skillId);
            }

            if (!string.IsNullOrEmpty(sourceLabel) && !_skillSources.ContainsKey(skillId))
            {
                _skillSources[skillId] = sourceLabel;
            }
        }

        /// <summary>返回某技能的来源标签（由甜蜜传递/技能复制获得时非空），无则返回 null。</summary>
        public string GetSkillSource(string skillId)
        {
            return skillId != null && _skillSources.TryGetValue(skillId, out string s) ? s : null;
        }

        /// <summary>技能来源标签映射（skillId → 来源标签）。</summary>
        public IReadOnlyDictionary<string, string> SkillSources => _skillSources;

        /// <summary>从另一实例复制技能来源标签（临时克隆时保留来源展示）。</summary>
        public void CopySkillSourcesFrom(DishInstance other)
        {
            if (other == null)
            {
                return;
            }

            foreach (KeyValuePair<string, string> kv in other._skillSources)
            {
                _skillSources[kv.Key] = kv.Value;
            }
        }

        /// <summary>是否为「临时复制」产生的克隆实例（品鉴结束时清理，且自身不再触发临时复制）。</summary>
        public bool IsTemporary { get; private set; }

        /// <summary>标记为临时克隆实例。</summary>
        public void MarkTemporary()
        {
            IsTemporary = true;
        }

        /// <summary>是否带有风味。</summary>
        public bool HasFlavor => !string.IsNullOrEmpty(FlavorId);

        /// <summary>绝对占格列表。</summary>
        public IReadOnlyList<GridPos> OccupiedCells => _occupiedCells;

        public bool Occupies(GridPos cell) => _occupiedCells.Contains(cell);
    }
}
