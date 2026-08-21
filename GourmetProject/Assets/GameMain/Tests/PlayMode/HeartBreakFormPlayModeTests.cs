using System.Collections;
using DG.Tweening;
using GourmetProject.Game.UI.Meta;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GourmetProject.Tests.PlayMode
{
    public sealed class HeartBreakFormPlayModeTests
    {
        private const string PrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Meta/Rewards/HeartBreakForm.prefab";

        [UnityTest]
        public IEnumerator Presentation_UsesUnscaledTime_ExtinguishesTargetAndReusesSlots()
        {
#if UNITY_EDITOR
            HeartBreakForm prefab = AssetDatabase.LoadAssetAtPath<HeartBreakForm>(PrefabPath);
#else
            HeartBreakForm prefab = null;
#endif
            Assert.That(prefab, Is.Not.Null);

            HeartBreakForm instance = Object.Instantiate(prefab);
            HeartBreakHeartRow row = instance.GetComponentInChildren<HeartBreakHeartRow>(true);
            Button continueButton = instance.transform.Find("Panel/Content/Continue").GetComponent<Button>();
            Assert.That(row, Is.Not.Null);
            Assert.That(continueButton, Is.Not.Null);

            float originalTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0f;
                var args = new HeartBreakFormOpenArgs(3, 2, 3, false);
                instance.Bind(args);

                Assert.That(continueButton.interactable, Is.False);
                Assert.That(row.StatusText, Is.EqualTo("当前红心 3 / 3"));
                Assert.That(row.GetHeartGlyph(0), Is.EqualTo(HeartBreakHeartRow.FullHeartGlyph));
                Assert.That(row.GetHeartGlyph(1), Is.EqualTo(HeartBreakHeartRow.FullHeartGlyph));
                Assert.That(row.GetHeartGlyph(2), Is.EqualTo(HeartBreakHeartRow.FullHeartGlyph));

                instance.Play();
                yield return new WaitForSecondsRealtime(1.4f);

                Assert.That(continueButton.interactable, Is.True);
                Assert.That(row.StatusText, Is.EqualTo("剩余红心 2 / 3"));
                Assert.That(row.GetHeartGlyph(0), Is.EqualTo(HeartBreakHeartRow.FullHeartGlyph));
                Assert.That(row.GetHeartGlyph(1), Is.EqualTo(HeartBreakHeartRow.FullHeartGlyph));
                Assert.That(row.GetHeartGlyph(2), Is.EqualTo(HeartBreakHeartRow.EmptyHeartGlyph));
                Assert.That(row.GetHeartColor(2), Is.Not.EqualTo(row.GetHeartColor(1)));
                Assert.That(row.GetHeartColor(2).a, Is.LessThan(row.GetHeartColor(1).a));
                Vector3 finalScale = row.GetHeartScale(2);
                Assert.That(finalScale.x, Is.EqualTo(0.84f).Within(0.001f));
                Assert.That(finalScale.y, Is.EqualTo(0.84f).Within(0.001f));
                Assert.That(finalScale.z, Is.EqualTo(0.84f).Within(0.001f));

                int createdSlotCount = row.SlotCount;
                instance.Bind(args);
                Assert.That(row.SlotCount, Is.EqualTo(createdSlotCount));
                Assert.That(row.GetHeartGlyph(2), Is.EqualTo(HeartBreakHeartRow.FullHeartGlyph));
                Assert.That(continueButton.interactable, Is.False);
            }
            finally
            {
                Time.timeScale = originalTimeScale;
                DOTween.Kill(instance);
                Object.Destroy(instance.gameObject);
            }
        }
    }
}
