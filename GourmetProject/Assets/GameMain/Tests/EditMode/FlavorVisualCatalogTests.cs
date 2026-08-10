using System.Collections.Generic;
using GourmetProject.Game.Presentation.Battle;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class FlavorVisualCatalogTests
    {
        [Test]
        public void Descriptors_UseStableSevenFlavorOrder()
        {
            string[] expected =
            {
                "t_sweet",
                "t_sour",
                "t_bitter",
                "t_salty",
                "t_numb",
                "t_rust",
                "t_spicy",
            };

            Assert.That(FlavorVisualCatalog.Descriptors.Count, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                FlavorVisualDescriptor descriptor = FlavorVisualCatalog.Descriptors[i];
                Assert.That(descriptor.Id, Is.EqualTo(expected[i]));
                Assert.That((int)descriptor.Kind, Is.EqualTo(i));
                Assert.That(descriptor.Bit, Is.EqualTo(1 << i));
            }
        }

        [Test]
        public void BuildMask_IgnoresUnknownAndDeduplicatesRepeatedFlavor()
        {
            var ids = new[]
            {
                "t_sour",
                "unknown",
                "t_sweet",
                "t_sour",
                null,
                "t_spicy",
            };

            int mask = FlavorVisualCatalog.BuildMask(ids);

            Assert.That(mask, Is.EqualTo((1 << 0) | (1 << 1) | (1 << 6)));
            Assert.That(FlavorVisualCatalog.CountBits(mask), Is.EqualTo(3));
        }

        [Test]
        public void FlavorIdsForMask_ReturnsCanonicalOrder()
        {
            int mask = (1 << 6) | (1 << 3) | (1 << 0) | (1 << 5);

            List<string> ids = FlavorVisualCatalog.FlavorIdsForMask(mask);

            Assert.That(ids, Is.EqualTo(new[] { "t_sweet", "t_salty", "t_rust", "t_spicy" }));
        }

        [Test]
        public void AllMask_ContainsEveryKnownFlavor()
        {
            var ids = new List<string>();
            foreach (FlavorVisualDescriptor descriptor in FlavorVisualCatalog.Descriptors)
            {
                ids.Add(descriptor.Id);
            }

            Assert.That(FlavorVisualCatalog.BuildMask(ids), Is.EqualTo(FlavorVisualCatalog.AllMask));
            Assert.That(FlavorVisualCatalog.CountBits(FlavorVisualCatalog.AllMask), Is.EqualTo(7));
        }
    }
}
