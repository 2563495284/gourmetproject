using GourmetProject.Game.UI.Battle;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BattleFormTransitionPrefabTests
    {
        private const string PrefabPath = "Assets/GameMain/Content/Prefabs/UI/BattleForm.prefab";

        [Test]
        public void CenterCover_IsBlackAndBetweenTopAxisAndPersistentColumns()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);

            Transform cover = FindDescendant(prefab.transform, "CenterTransitionCover");
            Transform axis = FindDescendant(prefab.transform, "TopAxis");
            Transform left = FindDescendant(prefab.transform, "LeftColumn");
            Transform right = FindDescendant(prefab.transform, "RightColumn");

            Assert.That(cover, Is.Not.Null);
            Assert.That(axis, Is.Not.Null);
            Assert.That(left, Is.Not.Null);
            Assert.That(right, Is.Not.Null);
            Assert.That(cover.parent, Is.EqualTo(axis.parent));
            Assert.That(cover.parent, Is.EqualTo(left.parent));
            Assert.That(cover.parent, Is.EqualTo(right.parent));
            Assert.That(cover.GetSiblingIndex(), Is.GreaterThan(axis.GetSiblingIndex()));
            Assert.That(cover.GetSiblingIndex(), Is.LessThan(left.GetSiblingIndex()));
            Assert.That(cover.GetSiblingIndex(), Is.LessThan(right.GetSiblingIndex()));

            Image image = cover.GetComponent<Image>();
            RectTransform rect = cover as RectTransform;
            Assert.That(image, Is.Not.Null);
            Assert.That(image.color, Is.EqualTo(Color.black));
            Assert.That(image.raycastTarget, Is.True);
            Assert.That(rect.anchorMin.x, Is.EqualTo(0.165f).Within(0.0001f));
            Assert.That(rect.anchorMax.x, Is.EqualTo(0.835f).Within(0.0001f));
        }

        [Test]
        public void TransitionTiming_UsesDeliberateBlackHold()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            BattleForm form = prefab != null ? prefab.GetComponent<BattleForm>() : null;
            Assert.That(form, Is.Not.Null);

            var serialized = new SerializedObject(form);
            SerializedProperty settings = serialized.FindProperty("_pageTransitionSettings");
            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.FindPropertyRelative("CenterFadeOut").floatValue, Is.EqualTo(0.12f).Within(0.0001f));
            Assert.That(settings.FindPropertyRelative("CenterFadeIn").floatValue, Is.EqualTo(0.18f).Within(0.0001f));
            Assert.That(settings.FindPropertyRelative("CoverDuration").floatValue, Is.EqualTo(0.16f).Within(0.0001f));
            Assert.That(settings.FindPropertyRelative("CoveredHoldDuration").floatValue, Is.EqualTo(0.03f).Within(0.0001f));
            Assert.That(settings.FindPropertyRelative("RevealDuration").floatValue, Is.EqualTo(0.24f).Within(0.0001f));
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDescendant(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
