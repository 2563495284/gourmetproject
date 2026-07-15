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

        public FoodSummaryTipsData(string foodName, IReadOnlyList<FoodInfoEntry> skills, IReadOnlyList<string> flavors)
        {
            FoodName = foodName ?? string.Empty;
            Skills = skills ?? Array.Empty<FoodInfoEntry>();
            Flavors = flavors ?? Array.Empty<string>();
        }

        public string FoodName { get; }

        public IReadOnlyList<FoodInfoEntry> Skills { get; }

        public IReadOnlyList<string> Flavors { get; }
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

        /// <summary>美味度：倍率 * 分数。</summary>
        public float Deliciousness => Score * Multiplier;
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
                BuildSkills(dish, db),
                BuildFlavorNames(dish, db));

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
                BuildTransferredSubSkills(dish),
                BuildSpecialTags(dish, db));
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

        private static IReadOnlyList<FoodInfoEntry> BuildSkills(DishInstance dish, GameplayDatabase db)
        {
            if (dish.SkillIds == null || db == null)
            {
                return Array.Empty<FoodInfoEntry>();
            }

            var entries = new List<FoodInfoEntry>();
            foreach (string skillId in dish.SkillIds)
            {
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

        private static IReadOnlyList<FoodInfoEntry> BuildTransferredSubSkills(DishInstance dish)
        {
            if (dish.TransferredSkills == null || dish.TransferredSkills.Count == 0)
            {
                return Array.Empty<FoodInfoEntry>();
            }

            var entries = new List<FoodInfoEntry>();
            foreach (TransferredSkill transferred in dish.TransferredSkills)
            {
                if (transferred == null)
                {
                    continue;
                }

                entries.Add(new FoodInfoEntry(transferred.SourceLabel, transferred.Desc));
            }

            return entries;
        }

        private static IReadOnlyList<FoodInfoEntry> BuildSpecialTags(DishInstance dish, GameplayDatabase db)
        {
            // 收集去重后的 termId（技能各子技能 + 甜蜜传递外来子技能），再解析为术语说明卡。
            var termIds = new List<string>();
            if (db != null && dish.SkillIds != null)
            {
                foreach (string skillId in dish.SkillIds)
                {
                    SkillDef skill = db.GetSkill(skillId);
                    if (skill != null)
                    {
                        AddUniqueRange(termIds, skill.TermIds);
                    }
                }
            }

            if (dish.TransferredSkills != null)
            {
                foreach (TransferredSkill transferred in dish.TransferredSkills)
                {
                    AddUniqueRange(termIds, transferred?.Effect?.TermIds);
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
