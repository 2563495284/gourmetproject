using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SweetTransferTargetWeightTests
    {
        private static readonly MethodInfo ApplyTransferRequestsMethod = typeof(BattleSession).GetMethod(
            "ApplyTransferRequests",
            BindingFlags.Instance | BindingFlags.NonPublic);

        [Test]
        public void FormalSettlement_UsesArchetypeCellWeightsWithoutReplacement()
        {
            SkillDef transferSkill = CreateTransferSkill("weighted_transfer", targetCount: 2);
            DishDef sourceDef = CreateDish("source", "来源", "X", new[] { transferSkill.Id }, sweetWeight: 0f);
            DishDef ordinaryFourCell = CreateDish("ordinary_4", "普通四格", "XXXX", Array.Empty<string>(), sweetWeight: 0f);
            DishDef sweetOneCell = CreateDish("sweet_1", "传递一格", "X", Array.Empty<string>(), sweetWeight: 0.25f);
            DishDef sweetTwoCell = CreateDish("sweet_2", "传递二格", "XX", Array.Empty<string>(), sweetWeight: 3f);
            DishDef sweetFourCell = CreateDish("sweet_4", "传递四格", "XXXX", Array.Empty<string>(), sweetWeight: 1f);
            var database = CreateDatabase(
                new[] { sourceDef, ordinaryFourCell, sweetOneCell, sweetTwoCell, sweetFourCell },
                new[] { transferSkill });
            var board = new DiningTable(12, 1);
            DishInstance source = Place(board, 1, sourceDef, 0);
            DishInstance ordinary = Place(board, 2, ordinaryFourCell, 1);
            DishInstance sweetOne = Place(board, 3, sweetOneCell, 5);
            DishInstance sweetTwo = Place(board, 4, sweetTwoCell, 6);
            DishInstance sweetFour = Place(board, 5, sweetFourCell, 8);
            var random = new RecordingRandomStream(3, 1);
            var session = CreateSession(board, database, random);

            session.Settle();

            Assert.That(random.WeightCalls, Has.Count.EqualTo(2));
            Assert.That(random.WeightCalls[0], Is.EqualTo(new[] { 1f, 1f, 2f, 4f }));
            Assert.That(random.WeightCalls[1], Is.EqualTo(new[] { 1f, 1f, 2f }));
            Assert.That(source.TransferredSkills, Is.Empty);
            Assert.That(ordinary.TransferredSkills, Is.Empty, "非传递流派即使占四格，权重仍为 1");
            Assert.That(sweetTwo.TransferredSkills, Is.Empty);
            Assert.That(sweetOne.TransferredSkills, Has.Count.EqualTo(1));
            Assert.That(sweetFour.TransferredSkills, Has.Count.EqualTo(1));
        }

        [Test]
        public void OnServeRequest_UsesTheSameTargetWeights()
        {
            DishDef sourceDef = CreateDish("source", "来源", "X", Array.Empty<string>(), sweetWeight: 0f);
            DishDef ordinaryThreeCell = CreateDish("ordinary_3", "普通三格", "XXX", Array.Empty<string>(), sweetWeight: 0f);
            DishDef sweetTwoCell = CreateDish("sweet_2", "传递二格", "XX", Array.Empty<string>(), sweetWeight: 0.01f);
            DishDef sweetFourCell = CreateDish("sweet_4", "传递四格", "XXXX", Array.Empty<string>(), sweetWeight: 10f);
            var database = CreateDatabase(
                new[] { sourceDef, ordinaryThreeCell, sweetTwoCell, sweetFourCell },
                Array.Empty<SkillDef>());
            var board = new DiningTable(10, 1);
            DishInstance source = Place(board, 1, sourceDef, 0);
            DishInstance ordinary = Place(board, 2, ordinaryThreeCell, 1);
            DishInstance sweetTwo = Place(board, 3, sweetTwoCell, 4);
            DishInstance sweetFour = Place(board, 4, sweetFourCell, 6);
            var random = new RecordingRandomStream(2);
            var session = CreateSession(board, database, random);
            var request = new SkillTransferRequest(
                source.Id,
                source.Def.Name,
                new[] { ordinary.Id, sweetTwo.Id, sweetFour.Id },
                new[] { CreateNoOpEffect(SkillTrigger.OnServe) },
                count: 1);

            InvokeApplyTransferRequests(session, new[] { request });

            Assert.That(random.WeightCalls, Has.Count.EqualTo(1));
            Assert.That(random.WeightCalls[0], Is.EqualTo(new[] { 1f, 2f, 4f }));
            Assert.That(ordinary.TransferredSkills, Is.Empty);
            Assert.That(sweetTwo.TransferredSkills, Is.Empty);
            Assert.That(sweetFour.TransferredSkills, Has.Count.EqualTo(1));
        }

        [Test]
        public void SelectingAllTargets_PreservesOrderAndConsumesNoRandomDraw()
        {
            DishDef sourceDef = CreateDish("source", "来源", "X", Array.Empty<string>(), sweetWeight: 0f);
            DishDef ordinaryDef = CreateDish("ordinary", "普通", "XXX", Array.Empty<string>(), sweetWeight: 0f);
            DishDef sweetDef = CreateDish("sweet", "传递", "XXXX", Array.Empty<string>(), sweetWeight: 1f);
            var database = CreateDatabase(new[] { sourceDef, ordinaryDef, sweetDef }, Array.Empty<SkillDef>());
            var board = new DiningTable(8, 1);
            DishInstance source = Place(board, 1, sourceDef, 0);
            DishInstance ordinary = Place(board, 2, ordinaryDef, 1);
            DishInstance sweet = Place(board, 3, sweetDef, 4);
            var random = new RecordingRandomStream();
            var session = CreateSession(board, database, random);
            var request = new SkillTransferRequest(
                source.Id,
                source.Def.Name,
                new[] { ordinary.Id, sweet.Id },
                new[] { CreateNoOpEffect(SkillTrigger.OnServe) },
                count: 0);
            var oversizedRequest = new SkillTransferRequest(
                source.Id,
                source.Def.Name,
                new[] { ordinary.Id, sweet.Id },
                new[] { CreateNoOpEffect(SkillTrigger.OnServe) },
                count: 99);

            InvokeApplyTransferRequests(session, new[] { request, oversizedRequest });

            Assert.That(random.WeightCalls, Is.Empty);
            Assert.That(ordinary.TransferredSkills, Has.Count.EqualTo(2));
            Assert.That(sweet.TransferredSkills, Has.Count.EqualTo(2));
        }

        [Test]
        public void TriggerSweetTransfer_SelectsSourcesUniformlyInsteadOfUsingTargetWeights()
        {
            SkillDef triggerSkill = CreateTriggerSkill("trigger", sourceCount: 1);
            SkillDef sourceSkillA = CreateTransferSkill("source_a_skill", targetCount: 1);
            SkillDef sourceSkillB = CreateTransferSkill("source_b_skill", targetCount: 1);
            DishDef activatorDef = CreateDish("activator", "代触发者", "X", new[] { triggerSkill.Id }, sweetWeight: 0f);
            DishDef sourceDefA = CreateDish("source_a", "来源A", "X", new[] { sourceSkillA.Id }, sweetWeight: 0f);
            DishDef sourceDefB = CreateDish("source_b", "来源B", "XX", new[] { sourceSkillB.Id }, sweetWeight: 1f);
            DishDef targetDef = CreateDish("target", "接收目标", "X", Array.Empty<string>(), sweetWeight: 0f);
            var database = CreateDatabase(
                new[] { activatorDef, sourceDefA, sourceDefB, targetDef },
                new[] { triggerSkill, sourceSkillA, sourceSkillB });
            var board = new DiningTable(5, 1);
            Place(board, 1, activatorDef, 0);
            Place(board, 2, sourceDefA, 1);
            Place(board, 3, sourceDefB, 2);
            DishInstance target = Place(board, 4, targetDef, 4);
            int sourceSelectionDraws = 0;
            int receiverSelections = 0;

            new ScoreCalculator().Calculate(
                board,
                database,
                transferTargetSelector: (candidates, count) =>
                {
                    receiverSelections++;
                    Assert.That(candidates, Does.Contain(target.Id), "接收者选择器不应被用于挑选代触发来源");
                    return new[] { target.Id };
                },
                randomIntegerSelector: (minimum, maximum) =>
                {
                    sourceSelectionDraws++;
                    return minimum;
                });

            Assert.That(sourceSelectionDraws, Is.EqualTo(1));
            Assert.That(receiverSelections, Is.EqualTo(3));
        }

        private static BattleSession CreateSession(
            DiningTable board,
            GameplayDatabase database,
            IRandomStream random)
            => new BattleSession(
                board,
                database,
                random,
                Array.Empty<RecipeSlot>(),
                requiredScore: 0);

        private static GameplayDatabase CreateDatabase(
            IReadOnlyList<DishDef> dishes,
            IReadOnlyList<SkillDef> skills)
            => new GameplayDatabase(
                dishes,
                skills,
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());

        private static DishDef CreateDish(
            string id,
            string name,
            string shapeRow,
            IReadOnlyList<string> skillIds,
            float sweetWeight)
            => new DishDef(
                id,
                name,
                deliciousness: 1,
                DishShape.FromRows(new[] { shapeRow }),
                hiddenMin: 0,
                hiddenMax: 0,
                baseWeight: 1f,
                skillIds,
                flavorId: string.Empty,
                archetypeWeights: new[] { sweetWeight, 0f, 0f });

        private static DishInstance Place(DiningTable board, int id, DishDef def, int x)
        {
            var instance = new DishInstance(
                id,
                def,
                new Placement(def.Shape, rotationIndex: 0, new GridPos(x, 0)),
                def.SkillIds,
                Array.Empty<string>());
            board.Place(instance);
            return instance;
        }

        private static SkillDef CreateTransferSkill(string skillId, int targetCount)
        {
            SkillRuleDef payload = CreateRule(
                $"{skillId}_payload",
                skillId,
                SkillActionType.None,
                actionCount: 0,
                SkillTrigger.OnSettle);
            SkillRuleDef transfer = CreateRule(
                $"{skillId}_transfer",
                skillId,
                SkillActionType.TransferSkills,
                targetCount,
                SkillTrigger.OnSettle);
            return new SkillDef(
                skillId,
                skillId,
                string.Empty,
                Array.Empty<string>(),
                new[] { payload, transfer },
                new[] { payload.Id, transfer.Id });
        }

        private static SkillDef CreateTriggerSkill(string skillId, int sourceCount)
        {
            SkillRuleDef trigger = CreateRule(
                $"{skillId}_trigger",
                skillId,
                SkillActionType.TriggerSweetTransfer,
                sourceCount,
                SkillTrigger.OnSettle);
            return new SkillDef(
                skillId,
                skillId,
                string.Empty,
                Array.Empty<string>(),
                new[] { trigger },
                new[] { trigger.Id });
        }

        private static SkillEffect CreateNoOpEffect(SkillTrigger trigger)
            => new SkillEffect(
                CreateRule("no_op_payload", "no_op_skill", SkillActionType.None, 0, trigger),
                "测试载荷");

        private static SkillRuleDef CreateRule(
            string id,
            string skillId,
            SkillActionType actionType,
            int actionCount,
            SkillTrigger trigger)
            => new SkillRuleDef(
                id,
                skillId,
                order: actionType == SkillActionType.TransferSkills ? 1 : 0,
                trigger,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                actionType,
                SkillScope.All,
                actionCount,
                new[] { 0f },
                Array.Empty<string>());

        private static void InvokeApplyTransferRequests(
            BattleSession session,
            IReadOnlyList<SkillTransferRequest> requests)
        {
            Assert.That(ApplyTransferRequestsMethod, Is.Not.Null);
            ApplyTransferRequestsMethod.Invoke(session, new object[] { requests });
        }

        private sealed class RecordingRandomStream : IRandomStream
        {
            private readonly Queue<int> _weightedIndices;

            public RecordingRandomStream(params int[] weightedIndices)
            {
                _weightedIndices = new Queue<int>(weightedIndices ?? Array.Empty<int>());
            }

            public List<float[]> WeightCalls { get; } = new List<float[]>();

            public RngState State { get; set; }

            public uint NextUInt() => 0u;

            public ulong NextULong() => 0UL;

            public int Range(int minInclusive, int maxExclusive) => minInclusive;

            public float Range(float minInclusive, float maxExclusive) => minInclusive;

            public float NextFloat() => 0f;

            public double NextDouble() => 0d;

            public bool NextBool(double probability = 0.5) => probability > 0d;

            public void Shuffle<T>(IList<T> list)
            {
            }

            public T Pick<T>(IReadOnlyList<T> list) => list[0];

            public int WeightedPickIndex(IReadOnlyList<float> weights)
            {
                WeightCalls.Add(weights.ToArray());
                return _weightedIndices.Count > 0 ? _weightedIndices.Dequeue() : 0;
            }
        }
    }
}
