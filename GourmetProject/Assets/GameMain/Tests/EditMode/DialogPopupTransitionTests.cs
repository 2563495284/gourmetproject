#if UNITY_EDITOR
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Meta;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class DialogPopupTransitionTests
    {
        private const string RewardFormPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Meta/Rewards/RewardForm.prefab";

        [TestCase(false, 0f)]
        [TestCase(true, 1f)]
        public void PreparePopupLayers_SeparatesBackdropFromContent(
            bool isHandoffArrival,
            float expectedBackdropAlpha)
        {
            var backdropObject = new GameObject("Backdrop", typeof(CanvasGroup));
            var contentObject = new GameObject("Content", typeof(CanvasGroup));
            CanvasGroup backdrop = backdropObject.GetComponent<CanvasGroup>();
            CanvasGroup content = contentObject.GetComponent<CanvasGroup>();

            try
            {
                UITransition.PreparePopupLayers(backdrop, content, isHandoffArrival);

                Assert.That(backdrop.alpha, Is.EqualTo(expectedBackdropAlpha));
                Assert.That(backdrop.interactable, Is.True);
                Assert.That(backdrop.blocksRaycasts, Is.True);
                Assert.That(content.alpha, Is.Zero);
                Assert.That(content.interactable, Is.False);
                Assert.That(content.blocksRaycasts, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(contentObject);
                Object.DestroyImmediate(backdropObject);
            }
        }

        [Test]
        public void RewardFormPrefab_HandoffEdgesCompleteDimOutsideSpan()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RewardFormPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            RectTransform dim = prefab.transform.Find("Dim") as RectTransform;
            RectTransform shade = prefab.transform.Find("HandoffEdgeShade") as RectTransform;
            Assert.That(dim, Is.Not.Null);
            Assert.That(shade, Is.Not.Null);
            Assert.That(shade.GetSiblingIndex(), Is.LessThan(dim.GetSiblingIndex()));

            CanvasGroup group = shade.GetComponent<CanvasGroup>();
            Assert.That(group, Is.Not.Null);
            Assert.That(shade.gameObject.activeSelf, Is.False);
            Assert.That(group.alpha, Is.Zero);
            Assert.That(group.interactable, Is.False);
            Assert.That(group.blocksRaycasts, Is.False);

            RectTransform left = shade.Find("Left") as RectTransform;
            RectTransform right = shade.Find("Right") as RectTransform;
            Assert.That(left, Is.Not.Null);
            Assert.That(right, Is.Not.Null);
            AssertEdgeRect(left, Vector2.zero, new Vector2(dim.anchorMin.x, 1f));
            AssertEdgeRect(right, new Vector2(dim.anchorMax.x, 0f), Vector2.one);

            Image dimImage = dim.GetComponent<Image>();
            Image leftImage = left.GetComponent<Image>();
            Image rightImage = right.GetComponent<Image>();
            Assert.That(dimImage, Is.Not.Null);
            Assert.That(leftImage, Is.Not.Null);
            Assert.That(rightImage, Is.Not.Null);
            Assert.That(leftImage.color, Is.EqualTo(dimImage.color));
            Assert.That(rightImage.color, Is.EqualTo(dimImage.color));
            Assert.That(leftImage.raycastTarget, Is.True);
            Assert.That(rightImage.raycastTarget, Is.True);

            var serializedForm = new SerializedObject(prefab.GetComponent<RewardForm>());
            Assert.That(
                serializedForm.FindProperty("_handoffEdgeGroup").objectReferenceValue,
                Is.EqualTo(group));
            Assert.That(
                serializedForm.FindProperty("_transitionSettings.HandoffEdgeFade").floatValue,
                Is.EqualTo(0.18f).Within(0.001f));
        }

        private static void AssertEdgeRect(
            RectTransform rect,
            Vector2 expectedAnchorMin,
            Vector2 expectedAnchorMax)
        {
            Assert.That(rect.anchorMin, Is.EqualTo(expectedAnchorMin));
            Assert.That(rect.anchorMax, Is.EqualTo(expectedAnchorMax));
            Assert.That(rect.offsetMin, Is.EqualTo(Vector2.zero));
            Assert.That(rect.offsetMax, Is.EqualTo(Vector2.zero));
        }
    }
}
#endif
