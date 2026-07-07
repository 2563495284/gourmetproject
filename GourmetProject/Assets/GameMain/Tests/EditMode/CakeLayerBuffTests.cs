using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using GpBoard = GourmetProject.Gameplay.Board.Board;

namespace GourmetProject.Tests
{
    /// <summary>欢乐蛋糕层数分段 buff 测试：AfterAllDishes 全局步，按全局层数累计应用到 cake 分类。</summary>
    public class CakeLayerBuffTests
    {
        // 默认三档：≥1 基础分 +5×层；≥50 倍率 +0.1×层；≥100 倍率 ×(0.02×层)。
        private static readonly CakeLayerBuffDef[] DefaultBuffs =
        {
            new CakeLayerBuffDef("clb_flat", 0, 1, "cake", SkillActionType.AddFlat, 5f, ""),
            new CakeLayerBuffDef("clb_multflat", 1, 50, "cake", SkillActionType.AddMultFlat, 0.1f, ""),
            new CakeLayerBuffDef("clb_mult", 2, 100, "cake", SkillActionType.AddMult, 0.02f, ""),
        };

        private static GameplayDatabase DbWithBuffs(params CakeLayerBuffDef[] buffs)
        {
            return new GameplayDatabase(
                new List<DishDef>(),
                new List<SkillDef>(),
                new List<FlavorDef>(),
                new List<CellTagDef>(),
                new List<RecipeDef>(),
                cakeLayerBuffs: buffs);
        }

        private static (GpBoard board, DishInstance cake, DishInstance plain) BuildBoard()
        {
            var board = new GpBoard(4, 1);
            DishDef cakeDef = GameplayTestFactory.Dish("cake", new[] { "X" }, deliciousness: 10, allowRotate: false, category: "cake");
            DishDef plainDef = GameplayTestFactory.Dish("plain", new[] { "X" }, deliciousness: 10, allowRotate: false);
            var cake = GameplayTestFactory.InstanceWithTags(1, cakeDef, 0, 0, System.Array.Empty<string>());
            var plain = GameplayTestFactory.InstanceWithTags(2, plainDef, 1, 0, System.Array.Empty<string>());
            board.Place(cake);
            board.Place(plain);
            return (board, cake, plain);
        }

        [Test]
        public void ZeroLayers_NoEffect()
        {
            (GpBoard board, _, _) = BuildBoard();
            ScoreResult result = new ScoreCalculator().Calculate(board, DbWithBuffs(DefaultBuffs), initialHappyCakeLayers: 0);

            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 2).Contribution, 0.001f);
        }

        [Test]
        public void FlatTierOnly_AddsBaseToCakesOnly()
        {
            (GpBoard board, _, _) = BuildBoard();
            // 层数 10：仅第一档生效 → cake 基础分 +5×10=50 → 10+50=60；非蛋糕不受影响。
            ScoreResult result = new ScoreCalculator().Calculate(board, DbWithBuffs(DefaultBuffs), initialHappyCakeLayers: 10);

            Assert.AreEqual(60f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 2).Contribution, 0.001f);
        }

        [Test]
        public void MidTier_FlatAndMultFlatStack()
        {
            (GpBoard board, _, _) = BuildBoard();
            // 层数 60：flat(+5×60=300) 且 multflat(+0.1×60=+6 倍率) → (10+300)×(1+6)=2170。
            ScoreResult result = new ScoreCalculator().Calculate(board, DbWithBuffs(DefaultBuffs), initialHappyCakeLayers: 60);

            Assert.AreEqual(2170f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 2).Contribution, 0.001f);
        }

        [Test]
        public void TopTier_AllThreeStack()
        {
            (GpBoard board, _, _) = BuildBoard();
            // 层数 100：flat(+500) → 510；multflat(+10) → 倍率 11；mult(×0.02×100=×2) → 倍率 22 → 510×22=11220。
            ScoreResult result = new ScoreCalculator().Calculate(board, DbWithBuffs(DefaultBuffs), initialHappyCakeLayers: 100);

            Assert.AreEqual(11220f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 2).Contribution, 0.001f);
        }

        [Test]
        public void NoBuffTable_NoEffect()
        {
            (GpBoard board, _, _) = BuildBoard();
            // 未配置 buff 表（空）→ 即使有层数也无任何加成。
            ScoreResult result = new ScoreCalculator().Calculate(board, DbWithBuffs(), initialHappyCakeLayers: 100);

            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 2).Contribution, 0.001f);
        }

        [Test]
        public void BuffLine_AttributedToCakeLayerSource()
        {
            (GpBoard board, _, _) = BuildBoard();
            ScoreResult result = new ScoreCalculator().Calculate(board, DbWithBuffs(DefaultBuffs), initialHappyCakeLayers: 10);

            Assert.IsTrue(
                result.ScoreLines.Any(l => l.DishInstanceId == 1 && l.Source != null && l.Source.Name == "欢乐蛋糕层数"),
                "结算明细应含来源「欢乐蛋糕层数」");
        }
    }
}
