using GourmetProject.Game.Meta;
using Luban.SimpleJSON;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class PassiveItemQualityLuckTests
    {
        [Test]
        public void QualityWeight_ClampsComputedWeightToConfiguredRange()
        {
            cfg.ItemQualityLuck epic = ParseQuality(
                cfg.ItemQuality.Epic,
                baseWeight: 10f,
                luckGrowth: 0.08f,
                constant: 0f,
                minWeight: 5f,
                maxWeight: 20f);

            Assert.That(PassiveItemRandomService.QualityWeight(epic, 90f), Is.EqualTo(20d));
            Assert.That(PassiveItemRandomService.QualityWeight(epic, 0f), Is.EqualTo(10d));
        }

        [Test]
        public void QualityWeight_AddsConstantThenClamps()
        {
            cfg.ItemQualityLuck rare = ParseQuality(
                cfg.ItemQuality.Rare,
                baseWeight: 10f,
                luckGrowth: 0f,
                constant: 3f,
                minWeight: 0f,
                maxWeight: 100f);

            Assert.That(PassiveItemRandomService.QualityWeight(rare, 40f), Is.EqualTo(13d));
        }

        [Test]
        public void QualityWeight_ZeroWhenClampedWeightIsNotPositive()
        {
            cfg.ItemQualityLuck common = ParseQuality(
                cfg.ItemQuality.Common,
                baseWeight: 10f,
                luckGrowth: 0f,
                constant: -20f,
                minWeight: -5f,
                maxWeight: 100f);

            Assert.That(PassiveItemRandomService.QualityWeight(common, 0f), Is.EqualTo(0d));
        }

        [Test]
        public void QualityWeight_AcceptsSwappedMinMaxWeight()
        {
            cfg.ItemQualityLuck uncommon = ParseQuality(
                cfg.ItemQuality.Uncommon,
                baseWeight: 1f,
                luckGrowth: 0f,
                constant: 0f,
                minWeight: 80f,
                maxWeight: 20f);

            Assert.That(PassiveItemRandomService.QualityWeight(uncommon, 0f), Is.EqualTo(20d));
        }

        private static cfg.ItemQualityLuck ParseQuality(
            cfg.ItemQuality quality,
            float baseWeight,
            float luckGrowth,
            float constant,
            float minWeight,
            float maxWeight)
        {
            string json =
                "{"
                + $"\"quality\":{(int)quality},"
                + $"\"baseWeight\":{baseWeight},"
                + $"\"luckGrowth\":{luckGrowth},"
                + $"\"constant\":{constant},"
                + $"\"minWeight\":{minWeight},"
                + $"\"maxWeight\":{maxWeight}"
                + "}";
            return new cfg.ItemQualityLuck(JSON.Parse(json));
        }
    }
}
