using System.Collections.Generic;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>
    /// 菜品技能定义（数量无上限）。纯数据，由 Game 层从 Luban TbSkill(正向有序引用) + TbSubSkill(具体子技能) 适配生成。
    /// Name 为展示标题（术语名；无 termId 则空）。Desc 为运行时拼接：各子技能占位符模板按序回填后用「；」连接，或由 TbSkill.descOverride 覆盖。
    /// </summary>
    public sealed class SkillDef
    {
        private static readonly IReadOnlyList<SkillRuleDef> EmptyRules = new SkillRuleDef[0];
        private static readonly IReadOnlyList<string> EmptyDescs = new string[0];

        public SkillDef(
            string id,
            string name,
            string desc,
            string termId,
            IReadOnlyList<SkillRuleDef> rules = null,
            IReadOnlyList<string> ruleDescs = null)
        {
            Id = id;
            Name = name;
            Desc = desc;
            TermId = termId ?? string.Empty;
            Rules = rules ?? EmptyRules;
            RuleDescs = ruleDescs ?? EmptyDescs;
        }

        public string Id { get; }

        public string Name { get; }

        public string Desc { get; }

        public string TermId { get; }

        public bool HasTerm => !string.IsNullOrEmpty(TermId);

        /// <summary>「前提×行为」组合规则（按 order 升序）。</summary>
        public IReadOnlyList<SkillRuleDef> Rules { get; }

        /// <summary>与 <see cref="Rules"/> 一一对应的单条子技能描述片段（供甜蜜传递把子技能带走时展示其独立描述）。</summary>
        public IReadOnlyList<string> RuleDescs { get; }

        /// <summary>是否使用组合规则模式（有子表规则）。</summary>
        public bool HasRules => Rules.Count > 0;
    }

    /// <summary>
    /// 一条「可携带的子技能」：规则本体 + 其独立描述片段。
    /// 甜蜜传递把来源 skill 内的非传递子技能打包成若干 <see cref="SkillEffect"/> 交给目标，
    /// 目标结算时施加 <see cref="Rule"/>，tips 展示 <see cref="Desc"/>。
    /// </summary>
    public sealed class SkillEffect
    {
        public SkillEffect(SkillRuleDef rule, string desc)
        {
            Rule = rule;
            Desc = desc ?? string.Empty;
        }

        public SkillRuleDef Rule { get; }

        public string Desc { get; }
    }
}
