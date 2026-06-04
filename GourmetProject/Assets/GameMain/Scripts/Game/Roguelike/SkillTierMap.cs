namespace GourmetProject.Game.Roguelike
{
    /// <summary>
    /// 检查点序号 → 技能段位映射（设计文档 13.5）。6 个普通检查点对应三池：
    /// CP1-2 → T1，CP3-4 → T2，CP5-6 → T3。终点（灯塔）不触发技能选择。
    /// </summary>
    public static class SkillTierMap
    {
        public const int MinTier = 1;
        public const int MaxTier = 3;

        /// <summary>
        /// 按"第几个被激活的普通检查点"（0 基）返回段位。
        /// 0,1→1；2,3→2；4,5→3；超出按上下限钳制，保证非常规 CP 数量也有合法 Tier。
        /// </summary>
        public static int TierForCheckpoint(int activatedIndex)
        {
            int tier = activatedIndex / 2 + 1;
            if (tier < MinTier) return MinTier;
            if (tier > MaxTier) return MaxTier;
            return tier;
        }
    }
}
