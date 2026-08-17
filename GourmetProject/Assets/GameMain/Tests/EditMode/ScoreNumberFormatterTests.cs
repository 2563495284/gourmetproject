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
        [TestCase(999_999_999d, "999999999")]
        public void Format_NineDigitsOrFewer_KeepsFullInteger(double value, string expected)
        {
            Assert.That(ScoreNumberFormatter.Format(value), Is.EqualTo(expected));
        }

        [Test]
        public void Format_TenDigits_UsesScientificNotation()
        {
            Assert.That(ScoreNumberFormatter.Format(1_000_000_000d), Is.EqualTo("1e9"));
            Assert.That(ScoreNumberFormatter.Format(1_230_000_000d), Is.EqualTo("1.23e9"));
        }

        [Test]
        public void FormatNumber_NineDigitsOrFewer_DoesNotUseGeneralScientific()
        {
            Assert.That(FoodTipUiUtility.FormatNumber(new BigDouble(1234d)), Is.EqualTo("1234"));
            Assert.That(FoodTipUiUtility.FormatNumber(new BigDouble(999_999_999d)), Is.EqualTo("999999999"));
            Assert.That(FoodTipUiUtility.FormatNumber(new BigDouble(1.5d)), Is.EqualTo("1.5"));
        }

        [Test]
        public void FormatNumber_TenDigits_UsesScientificNotation()
        {
            Assert.That(
                FoodTipUiUtility.FormatNumber(new BigDouble(1_000_000_000d)),
                Is.EqualTo("1e9"));
        }
    }
}
