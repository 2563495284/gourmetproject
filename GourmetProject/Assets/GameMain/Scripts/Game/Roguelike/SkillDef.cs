using System;
using System.Collections.Generic;

namespace GourmetProject.Game.Roguelike
{
    /// <summary>
    /// 技能定义（运行时只读快照，玩法层 POCO）。由 <see cref="SkillCatalog"/> 从 Luban
    /// <see cref="cfg.Skill"/> 转换而来；抽取算法 <see cref="SkillDraftService"/> 只依赖本类型与
    /// <see cref="GourmetProject.Core.Rng.IRandomStream"/>，因此可脱离 Unity/Config 在 EditMode 单测。
    /// </summary>
    public sealed class SkillDef
    {
        /// <summary>唯一 id（前置/互斥引用的就是它）。</summary>
        public string Id { get; set; }

        /// <summary>段位池：1=T1(CP1-2) 2=T2(CP3-4) 3=T3(CP5-6)。</summary>
        public int Tier { get; set; }

        /// <summary>软引导标签。</summary>
        public SkillTag Tag { get; set; }

        /// <summary>显示名。</summary>
        public string Name { get; set; }

        /// <summary>效果描述。</summary>
        public string Desc { get; set; }

        /// <summary>基础抽取权重（应为正）。</summary>
        public float Weight { get; set; } = 1f;

        /// <summary>是否为核心移动技（#1-#9）：true 表示全局唯一、被选后不再出现。</summary>
        public bool IsMovement { get; set; }

        /// <summary>同一技能可被选取次数上限：0 表示无限（经济系）。</summary>
        public int MaxStacks { get; set; } = 1;

        /// <summary>前置技能 id：拥有其中任一即解锁；为空表示无前置。</summary>
        public IReadOnlyList<string> PrereqAny { get; set; } = Array.Empty<string>();

        /// <summary>互斥技能 id：拥有其中任一则本技能不再出现。</summary>
        public IReadOnlyList<string> Mutex { get; set; } = Array.Empty<string>();

        /// <summary>是否可无限叠加（经济系）。</summary>
        public bool Unlimited => MaxStacks <= 0;

        public override string ToString() => $"{Id}(T{Tier},{Tag})";
    }
}
