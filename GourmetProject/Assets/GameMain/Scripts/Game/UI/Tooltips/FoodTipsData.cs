using System;
using System.Collections.Generic;
using BreakInfinity;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 食物 Tips 的纯展示数据。1/2/3 模块都只依赖这里，方便单独展示。
    /// </summary>
    public sealed class FoodTipsData
    {
        public FoodTipsData(
            FoodSummaryTipsData summary,
            FoodScoreTipsData score,
            IReadOnlyList<FoodMaterialTipsEntry> materials,
            IReadOnlyList<FoodInfoEntry> flavorDetails,
            IReadOnlyList<FoodInfoEntry> externalSkills,
            IReadOnlyList<FoodInfoEntry> specialTags)
        {
            Summary = summary ?? FoodSummaryTipsData.Empty;
            Score = score ?? FoodScoreTipsData.Empty;
            Materials = materials ?? Array.Empty<FoodMaterialTipsEntry>();
            FlavorDetails = flavorDetails ?? Array.Empty<FoodInfoEntry>();
            ExternalSkills = externalSkills ?? Array.Empty<FoodInfoEntry>();
            SpecialTags = specialTags ?? Array.Empty<FoodInfoEntry>();
        }

        public FoodSummaryTipsData Summary { get; }

        public FoodScoreTipsData Score { get; }

        public IReadOnlyList<FoodMaterialTipsEntry> Materials { get; }

        public IReadOnlyList<FoodInfoEntry> FlavorDetails { get; }

        /// <summary>外源技能：复制获得的技能、甜蜜传递子技能、临时效果。</summary>
        public IReadOnlyList<FoodInfoEntry> ExternalSkills { get; }

        public IReadOnlyList<FoodInfoEntry> SpecialTags { get; }
    }

    public sealed class FoodSummaryTipsData
    {
        public static readonly FoodSummaryTipsData Empty = new FoodSummaryTipsData(
            string.Empty,
            Array.Empty<FoodInfoEntry>(),
            Array.Empty<string>());

        public FoodSummaryTipsData(
            string foodName,
            IReadOnlyList<FoodInfoEntry> skills,
            IReadOnlyList<string> flavors,
            bool skillsDisabled = false,
            bool isTemporaryCopy = false,
            int countAs = 1)
        {
            FoodName = foodName ?? string.Empty;
            Skills = skills ?? Array.Empty<FoodInfoEntry>();
            Flavors = flavors ?? Array.Empty<string>();
            SkillsDisabled = skillsDisabled;
            IsTemporaryCopy = isTemporaryCopy;
            CountAs = Math.Max(1, countAs);
        }

        public string FoodName { get; }

        public IReadOnlyList<FoodInfoEntry> Skills { get; }

        public IReadOnlyList<string> Flavors { get; }

        public bool SkillsDisabled { get; }

        /// <summary>是否为临时复制产生的食物；永久复制品不属于该标记。</summary>
        public bool IsTemporaryCopy { get; }

        /// <summary>当前展示场景下的有效份数。</summary>
        public int CountAs { get; }
    }

    public sealed class FoodScoreTipsData
    {
        public static readonly FoodScoreTipsData Empty = new FoodScoreTipsData(0f, 1f);

        public FoodScoreTipsData(BigDouble score, BigDouble multiplier)
        {
            Score = score;
            Multiplier = multiplier;
        }

        /// <summary>分数：基础分数 + 加法分。</summary>
        public BigDouble Score { get; }

        /// <summary>倍率：本食物当前倍率。</summary>
        public BigDouble Multiplier { get; }

        /// <summary>美味值：倍率 * 分数 后向上取整。</summary>
        public BigDouble Deliciousness => DishScore.CeilContribution(Score, Multiplier);
    }

    public sealed class FoodMaterialTipsEntry
    {
        public FoodMaterialTipsEntry(string id, string name, string desc, int cellCount)
        {
            Id = id ?? string.Empty;
            Name = name ?? Id;
            Desc = desc ?? string.Empty;
            CellCount = Math.Max(1, cellCount);
        }

        public string Id { get; }

        public string Name { get; }

        public string Desc { get; }

        public int CellCount { get; }
    }

    public sealed class FoodInfoEntry
    {
        public FoodInfoEntry(string title, string desc)
        {
            Title = title ?? string.Empty;
            Desc = desc ?? string.Empty;
        }

        public string Title { get; }

        public string Desc { get; }
    }

    /// <summary>
    /// 结算演出「渐进揭示」用的覆盖参数：只显示当前已表演到的分数/倍率与技能条目数量。
    /// </summary>
    public sealed class FoodTipsReveal
    {
        public FoodTipsReveal(
            BigDouble score,
            BigDouble multiplier,
            int maxSkills,
            int maxCopied,
            int maxTransferred,
            int countAs = 1,
            int maxTemporaryEffects = -1)
        {
            Score = score;
            Multiplier = multiplier;
            MaxSkills = maxSkills;
            MaxCopied = maxCopied;
            MaxTransferred = maxTransferred;
            CountAs = Math.Max(1, countAs);
            MaxTemporaryEffects = maxTemporaryEffects;
        }

        /// <summary>已揭示的分数（基础分数 + 已表演的加法分）。</summary>
        public BigDouble Score { get; }

        /// <summary>已揭示的倍率（已表演到的倍率）。</summary>
        public BigDouble Multiplier { get; }

        /// <summary>固有技能最多显示前几条；-1 表示全部。</summary>
        public int MaxSkills { get; }

        /// <summary>复制获得的技能最多显示前几条；-1 表示全部。</summary>
        public int MaxCopied { get; }

        /// <summary>甜蜜传递子技能最多显示前几条；-1 表示全部。</summary>
        public int MaxTransferred { get; }

        /// <summary>已揭示的有效份数。</summary>
        public int CountAs { get; }

        /// <summary>临时效果说明最多显示几条；-1 表示全部。</summary>
        public int MaxTemporaryEffects { get; }
    }

    public static class FoodTipsDataFactory
    {
        public static FoodTipsData Build(
            DishInstance dish,
            DiningTable table,
            GameplayDatabase db,
            ScoreResult scoreResult = null,
            int? effectiveCountAsOverride = null)
        {
            if (dish == null)
            {
                return new FoodTipsData(null, null, null, null, null, null);
            }

            DishScore score = FindScore(scoreResult, dish.Id);
            return Build(dish, table, db, score, effectiveCountAsOverride);
        }

        public static FoodTipsData Build(
            DishInstance dish,
            DiningTable table,
            GameplayDatabase db,
            DishScore score,
            int? effectiveCountAsOverride = null)
        {
            if (dish == null)
            {
                return new FoodTipsData(null, null, null, null, null, null);
            }

            int effectiveCountAs = Math.Max(
                1,
                effectiveCountAsOverride
                    ?? score?.EffectiveCountAs
                    ?? ResolveIntrinsicCountAs(dish.Def, dish.SkillIds, dish.FlavorIds, db));
            var summary = new FoodSummaryTipsData(
                dish.Def != null ? dish.Def.Name : string.Empty,
                BuildSkills(dish, db, -1),
                BuildFlavorNames(dish, db),
                dish.SkillsDisabled,
                dish.IsTemporary,
                effectiveCountAs);

            BigDouble scoreValue;
            BigDouble multiplier;
            if (score != null)
            {
                scoreValue = score.BaseValue + score.FlatBonus;
                multiplier = score.Multiplier;
            }
            else
            {
                scoreValue = dish.BaseScoreBeforeSettlement;
                multiplier = dish.BaseMultiplierBeforeSettlement;
            }

            return new FoodTipsData(
                summary,
                new FoodScoreTipsData(scoreValue, multiplier),
                BuildMaterials(dish, table, db),
                BuildFlavorDetails(dish, db),
                BuildExternalSkills(dish, db, -1, -1, -1),
                BuildSpecialTags(dish, db, -1, -1, -1));
        }

        /// <summary>
        /// 结算演出期间的渐进揭示构建：分数/倍率与技能条目数量都受 <paramref name="reveal"/> 约束，
        /// 让 tips 与演出 cue 同步逐步显示（而非一开始就展示全部结算信息）。
        /// </summary>
        public static FoodTipsData BuildRevealed(
            DishInstance dish,
            DiningTable table,
            GameplayDatabase db,
            FoodTipsReveal reveal,
            ScoreResult scoreResult = null,
            int? effectiveCountAsOverride = null)
        {
            if (dish == null)
            {
                return new FoodTipsData(null, null, null, null, null, null);
            }

            reveal ??= new FoodTipsReveal(
                dish.BaseScoreBeforeSettlement,
                dish.BaseMultiplierBeforeSettlement,
                -1,
                -1,
                -1,
                dish.EffectiveCountAs,
                -1);

            DishScore score = FindScore(scoreResult, dish.Id);
            int effectiveCountAs = Math.Max(1, reveal.CountAs);
            var summary = new FoodSummaryTipsData(
                dish.Def != null ? dish.Def.Name : string.Empty,
                BuildSkills(dish, db, reveal.MaxSkills),
                BuildFlavorNames(dish, db),
                dish.SkillsDisabled,
                dish.IsTemporary,
                effectiveCountAs);

            return new FoodTipsData(
                summary,
                new FoodScoreTipsData(reveal.Score, reveal.Multiplier),
                BuildMaterials(dish, table, db),
                BuildFlavorDetails(dish, db),
                BuildExternalSkills(
                    dish,
                    db,
                    reveal.MaxCopied,
                    reveal.MaxTransferred,
                    reveal.MaxTemporaryEffects),
                BuildSpecialTags(dish, db, reveal.MaxSkills, reveal.MaxCopied, reveal.MaxTransferred));
        }

        /// <summary>
        /// 无餐桌预览只展示食物的固有份数。AddCountAs 是主动结算技能，
        /// 必须等点击“吃”并播放对应 cue 后才揭示，不能在摆放/商店/食谱预览中当成被动属性。
        /// </summary>
        public static int ResolveIntrinsicCountAs(
            DishDef definition,
            IReadOnlyList<string> skillIds,
            IReadOnlyList<string> flavorIds,
            GameplayDatabase db)
        {
            if (definition == null)
            {
                return 1;
            }

            return Math.Max(1, definition.CountAs);
        }

        public static int ResolveIntrinsicCountAs(DishDef definition, GameplayDatabase db)
        {
            return ResolveIntrinsicCountAs(
                definition,
                definition?.SkillIds,
                Array.Empty<string>(),
                db);
        }

        private static DishScore FindScore(ScoreResult result, int dishId)
        {
            if (result?.DishScores == null)
            {
                return null;
            }

            for (int i = 0; i < result.DishScores.Count; i++)
            {
                DishScore score = result.DishScores[i];
                if (score != null && score.DishInstanceId == dishId)
                {
                    return score;
                }
            }

            return null;
        }

        private static IReadOnlyList<FoodInfoEntry> BuildSkills(DishInstance dish, GameplayDatabase db, int maxEntries)
        {
            if (dish.SkillIds == null || db == null)
            {
                return Array.Empty<FoodInfoEntry>();
            }

            var entries = new List<FoodInfoEntry>();
            int added = 0;
            for (int index = 0; index < dish.SkillIds.Count; index++)
            {
                string skillId = dish.SkillIds[index];
                if (HasSkillSource(dish, skillId))
                {
                    continue;
                }

                if (maxEntries >= 0 && added >= maxEntries)
                {
                    break;
                }

                added++;
                SkillDef skill = db.GetSkill(skillId);
                if (skill == null)
                {
                    continue;
                }

                AppendSkillEntries(entries, skill, skill.Name);
            }

            return entries;
        }

        /// <summary>
        /// 将技能按子技能描述拆成独立展示项；一个子技能对应一张 FoodTipCardView。
        /// 旧数据没有子技能描述时，仍回退到技能聚合描述。
        /// </summary>
        internal static void AppendSkillEntries(
            List<FoodInfoEntry> entries,
            SkillDef skill,
            string title = null)
        {
            if (entries == null || skill == null)
            {
                return;
            }

            string resolvedTitle = title ?? skill.Name;
            if (skill.RuleDescs == null || skill.RuleDescs.Count == 0)
            {
                entries.Add(new FoodInfoEntry(resolvedTitle, skill.Desc));
                return;
            }

            for (int i = 0; i < skill.RuleDescs.Count; i++)
            {
                entries.Add(new FoodInfoEntry(resolvedTitle, skill.RuleDescs[i]));
            }
        }

        private static IReadOnlyList<string> BuildFlavorNames(DishInstance dish, GameplayDatabase db)
        {
            if (dish.FlavorIds == null || db == null)
            {
                return Array.Empty<string>();
            }

            var names = new List<string>();
            foreach (string flavorId in dish.FlavorIds)
            {
                FlavorDef flavor = db.GetFlavor(flavorId);
                if (flavor != null && !string.IsNullOrEmpty(flavor.Name))
                {
                    names.Add(flavor.Name);
                }
            }

            return names;
        }

        private static IReadOnlyList<FoodInfoEntry> BuildFlavorDetails(DishInstance dish, GameplayDatabase db)
        {
            if (dish.FlavorIds == null || db == null)
            {
                return Array.Empty<FoodInfoEntry>();
            }

            var entries = new List<FoodInfoEntry>();
            foreach (string flavorId in dish.FlavorIds)
            {
                FlavorDef flavor = db.GetFlavor(flavorId);
                if (flavor != null)
                {
                    entries.Add(new FoodInfoEntry(flavor.Name, flavor.Desc));
                }
            }

            return entries;
        }

        private sealed class MaterialAggregate
        {
            public int Count;
            public int BoardOrder;
        }

        public static IReadOnlyList<FoodMaterialTipsEntry> BuildMaterialsForCells(
            IReadOnlyList<GridPos> cells,
            DiningTable table,
            GameplayDatabase db)
        {
            if (table == null || db == null || cells == null)
            {
                return Array.Empty<FoodMaterialTipsEntry>();
            }

            var byMaterial = new Dictionary<string, MaterialAggregate>();
            foreach (GridPos cell in cells)
            {
                int boardOrder = cell.Y * table.Width + cell.X;
                foreach (string materialId in table.MaterialsAt(cell))
                {
                    if (string.IsNullOrEmpty(materialId))
                    {
                        continue;
                    }

                    if (!byMaterial.TryGetValue(materialId, out MaterialAggregate aggregate))
                    {
                        byMaterial[materialId] = new MaterialAggregate
                        {
                            Count = 1,
                            BoardOrder = boardOrder
                        };
                    }
                    else
                    {
                        aggregate.Count++;
                        if (boardOrder < aggregate.BoardOrder)
                        {
                            aggregate.BoardOrder = boardOrder;
                        }
                    }
                }
            }

            return BuildMaterialEntries(byMaterial, db);
        }

        public static IReadOnlyList<FoodMaterialTipsEntry> BuildMaterialsForFragment(
            TableFragmentDef fragment,
            GameplayDatabase db)
        {
            if (fragment == null || db == null || fragment.CellMaterials == null)
            {
                return Array.Empty<FoodMaterialTipsEntry>();
            }

            int width = 1;
            if (fragment.ShapeRows != null)
            {
                foreach (string row in fragment.ShapeRows)
                {
                    width = Math.Max(width, row?.Length ?? 0);
                }
            }

            var byMaterial = new Dictionary<string, MaterialAggregate>();
            foreach (CellMaterial cellMaterial in fragment.CellMaterials)
            {
                string materialId = cellMaterial.MaterialId;
                if (string.IsNullOrEmpty(materialId))
                {
                    continue;
                }

                int boardOrder = cellMaterial.Pos.Y * width + cellMaterial.Pos.X;
                if (!byMaterial.TryGetValue(materialId, out MaterialAggregate aggregate))
                {
                    byMaterial[materialId] = new MaterialAggregate
                    {
                        Count = 1,
                        BoardOrder = boardOrder,
                    };
                }
                else
                {
                    aggregate.Count++;
                    aggregate.BoardOrder = Math.Min(aggregate.BoardOrder, boardOrder);
                }
            }

            return BuildMaterialEntries(byMaterial, db);
        }

        private static IReadOnlyList<FoodMaterialTipsEntry> BuildMaterialEntries(
            IReadOnlyDictionary<string, MaterialAggregate> byMaterial,
            GameplayDatabase db)
        {
            return byMaterial
                .OrderBy(e => e.Value.BoardOrder)
                .Select(e =>
                {
                    MaterialDef material = db.GetMaterial(e.Key);
                    return material != null
                        ? new FoodMaterialTipsEntry(material.Id, material.Name, material.Desc, e.Value.Count)
                        : null;
                })
                .Where(e => e != null)
                .ToArray();
        }

        private static IReadOnlyList<FoodMaterialTipsEntry> BuildMaterials(
            DishInstance dish,
            DiningTable table,
            GameplayDatabase db)
        {
            return BuildMaterialsForCells(dish.OccupiedCells, table, db);
        }

        private static IReadOnlyList<FoodInfoEntry> BuildExternalSkills(
            DishInstance dish,
            GameplayDatabase db,
            int maxCopied,
            int maxTransferred,
            int maxTemporaryEffects)
        {
            var entries = new List<FoodInfoEntry>();
            AppendCopiedSkillEntries(entries, dish, db, maxCopied);

            int transferredCount = dish.TransferredSkills?.Count ?? 0;
            int transferredLimit = maxTransferred < 0
                ? transferredCount
                : Math.Min(maxTransferred, transferredCount);
            for (int index = 0; index < transferredLimit; index++)
            {
                TransferredSkill transferred = dish.TransferredSkills[index];
                if (transferred == null)
                {
                    continue;
                }

                entries.Add(new FoodInfoEntry(transferred.SourceLabel, transferred.Desc));
            }

            int temporaryCount = dish.TemporaryCategoryEffects?.Count ?? 0;
            int temporaryLimit = maxTemporaryEffects < 0
                ? temporaryCount
                : Math.Min(maxTemporaryEffects, temporaryCount);
            for (int index = 0; index < temporaryLimit; index++)
            {
                TemporaryCategoryEffect effect = dish.TemporaryCategoryEffects[index];
                if (effect == null)
                {
                    continue;
                }

                string title = string.IsNullOrEmpty(effect.SourceName)
                    ? "临时效果"
                    : $"{effect.SourceName}<分类赋予>";
                string desc = string.IsNullOrEmpty(effect.EffectDescription)
                    ? $"视为{effect.Category}"
                    : effect.EffectDescription;
                entries.Add(new FoodInfoEntry(title, desc));
            }

            return entries;
        }

        private static void AppendCopiedSkillEntries(
            List<FoodInfoEntry> entries,
            DishInstance dish,
            GameplayDatabase db,
            int maxCopied)
        {
            if (entries == null || dish.SkillIds == null || db == null)
            {
                return;
            }

            int added = 0;
            for (int index = 0; index < dish.SkillIds.Count; index++)
            {
                string skillId = dish.SkillIds[index];
                string sourceLabel = dish.GetSkillSource(skillId);
                if (string.IsNullOrEmpty(sourceLabel))
                {
                    continue;
                }

                if (maxCopied >= 0 && added >= maxCopied)
                {
                    break;
                }

                added++;
                SkillDef skill = db.GetSkill(skillId);
                if (skill == null)
                {
                    continue;
                }

                AppendSkillEntries(entries, skill, sourceLabel);
            }
        }

        private static bool HasSkillSource(DishInstance dish, string skillId)
        {
            return !string.IsNullOrEmpty(dish.GetSkillSource(skillId));
        }

        private static IReadOnlyList<FoodInfoEntry> BuildSpecialTags(
            DishInstance dish,
            GameplayDatabase db,
            int maxSkills,
            int maxCopied,
            int maxTransferred)
        {
            var tags = new List<FoodInfoEntry>();
            if (dish.SkillsDisabled)
            {
                tags.Add(new FoodInfoEntry("技能失效", "该食物的技能不会生效。"));
            }

            if (dish.ExcludedFromScore)
            {
                tags.Add(new FoodInfoEntry("不计分", "该食物不会参与结算得分。"));
            }

            // 收集去重后的 termId（固有技能 + 复制技能 + 甜蜜传递外来子技能），再解析为术语说明卡。
            var termIds = new List<string>();
            if (db != null && dish.SkillIds != null)
            {
                int intrinsicAdded = 0;
                int copiedAdded = 0;
                for (int index = 0; index < dish.SkillIds.Count; index++)
                {
                    string skillId = dish.SkillIds[index];
                    bool copied = HasSkillSource(dish, skillId);
                    if (copied)
                    {
                        if (maxCopied >= 0 && copiedAdded >= maxCopied)
                        {
                            continue;
                        }

                        copiedAdded++;
                    }
                    else
                    {
                        if (maxSkills >= 0 && intrinsicAdded >= maxSkills)
                        {
                            continue;
                        }

                        intrinsicAdded++;
                    }

                    SkillDef skill = db.GetSkill(skillId);
                    if (skill != null)
                    {
                        AddUniqueRange(termIds, skill.TermIds);
                    }
                }
            }

            if (dish.TransferredSkills != null)
            {
                int transferredLimit = maxTransferred < 0 ? dish.TransferredSkills.Count : Math.Min(maxTransferred, dish.TransferredSkills.Count);
                for (int index = 0; index < transferredLimit; index++)
                {
                    AddUniqueRange(termIds, dish.TransferredSkills[index]?.Effect?.TermIds);
                }
            }

            foreach (string termId in termIds)
            {
                cfg.Term term = GourmetProject.Runtime.GameApp.Config?.Tables?.TbTerm?.GetOrDefault(termId);
                tags.Add(term != null
                    ? new FoodInfoEntry(term.Name, term.Desc)
                    : new FoodInfoEntry(termId, string.Empty));
            }

            return tags;
        }

        private static void AddUniqueRange(List<string> list, IReadOnlyList<string> values)
        {
            if (values == null)
            {
                return;
            }

            foreach (string value in values)
            {
                AddUnique(list, value);
            }
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (!string.IsNullOrEmpty(value) && !list.Contains(value))
            {
                list.Add(value);
            }
        }
    }
}
