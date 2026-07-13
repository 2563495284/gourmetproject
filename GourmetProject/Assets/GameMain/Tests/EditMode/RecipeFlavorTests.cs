using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Tests
{
    /// <summary>酸/咸：整体结算末尾遍历未上菜菜谱，作用于场上同菜谱食物。</summary>
    public class RecipeFlavorTests
    {
        private static GameplayDatabase Db(DishDef[] dishes, FlavorDef[] flavors)
        {
            return new GameplayDatabase(
                new List<DishDef>(dishes),
                new List<SkillDef>(),
                new List<FlavorDef>(flavors),
                new List<MaterialDef>(),
                new List<RecipeDef>());
        }

        private static DishInstance PlaceServed(GpTable board, int id, DishDef def, int x, int y, int slotIndex)
        {
            var placement = new Placement(def.Shape.RotatedBy(0), 0, new GridPos(x, y));
            var inst = new DishInstance(id, def, placement, System.Array.Empty<string>(), System.Array.Empty<string>());
            inst.SetSourceSlotIndex(slotIndex);
            board.Place(inst);
            return inst;
        }

        [Test]
        public void Sour_Unserved_MultipliesServedSameRecipeDishes()
        {
            DishDef served = GameplayTestFactory.Dish("served", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishDef sourDish = GameplayTestFactory.Dish("sour_dish", new[] { "X" }, deliciousness: 10, allowRotate: false, flavor: "fl_sour");
            GameplayDatabase db = Db(
                new[] { served, sourDish },
                new[] { GameplayTestFactory.Flavor("fl_sour", TagEffectType.SourRecipeMult, 1.5f) });

            var board = new GpTable(4, 4);
            PlaceServed(board, 1, served, 0, 0, slotIndex: 0);
            PlaceServed(board, 2, served, 1, 0, slotIndex: 0);

            var unserved = new List<UnservedRecipeDish> { new UnservedRecipeDish(0, "sour_dish") };
            ScoreResult result = new ScoreCalculator().Calculate(board, db, unservedRecipeDishes: unserved);

            // 两个 slot0 食物各 ×1.5：10*1.5 + 10*1.5 = 30。
            Assert.AreEqual(30f, result.RawSum, 0.001f);
        }

        [Test]
        public void Sour_OnlyAffectsSameRecipeSlot()
        {
            DishDef served = GameplayTestFactory.Dish("served", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishDef sourDish = GameplayTestFactory.Dish("sour_dish", new[] { "X" }, deliciousness: 10, allowRotate: false, flavor: "fl_sour");
            GameplayDatabase db = Db(
                new[] { served, sourDish },
                new[] { GameplayTestFactory.Flavor("fl_sour", TagEffectType.SourRecipeMult, 1.5f) });

            var board = new GpTable(4, 4);
            PlaceServed(board, 1, served, 0, 0, slotIndex: 1); // 不同槽

            var unserved = new List<UnservedRecipeDish> { new UnservedRecipeDish(0, "sour_dish") };
            ScoreResult result = new ScoreCalculator().Calculate(board, db, unservedRecipeDishes: unserved);

            // 场上食物来自 slot1，未上菜酸在 slot0 → 不影响。
            Assert.AreEqual(10f, result.RawSum, 0.001f);
        }

        [Test]
        public void Salty_Unserved_GrantsGoldPerServedSameRecipeDish()
        {
            DishDef served = GameplayTestFactory.Dish("served", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishDef saltyDish = GameplayTestFactory.Dish("salty_dish", new[] { "X" }, deliciousness: 10, allowRotate: false, flavor: "fl_salty");
            GameplayDatabase db = Db(
                new[] { served, saltyDish },
                new[] { GameplayTestFactory.Flavor("fl_salty", TagEffectType.SaltyRecipeGold, 2f) });

            var board = new GpTable(4, 4);
            PlaceServed(board, 1, served, 0, 0, slotIndex: 0);
            PlaceServed(board, 2, served, 1, 0, slotIndex: 0);
            PlaceServed(board, 3, served, 2, 0, slotIndex: 0);

            var unserved = new List<UnservedRecipeDish> { new UnservedRecipeDish(0, "salty_dish") };
            ScoreResult result = new ScoreCalculator().Calculate(board, db, unservedRecipeDishes: unserved);

            // 3 个 slot0 食物，每个 +2 金币 = 6；分数不变。
            Assert.AreEqual(6f, result.GoldDelta, 0.001f);
            Assert.AreEqual(30f, result.RawSum, 0.001f);
        }

        [Test]
        public void NoUnservedRecipeDishes_NoEffect()
        {
            DishDef served = GameplayTestFactory.Dish("served", new[] { "X" }, deliciousness: 10, allowRotate: false);
            GameplayDatabase db = Db(new[] { served }, System.Array.Empty<FlavorDef>());
            var board = new GpTable(4, 4);
            PlaceServed(board, 1, served, 0, 0, slotIndex: 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(10f, result.RawSum, 0.001f);
            Assert.AreEqual(0f, result.GoldDelta, 0.001f);
        }
    }
}
