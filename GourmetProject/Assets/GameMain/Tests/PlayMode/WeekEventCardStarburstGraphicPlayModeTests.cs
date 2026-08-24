using System.Collections;
using GourmetProject.Game.UI.Meta;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class WeekEventCardStarburstGraphicPlayModeTests
    {
        [UnityTest]
        public IEnumerator StarEvaluation_SpawnsMoreThanHotCapacity_AndClearsImmediately()
        {
            var canvasObject = new GameObject("WeekEventCardStarburstTestCanvas", typeof(Canvas));
            var graphicObject = new GameObject(
                "WeekEventCardStarburstGraphic",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(WeekEventCardStarburstGraphic));
            graphicObject.transform.SetParent(canvasObject.transform, false);

            WeekEventCardStarburstGraphic graphic = graphicObject.GetComponent<WeekEventCardStarburstGraphic>();
            graphic.rectTransform.sizeDelta = new Vector2(360f, 540f);

            try
            {
                Assert.That(graphic.ActiveParticleCount, Is.Zero);
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(graphic.ActiveParticleCount, Is.Zero);

                graphic.SetStarEvaluation(true);
                graphic.TriggerBurst();
                graphic.TriggerBurst();
                Assert.That(graphic.ActiveParticleCount, Is.GreaterThan(14));
                Assert.That(graphic.ActiveParticleCount, Is.LessThanOrEqualTo(28));

                yield return new WaitForSecondsRealtime(0.15f);
                Assert.That(graphic.ActiveParticleCount, Is.LessThanOrEqualTo(28));

                graphic.SetStarEvaluation(false);
                Assert.That(graphic.ActiveParticleCount, Is.Zero);

                graphic.SetStarEvaluation(true);
                graphic.TriggerBurst();
                Assert.That(graphic.ActiveParticleCount, Is.GreaterThan(0));

                graphic.enabled = false;
                Assert.That(graphic.ActiveParticleCount, Is.Zero);
            }
            finally
            {
                Object.Destroy(canvasObject);
            }
        }
    }
}
