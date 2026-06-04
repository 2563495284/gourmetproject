using System.Collections.Generic;

namespace GourmetProject.Game.Roguelike
{
    /// <summary>
    /// 单局已选技能状态：记录每个技能的叠加层数、各 tag 的累计选取数（用于 Tag 软引导加权），
    /// 以及选取顺序（用于存档重放）。<see cref="SkillDraftService"/> 据此过滤候选与计算权重。
    /// </summary>
    public sealed class RunSkillState
    {
        private readonly Dictionary<string, int> _stacks = new Dictionary<string, int>();
        private readonly Dictionary<SkillTag, int> _tagCounts = new Dictionary<SkillTag, int>();
        private readonly List<string> _pickedOrder = new List<string>();

        /// <summary>按选取先后排列的技能 id（含重复，用于存档重放）。</summary>
        public IReadOnlyList<string> PickedOrder => _pickedOrder;

        /// <summary>是否已拥有该技能（层数 ≥ 1）。</summary>
        public bool Has(string id) => _stacks.TryGetValue(id, out int n) && n > 0;

        /// <summary>该技能当前叠加层数。</summary>
        public int StacksOf(string id) => _stacks.TryGetValue(id, out int n) ? n : 0;

        /// <summary>玩家已选的该 tag 技能数（Tag 软引导用）。</summary>
        public int TagCount(SkillTag tag) => _tagCounts.TryGetValue(tag, out int n) ? n : 0;

        /// <summary>应用一次选取：层数 +1、tag 计数 +1、记录顺序。</summary>
        public void Apply(SkillDef def)
        {
            if (def == null) return;

            _stacks.TryGetValue(def.Id, out int cur);
            _stacks[def.Id] = cur + 1;

            _tagCounts.TryGetValue(def.Tag, out int t);
            _tagCounts[def.Tag] = t + 1;

            _pickedOrder.Add(def.Id);
        }

        /// <summary>清空所有状态（开新局时调用）。</summary>
        public void Clear()
        {
            _stacks.Clear();
            _tagCounts.Clear();
            _pickedOrder.Clear();
        }
    }
}
