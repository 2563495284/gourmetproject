using GameStartStudio.UI;
using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Meta;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class WeekEventCardPresentationTests
    {
        private const string PrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Meta/Events/WeekEventCardView.prefab";
        private const string StarBodyPath =
            "Assets/GameMain/Content/Resources/Sprites/UI/WeekEventCards/card_choice_star_event_body.png";
        private const string StarTitlePath =
            "Assets/GameMain/Content/Resources/Sprites/UI/WeekEventCards/card_choice_star_node_title.png";
        private const string StarFooterPath =
            "Assets/GameMain/Content/Resources/Sprites/UI/WeekEventCards/card_choice_star_node_footer.png";

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
            Assert.That(presentation.IsStarEvaluation, Is.False);
            Assert.That(ColorUtility.ToHtmlStringRGB(presentation.TitleColor), Is.EqualTo("4E2515"));
        }

        [Test]
        public void StarEvaluationPresentation_IsStrongerAndDistinctFromHotBusiness()
        {
            WeekEventCardPresentation hot = WeekEventCardView.ResolvePresentation(
                ActionDisplayKind.Food,
                cfg.FoodActionKind.Super);
            WeekEventCardPresentation star = WeekEventCardView.ResolvePresentation(
                ActionDisplayKind.Boss,
                cfg.FoodActionKind.Feast);

            Assert.That(star.IsStarEvaluation, Is.True);
            Assert.That(star.IsHotBusiness, Is.False);
            Assert.That(star.TitleAnimation.Preset, Is.EqualTo(TmpTextAnimationPreset.StarEvaluation));
            Assert.That(star.TitleAnimation.Intensity, Is.GreaterThan(hot.TitleAnimation.Intensity));
            Assert.That(star.TitleAnimation.Speed, Is.GreaterThan(hot.TitleAnimation.Speed));
            Assert.That(ColorUtility.ToHtmlStringRGB(star.TitleColor), Is.EqualTo("F3D38A"));
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

            Transform starEffects = particles.Find("StarEffects");
            Assert.That(starEffects, Is.Not.Null);
            Assert.That(starEffects.GetComponent<CanvasRenderer>(), Is.Not.Null);
            WeekEventCardStarburstGraphic starburst = starEffects.GetComponent<WeekEventCardStarburstGraphic>();
            Assert.That(starburst, Is.Not.Null);
            Assert.That(starburst.raycastTarget, Is.False);

            var serializedStarburst = new SerializedObject(starburst);
            Assert.That(serializedStarburst.FindProperty("_maxParticles").intValue, Is.EqualTo(28));
            Assert.That(
                serializedStarburst.FindProperty("_spawnInterval").vector2Value,
                Is.EqualTo(new Vector2(0.07f, 0.12f)));
            Assert.That(
                serializedStarburst.FindProperty("_maxParticles").intValue,
                Is.GreaterThan(serializedEmbers.FindProperty("_maxParticles").intValue));
        }

        [Test]
        public void StarEvaluationBinding_UsesDedicatedNineSlicedCardArtwork()
        {
            AssertNineSlice(StarBodyPath, new Vector4(40f, 40f, 40f, 40f));
            AssertNineSlice(StarTitlePath, new Vector4(48f, 12f, 48f, 12f));
            AssertNineSlice(StarFooterPath, new Vector4(40f, 12f, 40f, 12f));

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                WeekEventCardView view = instance.GetComponent<WeekEventCardView>();
                view.BindBossNode(null, "高压评鉴", 52000, null);

                var serializedView = new SerializedObject(view);
                var body = serializedView.FindProperty("_cardBackingImage").objectReferenceValue as Image;
                var title = serializedView.FindProperty("_titleBackingImage").objectReferenceValue as Image;
                var footer = serializedView.FindProperty("_footerBackingImage").objectReferenceValue as Image;

                Assert.That(AssetDatabase.GetAssetPath(body.sprite), Is.EqualTo(StarBodyPath));
                Assert.That(AssetDatabase.GetAssetPath(title.sprite), Is.EqualTo(StarTitlePath));
                Assert.That(AssetDatabase.GetAssetPath(footer.sprite), Is.EqualTo(StarFooterPath));
                Assert.That(body.type, Is.EqualTo(Image.Type.Sliced));
                Assert.That(title.type, Is.EqualTo(Image.Type.Sliced));
                Assert.That(footer.type, Is.EqualTo(Image.Type.Sliced));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void AssertNineSlice(string path, Vector4 expectedBorder)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Assert.That(sprite, Is.Not.Null, path);
            Assert.That(sprite.border, Is.EqualTo(expectedBorder), path);
        }
    }
}
