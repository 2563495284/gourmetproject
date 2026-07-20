using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Board
{
    /// <summary>
    /// 餐桌上的一个菜品实例：引用菜品定义，记录其朝向、占格、技能集合与风味。
    /// 技能与风味在创建时由「初始技能列表 + 单槽风味」确定，运行时可被道具追加/替换。
    /// </summary>
    public sealed class DishInstance
    {
        private readonly List<GridPos> _occupiedCells;
        private readonly List<string> _skillIds;
        private readonly List<string> _flavorIds;
        private readonly Dictionary<string, string> _skillSources = new Dictionary<string, string>();
        private readonly List<TransferredSkill> _transferredSkills = new List<TransferredSkill>();

        public DishInstance(int id, DishDef def, Placement placement, IReadOnlyList<string> skillIds, IReadOnlyList<string> flavorIds)
        {
            Id = id;
            Def = def ?? throw new ArgumentNullException(nameof(def));
            Placement = placement;
            _skillIds = skillIds != null ? new List<string>(skillIds) : new List<string>();
            _flavorIds = new List<string>();
            if (flavorIds != null)
            {
                foreach (string f in flavorIds)
                {
                    if (!string.IsNullOrEmpty(f))
                    {
                        _flavorIds.Add(f);
                    }
                }
            }

            _occupiedCells = placement.Orientation.Cells
                .Select(c => c.Offset(placement.Origin.X, placement.Origin.Y))
                .ToList();
        }

        /// <summary>餐桌内唯一序号，用于稳定排序与表现层映射。</summary>
        public int Id { get; }

        public DishDef Def { get; }

        public Placement Placement { get; private set; }

        /// <summary>
        /// 迁移到新的摆放（朝向 + 原点）并重算绝对占格，保留 Id/层数/技能/风味。
        /// 供「食物调整」态移动菜品：调用方需先 <see cref="DiningTable.RemoveDish"/>，再 Relocate，最后 <see cref="DiningTable.Place"/>。
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

        /// <summary>溯源：本菜来自哪个菜谱槽（0 基），未知为 -1。供酸/咸「同菜谱」判定。</summary>
        public int SourceSlotIndex { get; private set; } = -1;

        /// <summary>溯源：本菜来自菜谱槽中的哪个条目（0 基），未知为 -1。供永久分写回菜谱条目。</summary>
        public int SourceDishIndex { get; private set; } = -1;

        /// <summary>设置菜谱槽溯源（上菜时写入）。</summary>
        public void SetSourceSlotIndex(int slotIndex)
        {
            SourceSlotIndex = slotIndex;
        }

        public void SetSourceRecipeIndex(int slotIndex, int dishIndex)
        {
            SourceSlotIndex = slotIndex;
            SourceDishIndex = dishIndex;
        }

        /// <summary>该实例的最终风味 id 列表（多槽，可叠加；同类风味按出现次数累计效果，如甜×n）。</summary>
        public IReadOnlyList<string> FlavorIds => _flavorIds;

        /// <summary>追加一个风味（道具/效果赋予）。允许重复以支持叠加计数。空串忽略。</summary>
        public void AddFlavor(string flavorId)
        {
            if (!string.IsNullOrEmpty(flavorId))
            {
                _flavorIds.Add(flavorId);
            }
        }

        public bool RemoveFlavor(string flavorId)
        {
            if (_flavorIds.Count == 0)
            {
                return false;
            }

            if (string.IsNullOrEmpty(flavorId))
            {
                _flavorIds.RemoveAt(_flavorIds.Count - 1);
                return true;
            }

            return _flavorIds.Remove(flavorId);
        }

        public bool ReplaceFlavor(string toFlavorId)
        {
            if (string.IsNullOrEmpty(toFlavorId))
            {
                return false;
            }

            if (_flavorIds.Count == 0)
            {
                _flavorIds.Add(toFlavorId);
            }
            else
            {
                _flavorIds[_flavorIds.Count - 1] = toFlavorId;
            }

            return true;
        }

        /// <summary>运行时「视为食物数」加成（AddCountAs 副作用累加，跨结算持久）。</summary>
        public int RuntimeCountAsBonus { get; private set; }

        /// <summary>本实例最终「视为食物数」= 定义值 + 运行时加成（下限 1）。技能计数时按此累加。</summary>
        public int EffectiveCountAs => Math.Max(1, Def.CountAs + RuntimeCountAsBonus);

        /// <summary>运行时永久加法分（PermanentAddFlat 累加，计入基础分）。</summary>
        public float PermanentFlatBonus { get; private set; }

        /// <summary>运行时永久乘区（PermanentAddMult 累乘，计入乘区初值），初始 1。</summary>
        public float PermanentMultBonus { get; private set; } = 1f;

        /// <summary>本场临时基础分倍率（Boss Debuff 等），只影响当前战斗内结算。</summary>
        public float TemporaryBaseMultiplier { get; private set; } = 1f;

        /// <summary>上菜时确定的临时乘区倍率（Boss Debuff 等），只影响当前战斗内结算。</summary>
        public float ServeMultiplier { get; private set; } = 1f;

        /// <summary>结算前「固化基础分」：基础美味度 + 永久加分，再乘本场基础分倍率（不含本次结算临时触发的加成）。</summary>
        public float BaseScoreBeforeSettlement => (Def.Deliciousness + PermanentFlatBonus) * TemporaryBaseMultiplier;

        /// <summary>结算前「固化乘区」：永久乘区 × 上菜临时乘区（不含本次结算临时触发的乘区）。</summary>
        public float BaseMultiplierBeforeSettlement => PermanentMultBonus * ServeMultiplier;

        /// <summary>本实例技能是否失效（清淡餐）。</summary>
        public bool SkillsDisabled { get; private set; }

        /// <summary>本实例是否不参与分数汇总与结算历史（斋饭/自助餐等）。</summary>
        public bool ExcludedFromScore { get; private set; }

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

        public void MultiplyTemporaryBase(float value)
        {
            if (value > 0f)
            {
                TemporaryBaseMultiplier *= value;
            }
        }

        public void MultiplyServeMultiplier(float value)
        {
            if (value > 0f)
            {
                ServeMultiplier *= value;
            }
        }

        public void DisableSkills()
        {
            SkillsDisabled = true;
        }

        public void ExcludeFromScore()
        {
            ExcludedFromScore = true;
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

        /// <summary>
        /// 由甜蜜传递获得的「外来子技能」：源 skill 内除传递外的子技能(rule)+其描述+来源标签。
        /// 随本实例生命周期存在（本次美食内临时，结算时随目标一并施加、tips 可见；美食结束实例销毁即清除）。
        /// </summary>
        public IReadOnlyList<TransferredSkill> TransferredSkills => _transferredSkills;

        /// <summary>追加一条外来子技能（甜蜜传递落地）。按 rule 引用去重，重复来源不叠加。</summary>
        public void AddTransferredSkill(SkillEffect effect, string sourceLabel)
        {
            AddTransferredSkill(effect, sourceLabel, 0);
        }

        public void AddTransferredSkill(SkillEffect effect, string sourceLabel, int sourceInstanceId)
        {
            if (effect?.Rule == null)
            {
                return;
            }

            for (int i = 0; i < _transferredSkills.Count; i++)
            {
                if (ReferenceEquals(_transferredSkills[i].Effect.Rule, effect.Rule))
                {
                    return;
                }
            }

            _transferredSkills.Add(new TransferredSkill(effect, sourceLabel, sourceInstanceId));
        }

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

            if (other.SkillsDisabled)
            {
                DisableSkills();
            }

            if (other.ExcludedFromScore)
            {
                ExcludeFromScore();
            }

            MultiplyTemporaryBase(other.TemporaryBaseMultiplier);
            MultiplyServeMultiplier(other.ServeMultiplier);
        }

        /// <summary>是否为「临时复制」产生的克隆实例（品鉴结束时清理，且自身不再触发临时复制）。</summary>
        public bool IsTemporary { get; private set; }

        /// <summary>标记为临时克隆实例。</summary>
        public void MarkTemporary()
        {
            IsTemporary = true;
        }

        /// <summary>是否带有风味。</summary>
        public bool HasFlavor => _flavorIds.Count > 0;

        /// <summary>绝对占格列表。</summary>
        public IReadOnlyList<GridPos> OccupiedCells => _occupiedCells;

        public bool Occupies(GridPos cell) => _occupiedCells.Contains(cell);

        /// <summary>把另一实例的外来子技能整体复制过来（临时克隆时保留传递效果与来源展示）。</summary>
        public void CopyTransferredSkillsFrom(DishInstance other)
        {
            if (other == null)
            {
                return;
            }

            foreach (TransferredSkill t in other._transferredSkills)
            {
                AddTransferredSkill(t.Effect, t.SourceLabel, t.SourceInstanceId);
            }
        }
    }

    /// <summary>目标实例上的一条外来子技能：效果(rule+描述) + 来源标签（如「巧克力棒&lt;甜蜜传递&gt;」）。</summary>
    public sealed class TransferredSkill
    {
        public TransferredSkill(GourmetProject.Gameplay.Model.SkillEffect effect, string sourceLabel)
            : this(effect, sourceLabel, 0)
        {
        }

        public TransferredSkill(GourmetProject.Gameplay.Model.SkillEffect effect, string sourceLabel, int sourceInstanceId)
        {
            Effect = effect;
            SourceLabel = sourceLabel ?? string.Empty;
            SourceInstanceId = sourceInstanceId;
        }

        public GourmetProject.Gameplay.Model.SkillEffect Effect { get; }

        public string SourceLabel { get; }

        public int SourceInstanceId { get; }

        /// <summary>该外来子技能的展示描述（等于 <see cref="Effect"/> 的描述片段），供 UI/tips 直接使用。</summary>
        public string Desc => Effect != null ? Effect.Desc : string.Empty;

        /// <summary>该外来子技能的规则本体（等于 <see cref="Effect"/> 的规则），供结算/测试直接使用。</summary>
        public GourmetProject.Gameplay.Model.SkillRuleDef Rule => Effect != null ? Effect.Rule : null;
    }
}
