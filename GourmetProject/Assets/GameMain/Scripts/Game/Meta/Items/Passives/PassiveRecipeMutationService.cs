using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.Meta.Passives
{
    public static class PassiveRecipeMutationService
    {
        public static RecipeMutationResult AddRandomFlavors(GameRun run, string title, int count, IRandomStream rng)
        {
            var result = new RecipeMutationResult { Title = title };
            List<RecipeTarget> targets = NoFlavorRecipeTargets(run);
            List<string> flavors = FlavorIds(run);
            if (run == null || rng == null || targets.Count == 0 || flavors.Count == 0 || count <= 0)
            {
                return result;
            }

            rng.Shuffle(targets);
            for (int i = 0; i < count && i < targets.Count; i++)
            {
                RecipeTarget target = targets[i];
                RecipeDishSnapshot before = Snapshot(run, target);
                string flavorId = flavors[rng.Range(0, flavors.Count)];
                if (!run.AddRecipeFlavor(target.DishIndex, flavorId))
                {
                    continue;
                }

                result.Entries.Add(new RecipeMutationEntry
                {
                    BookIndex = 0,
                    DishIndex = target.DishIndex,
                    Before = before,
                    After = Snapshot(run, target),
                });
            }

            return result;
        }

        public static RecipeMutationResult RemoveFlavorForGold(GameRun run, string title, int gold, IRandomStream rng)
        {
            RecipeMutationResult result = RemoveFlavor(run, title, rng, target =>
            {
                run.Gold += System.Math.Max(0, gold);
                return true;
            });
            return result;
        }

        public static RecipeMutationResult RemoveFlavorCopySkill(GameRun run, string title, IRandomStream rng)
        {
            return RemoveFlavor(run, title, rng, target =>
            {
                DishDef dish = Dish(run, target);
                if (dish == null || dish.SkillIds.Count == 0)
                {
                    return false;
                }

                string skillId = dish.SkillIds[rng.Range(0, dish.SkillIds.Count)];
                return run.AddRecipeExtraSkill(target.DishIndex, skillId);
            });
        }

        public static RecipeMutationResult RemoveFlavorDoubleScore(GameRun run, string title, float multiplier, IRandomStream rng)
        {
            return RemoveFlavor(run, title, rng, target =>
                run.MultiplyRecipeScore(target.DishIndex, multiplier));
        }

        public static RecipeMutationResult ContagionFlavor(GameRun run, string title, IRandomStream rng)
        {
            var result = new RecipeMutationResult { Title = title };
            List<RecipeTarget> sources = ExtraFlavorTargets(run);
            List<RecipeTarget> targets = NoFlavorRecipeTargets(run);
            if (run == null || rng == null || sources.Count == 0 || targets.Count == 0)
            {
                return result;
            }

            RecipeTarget source = sources[rng.Range(0, sources.Count)];
            RecipeBookSlot sourceSlot = Slot(run, source);
            if (sourceSlot == null || sourceSlot.ExtraFlavorIds.Count == 0)
            {
                return result;
            }

            string flavorId = sourceSlot.ExtraFlavorIds[rng.Range(0, sourceSlot.ExtraFlavorIds.Count)];
            rng.Shuffle(targets);
            foreach (RecipeTarget target in targets)
            {
                if (target.DishIndex == source.DishIndex)
                {
                    continue;
                }

                RecipeDishSnapshot before = Snapshot(run, target);
                if (!run.AddRecipeFlavor(target.DishIndex, flavorId))
                {
                    continue;
                }

                result.Entries.Add(new RecipeMutationEntry
                {
                    BookIndex = 0,
                    DishIndex = target.DishIndex,
                    Before = before,
                    After = Snapshot(run, target),
                });
                break;
            }

            return result;
        }

        public static CellMutationResult AddRandomMaterials(GameRun run, string title, int count, IRandomStream rng)
        {
            var result = new CellMutationResult { Title = title };
            List<GridPos> targets = EmptyMaterialCells(run);
            List<string> materials = MaterialIds(run);
            if (run == null || rng == null || targets.Count == 0 || materials.Count == 0 || count <= 0)
            {
                return result;
            }

            rng.Shuffle(targets);
            for (int i = 0; i < count && i < targets.Count; i++)
            {
                GridPos pos = targets[i];
                string materialId = materials[rng.Range(0, materials.Count)];
                if (run.AddCellMaterial(pos, materialId))
                {
                    result.Entries.Add(new CellMutationEntry { Pos = pos, MaterialId = materialId });
                }
            }

            return result;
        }

        public static CellMutationResult ContagionMaterial(GameRun run, string title, IRandomStream rng)
        {
            var result = new CellMutationResult { Title = title };
            if (run == null || rng == null)
            {
                return result;
            }

            DiningTable preview = run.BuildTablePreviewFromFragments();
            var sources = new List<CellMutationEntry>();
            foreach (GridPos cell in preview.ExistingCells())
            {
                IReadOnlyList<string> materials = preview.MaterialsAt(cell);
                if (materials.Count > 0)
                {
                    sources.Add(new CellMutationEntry { Pos = cell, MaterialId = materials[rng.Range(0, materials.Count)] });
                }
            }

            List<GridPos> targets = EmptyMaterialCells(run);
            if (sources.Count == 0 || targets.Count == 0)
            {
                return result;
            }

            CellMutationEntry source = sources[rng.Range(0, sources.Count)];
            rng.Shuffle(targets);
            foreach (GridPos target in targets)
            {
                if (target.Equals(source.Pos))
                {
                    continue;
                }

                if (run.AddCellMaterial(target, source.MaterialId))
                {
                    result.Entries.Add(new CellMutationEntry { Pos = target, MaterialId = source.MaterialId });
                    break;
                }
            }

            return result;
        }

        private static RecipeMutationResult RemoveFlavor(
            GameRun run,
            string title,
            IRandomStream rng,
            System.Func<RecipeTarget, bool> afterRemove)
        {
            var result = new RecipeMutationResult { Title = title };
            List<RecipeTarget> targets = ExtraFlavorTargets(run);
            if (run == null || rng == null || targets.Count == 0)
            {
                return result;
            }

            RecipeTarget target = targets[rng.Range(0, targets.Count)];
            RecipeDishSnapshot before = Snapshot(run, target);
            if (!run.RemoveRecipeFlavor(target.DishIndex, string.Empty))
            {
                return result;
            }

            afterRemove?.Invoke(target);
            result.Entries.Add(new RecipeMutationEntry
            {
                BookIndex = 0,
                DishIndex = target.DishIndex,
                Before = before,
                After = Snapshot(run, target),
            });
            return result;
        }

        private static List<RecipeTarget> NoFlavorRecipeTargets(GameRun run)
        {
            var targets = new List<RecipeTarget>();
            if (run == null)
            {
                return targets;
            }

            IReadOnlyList<RecipeBookSlot> entries = run.RecipeEntries;
            for (int dish = 0; dish < entries.Count; dish++)
            {
                RecipeBookSlot slot = entries[dish];
                DishDef def = run.Database.GetDish(slot.DishId);
                if (def != null && !def.HasFlavor && slot.ExtraFlavorIds.Count == 0)
                {
                    targets.Add(new RecipeTarget(dish));
                }
            }

            return targets;
        }

        private static List<RecipeTarget> ExtraFlavorTargets(GameRun run)
        {
            var targets = new List<RecipeTarget>();
            if (run == null)
            {
                return targets;
            }

            IReadOnlyList<RecipeBookSlot> entries = run.RecipeEntries;
            for (int dish = 0; dish < entries.Count; dish++)
            {
                if (entries[dish].ExtraFlavorIds.Count > 0)
                {
                    targets.Add(new RecipeTarget(dish));
                }
            }

            return targets;
        }

        private static List<GridPos> EmptyMaterialCells(GameRun run)
        {
            var targets = new List<GridPos>();
            if (run == null)
            {
                return targets;
            }

            DiningTable preview = run.BuildTablePreviewFromFragments();
            foreach (GridPos cell in preview.ExistingCells())
            {
                if (preview.MaterialsAt(cell).Count == 0)
                {
                    targets.Add(cell);
                }
            }

            return targets;
        }

        private static List<string> FlavorIds(GameRun run)
        {
            var ids = new List<string>();
            if (run?.Database?.AllFlavors == null)
            {
                return ids;
            }

            foreach (FlavorDef flavor in run.Database.AllFlavors)
            {
                if (flavor != null && !string.IsNullOrEmpty(flavor.Id))
                {
                    ids.Add(flavor.Id);
                }
            }

            return ids;
        }

        private static List<string> MaterialIds(GameRun run)
        {
            var ids = new List<string>();
            if (run?.Database?.AllMaterials == null)
            {
                return ids;
            }

            foreach (MaterialDef material in run.Database.AllMaterials)
            {
                if (material != null && !string.IsNullOrEmpty(material.Id))
                {
                    ids.Add(material.Id);
                }
            }

            return ids;
        }

        private static RecipeDishSnapshot Snapshot(GameRun run, RecipeTarget target)
        {
            RecipeBookSlot slot = Slot(run, target);
            DishDef dish = Dish(run, target);
            if (slot == null || dish == null)
            {
                return new RecipeDishSnapshot();
            }

            var flavors = new List<string>();
            if (!string.IsNullOrEmpty(dish.FlavorId))
            {
                flavors.Add(dish.FlavorId);
            }

            flavors.AddRange(slot.ExtraFlavorIds);

            var skills = new List<string>(dish.SkillIds);
            skills.AddRange(slot.ExtraSkillIds);

            return new RecipeDishSnapshot
            {
                DishId = slot.DishId,
                FlavorIds = flavors,
                SkillIds = skills,
                ScoreFlatBonus = slot.ScoreFlatBonus,
                ScoreMultiplier = slot.ScoreMultiplier,
            };
        }

        private static RecipeBookSlot Slot(GameRun run, RecipeTarget target)
        {
            if (run == null)
            {
                return null;
            }

            IReadOnlyList<RecipeBookSlot> entries = run.RecipeEntries;
            return target.DishIndex >= 0 && target.DishIndex < entries.Count ? entries[target.DishIndex] : null;
        }

        private static DishDef Dish(GameRun run, RecipeTarget target)
        {
            RecipeBookSlot slot = Slot(run, target);
            return slot != null ? run.Database.GetDish(slot.DishId) : null;
        }

        private readonly struct RecipeTarget
        {
            public RecipeTarget(int dishIndex)
            {
                DishIndex = dishIndex;
            }

            public int DishIndex { get; }
        }
    }
}
