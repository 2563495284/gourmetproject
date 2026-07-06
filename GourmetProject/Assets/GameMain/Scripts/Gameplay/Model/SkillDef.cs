using System.Collections.Generic;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 菜品技能定义（数量无上限）。纯数据，由 Game 层从 Luban TbSkill + TbSkillRule 适配生成。
    /// </summary>
    public sealed class SkillDef
    {
        private static readonly IReadOnlyList<SkillRuleDef> EmptyRules = new SkillRuleDef[0];

        public SkillDef(
            string id,
            string name,
            string desc,
            string termId,
            IReadOnlyList<SkillRuleDef> rules = null)
        {
            Id = id;
            Name = name;
            Desc = desc;
            TermId = termId ?? string.Empty;
            Rules = rules ?? EmptyRules;
        }

        public string Id { get; }

        public string Name { get; }

        public string Desc { get; }

        public string TermId { get; }

        public bool HasTerm => !string.IsNullOrEmpty(TermId);

        /// <summary>「前提×行为」组合规则（按 order 升序）。</summary>
        public IReadOnlyList<SkillRuleDef> Rules { get; }

        /// <summary>是否使用组合规则模式（有子表规则）。</summary>
        public bool HasRules => Rules.Count > 0;
    }
}
