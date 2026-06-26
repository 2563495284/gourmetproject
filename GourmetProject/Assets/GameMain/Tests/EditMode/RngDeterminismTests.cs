using GourmetProject.Core.Rng;
using GourmetProject.Game.Run;
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
        public void DomainStream_SameSeedDomainKey_ProducesIdenticalSequence()
        {
            var s1 = new RandomService();
            s1.Init("balatro-like-seed");
            var s2 = new RandomService();
            s2.Init("balatro-like-seed");

            IRandomStream a = s1.DomainStream(SeedDomains.Shop, "w2_d3");
            IRandomStream b = s2.DomainStream(SeedDomains.Shop, "w2_d3");

            for (int i = 0; i < 500; i++)
            {
                Assert.AreEqual(a.NextULong(), b.NextULong(), $"domain streams diverged at draw {i}");
            }
        }

        [Test]
        public void DomainStream_DifferentDomains_SameKey_AreUncorrelated()
        {
            var s = new RandomService();
            s.Init(12345UL);
            IRandomStream shop = s.DomainStream(SeedDomains.Shop, "w1_d1");
            IRandomStream reward = s.DomainStream(SeedDomains.Reward, "w1_d1");

            bool diverged = false;
            for (int i = 0; i < 100; i++)
            {
                if (shop.NextULong() != reward.NextULong())
                {
                    diverged = true;
                    break;
                }
            }

            Assert.IsTrue(diverged, "same key under different domains must not share a sequence");
        }

        [Test]
        public void DomainStream_SameDomain_DifferentKeys_AreUncorrelated()
        {
            var s = new RandomService();
            s.Init(12345UL);
            IRandomStream w1 = s.DomainStream(SeedDomains.Boss, "w1");
            IRandomStream w2 = s.DomainStream(SeedDomains.Boss, "w2");

            bool diverged = false;
            for (int i = 0; i < 100; i++)
            {
                if (w1.NextULong() != w2.NextULong())
                {
                    diverged = true;
                    break;
                }
            }

            Assert.IsTrue(diverged, "different instance keys in one domain must not share a sequence");
        }

        [Test]
        public void DomainStream_DomainsAreOrderIndependent()
        {
            // 一个域消耗多少随机数，不应影响另一个域的序列。
            var s1 = new RandomService();
            s1.Init(987654321UL);
            var s2 = new RandomService();
            s2.Init(987654321UL);

            IRandomStream shop = s1.DomainStream(SeedDomains.Shop, "w1_d1");
            for (int i = 0; i < 333; i++)
            {
                shop.NextULong();
            }

            IRandomStream boss1 = s1.DomainStream(SeedDomains.Boss, "w1");
            IRandomStream boss2 = s2.DomainStream(SeedDomains.Boss, "w1");
            for (int i = 0; i < 200; i++)
            {
                Assert.AreEqual(boss2.NextULong(), boss1.NextULong(), $"boss domain polluted at draw {i}");
            }
        }

        [Test]
        public void DomainStream_LootIsolatedFromShop()
        {
            // 商店道具池(loot)与商店其它随机(shop)必须互相独立：
            // 调整道具数量不会改变同一刷新里的菜品/碎片序列。
            var s = new RandomService();
            s.Init("seed-shop-loot");
            IRandomStream shop = s.DomainStream(SeedDomains.Shop, "w1_d1");
            IRandomStream loot = s.DomainStream(SeedDomains.Loot, "shop_w1_d1");

            bool diverged = false;
            for (int i = 0; i < 100; i++)
            {
                if (shop.NextULong() != loot.NextULong())
                {
                    diverged = true;
                    break;
                }
            }

            Assert.IsTrue(diverged, "shop loot stream must be independent from other shop randomness");
        }

        [Test]
        public void Capture_ExcludesCosmeticDomain()
        {
            var s = new RandomService();
            s.Init("seed-cosmetic");

            IRandomStream gameplay = s.DomainStream(SeedDomains.Combat, "battle_1");
            IRandomStream cosmetic = s.Cosmetic("hit_shake_1");
            gameplay.NextULong();
            cosmetic.NextULong();

            RandomSnapshot snapshot = s.Capture();

            foreach (string key in snapshot.Streams.Keys)
            {
                Assert.IsFalse(
                    key.StartsWith(SeedDomains.Cosmetic + "\u0001", System.StringComparison.Ordinal),
                    "cosmetic streams must not be captured into the save snapshot");
            }
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

        [Test]
        public void RunSaveData_CanCarryRandomSnapshot()
        {
            var s = new RandomService();
            s.Init("save-rng");
            IRandomStream stream = s.DomainStream(SeedDomains.Shop, "w1_d1");
            for (int i = 0; i < 7; i++)
            {
                stream.NextULong();
            }

            var data = new RunSaveData
            {
                SeedText = s.SeedText,
                RandomSnapshot = s.Capture(),
            };

            ulong expected = stream.NextULong();
            var restored = new RandomService();
            restored.Restore(data.RandomSnapshot);

            Assert.AreEqual(expected, restored.DomainStream(SeedDomains.Shop, "w1_d1").NextULong());
        }
    }
}
