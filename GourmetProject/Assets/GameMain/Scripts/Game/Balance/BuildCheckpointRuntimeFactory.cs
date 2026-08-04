using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.Balance
{
    public sealed class BalanceRuntime
    {
        public GameRun Run;
        public DiningTable Board;
        public List<string> ComponentIds = new List<string>();
    }

    public static class BuildCheckpointRuntimeFactory
    {
        public static BalanceRuntime Create(
            cfg.Tables tables,
            GameplayDatabase database,
            BuildCheckpoint checkpoint,
            int seed,
            string excludedComponentId = null,
            bool applyPerturbation = true)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));

            var random = new Xoshiro256SS(unchecked((ulong)(uint)seed));
            List<ResolvedDish> resolved = ResolveDishes(checkpoint, database, random, applyPerturbation);
            var save = BuildSave(checkpoint, resolved, excludedComponentId);
            GameRun run = GameRun.FromSaveData(tables, database, save);
            DiningTable board = BattleSessionFactory.BuildTablePreviewForBalance(run, checkpoint.BossDebuffId, random);
            var componentIds = new List<string>();
            int instanceId = 1;

            foreach (ResolvedDish entry in resolved)
            {
                string componentId = $"dish:{entry.Index}:{entry.Step.DishId}";
                if (!entry.Retained || componentId == excludedComponentId) continue;
                DishShape orientation = entry.Def.Shape.RotatedBy(entry.Step.Rotation);
                var placement = new Placement(orientation, entry.Step.Rotation, new GridPos(entry.Step.X, entry.Step.Y));
                if (!board.CanPlace(orientation, placement.Origin))
                {
                    throw new InvalidOperationException($"{componentId} 在 ({entry.Step.X},{entry.Step.Y}) 的摆放越界、重叠或位于胃外。");
                }

                var skills = entry.Def.SkillIds.Concat(entry.Step.ExtraSkillIds ?? new List<string>()).Where(v => !string.IsNullOrEmpty(v)).Distinct().ToList();
                var flavors = new List<string>();
                if (!string.IsNullOrEmpty(entry.Def.FlavorId)) flavors.Add(entry.Def.FlavorId);
                if (entry.Step.ExtraFlavorIds != null) flavors.AddRange(entry.Step.ExtraFlavorIds.Where(v => !string.IsNullOrEmpty(v)));
                var dish = new DishInstance(instanceId++, entry.Def, placement, skills, flavors);
                dish.SetSourceRecipeIndex(0, entry.Index);
                dish.AddPermanentFlat(entry.PermanentFlat);
                dish.MultiplyPermanentMult(entry.PermanentMultiplier);
                board.Place(dish);
                if (entry.Step.AnalyzeContribution) componentIds.Add(componentId);
            }

            for (int i = 0; i < checkpoint.Items.Count; i++)
            {
                BalanceItemEntry item = checkpoint.Items[i];
                string componentId = $"item:{i}:{item.ItemId}";
                if (item.AnalyzeContribution && componentId != excludedComponentId) componentIds.Add(componentId);
            }

            return new BalanceRuntime { Run = run, Board = board, ComponentIds = componentIds };
        }

        public static List<string> Validate(cfg.Tables tables, GameplayDatabase database, BuildCheckpoint checkpoint)
        {
            var errors = new List<string>();
            if (checkpoint == null) return new List<string> { "Checkpoint 为空。" };
            if (tables.TbCharacter.GetOrDefault(checkpoint.CharacterId) == null) errors.Add($"经营方向不存在：{checkpoint.CharacterId}");
            if (!string.IsNullOrEmpty(checkpoint.BossDebuffId) && tables.TbBossDebuff.GetOrDefault(checkpoint.BossDebuffId) == null) errors.Add($"星级评鉴 Debuff 不存在：{checkpoint.BossDebuffId}");
            var passives = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < checkpoint.Items.Count; i++)
            {
                BalanceItemEntry item = checkpoint.Items[i];
                ItemDefinition def = ItemDefinition.Get(tables, item.ItemId);
                if (def == null) errors.Add($"装饰品和消耗品不存在：{item.ItemId}");
                else if (def.IsPassive && !passives.Add(item.ItemId)) errors.Add($"装饰品不可重复：{item.ItemId}");
            }
            foreach (string id in checkpoint.TableFragmentIds)
                if (database.GetFragment(id) == null) errors.Add($"餐桌格不存在：{id}");
            foreach (BalanceFragmentPlacement p in checkpoint.FragmentPlacements)
                if (database.GetFragment(p.FragmentId) == null) errors.Add($"餐桌格不存在：{p.FragmentId}");
            foreach (BalanceCellMaterial m in checkpoint.CellMaterials)
                if (database.GetMaterial(m.MaterialId) == null) errors.Add($"材质不存在：{m.MaterialId}");
            for (int i = 0; i < checkpoint.Dishes.Count; i++)
            {
                BuildReplayStep step = checkpoint.Dishes[i];
                if (database.GetDish(step.DishId) == null) errors.Add($"食物不存在：{step.DishId}");
                foreach (string id in step.ExtraSkillIds)
                    if (database.GetSkill(id) == null) errors.Add($"技能不存在：{id}");
                foreach (string id in step.ExtraFlavorIds)
                    if (database.GetFlavor(id) == null) errors.Add($"风味不存在：{id}");
                foreach (WeightedDishReplacement replacement in step.Replacements)
                    if (database.GetDish(replacement.DishId) == null) errors.Add($"替代食物不存在：{replacement.DishId}");
            }
            if (errors.Count == 0)
            {
                try { Create(tables, database, checkpoint, 1, applyPerturbation: false); }
                catch (Exception e) { errors.Add(e.Message); }
            }
            return errors.Distinct().ToList();
        }

        private static RunSaveData BuildSave(BuildCheckpoint checkpoint, List<ResolvedDish> dishes, string excludedComponentId)
        {
            var save = new RunSaveData
            {
                CharacterId = checkpoint.CharacterId,
                SeedText = "balance-lab",
                WeekIndex = Math.Max(1, checkpoint.Week),
                CurrentDay = Math.Max(0f, checkpoint.Day),
                Gold = Math.Max(0, checkpoint.Gold),
                HeartCapacity = 3,
                HeartsRemaining = 3,
                Recipe = new RunRecipeBookSaveData(),
                TableFragmentIds = new List<string>(checkpoint.TableFragmentIds ?? new List<string>()),
            };
            foreach (ResolvedDish entry in dishes.Where(d => d.Retained))
            {
                save.Recipe.DishIds.Add(entry.Def.Id);
                save.Recipe.DishExtraFlavors.Add(new RunRecipeDishFlavorSaveData());
            }
            for (int i = 0; i < checkpoint.Items.Count; i++)
            {
                BalanceItemEntry item = checkpoint.Items[i];
                if ($"item:{i}:{item.ItemId}" == excludedComponentId) continue;
                save.Items.Add(new RunItemSaveData { ItemId = item.ItemId, Level = Math.Max(1, item.Level), Count = 1 });
            }
            foreach (BalanceFragmentPlacement p in checkpoint.FragmentPlacements)
                save.FragmentPlacements.Add(new TableFragmentPlacementSaveData { FragmentId = p.FragmentId, Rotation = p.Rotation, OriginX = p.X, OriginY = p.Y });
            foreach (BalanceCellMaterial m in checkpoint.CellMaterials)
                save.CellMaterialOverrides.Add(new CellMaterialSaveData { X = m.X, Y = m.Y, MaterialId = m.MaterialId });
            return save;
        }

        private static List<ResolvedDish> ResolveDishes(BuildCheckpoint checkpoint, GameplayDatabase database, IRandomStream random, bool applyPerturbation)
        {
            var result = new List<ResolvedDish>();
            BalancePerturbation p = checkpoint.Perturbation ?? new BalancePerturbation();
            for (int i = 0; i < checkpoint.Dishes.Count; i++)
            {
                BuildReplayStep step = checkpoint.Dishes[i];
                bool perturb = applyPerturbation && p.Enabled;
                bool retained = !perturb || random.NextBool(step.RetainProbability);
                string dishId = step.DishId;
                if (perturb && retained && step.Replacements != null && step.Replacements.Count > 0)
                {
                    var weights = step.Replacements.Select(v => Math.Max(0f, v.Weight)).ToList();
                    if (weights.Sum() > 0f) dishId = step.Replacements[random.WeightedPickIndex(weights)].DishId;
                }
                DishDef def = database.GetDish(dishId) ?? throw new InvalidOperationException($"食物不存在：{dishId}");
                float flatFactor = perturb ? random.Range(Math.Min(p.PermanentFlatMinFactor, p.PermanentFlatMaxFactor), Math.Max(p.PermanentFlatMinFactor, p.PermanentFlatMaxFactor) + float.Epsilon) : 1f;
                float multFactor = perturb ? random.Range(Math.Min(p.PermanentMultiplierMinFactor, p.PermanentMultiplierMaxFactor), Math.Max(p.PermanentMultiplierMinFactor, p.PermanentMultiplierMaxFactor) + float.Epsilon) : 1f;
                result.Add(new ResolvedDish { Index = i, Step = step, Def = def, Retained = retained, PermanentFlat = step.PermanentFlat * flatFactor, PermanentMultiplier = step.PermanentMultiplier * multFactor });
            }
            return result;
        }

        private sealed class ResolvedDish
        {
            public int Index;
            public BuildReplayStep Step;
            public DishDef Def;
            public bool Retained;
            public float PermanentFlat;
            public float PermanentMultiplier;
        }
    }
}
