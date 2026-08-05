using GourmetProject.Game.Meta;
using GourmetProject.Game.Orchestration;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GourmetProject.Tests.EditMode
{
    public sealed class EventOptionRequirementTests
    {
        [Test]
        public void MinimumFlavoredDishCondition_UsesConfiguredCount()
        {
            var context = new FakePreconditionContext { FlavoredRecipeDishCount = 4 };

            Assert.That(
                PreconditionEvaluator.IsSatisfied(context, "minFlavoredRecipeDish:5"),
                Is.False);

            context.FlavoredRecipeDishCount = 5;

            Assert.That(
                PreconditionEvaluator.IsSatisfied(context, "minFlavoredRecipeDish:5"),
                Is.True);
        }

        [Test]
        public void EventOption_PreservesConfiguredConditionTextVerbatim()
        {
            cfg.EventOption option = CreateOption("minGold:30", "这是策划配置的原文");

            Assert.That(option.ConditionText, Is.EqualTo("这是策划配置的原文"));
            Assert.That(
                WeekLoopController.ResolveConditionText(run: null, option),
                Is.EqualTo("这是策划配置的原文"));
        }

        [Test]
        public void ConditionText_IsIgnoredWhenConditionIsEmpty()
        {
            cfg.EventOption option = CreateOption(string.Empty, "这段文案不应显示");

            Assert.That(
                WeekLoopController.ResolveConditionText(run: null, option),
                Is.Empty);
        }

        [Test]
        public void MissingConditionText_ShowsConfigurationError()
        {
            cfg.EventOption option = CreateOption("hasRecipeDish", string.Empty);
            LogAssert.Expect(
                LogType.Warning,
                "事件选项 test_option 配置了 condition，但未配置 conditionText。事件页将显示配置错误提示。");

            Assert.That(
                WeekLoopController.ResolveConditionText(run: null, option),
                Is.EqualTo("条件文案未配置"));
        }

        private static cfg.EventOption CreateOption(string condition, string conditionText)
        {
            string json = "{"
                + "\"id\":\"test_option\","
                + "\"eventId\":\"test_event\","
                + "\"parentId\":\"\","
                + "\"text\":\"买下这些蘑菇\","
                + "\"resultText\":\"\","
                + $"\"condition\":\"{condition}\","
                + $"\"conditionText\":\"{conditionText}\","
                + "\"effectTypes\":[0],"
                + "\"effectValues\":[0],"
                + "\"effectParams\":[\"-\"],"
                + "\"autoEnd\":false,"
                + "\"branchWeight\":0,"
                + "\"branchPageText\":\"\""
                + "}";
            return cfg.EventOption.DeserializeEventOption(JSON.Parse(json));
        }

        private sealed class FakePreconditionContext : IPreconditionContext
        {
            public int Gold { get; set; }

            public int WeekIndex { get; set; }

            public int ActEventActionCount { get; set; }

            public int RecipeDishCount { get; set; }

            public int FlavoredRecipeDishCount { get; set; }

            public bool HasItem(string itemId) => false;

            public bool HasRecipeDish(bool requireFlavor) => GetRecipeDishCount(requireFlavor) > 0;

            public int GetRecipeDishCount(bool requireFlavor)
            {
                return requireFlavor ? FlavoredRecipeDishCount : RecipeDishCount;
            }

            public int GetEventCounter(string counterId) => 0;
        }
    }
}
