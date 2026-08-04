using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Game.Adapter;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class FoodSkillRuntimeTests
    {
        private static readonly SkillActionType[] CurrentActionTypes =
        {
            SkillActionType.AddFlat,
            SkillActionType.AddMult,
            SkillActionType.TransferSkills,
            SkillActionType.AddLayer,
            SkillActionType.AddMultFlat,
            SkillActionType.PermanentAddFlat,
            SkillActionType.AddCountAs,
            SkillActionType.CopySkill,
            SkillActionType.TriggerSweetTransfer,
            SkillActionType.AddCurrentScore,
        };

        private static readonly SkillConditionType[] CurrentConditionTypes =
        {
            SkillConditionType.None,
            SkillConditionType.PositionFilled,
            SkillConditionType.DishCount,
            SkillConditionType.OccupiedCell,
            SkillConditionType.CategoryCount,
            SkillConditionType.SkillCount,
        };

        private cfg.Tables _tables;
        private GameplayDatabase _database;
        private GameplayDatabase _scoringDatabase;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = GameplayContentBuilder.BuildDatabase(_tables);
            _scoringDatabase = new GameplayDatabase(
                _database.AllDishes,
                _database.AllSkills,
                _database.AllFlavors,
                _database.AllMaterials,
                Array.Empty<RecipeDef>(),
                _database.AllFragments);
        }

        [Test]
        public void CurrentConfig_AllFoodSkillsResolveAndUseOnSettle()
        {
            var referencedSkillIds = new HashSet<string>();
            foreach (cfg.DishBase row in _tables.TbDishBase.DataList)
            {
                foreach (string skillId in SplitPipeList(row.Skills))
                {
                    referencedSkillIds.Add(skillId);
                    Assert.That(
                        _database.GetSkill(skillId),
                        Is.Not.Null,
                        $"食物 {row.Id} 引用了不存在的技能 {skillId}。" );
                }
            }

            Assert.That(referencedSkillIds.Count, Is.EqualTo(40));
            Assert.That(_tables.TbSubSkill.DataList.Count, Is.EqualTo(56));

            List<SkillRuleDef> rules = referencedSkillIds
                .Select(_database.GetSkill)
                .SelectMany(skill => skill.Rules)
                .ToList();
            Assert.That(rules.Count, Is.EqualTo(56));
            Assert.That(
                rules.All(rule => rule.Trigger == SkillTrigger.OnSettle),
                Is.True,
                "当前食物技能必须全部在结算时触发。" );
            Assert.That(
                rules.Select(rule => rule.ActionType).Distinct(),
                Is.EquivalentTo(CurrentActionTypes),
                "食物表新增了尚未纳入运行测试的行为类型。" );
            Assert.That(
                rules.Select(rule => rule.CondType).Distinct(),
                Is.EquivalentTo(CurrentConditionTypes),
                "食物表新增了尚未纳入运行测试的条件类型。" );
        }

        [Test]
        public void CurrentConfig_AllFoodSkillsCanRunInOneSettlement()
        {
            List<cfg.DishBase> skilledRows = _tables.TbDishBase.DataList
                .Where(row => SplitPipeList(row.Skills).Count > 0)
                .ToList();
            var table = new DiningTable(4, skilledRows.Count * 4);
            int instanceId = 1;
            for (int i = 0; i < skilledRows.Count; i++)
            {
                Place(table, skilledRows[i].Id, instanceId++, 0, i * 4);
            }

            ScoreResult result = null;
            Assert.DoesNotThrow(() =>
                result = new ScoreCalculator().Calculate(
                    table,
                    _scoringDatabase,
                    copySkillSelector: TakeFirst,
                    transferTargetSelector: TakeFirst));
            Assert.That(result, Is.Not.Null);
            Assert.That(result.DishScores.Count, Is.EqualTo(skilledRows.Count));
        }

        [Test]
        public void CurrentConfig_DoesNotAcquireCountAsAfterTheSettlementPrecalculation()
        {
            foreach (SkillDef skill in _database.AllSkills)
            {
                if (skill.Rules.Any(rule => rule.ActionType == SkillActionType.TransferSkills))
                {
                    Assert.That(
                        skill.Rules.Any(rule => rule.ActionType == SkillActionType.AddCountAs),
                        Is.False,
                        $"{skill.Id} 会在结算中传递“视为食物数”，需要先扩展结算前预计算。" );
                }

                foreach (SkillRuleDef copyRule in skill.Rules.Where(
                             rule => rule.ActionType == SkillActionType.CopySkill))
                {
                    string category = ParseCategory(copyRule.ActionParams);
                    Assert.That(
                        category,
                        Is.Not.Empty,
                        $"{skill.Id} 的复制范围不受分类约束，需要人工确认是否可能复制 AddCountAs。" );

                    bool canCopyCountAs = _database.AllDishes
                        .Where(dish => dish.IsCategory(category))
                        .SelectMany(dish => dish.SkillIds)
                        .Distinct()
                        .Select(_database.GetSkill)
                        .Where(candidate => candidate != null)
                        .SelectMany(candidate => candidate.Rules)
                        .Any(rule => rule.ActionType == SkillActionType.AddCountAs);
                    Assert.That(
                        canCopyCountAs,
                        Is.False,
                        $"{skill.Id} 可能在结算中复制“视为食物数”，需要先扩展结算前预计算。" );
                }
            }
        }

        [Test]
        public void Cheesecake_AddsFlatScoreAndHappyCakeLayers()
        {
            var table = new DiningTable(2, 1);
            DishInstance cheesecake = Place(table, "cheesecake", 1, 0, 0);

            ScoreResult result = new ScoreCalculator().Calculate(table, _scoringDatabase);
            DishScore score = ScoreOf(result, cheesecake);

            Assert.That(score.FlatBonus, Is.EqualTo(10f));
            Assert.That(score.Contribution, Is.EqualTo(30f));
            Assert.That(result.HappyCakeLayerDelta, Is.EqualTo(6));
        }

        [Test]
        public void Eclair_UsesTheFirstTierWhenColumnCountReachesSix()
        {
            var table = new DiningTable(1, 4);
            DishInstance eclair = Place(table, "eclair", 1, 0, 0);
            DishInstance mantou = Place(table, "mantou", 2, 0, 3);
            mantou.AddCountAsBonus(2);

            ScoreResult result = new ScoreCalculator().Calculate(table, _scoringDatabase);

            Assert.That(ScoreOf(result, eclair).Multiplier, Is.EqualTo(3.5f));
            Assert.That(ScoreOf(result, mantou).Multiplier, Is.EqualTo(3.5f));
        }

        [Test]
        public void Sugarbean_TransfersItsScoringRuleToTheSelectedRightTarget()
        {
            var table = new DiningTable(4, 1);
            DishInstance sugarbean = Place(table, "sugarbean", 1, 0, 0);
            DishInstance jelly = Place(table, "jelly", 2, 2, 0);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                _scoringDatabase,
                transferTargetSelector: TakeFirst);

            Assert.That(result.SkillTransfers.Count, Is.EqualTo(1));
            SkillTransferSideEffect transfer = result.SkillTransfers.Single();
            Assert.That(transfer.SourceInstanceId, Is.EqualTo(sugarbean.Id));
            Assert.That(transfer.TargetInstanceId, Is.EqualTo(jelly.Id));
            Assert.That(transfer.Effects.Count, Is.EqualTo(1));
            Assert.That(transfer.Effects[0].Rule.ActionType, Is.EqualTo(SkillActionType.AddFlat));
            Assert.That(ScoreOf(result, sugarbean).FlatBonus, Is.EqualTo(12f));
            Assert.That(ScoreOf(result, jelly).FlatBonus, Is.EqualTo(18f));
        }

        [Test]
        public void DoubleCake_ExecutesTheSelectedCopiedSkillInTheSameSettlement()
        {
            var table = new DiningTable(3, 4);
            DishInstance doubleCake = Place(table, "double_cake", 1, 0, 0);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                _scoringDatabase,
                copySkillSelector: (candidates, _) => candidates
                    .Where(id => id == "sk_cheesecake")
                    .ToArray());

            CopySkillRequest request = result.CopySkillRequests.Single();
            Assert.That(request.TargetInstanceId, Is.EqualTo(doubleCake.Id));
            Assert.That(request.SelectedSkillIds, Is.EqualTo(new[] { "sk_cheesecake" }));
            Assert.That(ScoreOf(result, doubleCake).FlatBonus, Is.EqualTo(10f));
            Assert.That(result.HappyCakeLayerDelta, Is.EqualTo(6));
        }

        private DishInstance Place(
            DiningTable table,
            string dishId,
            int instanceId,
            int x,
            int y)
        {
            DishDef definition = _scoringDatabase.GetDish(dishId);
            Assert.That(definition, Is.Not.Null, dishId);
            var placement = new Placement(
                definition.Shape,
                0,
                new GridPos(x, y));
            Assert.That(table.CanPlace(placement.Orientation, placement.Origin), Is.True, dishId);
            var instance = new DishInstance(
                instanceId,
                definition,
                placement,
                definition.SkillIds,
                Array.Empty<string>());
            table.Place(instance);
            return instance;
        }

        private static DishScore ScoreOf(ScoreResult result, DishInstance dish)
            => result.DishScores.Single(score => score.DishInstanceId == dish.Id);

        private static IReadOnlyList<string> TakeFirst(
            IReadOnlyList<string> candidates,
            int count)
            => candidates.Take(count).ToArray();

        private static IReadOnlyList<int> TakeFirst(
            IReadOnlyList<int> candidates,
            int count)
            => candidates.Take(count).ToArray();

        private static List<string> SplitPipeList(string value)
            => string.IsNullOrWhiteSpace(value)
                ? new List<string>()
                : value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Trim())
                    .Where(part => part.Length > 0)
                    .ToList();

        private static string ParseCategory(IEnumerable<string> parameters)
        {
            foreach (string parameter in parameters ?? Array.Empty<string>())
            {
                foreach (string segment in parameter.Split(';'))
                {
                    if (segment.StartsWith("cat:", StringComparison.OrdinalIgnoreCase))
                    {
                        return segment.Substring("cat:".Length).Trim();
                    }
                }
            }

            return string.Empty;
        }
    }
}
