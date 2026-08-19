using BreakInfinity;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SettlementSequencerTests
    {
        [Test]
        public void ResultVisualDishInstanceIds_SweetTransferResponse_ReturnsEveryBuffTarget()
        {
            var trace = new SkillExecutionTrace(
                SkillExecutionKind.NativeSkill,
                ownerDishInstanceId: 10,
                ownerDishId: "popping_candy",
                ownerDishName: "跳跳糖",
                runtimeSelfDishInstanceId: 20,
                runtimeSelfDishId: "transfer_source",
                runtimeSelfDishName: "传递来源",
                skillId: "sk_popping_candy",
                skillName: "跳跳糖",
                ruleId: "sk_popping_candy_1",
                ruleOrder: 0,
                trigger: SkillTrigger.OnSettle,
                actionType: SkillActionType.AddFlat,
                conditionType: SkillConditionType.None,
                conditionScope: SkillScope.Self,
                actionScope: SkillScope.ColumnAndSelf,
                sourceLabel: "跳跳糖",
                visualTargetDishInstanceIds: new[] { 10, 30, 40, 30 });
            var line = new ScoreLine(
                ScorePhase.DishSkills,
                ScoreLineKind.SweetTransferBuffTriggered,
                source: null,
                dishInstanceId: 10,
                dishId: "popping_candy",
                cell: null,
                value: 50,
                before: BigDouble.Zero,
                after: 50,
                message: string.Empty,
                trace);

            CollectionAssert.AreEqual(
                new[] { 10, 30, 40 },
                SettlementSequencer.ResultVisualDishInstanceIds(line));
        }
    }
}
