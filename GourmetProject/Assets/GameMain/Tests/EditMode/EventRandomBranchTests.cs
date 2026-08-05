using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta;
using Luban.SimpleJSON;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class EventRandomBranchTests
    {
        [Test]
        public void TwentyEightyWeights_UseRelativeProbability()
        {
            var children = new List<cfg.EventOption>
            {
                CreateOption("branch_twenty", 20f, "发现一袋金币"),
                CreateOption("branch_eighty", 80f, "发现一锅蘑菇"),
            };
            var rng = new Xoshiro256SS(20260805UL);
            int firstCount = 0;

            for (int i = 0; i < 10000; i++)
            {
                Assert.That(EventService.TryRollWeightedChild(children, rng, out cfg.EventOption selected), Is.True);
                if (selected.Id == "branch_twenty")
                {
                    firstCount++;
                }
            }

            Assert.That(firstCount, Is.InRange(1800, 2200));
        }

        [Test]
        public void AllZeroWeights_KeepManualChildSelection()
        {
            var children = new List<cfg.EventOption>
            {
                CreateOption("branch_a", 0f, "正文甲"),
                CreateOption("branch_b", 0f, "正文乙"),
            };

            Assert.That(
                EventService.TryRollWeightedChild(children, new Xoshiro256SS(1UL), out cfg.EventOption selected),
                Is.False);
            Assert.That(selected, Is.Null);
        }

        [Test]
        public void ZeroWeightSibling_IsNeverSelectedInRandomGroup()
        {
            var children = new List<cfg.EventOption>
            {
                CreateOption("branch_zero", 0f, "不会出现"),
                CreateOption("branch_only", 5f, "必定出现"),
            };
            var rng = new Xoshiro256SS(2UL);

            for (int i = 0; i < 100; i++)
            {
                Assert.That(EventService.TryRollWeightedChild(children, rng, out cfg.EventOption selected), Is.True);
                Assert.That(selected.Id, Is.EqualTo("branch_only"));
            }
        }

        [Test]
        public void EventOption_PreservesRandomBranchPageText()
        {
            cfg.EventOption option = CreateOption("branch_text", 20f, "只有这个随机结果会显示的正文");

            Assert.That(option.BranchWeight, Is.EqualTo(20f));
            Assert.That(option.BranchPageText, Is.EqualTo("只有这个随机结果会显示的正文"));
            Assert.That(option.Text, Is.EqualTo("随机结果按钮"));
            Assert.That(option.ResultText, Is.EqualTo("点击按钮后的结果"));
        }

        private static cfg.EventOption CreateOption(string id, float branchWeight, string branchPageText)
        {
            string json = "{"
                + $"\"id\":\"{id}\","
                + "\"eventId\":\"test_event\","
                + "\"parentId\":\"parent_option\","
                + "\"text\":\"随机结果按钮\","
                + "\"resultText\":\"点击按钮后的结果\","
                + "\"condition\":\"\","
                + "\"conditionText\":\"\","
                + "\"effectTypes\":[0],"
                + "\"effectValues\":[0],"
                + "\"effectParams\":[\"-\"],"
                + "\"autoEnd\":false,"
                + $"\"branchWeight\":{branchWeight.ToString(System.Globalization.CultureInfo.InvariantCulture)},"
                + $"\"branchPageText\":\"{branchPageText}\""
                + "}";
            return cfg.EventOption.DeserializeEventOption(JSON.Parse(json));
        }
    }
}
