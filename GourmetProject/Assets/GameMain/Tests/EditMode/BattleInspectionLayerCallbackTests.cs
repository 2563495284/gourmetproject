using GourmetProject.Game.UI.Battle.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BattleInspectionLayerCallbackTests
    {
        [Test]
        public void ForceHide_CompletesPendingShownCallback()
        {
            var host = new GameObject(
                "InspectionLayer",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(BattleInspectionLayer));
            BattleInspectionLayer layer = host.GetComponent<BattleInspectionLayer>();
            var serialized = new SerializedObject(layer);
            serialized.FindProperty("_group").objectReferenceValue = host.GetComponent<CanvasGroup>();
            serialized.ApplyModifiedPropertiesWithoutUndo();

            bool shown = false;
            try
            {
                layer.TransitionTo(BattleInspectionView.Table, onShown: () => shown = true);
                Assert.That(shown, Is.False);

                layer.ForceHide();

                Assert.That(shown, Is.True);
                Assert.That(host.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
