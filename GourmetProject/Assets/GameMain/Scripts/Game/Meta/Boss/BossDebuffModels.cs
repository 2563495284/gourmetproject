using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using UnityEngine.Scripting;

namespace GourmetProject.Game.Meta.BossDebuffs
{
    [Preserve]
    [BossDebuffModel("debuff_indulgent")]
    public sealed class IndulgentBossDebuffModel : BossDebuffModel
    {
        public override void ModifyTableBounds(ref int maxWidth, ref int maxHeight) => maxHeight += 1;

        public override void ModifyBuiltTable(DiningTable table, int recipeEntryCount, IRandomStream rng)
            => BossDebuffOperations.AddBottomCells(table);
    }

    [Preserve]
    [BossDebuffModel("debuff_binge")]
    public sealed class BingeBossDebuffModel : BossDebuffModel
    {
        public override void ModifyTableBounds(ref int maxWidth, ref int maxHeight) => maxWidth += 1;

        public override void ModifyBuiltTable(DiningTable table, int recipeEntryCount, IRandomStream rng)
            => BossDebuffOperations.AddRightCells(table);
    }

    [Preserve]
    [BossDebuffModel("debuff_kids_meal")]
    public sealed class KidsMealBossDebuffModel : BossDebuffModel
    {
        public override void ModifyBuiltTable(DiningTable table, int recipeEntryCount, IRandomStream rng)
            => BossDebuffOperations.RemoveBottomCells(table);
    }

    [Preserve]
    [BossDebuffModel("debuff_weight_loss")]
    public sealed class WeightLossBossDebuffModel : BossDebuffModel
    {
        public override void ModifyBuiltTable(DiningTable table, int recipeEntryCount, IRandomStream rng)
            => BossDebuffOperations.RemoveRightCells(table);
    }

    [Preserve]
    [BossDebuffModel("debuff_gluttony")]
    public sealed class GluttonyBossDebuffModel : BossDebuffModel
    {
        public override void ModifyRecipeSlots(List<RecipeSlot> slots, IRandomStream rng)
            => BossDebuffOperations.CopyRecipeEntries(slots, 1);
    }

    [Preserve]
    [BossDebuffModel("debuff_omakase")]
    public sealed class OmakaseBossDebuffModel : BossDebuffModel
    {
        public override void ApplyToBattle(BattleSession session) => session.ConfigureFoodDiscardLimit(0);
    }

    [Preserve]
    [BossDebuffModel("debuff_light_meal")]
    public sealed class LightMealBossDebuffModel : BossDebuffModel
    {
        public override void ModifyRecipeSlots(List<RecipeSlot> slots, IRandomStream rng)
        {
            int total = TotalEntries(slots);
            BossDebuffOperations.MarkRandomRecipeEntries(
                slots,
                Math.Max(1, total / 8 + 1),
                rng,
                disableSkills: true);
        }

        private static int TotalEntries(List<RecipeSlot> slots)
        {
            int total = 0;
            foreach (RecipeSlot slot in slots)
            {
                total += slot.Count;
            }

            return total;
        }
    }

    [Preserve]
    [BossDebuffModel("debuff_vegan_meal")]
    public sealed class VeganMealBossDebuffModel : BossDebuffModel
    {
        public override void ModifyRecipeSlots(List<RecipeSlot> slots, IRandomStream rng)
        {
            int total = 0;
            foreach (RecipeSlot slot in slots)
            {
                total += slot.Count;
            }

            BossDebuffOperations.MarkRandomRecipeEntries(
                slots,
                Math.Max(1, total / 8),
                rng,
                excludeFromScore: true);
        }
    }

    [Preserve]
    [BossDebuffModel("debuff_dine_and_dash")]
    public sealed class DineAndDashBossDebuffModel : BossDebuffModel
    {
        public override void ApplyToBattle(BattleSession session)
            => session.ConfigureConfirmedServeGoldCost(5, DebuffId, Definition?.Name);
    }

    [Preserve]
    [BossDebuffModel("debuff_fine_dining")]
    public sealed class FineDiningBossDebuffModel : BossDebuffModel
    {
        public override void ApplyToBattle(BattleSession session)
            => session.ConfigureBaseScoreMultiplier(0.5f, DebuffId, Definition?.Name);
    }

    [Preserve]
    [BossDebuffModel("debuff_vegetarian")]
    public sealed class VegetarianBossDebuffModel : BossDebuffModel
    {
        public override void ModifyPreparedTable(DiningTable table, int recipeEntryCount, IRandomStream rng)
        {
            int disableCount = Math.Max(0, recipeEntryCount / 12 + 1);
            List<GridPos> cells = table.ExistingCells();
            rng.Shuffle(cells);
            int disabled = 0;
            foreach (GridPos cell in cells)
            {
                if (!table.IsEmpty(cell))
                {
                    continue;
                }

                table.SetDisabled(cell, true);
                disabled++;
                if (disabled >= disableCount)
                {
                    break;
                }
            }
        }
    }

    [Preserve]
    [BossDebuffModel("debuff_carb_meal")]
    public sealed class CarbMealBossDebuffModel : BossDebuffModel
    {
        public override void ApplyToBattle(BattleSession session)
            => session.ConfigureInsertedDishSequence("custard_bun", windowSize: 5, countPerWindow: 1);
    }

    [Preserve]
    [BossDebuffModel("debuff_dark_cuisine")]
    public sealed class DarkCuisineBossDebuffModel : BossDebuffModel
    {
        public override void ApplyToBattle(BattleSession session)
        {
            session.ConfigureRandomServeMultiplier(
                0.5f,
                1.5f,
                0.1f,
                DebuffId,
                Definition?.Name);
            session.RandomServeMultiplier = true;
        }
    }

    [Preserve]
    [BossDebuffModel("debuff_late_night")]
    public sealed class LateNightBossDebuffModel : BossDebuffModel
    {
        public override void ApplyToBattle(BattleSession session) => session.ReverseSettlementOrder = true;
    }

    [Preserve]
    [BossDebuffModel("debuff_appetizer")]
    public sealed class AppetizerBossDebuffModel : BossDebuffModel
    {
        public override void ApplyToBattle(BattleSession session)
        {
            session.RemoveFirstServedDishes = true;
            session.FirstServedDishesToRemove = 2;
        }
    }

    [Preserve]
    [BossDebuffModel("debuff_tasting")]
    public sealed class TastingBossDebuffModel : BossDebuffModel
    {
        public override void ApplyToBattle(BattleSession session)
            => session.ConfigureAlternateServeMultiplier(
                0.5f,
                1.5f,
                DebuffId,
                Definition?.Name);
    }

    [Preserve]
    [BossDebuffModel("debuff_buffet")]
    public sealed class BuffetBossDebuffModel : BossDebuffModel
    {
        public override void ApplyToBattle(BattleSession session) => session.MinimumServesForScore = 10;
    }
}
