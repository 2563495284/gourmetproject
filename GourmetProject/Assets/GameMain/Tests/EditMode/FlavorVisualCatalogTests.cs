using System.Collections.Generic;
using GourmetProject.Game.Presentation.Battle;
using NUnit.Framework;
using UnityEngine;

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

        [Test]
        public void FormalOrganicVisual_UsesSharedMaterialAndPerRendererMask()
        {
            var texture = new Texture2D(8, 4);
            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 8f, 4f),
                new Vector2(0.5f, 0.5f),
                8f);
            var gameObject = new GameObject("OrganicFlavorTest");
            SpriteRenderer renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            MaterialPropertyBlock block = null;

            try
            {
                FlavorOrganicVisual.ApplyToSpriteRenderer(
                    renderer,
                    new[] { "t_sour", "t_sour", "unknown", "t_spicy" },
                    ref block,
                    123f,
                    0.72f,
                    useGlobalTime: true);

                Assert.That(renderer.sharedMaterial, Is.SameAs(SpriteRenderStyle.SpriteFlavorOrganicMaterial));
                Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("GourmetProject/FlavorOrganicRegions"));
                Assert.That(renderer.sharedMaterial.shader.isSupported, Is.True);

                var actual = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(actual);
                Assert.That(actual.GetFloat("_FlavorMask"), Is.EqualTo((1 << 1) | (1 << 6)));
                Assert.That(actual.GetFloat("_FlavorCount"), Is.EqualTo(2f));
                Assert.That(actual.GetFloat("_Aspect"), Is.EqualTo(2f).Within(0.001f));
                Assert.That(actual.GetFloat("_UseGlobalTime"), Is.EqualTo(1f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void DigestDissolve_AcceptsOrganicFlavorPropertyBlock()
        {
            Material material = SpriteRenderStyle.DigestDissolveMaterial;

            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader.isSupported, Is.True);
            Assert.That(material.HasProperty("_FlavorMask"), Is.True);
            Assert.That(material.HasProperty("_FlavorCount"), Is.True);
            Assert.That(material.HasProperty("_UseGlobalTime"), Is.True);
            Assert.That(material.HasProperty("_SpriteUvRect"), Is.True);
            Assert.That(material.HasProperty("_StainCount"), Is.False);
        }
    }
}
