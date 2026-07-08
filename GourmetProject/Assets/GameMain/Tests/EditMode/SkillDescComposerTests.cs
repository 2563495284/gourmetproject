using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Gameplay.Tests
{
    [TestFixture]
    public class SkillDescComposerTests
    {
        private static SkillRuleDef Rule(
            SkillActionType actionType = SkillActionType.AddFlat,
            SkillConditionType condType = SkillConditionType.None,
            SkillScope condScope = SkillScope.Self,
            SkillScope actionScope = SkillScope.Self,
            CountUnit condUnit = CountUnit.Instances,
            CountMode condMode = CountMode.Per,
            CompareOp condCompare = CompareOp.None,
            int condThreshold = 0,
            string condParam = "",
            int actionCount = 0,
            float[] actionValues = null,
            string[] actionParams = null)
        {
            return new SkillRuleDef(
                "r", "s", 0, SkillTrigger.OnSettle,
                condType, condScope, condUnit, condMode, condCompare,
                condThreshold, condParam,
                actionType, actionScope, actionCount,
                actionValues ?? new[] { 0f },
                actionParams ?? System.Array.Empty<string>());
        }

        [Test]
        public void SignedValue_PositiveGetsPlus()
        {
            string r = SkillDescComposer.ComposeComponent(
                "{cscope}每有 1 {unit}食物，分数 {0}。",
                Rule(condType: SkillConditionType.DishCount, condScope: SkillScope.Adjacent, condUnit: CountUnit.Kinds, actionValues: new[] { 8f }),
                signed: true);
            Assert.AreEqual("相邻每有 1 种食物，分数 +8。", r);
        }

        [Test]
        public void SignedValue_NegativeKeepsMinus()
        {
            string r = SkillDescComposer.ComposeComponent(
                "{cscope}每有 1 个空格，分数 {0}。",
                Rule(condType: SkillConditionType.EmptyCell, condScope: SkillScope.Adjacent, actionValues: new[] { -2f }),
                signed: true);
            Assert.AreEqual("相邻每有 1 个空格，分数 -2。", r);
        }

        [Test]
        public void MultiplierValue_NotSigned()
        {
            string r = SkillDescComposer.ComposeComponent(
                "{ascope}食物倍率 ×{0}。",
                Rule(actionType: SkillActionType.AddMult, actionScope: SkillScope.Column, actionValues: new[] { 2f }),
                signed: false);
            Assert.AreEqual("同列所有食物倍率 ×2。", r);
        }

        [Test]
        public void SelfActionScope_UsesSelf()
        {
            string r = SkillDescComposer.ComposeComponent(
                "{ascope}食物倍率 ×{0}。",
                Rule(actionType: SkillActionType.AddMult, actionScope: SkillScope.Self, actionValues: new[] { 3f }),
                signed: false);
            Assert.AreEqual("自身食物倍率 ×3。", r);
        }

        [Test]
        public void ActionScope_IncludesActionCount()
        {
            string r = SkillDescComposer.ComposeComponent(
                "{ascope}食物额外视为 {0} 个食物。",
                Rule(actionType: SkillActionType.AddCountAs, actionScope: SkillScope.Column, actionCount: 2, actionValues: new[] { 2f }),
                signed: false);
            Assert.AreEqual("同列 2 个食物额外视为 2 个食物。", r);
        }

        [Test]
        public void CountAsBase_ShowsTotalFromDelta()
        {
            // 本体「视为N个食物」：actionValue 存 N-1（增量），描述显示总数 N。
            string r = SkillDescComposer.ComposeComponent(
                "视为 {countas} 个食物。",
                Rule(actionType: SkillActionType.AddCountAs, actionScope: SkillScope.Self, actionValues: new[] { 1f }),
                signed: false);
            Assert.AreEqual("视为 2 个食物。", r);
        }

        [Test]
        public void ActionScope_WithCount()
        {
            string r = SkillDescComposer.ComposeComponent(
                "上菜时，将本菜技能甜蜜传递给{ascope}食物。",
                Rule(actionType: SkillActionType.TransferSkills, actionScope: SkillScope.Row, actionCount: 1),
                signed: false);
            Assert.AreEqual("上菜时，将本菜技能甜蜜传递给同行 1 个食物。", r);
        }

        [Test]
        public void ActionScope_AllWhenZeroCount()
        {
            string r = SkillDescComposer.ComposeComponent(
                "上菜时，将本菜技能甜蜜传递给{ascope}食物。",
                Rule(actionType: SkillActionType.TransferSkills, actionScope: SkillScope.Row, actionCount: 0),
                signed: false);
            Assert.AreEqual("上菜时，将本菜技能甜蜜传递给同行所有食物。", r);
        }

        [Test]
        public void Scope_WithSelfSuffix()
        {
            string cond = SkillDescComposer.ComposeComponent(
                "{cscope}每有 1 个食物，分数 {0}。",
                Rule(condType: SkillConditionType.DishCount, condScope: SkillScope.RoundAndSelf, actionValues: new[] { 2f }),
                signed: true);
            Assert.AreEqual("周围及自身每有 1 个食物，分数 +2。", cond);

            string target = SkillDescComposer.ComposeComponent(
                "{ascope}食物分数 {0}。",
                Rule(actionScope: SkillScope.ColumnAndSelf, actionCount: 0, actionValues: new[] { 3f }),
                signed: true);
            Assert.AreEqual("同列及自身所有食物分数 +3。", target);

            string row = SkillDescComposer.ComposeComponent(
                "{cscope}食物达标。",
                Rule(condType: SkillConditionType.DishCount, condScope: SkillScope.RowAndSelf),
                signed: false);
            Assert.AreEqual("同行及自身食物达标。", row);
        }

        [Test]
        public void ActionScope_AllScopeWithCount()
        {
            string r = SkillDescComposer.ComposeComponent(
                "给{ascope}食物。",
                Rule(actionType: SkillActionType.TransferSkills, actionScope: SkillScope.All, actionCount: 2),
                signed: false);
            Assert.AreEqual("给2 个食物。", r);
        }

        [Test]
        public void LegacyTargetTokens_UseUnifiedActionScope()
        {
            string transfer = SkillDescComposer.ComposeComponent(
                "给{targets}。",
                Rule(actionType: SkillActionType.TransferSkills, actionScope: SkillScope.Row, actionCount: 1),
                signed: false);
            Assert.AreEqual("给同行 1 个食物。", transfer);

            string targetPrefix = SkillDescComposer.ComposeComponent(
                "{atargets}倍率 ×{0}。",
                Rule(actionType: SkillActionType.AddMult, actionScope: SkillScope.Column, actionCount: 2, actionValues: new[] { 2f }),
                signed: false);
            Assert.AreEqual("同列 2 个食物倍率 ×2。", targetPrefix);
        }

        [Test]
        public void Tiers_Multiply()
        {
            string r = SkillDescComposer.ComposeComponent(
                "{cscope}食物达 {tiers} 个时，{ascope}食物倍率 ×{tiervals}。",
                Rule(
                    actionType: SkillActionType.AddMult,
                    condType: SkillConditionType.DishCount,
                    condScope: SkillScope.Row,
                    condMode: CountMode.Reach,
                    actionScope: SkillScope.Row,
                    condParam: "tiers:5|15|25",
                    actionParams: new[] { "tiervals:1.5|2.5|5" }),
                signed: false);
            Assert.AreEqual("同行食物达 5/15/25 个时，同行所有食物倍率 ×1.5/2.5/5。", r);
        }

        [Test]
        public void Category_Cake()
        {
            string r = SkillDescComposer.ComposeComponent(
                "将分数加到所有{cat}上（每个{cat} {0} 分）。",
                Rule(actionType: SkillActionType.AddFlat, actionScope: SkillScope.Category, actionValues: new[] { 10f }, actionParams: new[] { "cat:cake" }),
                signed: true);
            Assert.AreEqual("将分数加到所有蛋糕上（每个蛋糕 +10 分）。", r);
        }

        [Test]
        public void Floor_FromMultfloor()
        {
            string r = SkillDescComposer.ComposeComponent(
                "欢乐蛋糕 ×{0} 层，至少 +{floor} 层。",
                Rule(actionType: SkillActionType.AddLayer, actionValues: new[] { 1.5f }, actionParams: new[] { "multfloor:5" }),
                signed: false);
            Assert.AreEqual("欢乐蛋糕 ×1.5 层，至少 +5 层。", r);
        }

        [Test]
        public void ComposeSkill_JoinsWithSeparator()
        {
            string r = SkillDescComposer.ComposeSkill(new[] { "倍率 ×1.5。", "技能额外结算 1 次。" });
            Assert.AreEqual("倍率 ×1.5。；技能额外结算 1 次。", r);
        }

        [Test]
        public void ComposeSkill_SkipsEmptyParts()
        {
            string r = SkillDescComposer.ComposeSkill(new[] { "A", "", null, "B" });
            Assert.AreEqual("A；B", r);
        }

        [Test]
        public void UnknownToken_KeptLiteral()
        {
            string r = SkillDescComposer.ComposeComponent("值 {unknown} 与 {0}", Rule(actionValues: new[] { 7f }), signed: false);
            Assert.AreEqual("值 {unknown} 与 7", r);
        }
    }
}
