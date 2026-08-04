namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 装饰品和消耗品 <see cref="cfg.ItemSpecialTag"/> 筛选规则。默认随机池排除负面，仅显式指定奖励池/随机参数时才纳入。
    /// </summary>
    public static class ItemTagFilter
    {
        public static bool HasTag(cfg.ItemSpecialTag itemTag, cfg.ItemSpecialTag tag)
        {
            return itemTag == tag;
        }

        public static bool IsNegative(cfg.ItemSpecialTag itemTag)
        {
            return itemTag == cfg.ItemSpecialTag.Negative;
        }

        /// <summary>
        /// <paramref name="requiredTag"/> 为 None：排除诅咒装饰品；否则装饰品和消耗品须匹配该标签。
        /// </summary>
        public static bool MatchesFilter(
            cfg.ItemSpecialTag itemTag,
            cfg.ItemSpecialTag requiredTag)
        {
            if (requiredTag != cfg.ItemSpecialTag.None)
            {
                return itemTag == requiredTag;
            }

            return !IsNegative(itemTag);
        }
    }
}
