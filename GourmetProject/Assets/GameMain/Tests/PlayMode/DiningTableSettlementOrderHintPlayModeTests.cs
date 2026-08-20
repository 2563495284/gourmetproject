#if UNITY_EDITOR
using System.Collections;
using GourmetProject.Game.Presentation.Battle;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class DiningTableSettlementOrderHintPlayModeTests
    {
        private const string CellPrefabPath =
            "Assets/GameMain/Content/Prefabs/Battle/Board/DiningTableCell.prefab";

        [UnityTest]
        public IEnumerator SettlementOrderPulse_CompletionAndCancellationRestoreVisualState()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CellPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = Object.Instantiate(prefab);
            DiningTableCellView view = instance.GetComponent<DiningTableCellView>();
            SpriteRenderer plate = instance.GetComponentInChildren<SpriteRenderer>(true);
            Assert.That(view, Is.Not.Null);
            Assert.That(plate, Is.Not.Null);
            Vector3 baseScale = plate.transform.localScale;
            Color baseColor = plate.color;

            try
            {
                view.PlaySettlementOrderHintPulse(0f);
                Assert.That(plate.sharedMaterial, Is.SameAs(SpriteRenderStyle.SpriteTransformMaterial));

                yield return new WaitForSecondsRealtime(0.34f);

                AssertVisualStateRestored(plate, baseScale, baseColor);

                view.PlaySettlementOrderHintPulse(0f);
                yield return new WaitForSecondsRealtime(0.06f);
                Assert.That(plate.sharedMaterial, Is.SameAs(SpriteRenderStyle.SpriteTransformMaterial));
                Assert.That(
                    plate.transform.localScale.sqrMagnitude,
                    Is.GreaterThan(baseScale.sqrMagnitude));

                view.CancelSettlementOrderHintPulse();

                AssertVisualStateRestored(plate, baseScale, baseColor);
            }
            finally
            {
                view?.CancelSettlementOrderHintPulse();
                Object.Destroy(instance);
            }

            yield return null;
        }

        private static void AssertVisualStateRestored(
            SpriteRenderer plate,
            Vector3 baseScale,
            Color baseColor)
        {
            Assert.That(plate.sharedMaterial, Is.SameAs(SpriteRenderStyle.SpriteUnlitMaterial));
            Assert.That(plate.transform.localScale, Is.EqualTo(baseScale));
            Assert.That(plate.color, Is.EqualTo(baseColor));

            var block = new MaterialPropertyBlock();
            plate.GetPropertyBlock(block);
            Assert.That(block.GetFloat(Shader.PropertyToID("_Brightness")), Is.Zero.Within(0.001f));
        }
    }
}
#endif
