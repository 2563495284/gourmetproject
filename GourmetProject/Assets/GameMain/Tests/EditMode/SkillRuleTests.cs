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
    /// <summary>「前提×行为」规则技能测试：前提 count 计算、各行为效果、跨菜命令、历史/菜谱/层数/金币。</summary>
    public class SkillRuleTests
    {
        private static GameplayDatabase Db(params SkillDef[] skills)
        {
            return new GameplayDatabase(
                new List<DishDef>(),
                skills,
                new List<FlavorDef>(),
                new List<CellTagDef>(),
                new List<RecipeDef>());
        }

        private static DishInstance Place(GpBoard board, int id, DishDef def, int x, int y, params string[] skills)
        {
            DishInstance inst = GameplayTestFactory.InstanceWithTags(id, def, x, y, skills);
            board.Place(inst);
            return inst;
        }

        // ---------- 无前提行为 ----------

        [Test]
        public void RuleAddFlat_NoCondition_AddsOnce()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.AddFlat, 5f)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(15f, result.RawSum, 0.001f);
        }

        [Test]
        public void RuleAddMult_WithPerCount_StacksAsPower()
        {
            // 前提：相邻每有 1 个菜 → count；行为：本菜乘区 x2 叠 count 次。
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddMult, 2f,
                    condType: SkillConditionType.DishCount, condScope: SkillScope.Adjacent, condMode: CountMode.Per)));
            var board = new GpBoard(4, 4);
            DishDef single = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishInstance self = Place(board, 1, single, 1, 1, "s");
            Place(board, 2, single, 0, 1);
            Place(board, 3, single, 2, 1);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // 2 个相邻 → x2^2=4 → 10*4=40
            DishScore score = result.DishScores.First(s => s.DishInstanceId == 1);
            Assert.AreEqual(40f, score.Contribution, 0.001f);
        }

        // ---------- 前提：空格 / 数量 / 大小 / 形状 / 相同 ----------

        [Test]
        public void EmptyCell_ScalesWithBoardEmpty()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 2f,
                    condType: SkillConditionType.EmptyCell, condScope: SkillScope.All, condMode: CountMode.Per)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 9, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(39f, result.RawSum, 0.001f); // 9 + 2*15
        }

        [Test]
        public void DishSize_CountsBigDishes()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 1f,
                    condType: SkillConditionType.DishSize, condScope: SkillScope.All, condMode: CountMode.Per,
                    condCompare: CompareOp.Gte, condThreshold: 2)));
            var board = new GpBoard(4, 4);
            DishDef small = GameplayTestFactory.Dish("s1", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishDef big = GameplayTestFactory.Dish("b1", new[] { "XX" }, deliciousness: 10, allowRotate: false);
            DishInstance self = Place(board, 1, small, 0, 0, "s");
            Place(board, 2, big, 0, 2);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(11f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        [Test]
        public void ShapeMatch_CountsMatchingShape()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 1f,
                    condType: SkillConditionType.ShapeMatch, condScope: SkillScope.All, condMode: CountMode.Per,
                    condParam: "1x1")));
            var board = new GpBoard(4, 4);
            DishDef single = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishInstance self = Place(board, 1, single, 0, 0, "s");
            Place(board, 2, single, 1, 0);
            Place(board, 3, single, 2, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(12f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f); // 2 个其它 1x1
        }

        [Test]
        public void SameDish_CountsSameBaseId()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 3f,
                    condType: SkillConditionType.SameDish, condScope: SkillScope.All, condMode: CountMode.Per)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishDef other = GameplayTestFactory.Dish("o", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");
            Place(board, 2, dish, 1, 0);
            Place(board, 3, other, 2, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(13f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        // ---------- 前提：闸门 / 阶梯 ----------

        [Test]
        public void Reach_GateOpensWhenThresholdMet()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 100f,
                    condType: SkillConditionType.DishCount, condScope: SkillScope.All, condMode: CountMode.Reach,
                    condCompare: CompareOp.Gte, condThreshold: 2)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishInstance self = Place(board, 1, dish, 0, 0, "s");
            Place(board, 2, dish, 1, 0);
            Place(board, 3, dish, 2, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // 其它菜 2 个 >= 2 → 闸门开 → +100
            Assert.AreEqual(110f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        [Test]
        public void Gate_ClosedProducesNoEffect()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddMult, 5f,
                    condType: SkillConditionType.PositionFilled, condScope: SkillScope.Row, condMode: CountMode.Gate)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s"); // 本行未填满

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        [Test]
        public void PositionFilled_RowFullOpensGate()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddMult, 2f,
                    condType: SkillConditionType.PositionFilled, condScope: SkillScope.Row, condMode: CountMode.Gate)));
            var board = new GpBoard(2, 1); // 2 格一行
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishInstance self = Place(board, 1, dish, 0, 0, "s");
            Place(board, 2, dish, 1, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(20f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        // ---------- 跨菜行为 ----------

        [Test]
        public void AddFlat_ToAdjacent_AffectsNeighborNotSelf()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.AddFlat, 3f, actionScope: SkillScope.Adjacent)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 1, 1, "s");
            Place(board, 2, dish, 0, 1);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
            Assert.AreEqual(13f, result.DishScores.First(s => s.DishInstanceId == 2).Contribution, 0.001f);
        }

        [Test]
        public void TransferScore_MovesFlatBetweenDishes()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.TransferScore, 0.5f, actionScope: SkillScope.All)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");
            Place(board, 2, dish, 1, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(5f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
            Assert.AreEqual(15f, result.DishScores.First(s => s.DishInstanceId == 2).Contribution, 0.001f);
            Assert.AreEqual(20f, result.RawSum, 0.001f);
        }

        [Test]
        public void ExtraSettlement_CountsContributionTwice()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.ExtraSettlement, 1f)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(20f, result.RawSum, 0.001f);
        }

        // ---------- 副作用：金币 / 层数 ----------

        [Test]
        public void GrantGold_AccumulatesGoldDelta()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.GrantGold, 5f,
                    condType: SkillConditionType.DishCount, condScope: SkillScope.All, condMode: CountMode.Per)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");
            Place(board, 2, dish, 1, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(5f, result.GoldDelta, 0.001f); // 其它菜 1 个 → 5*1
        }

        [Test]
        public void LayerCount_ReadsInstanceLayers()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 2f,
                    condType: SkillConditionType.LayerCount, condScope: SkillScope.Self, condMode: CountMode.Per)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishInstance self = Place(board, 1, dish, 0, 0, "s");
            self.AddLayers(3);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(16f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        [Test]
        public void AddLayer_ProducesLayerDelta()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.AddLayer, 2f)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishInstance self = Place(board, 1, dish, 0, 0, "s");

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.IsTrue(result.LayerDeltas.TryGetValue(1, out int delta));
            Assert.AreEqual(2, delta);
        }

        // ---------- 历史 / 菜谱 ----------

        [Test]
        public void SameKindInRun_ReadsRunHistory()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 2f,
                    condType: SkillConditionType.SameKindInRun, condScope: SkillScope.Self, condMode: CountMode.Per)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");
            var history = new ScoreHistory(
                new Dictionary<string, int> { ["d"] = 3 },
                new Dictionary<string, int>(),
                new List<string>());

            ScoreResult result = new ScoreCalculator().Calculate(board, db, history: history);

            Assert.AreEqual(16f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        [Test]
        public void RecipeCount_GateFromRecipe()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 5f,
                    condType: SkillConditionType.RecipeCount, condScope: SkillScope.Recipe, condMode: CountMode.Gate,
                    condCompare: CompareOp.Gte, condThreshold: 3)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");
            var history = new ScoreHistory(
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                new List<string> { "a", "a", "b" });

            ScoreResult result = new ScoreCalculator().Calculate(board, db, history: history);

            Assert.AreEqual(15f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        // ---------- 多条规则组合 ----------

        [Test]
        public void MultipleRules_ActionPlusAction()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.AddFlat, 5f, order: 0),
                GameplayTestFactory.Rule(SkillActionType.AddMult, 2f, order: 1)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(30f, result.RawSum, 0.001f); // (10+5)*2
        }
    }
}
