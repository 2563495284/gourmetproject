using GourmetProject.Core.Rng;
using NUnit.Framework;

namespace GourmetProject.Tests
{
    /// <summary>
    /// 随机系统的可复现性测试：同种子逐位相等、命名流互相独立、快照恢复后未来序列一致。
    /// 这是 roguelike "同种子同结果" 的基石。
    /// </summary>
    public class RngDeterminismTests
    {
        [Test]
        public void Xoshiro_SameSeed_IsBitwiseIdentical()
        {
            var a = new Xoshiro256SS(0xC0FFEEUL);
            var b = new Xoshiro256SS(0xC0FFEEUL);

            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(a.NextULong(), b.NextULong(), $"streams diverged at draw {i}");
            }
        }

        [Test]
        public void RandomService_SameSeedText_ProducesIdenticalStreamSequence()
        {
            var s1 = new RandomService();
            s1.Init("balatro-like-seed");
            var s2 = new RandomService();
            s2.Init("balatro-like-seed");

            IRandomStream a = s1.Stream("loot");
            IRandomStream b = s2.Stream("loot");

            for (int i = 0; i < 500; i++)
            {
                Assert.AreEqual(a.NextULong(), b.NextULong(), $"named streams diverged at draw {i}");
            }
        }

        [Test]
        public void RandomService_SameSeed_StreamOrderIndependent()
        {
            // 一条流消耗多少随机数，不应影响另一条流的序列。
            var s1 = new RandomService();
            s1.Init(987654321UL);
            var s2 = new RandomService();
            s2.Init(987654321UL);

            // s1 先大量消耗 "shop" 再读 "enemy"；s2 直接读 "enemy"。
            IRandomStream shop = s1.Stream("shop");
            for (int i = 0; i < 333; i++)
            {
                shop.NextULong();
            }

            IRandomStream enemy1 = s1.Stream("enemy");
            IRandomStream enemy2 = s2.Stream("enemy");
            for (int i = 0; i < 200; i++)
            {
                Assert.AreEqual(enemy2.NextULong(), enemy1.NextULong(), $"enemy stream polluted at draw {i}");
            }
        }

        [Test]
        public void RandomService_DifferentStreamNames_AreUncorrelated()
        {
            var s = new RandomService();
            s.Init(12345UL);
            IRandomStream x = s.Stream("a");
            IRandomStream y = s.Stream("b");

            bool diverged = false;
            for (int i = 0; i < 100; i++)
            {
                if (x.NextULong() != y.NextULong())
                {
                    diverged = true;
                    break;
                }
            }

            Assert.IsTrue(diverged, "two differently-named streams should not produce identical sequences");
        }

        [Test]
        public void RandomService_SnapshotRestore_ReproducesFutureSequence()
        {
            var s = new RandomService();
            s.Init("seed-42");
            IRandomStream stream = s.Stream("loot");

            for (int i = 0; i < 10; i++)
            {
                stream.NextULong();
            }

            RandomSnapshot snapshot = s.Capture();

            ulong[] expected = new ulong[20];
            for (int i = 0; i < expected.Length; i++)
            {
                expected[i] = stream.NextULong();
            }

            var restored = new RandomService();
            restored.Restore(snapshot);
            IRandomStream restoredStream = restored.Stream("loot");

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], restoredStream.NextULong(), $"restored stream diverged at draw {i}");
            }
        }
    }
}
