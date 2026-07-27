using System;
using System.Collections.Generic;
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
            IReadOnlyList<FoodInfoEntry> transferredSubSkills,
            IReadOnlyList<FoodInfoEntry> specialTags)
        {
            Summary = summary ?? FoodSummaryTipsData.Empty;
            Score = score ?? FoodScoreTipsData.Empty;
            Materials = materials ?? Array.Empty<FoodMaterialTipsEntry>();
            FlavorDetails = flavorDetails ?? Array.Empty<FoodInfoEntry>();
            TransferredSubSkills = transferredSubSkills ?? Array.Empty<FoodInfoEntry>();
            SpecialTags = specialTags ?? Array.Empty<FoodInfoEntry>();
        }

        public FoodSummaryTipsData Summary { get; }

        public FoodScoreTipsData Score { get; }

        public IReadOnlyList<FoodMaterialTipsEntry> Materials { get; }

        public IReadOnlyList<FoodInfoEntry> FlavorDetails { get; }

        public IReadOnlyList<FoodInfoEntry> TransferredSubSkills { get; }

        public IReadOnlyList<FoodInfoEntry> SpecialTags { get; }
    }

    public sealed class FoodSummaryTipsData
    {
        public static readonly FoodSummaryTipsData Empty = new FoodSummaryTipsData(
            string.Empty,
            Array.Empty<FoodInfoEntry>(),
            Array.Empty<string>());

        public FoodSummaryTipsData(string foodName, IReadOnlyList<FoodInfoEntry> skills, IReadOnlyList<string> flavors, bool skillsDisabled = false)
        {
            FoodName = foodName ?? string.Empty;
            Skills = skills ?? Array.Empty<FoodInfoEntry>();
            Flavors = flavors ?? Array.Empty<string>();
            SkillsDisabled = skillsDisabled;
        }

        public string FoodName { get; }

        public IReadOnlyList<FoodInfoEntry> Skills { get; }

        public IReadOnlyList<string> Flavors { get; }

        public bool SkillsDisabled { get; }
    }

    public sealed class FoodScoreTipsData
    {
        public static readonly FoodScoreTipsData Empty = new FoodScoreTipsData(0f, 1f);

        public FoodScoreTipsData(float score, float multiplier)
        {
            Score = score;
            Multiplier = multiplier;
        }

        /// <summary>分数：基础美味度 + 加法分。</summary>
        public float Score { get; }

        /// <summary>倍率：本食物当前乘区。</summary>
        public float Multiplier { get; }

        /// <summary>美味度：倍率 * 分数 后向上取整。</summary>
        public float Deliciousness => DishScore.CeilContribution(Score, Multiplier);
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
        public FoodTipsReveal(float score, float multiplier, int maxSkills, int maxTransferred)
        {
            Score = score;
            Multiplier = multiplier;
            MaxSkills = maxSkills;
            MaxTransferred = maxTransferred;
        }

        /// <summary>已揭示的分数（基础美味度 + 已表演的加法分）。</summary>
        public float Score { get; }

        /// <summary>已揭示的倍率（已表演到的乘区）。</summary>
        public float Multiplier { get; }

        /// <summary>技能列表最多显示前几条（含复制技能追加项）；-1 表示全部。</summary>
        public int MaxSkills { get; }

        /// <summary>甜蜜传递子技能最多显示前几条；-1 表示全部。</summary>
        public int MaxTransferred { get; }
    }

    public static class FoodTipsDataFactory
    {
        public static FoodTipsData Build(
            DishInstance dish,
            DiningTable table,
            GameplayDatabase db,
            ScoreResult scoreResult = null)
        {
            if (dish == null)
            {
                return new FoodTipsData(null, null, null, null, null, null);
            }

            DishScore score = FindScore(scoreResult, dish.Id);
            return Build(dish, table, db, score);
        }

        public static FoodTipsData Build(
            DishInstance dish,
            DiningTable table,
            GameplayDatabase db,
            DishScore score)
        {
            if (dish == null)
            {
                return new FoodTipsData(null, null, null, null, null, null);
            }

            var summary = new FoodSummaryTipsData(
                dish.Def != null ? dish.Def.Name : string.Empty,
                BuildSkills(dish, db, -1),
                BuildFlavorNames(dish, db),
                dish.SkillsDisabled);

            float scoreValue;
            float multiplier;
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
                BuildTransferredSubSkills(dish, -1),
                BuildSpecialTags(dish, db, -1, -1));
        }

        /// <summary>
        /// 结算演出期间的渐进揭示构建：分数/倍率与技能条目数量都受 <paramref name="reveal"/> 约束，
        /// 让 tips 与演出 cue 同步逐步显示（而非一开始就展示全部结算信息）。
        /// </summary>
        public static FoodTipsData BuildRevealed(
            DishInstance dish,
            DiningTable table,
            GameplayDatabase db,
            FoodTipsReveal reveal)
        {
            if (dish == null)
            {
                return new FoodTipsData(null, null, null, null, null, null);
            }

            reveal ??= new FoodTipsReveal(dish.BaseScoreBeforeSettlement, dish.BaseMultiplierBeforeSettlement, -1, -1);

            var summary = new FoodSummaryTipsData(
                dish.Def != null ? dish.Def.Name : string.Empty,
                BuildSkills(dish, db, reveal.MaxSkills),
                BuildFlavorNames(dish, db),
                dish.SkillsDisabled);

            return new FoodTipsData(
                summary,
                new FoodScoreTipsData(reveal.Score, reveal.Multiplier),
                BuildMaterials(dish, table, db),
                BuildFlavorDetails(dish, db),
                BuildTransferredSubSkills(dish, reveal.MaxTransferred),
                BuildSpecialTags(dish, db, reveal.MaxSkills, reveal.MaxTransferred));
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

            int limit = maxEntries < 0 ? dish.SkillIds.Count : Math.Min(maxEntries, dish.SkillIds.Count);
            var entries = new List<FoodInfoEntry>();
            for (int index = 0; index < limit; index++)
            {
                string skillId = dish.SkillIds[index];
                SkillDef skill = db.GetSkill(skillId);
                if (skill == null)
                {
                    continue;
                }

                string title = skill.Name;
                string sourceLabel = dish.GetSkillSource(skillId);
                if (!string.IsNullOrEmpty(sourceLabel))
                {
                    title = sourceLabel;
                }

                entries.Add(new FoodInfoEntry(title, skill.Desc));
            }

            return entries;
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

        private static IReadOnlyList<FoodInfoEntry> BuildTransferredSubSkills(DishInstance dish, int maxEntries)
        {
            if (dish.TransferredSkills == null || dish.TransferredSkills.Count == 0)
            {
                return Array.Empty<FoodInfoEntry>();
            }

            int limit = maxEntries < 0 ? dish.TransferredSkills.Count : Math.Min(maxEntries, dish.TransferredSkills.Count);
            var entries = new List<FoodInfoEntry>();
            for (int index = 0; index < limit; index++)
            {
                TransferredSkill transferred = dish.TransferredSkills[index];
                if (transferred == null)
                {
                    continue;
                }

                entries.Add(new FoodInfoEntry(transferred.SourceLabel, transferred.Desc));
            }

            return entries;
        }

        private static IReadOnlyList<FoodInfoEntry> BuildSpecialTags(DishInstance dish, GameplayDatabase db, int maxSkills, int maxTransferred)
        {
            // 收集去重后的 termId（技能各子技能 + 甜蜜传递外来子技能），再解析为术语说明卡。
            var termIds = new List<string>();
            if (db != null && dish.SkillIds != null)
            {
                int skillLimit = maxSkills < 0 ? dish.SkillIds.Count : Math.Min(maxSkills, dish.SkillIds.Count);
                for (int index = 0; index < skillLimit; index++)
                {
                    SkillDef skill = db.GetSkill(dish.SkillIds[index]);
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

            var tags = new List<FoodInfoEntry>(termIds.Count);
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
