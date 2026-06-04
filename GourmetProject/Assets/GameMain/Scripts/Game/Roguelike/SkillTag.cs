namespace GourmetProject.Game.Roguelike
{
    /// <summary>
    /// 技能软引导标签（设计文档 13.4/13.5）。玩法核心逻辑使用本枚举，
    /// 与 Luban 生成的 <see cref="cfg.SkillTag"/> 数值一一对应，由 <see cref="SkillCatalog"/> 转换，
    /// 以保证抽取算法不直接依赖配置生成代码、便于独立单元测试。
    /// </summary>
    public enum SkillTag
    {
        /// <summary>移动 — 怎么爬。</summary>
        Move = 0,

        /// <summary>光源/信息 — 怎么看。</summary>
        Light = 1,

        /// <summary>生存/容错 — 怎么活。</summary>
        Survival = 2,

        /// <summary>资源经济 — 怎么管能量。</summary>
        Economy = 3,

        /// <summary>怪物交互 — 怎么应对怪。</summary>
        Monster = 4,
    }
}
