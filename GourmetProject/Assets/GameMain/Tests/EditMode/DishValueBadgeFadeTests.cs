#if UNITY_EDITOR
using GourmetProject.Game.Presentation.Battle;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class DishValueBadgeFadeTests
    {
        private const string BadgePrefabPath =
            "Assets/GameMain/Content/Prefabs/Battle/Dishes/DishValueBadge.prefab";

        [Test]
        public void SetAlpha_FadesWholeBadge_AndRestoresOriginalRendererAlphas()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BadgePrefabPath);
            Assert.That(prefab, Is.Not.Null);

            GameObject instanceObject = Object.Instantiate(prefab);
            try
            {
                DishValueBadgeView badge = instanceObject.GetComponent<DishValueBadgeView>();
                SpriteRenderer[] sprites = instanceObject.GetComponentsInChildren<SpriteRenderer>(true);
                TextMeshPro valueText = instanceObject.GetComponentInChildren<TextMeshPro>(true);
                Assert.That(badge, Is.Not.Null);
                Assert.That(sprites, Has.Length.GreaterThan(0));
                Assert.That(valueText, Is.Not.Null);

                float[] originalSpriteAlphas = new float[sprites.Length];
                for (int i = 0; i < sprites.Length; i++)
                {
                    originalSpriteAlphas[i] = sprites[i].color.a;
                }

                float originalTextAlpha = valueText.color.a;
                badge.SetAlpha(0.35f);

                for (int i = 0; i < sprites.Length; i++)
                {
                    Assert.That(
                        sprites[i].color.a,
                        Is.EqualTo(originalSpriteAlphas[i] * 0.35f).Within(0.001f));
                }

                Assert.That(
                    valueText.color.a,
                    Is.EqualTo(originalTextAlpha * 0.35f).Within(0.001f));

                badge.SetAlpha(1f);
                for (int i = 0; i < sprites.Length; i++)
                {
                    Assert.That(
                        sprites[i].color.a,
                        Is.EqualTo(originalSpriteAlphas[i]).Within(0.001f));
                }

                Assert.That(valueText.color.a, Is.EqualTo(originalTextAlpha).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(instanceObject);
            }
        }
    }
}
#endif
