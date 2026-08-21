using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta.BossDebuffs;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BossGluttonyTemporaryCopyTests
    {
        [Test]
        public void CopyRecipeEntries_MarksOnlyCopiesAndPreservesFlagWhenCloned()
        {
            var slot = new RecipeSlot("recipe", new[] { new RecipeSlotEntry("dish_test") });
            var slots = new List<RecipeSlot> { slot };

            BossDebuffOperations.CopyRecipeEntries(slots, 1);

            Assert.That(slot.Entries, Has.Count.EqualTo(2));
            Assert.That(slot.Entries[0].IsTemporaryCopy, Is.False);
            Assert.That(slot.Entries[1].IsTemporaryCopy, Is.True);
            Assert.That(slot.Entries[1].Clone().IsTemporaryCopy, Is.True);
        }

        [Test]
        public void PrepareServe_TemporaryRecipeCopy_ShowsCopyMarkerInFoodTips()
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            var dish = new DishDef(
                "dish_test",
                "测试食物",
                deliciousness: 10,
                shape,
                hiddenMin: 0,
                hiddenMax: 0,
                baseWeight: 1f,
                skillIds: Array.Empty<string>(),
                flavorId: string.Empty);
            var database = new GameplayDatabase(
                new[] { dish },
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var entry = new RecipeSlotEntry(dish.Id);
            entry.MarkTemporaryCopy();
            var session = new BattleSession(
                new DiningTable(1, 1),
                database,
                new Xoshiro256SS(1UL),
                new[] { new RecipeSlot("recipe", new[] { entry }) },
                requiredScore: 1);

            IReadOnlyList<BattleRecipeEntrySnapshot> recipeEntries =
                session.GetBattleRecipeEntries(0);
            Assert.That(recipeEntries, Has.Count.EqualTo(1));
            Assert.That(recipeEntries[0].IsTemporaryCopy, Is.True);

            ServePrepareResult result = session.PrepareServe(0);

            Assert.That(result.Success, Is.True);
            Assert.That(result.PreparedDish.Dish.IsTemporary, Is.True);
            FoodTipsData tips = FoodTipsDataFactory.Build(
                result.PreparedDish.Dish,
                session.DiningTable,
                database);
            Assert.That(tips.Summary.IsTemporaryCopy, Is.True);
        }
    }

    public sealed class KidsMealBossDebuffTests
    {
        [Test]
        public void ModifyPreparedTable_DisablesBottomEdgeWithoutRemovingIt()
        {
            var table = new DiningTable(3, 3);
            int originalCapacity = table.CellCapacity;
            var model = new KidsMealBossDebuffModel();

            model.ModifyPreparedTable(table, recipeEntryCount: 0, rng: null);

            Assert.That(table.CellCapacity, Is.EqualTo(originalCapacity));
            for (int x = 0; x < table.Width; x++)
            {
                var bottom = new GridPos(x, table.Height - 1);
                Assert.That(table.Exists(bottom), Is.True, $"{bottom} 应保留在餐桌轮廓中。");
                Assert.That(table.IsDisabled(bottom), Is.True, $"{bottom} 应在轮廓计算后被禁用。");
            }
        }
    }

    public sealed class VegetarianBossDebuffTests
    {
        [Test]
        public void ModifyPreparedTable_DisablesCellWithoutRemovingIt()
        {
            var table = new DiningTable(1, 1);
            var cell = new GridPos(0, 0);
            var model = new VegetarianBossDebuffModel();

            model.ModifyPreparedTable(
                table,
                recipeEntryCount: 0,
                rng: new Xoshiro256SS(1UL));

            Assert.That(table.Exists(cell), Is.True, "素食禁用格必须保留在餐桌轮廓中。");
            Assert.That(table.IsDisabled(cell), Is.True, "素食选中的格子应保持禁用。");
        }
    }
}
