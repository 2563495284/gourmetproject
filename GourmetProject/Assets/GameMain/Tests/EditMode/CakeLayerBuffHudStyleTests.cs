#if UNITY_EDITOR
using GourmetProject.Game.UI.Battle.View;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools.Utils;

namespace GourmetProject.Tests.EditMode
{
    public sealed class CakeLayerBuffHudStyleTests
    {
        [Test]
        public void LayerCountStyle_EnablesWhiteFaceWithBlackOutlineAtRuntime()
        {
            var textObject = new GameObject(
                "CakeLayerCountStyleTest",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            Material seedMaterial = null;
            Material configuredMaterial = null;
            try
            {
                var text = textObject.GetComponent<TextMeshProUGUI>();
                TMP_FontAsset font = Resources.Load<TMP_FontAsset>(
                    "Fonts/AlimamaShuHeiTi-Bold SDF");
                Assert.That(font, Is.Not.Null);

                seedMaterial = new Material(font.material);
                text.font = font;
                text.fontSharedMaterial = seedMaterial;

                CakeLayerBuffHud.ApplyCountTextStyle(text);

                configuredMaterial = text.fontMaterial;
                Assert.That(
                    configuredMaterial.IsKeywordEnabled(ShaderUtilities.Keyword_Outline),
                    Is.True);
                Assert.That(
                    configuredMaterial.GetFloat(ShaderUtilities.ID_OutlineWidth),
                    Is.EqualTo(0.45f).Within(0.0001f));
                Assert.That(
                    configuredMaterial.GetColor(ShaderUtilities.ID_FaceColor),
                    Is.EqualTo(Color.white).Using(ColorEqualityComparer.Instance));
                Assert.That(
                    configuredMaterial.GetColor(ShaderUtilities.ID_OutlineColor),
                    Is.EqualTo(Color.black).Using(ColorEqualityComparer.Instance));

                Outline graphicOutline = text.GetComponent<Outline>();
                Assert.That(graphicOutline, Is.Not.Null);
                Assert.That(
                    graphicOutline.effectColor,
                    Is.EqualTo(Color.black).Using(ColorEqualityComparer.Instance));
                Assert.That(
                    graphicOutline.effectDistance,
                    Is.EqualTo(new Vector2(1.5f, -1.5f)));
            }
            finally
            {
                Object.DestroyImmediate(textObject);
                if (configuredMaterial != null && configuredMaterial != seedMaterial)
                {
                    Object.DestroyImmediate(configuredMaterial);
                }
                if (seedMaterial != null)
                {
                    Object.DestroyImmediate(seedMaterial);
                }
            }
        }
    }
}
#endif
