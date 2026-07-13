using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Tests
{
    /// <summary>计分管线测试：加法/乘区/相邻/空位效果、贡献汇总、结算顺序、局级修正。</summary>
    public class ScoreCalculatorTests
    {
        private static GameplayDatabase Db(params SkillDef[] skills)
        {
            return new GameplayDatabase(
                new List<DishDef>(),
                skills,
                new List<FlavorDef>(),
                new List<MaterialDef>(),
                new List<RecipeDef>());
        }

        private static GameplayDatabase Db(SkillDef[] skills, FlavorDef[] flavors, MaterialDef[] materials)
        {
            return new GameplayDatabase(
                new List<DishDef>(),
                skills,
                flavors,
                materials,
                new List<RecipeDef>());
        }

        private sealed class RelicFinalMultiplierSource : IScoreEffectSource
        {
            public void CollectEffects(ScoreSnapshot snapshot, ScoreEffectCollector collector)
            {
                collector.Add(new ScoreEffectEntry(
                    ScorePhase.Final,
                    ScoreSource.Relic("double_relic", "双倍遗物"),
                    new MultiplyFinalEffect(2f)));
            }
        }

        private sealed class RelicDishFlatSource : IScoreEffectSource
        {
            private readonly float _value;

            public RelicDishFlatSource(float value)
            {
                _value = value;
            }

            public void CollectEffects(ScoreSnapshot snapshot, ScoreEffectCollector collector)
            {
                collector.Add(new ScoreEffectEntry(
                    ScorePhase.AfterDish,
                    ScoreSource.Relic("salt_relic", "撒盐遗物"),
                    new AddCurrentDishFlatEffect(_value)));
            }
        }

        private sealed class PrioritySource : IScoreEffectSource
        {
            public void CollectEffects(ScoreSnapshot snapshot, ScoreEffectCollector collector)
            {
                collector.Add(new ScoreEffectEntry(
                    ScorePhase.BeforeDish,
                    ScoreSource.TableTag("late", "后执行"),
                    new AddCurrentDishFlatEffect(1f),
                    priority: 10));
                collector.Add(new ScoreEffectEntry(
                    ScorePhase.BeforeDish,
                    ScoreSource.TableTag("early", "先执行"),
                    new AddCurrentDishFlatEffect(2f),
                    priority: -10));
            }
        }

        private sealed class ChainSource : IScoreEffectSource
        {
            public void CollectEffects(ScoreSnapshot snapshot, ScoreEffectCollector collector)
            {
                collector.Add(new ScoreEffectEntry(
                    ScorePhase.BeforeDish,
                    ScoreSource.Relic("chain_relic", "连锁遗物"),
                    new ChainEffect()));
            }
        }

        private sealed class AddCurrentDishFlatEffect : IScoreEffect
        {
            private readonly float _value;

            public AddCurrentDishFlatEffect(float value)
            {
                _value = value;
            }

            public void Apply(ScoreContext context)
            {
                context.AddFlat(_value);
            }
        }

        private sealed class ChainEffect : IScoreEffect
        {
            public void Apply(ScoreContext context)
            {
                var chainedEntry = new ScoreEffectEntry(
                    context.Phase,
                    ScoreSource.Relic("chain_child", "连锁加分"),
                    new AddCurrentDishFlatEffect(7f),
                    context.Dish);
                context.SubmitCommand(new ResolveScoreEffectCommand(chainedEntry));
            }
        }

        [Test]
        public void AddFlat_IncreasesContribution()
        {
            GameplayDatabase db = Db(GameplayTestFactory.Skill("fresh", FlavorEffectType.AddFlat, 5f));
            var board = new GpTable(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.InstanceWithTags(1, dish, 0, 0, new[] { "fresh" }));

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(15f, result.RawSum, 0.001f);
            Assert.AreEqual(15, result.Total);
        }

        [Test]
        public void AddMult_MultipliesContribution()
        {
            GameplayDatabase db = Db(GameplayTestFactory.Skill("sweet", FlavorEffectType.AddMult, 1.5f));
            var board = new GpTable(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.InstanceWithTags(1, dish, 0, 0, new[] { "sweet" }));

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(15f, result.RawSum, 0.001f);
        }

        [Test]
        public void FlatThenMult_AppliesFlatBeforeMultiplier()
        {
            GameplayDatabase db = Db(
                GameplayTestFactory.Skill("fresh", FlavorEffectType.AddFlat, 5f),
                GameplayTestFactory.Skill("sweet", FlavorEffectType.AddMult, 1.5f));
            var board = new GpTable(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.InstanceWithTags(1, dish, 0, 0, new[] { "fresh", "sweet" }));

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // (10 + 5) * 1.5 = 22.5 -> 四舍五入(向上) = 23
            Assert.AreEqual(22.5f, result.RawSum, 0.001f);
            Assert.AreEqual(23, result.Total);
        }

        [Test]
        public void PerAdjacentDish_ScalesWithNeighborCount()
        {
            GameplayDatabase db = Db(GameplayTestFactory.Skill("spicy", FlavorEffectType.PerAdjacentDish, 3f));
            var board = new GpTable(4, 4);
            DishDef single = GameplayTestFactory.Dish("s", new[] { "X" }, deliciousness: 4, allowRotate: false);

            DishInstance spicy = GameplayTestFactory.InstanceWithTags(1, single, 1, 1, new[] { "spicy" });
            board.Place(spicy);
            board.Place(GameplayTestFactory.Instance(2, single, 0, 1)); // left neighbor
            board.Place(GameplayTestFactory.Instance(3, single, 2, 1)); // right neighbor

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            DishScore spicyScore = result.DishScores.First(s => s.DishInstanceId == 1);
            // 4 + 3*2 = 10
            Assert.AreEqual(10f, spicyScore.Contribution, 0.001f);
        }

        [Test]
        public void PerEmptyCell_ScalesWithBoardEmptyCells()
        {
            GameplayDatabase db = Db(GameplayTestFactory.Skill("lonely", FlavorEffectType.PerEmptyCell, 2f));
            var board = new GpTable(4, 4); // 16 cells
            DishDef single = GameplayTestFactory.Dish("s", new[] { "X" }, deliciousness: 9, allowRotate: false);
            board.Place(GameplayTestFactory.InstanceWithTags(1, single, 0, 0, new[] { "lonely" }));

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // 1 occupied -> 15 empty. 9 + 2*15 = 39
            Assert.AreEqual(39f, result.RawSum, 0.001f);
        }

        [Test]
        public void FinalModifiers_ApplyAfterSum()
        {
            GameplayDatabase db = Db();
            var board = new GpTable(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.InstanceWithTags(1, dish, 0, 0, new string[0]));

            ScoreResult result = new ScoreCalculator().Calculate(board, db, finalFlat: 50f, finalMultiplier: 2f);

            // (10 + 50) * 2 = 120
            Assert.AreEqual(120, result.Total);
        }

        [Test]
        public void ScoreLines_RecordSkillThenFlavorThenCellTag()
        {
            GameplayDatabase db = Db(
                new[] { GameplayTestFactory.Skill("fresh", FlavorEffectType.AddFlat, 5f) },
                new[] { GameplayTestFactory.Flavor("sweet", FlavorEffectType.AddMult, 1.5f) },
                new[] { GameplayTestFactory.CellMaterial("gold", MaterialEffectType.AddMult, 2f) });
            var materials = new Dictionary<GridPos, IReadOnlyList<string>>
            {
                [new GridPos(0, 0)] = new List<string> { "gold" },
            };
            var board = new GpTable(2, 2, null, materials);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.InstanceWithTags(1, dish, 0, 0, new[] { "fresh" }, flavorId: "sweet"));

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            List<ScoreLine> effectLines = result.ScoreLines
                .Where(l => l.Kind == ScoreLineKind.DishFlat || l.Kind == ScoreLineKind.DishMultiplier)
                .ToList();
            Assert.AreEqual(3, effectLines.Count);
            Assert.AreEqual(ScoreSourceType.DishSkill, effectLines[0].Source.Type);
            Assert.AreEqual(ScorePhase.DishSkills, effectLines[0].Phase);
            Assert.AreEqual(ScoreSourceType.DishFlavor, effectLines[1].Source.Type);
            Assert.AreEqual(ScorePhase.DishFlavor, effectLines[1].Phase);
            Assert.AreEqual(ScoreSourceType.Material, effectLines[2].Source.Type);
            Assert.AreEqual(ScorePhase.Materials, effectLines[2].Phase);
            // ((10 + 5(技能)) * 1.5(风味)) * 2(格子) = 45
            Assert.AreEqual(45, result.Total);
        }

        [Test]
        public void ExtraRelicSource_CanModifyFinalScore()
        {
            GameplayDatabase db = Db();
            var board = new GpTable(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.Instance(1, dish, 0, 0));

            ScoreResult result = new ScoreCalculator(effectSources: new[] { new RelicFinalMultiplierSource() })
                .Calculate(board, db);

            Assert.AreEqual(20, result.Total);
            Assert.IsTrue(result.ScoreLines.Any(l => l.Source.Type == ScoreSourceType.Relic && l.Kind == ScoreLineKind.FinalMultiplier));
        }

        [Test]
        public void GlobalDishEffect_AppliesToEveryDishInDishPhase()
        {
            GameplayDatabase db = Db();
            var board = new GpTable(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.Instance(1, dish, 0, 0));
            board.Place(GameplayTestFactory.Instance(2, dish, 1, 0));

            ScoreResult result = new ScoreCalculator(effectSources: new[] { new RelicDishFlatSource(2f) })
                .Calculate(board, db);

            Assert.AreEqual(24f, result.RawSum, 0.001f);
            Assert.AreEqual(2, result.ScoreLines.Count(l => l.Source.Type == ScoreSourceType.Relic && l.Kind == ScoreLineKind.DishFlat));
        }

        [Test]
        public void Priority_ControlsOrderWithinSamePhase()
        {
            GameplayDatabase db = Db();
            var board = new GpTable(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.Instance(1, dish, 0, 0));

            ScoreResult result = new ScoreCalculator(effectSources: new[] { new PrioritySource() })
                .Calculate(board, db);

            List<ScoreLine> flatLines = result.ScoreLines
                .Where(l => l.Kind == ScoreLineKind.DishFlat)
                .ToList();
            Assert.AreEqual(2f, flatLines[0].Value, 0.001f);
            Assert.AreEqual(1f, flatLines[1].Value, 0.001f);
            Assert.AreEqual(13, result.Total);
        }

        [Test]
        public void CommandQueue_CanResolveChainedEffect()
        {
            GameplayDatabase db = Db();
            var board = new GpTable(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.Instance(1, dish, 0, 0));

            ScoreResult result = new ScoreCalculator(effectSources: new[] { new ChainSource() })
                .Calculate(board, db);

            Assert.AreEqual(17, result.Total);
            Assert.IsTrue(result.ScoreLines.Any(l => l.Source.Id == "chain_child" && l.Kind == ScoreLineKind.DishFlat));
        }
    }
}
