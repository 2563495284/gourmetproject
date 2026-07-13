using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Gameplay.Tests
{
    [TestFixture]
    public class EffectDescFormatterTests
    {
        [Test]
        public void Format_Plain_PositiveHasNoSign()
        {
            string result = EffectDescFormatter.Format("分数 ×{0}。", new float[] { 1.5f });
            Assert.AreEqual("分数 ×1.5。", result);
        }

        [Test]
        public void Format_Plain_TrimsTrailingZeros()
        {
            string result = EffectDescFormatter.Format("×{0}", new float[] { 2f });
            Assert.AreEqual("×2", result);
        }

        [Test]
        public void Format_Signed_PositiveGetsPlus()
        {
            string result = EffectDescFormatter.Format("结算时美味度 {0}。", new float[] { 5f }, signed: true);
            Assert.AreEqual("结算时美味度 +5。", result);
        }

        [Test]
        public void Format_Signed_NegativeKeepsMinus()
        {
            string result = EffectDescFormatter.Format("美味度 {0}。", new float[] { -2f }, signed: true);
            Assert.AreEqual("美味度 -2。", result);
        }

        [Test]
        public void Format_Signed_ZeroHasNoSign()
        {
            string result = EffectDescFormatter.Format("美味度 {0}。", new float[] { 0f }, signed: true);
            Assert.AreEqual("美味度 0。", result);
        }

        [Test]
        public void Format_Signed_MultipleValues()
        {
            string result = EffectDescFormatter.Format("美味度{0}，价格{1}", new float[] { -2f, 5f }, signed: true);
            Assert.AreEqual("美味度-2，价格+5", result);
        }

        [Test]
        public void Format_IndexOutOfRange_KeepsLiteral()
        {
            string result = EffectDescFormatter.Format("值{0}与{1}", new float[] { 7f });
            Assert.AreEqual("值7与{1}", result);
        }

        [Test]
        public void Format_EmptyTemplate_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, EffectDescFormatter.Format("", new float[] { 1f }));
            Assert.AreEqual(string.Empty, EffectDescFormatter.Format(null, new float[] { 1f }));
        }

        [Test]
        public void Format_NoValues_ReturnsTemplateUnchanged()
        {
            Assert.AreEqual("占位测试{0}", EffectDescFormatter.Format("占位测试{0}", null));
            Assert.AreEqual("占位测试{0}", EffectDescFormatter.Format("占位测试{0}", new float[0]));
        }
    }
}
