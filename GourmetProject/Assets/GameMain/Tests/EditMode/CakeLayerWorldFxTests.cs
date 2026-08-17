using GourmetProject.Game.Presentation.Battle;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class CakeLayerWorldFxTests
    {
        [Test]
        public void PlayChange_ReactivatesHiddenRootBeforeSpawningCakes()
        {
            var host = new GameObject("CakeLayerFxHost", typeof(CakeLayerWorldFx));
            var root = new GameObject("CakeLayers");
            var prefabObject = new GameObject("CakePrefab", typeof(SpriteRenderer));
            root.transform.SetParent(host.transform, false);
            CakeLayerWorldFx fx = host.GetComponent<CakeLayerWorldFx>();
            try
            {
                var serialized = new SerializedObject(fx);
                serialized.FindProperty("_root").objectReferenceValue = root.transform;
                serialized.FindProperty("_cakePrefab").objectReferenceValue =
                    prefabObject.GetComponent<SpriteRenderer>();
                serialized.ApplyModifiedPropertiesWithoutUndo();

                fx.Configure(null, null);
                fx.SetVisible(false);
                Assert.That(root.activeSelf, Is.False);

                fx.PlayChange(0, 2);

                Assert.That(root.activeSelf, Is.True);
                Assert.That(root.transform.childCount, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(prefabObject);
            }
        }
    }
}
