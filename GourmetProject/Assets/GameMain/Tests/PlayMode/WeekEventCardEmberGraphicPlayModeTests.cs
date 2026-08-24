using System.Collections;
using GourmetProject.Game.UI.Meta;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class WeekEventCardEmberGraphicPlayModeTests
    {
        [UnityTest]
        public IEnumerator HotState_SpawnsBoundedParticles_AndClearsWhenStoppedOrDisabled()
        {
            var canvasObject = new GameObject("WeekEventCardEmberTestCanvas", typeof(Canvas));
            var graphicObject = new GameObject(
                "WeekEventCardEmberGraphic",
                typeof(RectTransform),
                typeof(WeekEventCardEmberGraphic));
            graphicObject.transform.SetParent(canvasObject.transform, false);

            WeekEventCardEmberGraphic graphic = graphicObject.GetComponent<WeekEventCardEmberGraphic>();
            graphic.rectTransform.sizeDelta = new Vector2(360f, 540f);

            try
            {
                Assert.That(graphic.ActiveParticleCount, Is.Zero);
                yield return new WaitForSecondsRealtime(0.55f);
                Assert.That(graphic.ActiveParticleCount, Is.Zero);

                graphic.SetHot(true);
                yield return new WaitForSecondsRealtime(0.6f);

                Assert.That(graphic.ActiveParticleCount, Is.GreaterThan(0));
                Assert.That(graphic.ActiveParticleCount, Is.LessThanOrEqualTo(14));

                graphic.SetHot(false);
                Assert.That(graphic.ActiveParticleCount, Is.Zero);

                graphic.SetHot(true);
                yield return new WaitForSecondsRealtime(0.6f);
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
