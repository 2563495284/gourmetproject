using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Gameplay.Tests
{
    [TestFixture]
    public class TagDescFormatterTests
    {
        [Test]
        public void Format_Plain_PositiveHasNoSign()
        {
            string result = TagDescFormatter.Format("分数 ×{0}。", new float[] { 1.5f });
            Assert.AreEqual("分数 ×1.5。", result);
        }

        [Test]
        public void Format_Plain_TrimsTrailingZeros()
        {
            string result = TagDescFormatter.Format("×{0}", new float[] { 2f });
            Assert.AreEqual("×2", result);
        }

        [Test]
        public void Format_Signed_PositiveGetsPlus()
        {
            string result = TagDescFormatter.Format("结算时美味度 {0}。", new float[] { 5f }, signed: true);
            Assert.AreEqual("结算时美味度 +5。", result);
        }

        [Test]
        public void Format_Signed_NegativeKeepsMinus()
        {
            string result = TagDescFormatter.Format("美味度 {0}。", new float[] { -2f }, signed: true);
            Assert.AreEqual("美味度 -2。", result);
        }

        [Test]
        public void Format_Signed_ZeroHasNoSign()
        {
            string result = TagDescFormatter.Format("美味度 {0}。", new float[] { 0f }, signed: true);
            Assert.AreEqual("美味度 0。", result);
        }

        [Test]
        public void Format_Signed_MultipleValues()
        {
            string result = TagDescFormatter.Format("美味度{0}，价格{1}", new float[] { -2f, 5f }, signed: true);
            Assert.AreEqual("美味度-2，价格+5", result);
        }

        [Test]
        public void Format_IndexOutOfRange_KeepsLiteral()
        {
            string result = TagDescFormatter.Format("值{0}与{1}", new float[] { 7f });
            Assert.AreEqual("值7与{1}", result);
        }

        [Test]
        public void Format_EmptyTemplate_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, TagDescFormatter.Format("", new float[] { 1f }));
            Assert.AreEqual(string.Empty, TagDescFormatter.Format(null, new float[] { 1f }));
        }

        [Test]
        public void Format_NoValues_ReturnsTemplateUnchanged()
        {
            Assert.AreEqual("占位测试{0}", TagDescFormatter.Format("占位测试{0}", null));
            Assert.AreEqual("占位测试{0}", TagDescFormatter.Format("占位测试{0}", new float[0]));
        }
    }
}
