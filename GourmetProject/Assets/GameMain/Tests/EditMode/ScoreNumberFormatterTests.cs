using BreakInfinity;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ScoreNumberFormatterTests
    {
        [TestCase(0d, "0")]
        [TestCase(9d, "9")]
        [TestCase(1234d, "1234")]
        [TestCase(9_999_999d, "9999999")]
        public void Format_SevenDigitsOrFewer_KeepsFullInteger(double value, string expected)
        {
            Assert.That(ScoreNumberFormatter.Format(value), Is.EqualTo(expected));
        }

        [Test]
        public void Format_EightDigits_UsesScientificNotation()
        {
            Assert.That(ScoreNumberFormatter.Format(10_000_000d), Is.EqualTo("1e7"));
            Assert.That(ScoreNumberFormatter.Format(12_300_000d), Is.EqualTo("1.23e7"));
        }

        [Test]
        public void FormatNumber_SevenDigitsOrFewer_DoesNotUseGeneralScientific()
        {
            Assert.That(FoodTipUiUtility.FormatNumber(new BigDouble(1234d)), Is.EqualTo("1234"));
            Assert.That(FoodTipUiUtility.FormatNumber(new BigDouble(9_999_999d)), Is.EqualTo("9999999"));
            Assert.That(FoodTipUiUtility.FormatNumber(new BigDouble(1.5d)), Is.EqualTo("1.5"));
        }

        [Test]
        public void FormatNumber_EightDigits_UsesScientificNotation()
        {
            Assert.That(
                FoodTipUiUtility.FormatNumber(new BigDouble(10_000_000d)),
                Is.EqualTo("1e7"));
        }
    }
}
