using System.Collections.Generic;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 运行时代码使用的统一道具定义视图。配置源已拆成被动/主动两张表，
    /// 这里负责收敛公共字段和按 id 查询，避免业务层到处判断具体表类型。
    /// </summary>
    public sealed class ItemDefinition
    {
        private ItemDefinition(cfg.PassiveItem passive)
        {
            Passive = passive;
            Kind = cfg.ItemKind.Passive;
            Id = passive.Id;
            Name = passive.Name;
            Desc = passive.Desc;
            Quality = passive.Quality;
            SpecialTags = passive.SpecialTags;
            TermIds = SplitTermIds(passive.TermId);
            // 被动道具已按 itemId → PassiveItemModel 绑定，不再依赖 effectType；此处不读配置列（便于后续从表中移除）。
            EffectType = string.Empty;
            EffectValue = passive.EffectValue;
            EffectParam = passive.EffectParam;
            BaseWeight = passive.BaseWeight;
            HiddenRange = passive.HiddenRange;
            TargetScoreHiddenOffset = passive.TargetScoreHiddenOffset;
            DishHiddenOffset = passive.DishHiddenOffset;
            PassiveItemHiddenOffset = passive.PassiveItemHiddenOffset;
            ActiveItemHiddenOffset = passive.ActiveItemHiddenOffset;
            FragmentHiddenOffset = passive.FragmentHiddenOffset;
            GoldHiddenOffset = passive.GoldHiddenOffset;
            TargetKind = cfg.ItemTargetKind.None;
            TargetCount = 0;
        }

        private ItemDefinition(cfg.ActiveItem active)
        {
            Active = active;
            Kind = cfg.ItemKind.Active;
            Id = active.Id;
            Name = active.Name;
            Desc = active.Desc;
            Quality = active.Quality;
            SpecialTags = active.SpecialTags;
            TermIds = SplitTermIds(active.TermId);
            EffectType = active.EffectType;
            EffectValue = active.EffectValue;
            EffectParam = active.EffectParam;
            BaseWeight = active.BaseWeight;
            TargetScoreHiddenOffset = 0;
            DishHiddenOffset = 0;
            PassiveItemHiddenOffset = 0;
            ActiveItemHiddenOffset = 0;
            FragmentHiddenOffset = 0;
            GoldHiddenOffset = 0;
            TargetKind = active.TargetKind;
            TargetCount = active.TargetCount;
        }

        public cfg.PassiveItem Passive { get; }

        public cfg.ActiveItem Active { get; }

        public cfg.ItemKind Kind { get; }

        public string Id { get; }

        public string Name { get; }

        public string Desc { get; }

        public cfg.ItemQuality Quality { get; }

        public string SpecialTags { get; }

        /// <summary>道具关联的专有名词 id（已去重）；来源为配置 termId 列（| 分隔）。供 tips 展示名词解释。</summary>
        public IReadOnlyList<string> TermIds { get; }

        public string EffectType { get; }

        public float EffectValue { get; }

        public string EffectParam { get; }

        public float BaseWeight { get; }

        public cfg.HiddenRange HiddenRange { get; }

        public float TargetScoreHiddenOffset { get; }

        public float DishHiddenOffset { get; }

        public float PassiveItemHiddenOffset { get; }

        public float ActiveItemHiddenOffset { get; }

        public float FragmentHiddenOffset { get; }

        public float GoldHiddenOffset { get; }

        public cfg.ItemTargetKind TargetKind { get; }

        public int TargetCount { get; }

        public bool IsPassive => Kind == cfg.ItemKind.Passive;

        public bool IsActive => Kind == cfg.ItemKind.Active;

        public float HiddenScoreOffset(HiddenScorePurpose purpose)
        {
            switch (purpose)
            {
                case HiddenScorePurpose.TargetScore:
                    return TargetScoreHiddenOffset;
                case HiddenScorePurpose.Dish:
                    return DishHiddenOffset;
                case HiddenScorePurpose.PassiveItem:
                    return PassiveItemHiddenOffset;
                case HiddenScorePurpose.ActiveItem:
                    return ActiveItemHiddenOffset;
                case HiddenScorePurpose.Fragment:
                    return FragmentHiddenOffset;
                case HiddenScorePurpose.Gold:
                    return GoldHiddenOffset;
                default:
                    return 0f;
            }
        }

        private static IReadOnlyList<string> SplitTermIds(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return System.Array.Empty<string>();
            }

            var result = new List<string>();
            foreach (string item in value.Split('|'))
            {
                string trimmed = item.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !result.Contains(trimmed))
                {
                    result.Add(trimmed);
                }
            }

            return result.Count > 0 ? result : System.Array.Empty<string>();
        }

        public static ItemDefinition From(cfg.PassiveItem passive)
        {
            return passive != null ? new ItemDefinition(passive) : null;
        }

        public static ItemDefinition From(cfg.ActiveItem active)
        {
            return active != null ? new ItemDefinition(active) : null;
        }

        public static ItemDefinition Get(cfg.Tables tables, string itemId)
        {
            if (tables == null || string.IsNullOrEmpty(itemId))
            {
                return null;
            }

            cfg.PassiveItem passive = tables.TbPassiveItem.GetOrDefault(itemId);
            if (passive != null)
            {
                return From(passive);
            }

            return From(tables.TbActiveItem.GetOrDefault(itemId));
        }

        public static ItemDefinition Get(cfg.Tables tables, string itemId, cfg.ItemKind kind)
        {
            if (tables == null || string.IsNullOrEmpty(itemId))
            {
                return null;
            }

            return kind == cfg.ItemKind.Passive
                ? From(tables.TbPassiveItem.GetOrDefault(itemId))
                : From(tables.TbActiveItem.GetOrDefault(itemId));
        }

        public static IEnumerable<ItemDefinition> All(cfg.Tables tables, cfg.ItemKind kind)
        {
            if (tables == null)
            {
                yield break;
            }

            if (kind == cfg.ItemKind.Passive)
            {
                foreach (cfg.PassiveItem item in tables.TbPassiveItem.DataList)
                {
                    yield return From(item);
                }
            }
            else
            {
                foreach (cfg.ActiveItem item in tables.TbActiveItem.DataList)
                {
                    yield return From(item);
                }
            }
        }
    }
}
