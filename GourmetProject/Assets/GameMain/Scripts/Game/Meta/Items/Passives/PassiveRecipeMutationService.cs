using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.Meta.Passives
{
    public static class PassiveRecipeMutationService
    {
        public static RecipeMutationResult CopyRandomFood(
            GameRun run,
            string title,
            int count,
            IRandomStream rng)
        {
            var result = new RecipeMutationResult { Title = title };
            int sourceCount = run?.RecipeEntries.Count ?? 0;
            if (run == null || rng == null || sourceCount == 0 || count <= 0)
            {
                return result;
            }

            // 候选固定为获得装饰品和消耗品当刻已有的格子，避免一次复制多个时让刚生成的副本
            // 反过来扩大自身的后续命中权重。
            for (int i = 0; i < count; i++)
            {
                int sourceIndex = rng.Range(0, sourceCount);
                int clonedIndex = run.CloneRecipeEntry(sourceIndex);
                if (clonedIndex < 0)
                {
                    continue;
                }

                result.Entries.Add(new RecipeMutationEntry
                {
                    BookIndex = 0,
                    DishIndex = clonedIndex,
                    Before = new RecipeDishSnapshot(),
                    After = Snapshot(run, new RecipeTarget(clonedIndex)),
                });
            }

            return result;
        }

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
            List<RecipeTarget> sources = FlavorSourceTargets(run);
            List<RecipeTarget> targets = RecipeTargetsWithFreeFlavorSlot(run);
            if (run == null || rng == null || sources.Count == 0 || targets.Count == 0)
            {
                return result;
            }

            RecipeTarget source = sources[rng.Range(0, sources.Count)];
            RecipeBookSlot sourceSlot = Slot(run, source);
            DishDef sourceDef = Dish(run, source);
            if (sourceSlot == null || sourceDef == null)
            {
                return result;
            }

            var sourceFlavors = new List<string>();
            if (!string.IsNullOrEmpty(sourceDef.FlavorId))
            {
                sourceFlavors.Add(sourceDef.FlavorId);
            }

            sourceFlavors.AddRange(sourceSlot.ExtraFlavorIds);
            if (sourceFlavors.Count == 0)
            {
                return result;
            }

            string flavorId = sourceFlavors[rng.Range(0, sourceFlavors.Count)];
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

        private static List<RecipeTarget> FlavorSourceTargets(GameRun run)
        {
            var targets = new List<RecipeTarget>();
            if (run == null)
            {
                return targets;
            }

            IReadOnlyList<RecipeBookSlot> entries = run.RecipeEntries;
            for (int dish = 0; dish < entries.Count; dish++)
            {
                if (run.GetRecipeFlavorIds(dish).Count > 0)
                {
                    targets.Add(new RecipeTarget(dish));
                }
            }

            return targets;
        }

        private static List<RecipeTarget> RecipeTargetsWithFreeFlavorSlot(GameRun run)
        {
            var targets = new List<RecipeTarget>();
            if (run == null)
            {
                return targets;
            }

            IReadOnlyList<RecipeBookSlot> entries = run.RecipeEntries;
            for (int dish = 0; dish < entries.Count; dish++)
            {
                if (run.GetRecipeFlavorIds(dish).Count < run.FoodFlavorLimit)
                {
                    targets.Add(new RecipeTarget(dish));
                }
            }

            return targets;
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
                IReadOnlyList<string> before = MaterialSnapshot(run, pos);
                if (run.SetCellMaterial(pos, materialId))
                {
                    result.Entries.Add(new CellMutationEntry
                    {
                        Pos = pos,
                        MaterialId = materialId,
                        BeforeMaterialIds = before,
                        AfterMaterialIds = MaterialAfter(materialId),
                    });
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

                IReadOnlyList<string> before = MaterialSnapshot(run, target);
                if (run.SetCellMaterial(target, source.MaterialId))
                {
                    result.Entries.Add(new CellMutationEntry
                    {
                        Pos = target,
                        MaterialId = source.MaterialId,
                        BeforeMaterialIds = before,
                        AfterMaterialIds = MaterialAfter(source.MaterialId),
                    });
                    break;
                }
            }

            return result;
        }

        public static CellMutationResult SpreadMaterialToAdjacentCell(
            GameRun run,
            string title,
            IRandomStream rng)
        {
            var result = new CellMutationResult { Title = title };
            if (run == null || rng == null)
            {
                return result;
            }

            DiningTable preview = run.BuildTablePreviewFromFragments();
            var sources = new List<GridPos>();
            foreach (GridPos cell in preview.ExistingCells())
            {
                if (preview.MaterialsAt(cell).Count > 0)
                {
                    sources.Add(cell);
                }
            }

            if (sources.Count == 0)
            {
                return result;
            }

            rng.Shuffle(sources);
            var directions = new[]
            {
                new GridPos(1, 0),
                new GridPos(-1, 0),
                new GridPos(0, 1),
                new GridPos(0, -1),
            };

            foreach (GridPos source in sources)
            {
                IReadOnlyList<string> sourceMaterials = preview.MaterialsAt(source);
                var candidates = new List<CellMutationEntry>();
                foreach (GridPos direction in directions)
                {
                    var target = new GridPos(source.X + direction.X, source.Y + direction.Y);
                    if (!preview.Exists(target))
                    {
                        continue;
                    }

                    IReadOnlyList<string> before = preview.MaterialsAt(target);
                    foreach (string materialId in sourceMaterials)
                    {
                        if (!ContainsIgnoreCase(before, materialId))
                        {
                            candidates.Add(new CellMutationEntry
                            {
                                Pos = target,
                                MaterialId = materialId,
                                BeforeMaterialIds = new List<string>(before),
                            });
                        }
                    }
                }

                if (candidates.Count == 0)
                {
                    continue;
                }

                CellMutationEntry selected = candidates[rng.Range(0, candidates.Count)];
                if (!run.SetCellMaterial(selected.Pos, selected.MaterialId))
                {
                    continue;
                }

                selected.AfterMaterialIds = MaterialAfter(selected.MaterialId);
                result.Entries.Add(selected);
                break;
            }

            return result;
        }

        public static RecipeMutationResult RemoveArrowCookies(GameRun run, string title, int maxCount)
        {
            var result = new RecipeMutationResult { Title = title };
            if (run == null || maxCount <= 0)
            {
                return result;
            }

            result.BeforeRecipe.AddRange(SnapshotRecipe(run));

            IEnumerable<string> cookieIds = run.Tables?.TbGameBase?.ServeCookieDishIds;
            var configuredIds = new HashSet<string>(
                cookieIds ?? System.Array.Empty<string>(),
                System.StringComparer.OrdinalIgnoreCase);
            var indices = new List<int>();
            for (int i = 0; i < run.RecipeEntries.Count && indices.Count < maxCount; i++)
            {
                string dishId = run.RecipeEntries[i].DishId;
                bool isArrowCookie = configuredIds.Count > 0
                    ? configuredIds.Contains(dishId)
                    : dishId.StartsWith("arrow_cookie_", System.StringComparison.OrdinalIgnoreCase);
                if (isArrowCookie)
                {
                    indices.Add(i);
                }
            }

            for (int i = indices.Count - 1; i >= 0; i--)
            {
                int dishIndex = indices[i];
                RecipeDishSnapshot before = Snapshot(run, new RecipeTarget(dishIndex));
                if (!run.RemoveBonusDishAt(dishIndex))
                {
                    continue;
                }

                result.Entries.Insert(0, new RecipeMutationEntry
                {
                    BookIndex = 0,
                    DishIndex = dishIndex,
                    Before = before,
                    After = new RecipeDishSnapshot(),
                });
            }

            if (result.HasChanges)
            {
                result.AfterRecipe.AddRange(SnapshotRecipe(run));
            }
            else
            {
                result.BeforeRecipe.Clear();
            }

            return result;
        }

        public static RecipeMutationResult RandomizeAllRecipeDishes(
            GameRun run,
            string title,
            IRandomStream rng)
        {
            var result = new RecipeMutationResult { Title = title };
            if (run == null || rng == null || run.Database?.AllDishes == null)
            {
                return result;
            }

            var pool = new List<DishDef>();
            foreach (DishDef dish in run.Database.AllDishes)
            {
                if (dish != null && !string.IsNullOrEmpty(dish.Id) && dish.BaseWeight > 0f)
                {
                    pool.Add(dish);
                }
            }

            for (int dishIndex = 0; dishIndex < run.RecipeEntries.Count; dishIndex++)
            {
                string currentId = run.RecipeEntries[dishIndex].DishId;
                var candidates = new List<DishDef>();
                var weights = new List<float>();
                foreach (DishDef dish in pool)
                {
                    if (string.Equals(dish.Id, currentId, System.StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    candidates.Add(dish);
                    weights.Add(dish.BaseWeight);
                }

                if (candidates.Count == 0)
                {
                    continue;
                }

                RecipeDishSnapshot before = Snapshot(run, new RecipeTarget(dishIndex));
                DishDef selected = candidates[rng.WeightedPickIndex(weights)];
                if (!run.ReplaceRecipeDishAt(dishIndex, selected.Id))
                {
                    continue;
                }

                result.Entries.Add(new RecipeMutationEntry
                {
                    BookIndex = 0,
                    DishIndex = dishIndex,
                    Before = before,
                    After = Snapshot(run, new RecipeTarget(dishIndex)),
                });
            }

            return result;
        }

        private static bool ContainsIgnoreCase(IReadOnlyList<string> values, string value)
        {
            if (values == null)
            {
                return false;
            }

            foreach (string candidate in values)
            {
                if (string.Equals(candidate, value, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<RecipeDishSnapshot> SnapshotRecipe(GameRun run)
        {
            var snapshots = new List<RecipeDishSnapshot>();
            if (run == null)
            {
                return snapshots;
            }

            for (int i = 0; i < run.RecipeEntries.Count; i++)
            {
                snapshots.Add(Snapshot(run, new RecipeTarget(i)));
            }

            return snapshots;
        }

        private static IReadOnlyList<string> MaterialSnapshot(GameRun run, GridPos pos)
        {
            DiningTable preview = run?.BuildTablePreviewFromFragments();
            return preview == null
                ? System.Array.Empty<string>()
                : new List<string>(preview.MaterialsAt(pos));
        }

        private static IReadOnlyList<string> MaterialAfter(string materialId)
        {
            return string.IsNullOrEmpty(materialId)
                ? System.Array.Empty<string>()
                : new[] { materialId };
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
                if (run.GetRecipeFlavorIds(dish).Count > 0)
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

        internal static RecipeDishSnapshot Snapshot(GameRun run, int dishIndex)
        {
            return Snapshot(run, new RecipeTarget(dishIndex));
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
