using GameStartStudio.UI;
using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Meta;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class WeekEventCardPresentationTests
    {
        private const string PrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Meta/Events/WeekEventCardView.prefab";

        [TestCase(cfg.FoodActionKind.Normal, TmpTextAnimationPreset.Food, false, "4E2515")]
        [TestCase(cfg.FoodActionKind.Super, TmpTextAnimationPreset.HotFood, true, "C84A22")]
        [TestCase(cfg.FoodActionKind.Feast, TmpTextAnimationPreset.Boss, false, "4E2515")]
        public void FoodPresentation_UsesExpectedTitleAndHotState(
            cfg.FoodActionKind foodKind,
            TmpTextAnimationPreset expectedPreset,
            bool expectedHot,
            string expectedTitleColor)
        {
            WeekEventCardPresentation presentation = WeekEventCardView.ResolvePresentation(
                ActionDisplayKind.Food,
                foodKind);

            Assert.That(presentation.TitleAnimation.Preset, Is.EqualTo(expectedPreset));
            Assert.That(presentation.IsHotBusiness, Is.EqualTo(expectedHot));
            Assert.That(ColorUtility.ToHtmlStringRGB(presentation.TitleColor), Is.EqualTo(expectedTitleColor));
        }

        [Test]
        public void NonFoodPresentation_ResetsHotTitleState()
        {
            WeekEventCardPresentation presentation = WeekEventCardView.ResolvePresentation(
                ActionDisplayKind.Event);

            Assert.That(presentation.IsHotBusiness, Is.False);
            Assert.That(ColorUtility.ToHtmlStringRGB(presentation.TitleColor), Is.EqualTo("4E2515"));
        }

        [Test]
        public void Prefab_HasNonBlockingEmberGraphicOnParticlesContainer()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);

            Transform particles = prefab.transform.Find("Particles");
            Assert.That(particles, Is.Not.Null);

            Assert.That(particles.GetComponent<CanvasRenderer>(), Is.Not.Null);
            WeekEventCardEmberGraphic embers = particles.GetComponent<WeekEventCardEmberGraphic>();
            Assert.That(embers, Is.Not.Null);
            Assert.That(embers.raycastTarget, Is.False);

            var serializedEmbers = new SerializedObject(embers);
            Assert.That(serializedEmbers.FindProperty("_maxParticles").intValue, Is.EqualTo(14));
            Assert.That(
                serializedEmbers.FindProperty("_spawnInterval").vector2Value,
                Is.EqualTo(new Vector2(0.18f, 0.28f)));
        }
    }
}
