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
        private const string FullHeartSpritePath =
            "Assets/GameMain/Content/Resources/Sprites/UI/FengKuangCanTing/Icons/icon_heart_active.png";
        private const string EmptyHeartSpritePath =
            "Assets/GameMain/Content/Resources/Sprites/UI/FengKuangCanTing/Icons/icon_heart_empty.png";

        [UnityTest]
        public IEnumerator Presentation_UsesHudSprites_ExtinguishesTargetAndReusesSlots()
        {
#if UNITY_EDITOR
            HeartBreakForm prefab = AssetDatabase.LoadAssetAtPath<HeartBreakForm>(PrefabPath);
            Sprite fullHeartSprite = AssetDatabase.LoadAssetAtPath<Sprite>(FullHeartSpritePath);
            Sprite emptyHeartSprite = AssetDatabase.LoadAssetAtPath<Sprite>(EmptyHeartSpritePath);
#else
            HeartBreakForm prefab = null;
            Sprite fullHeartSprite = null;
            Sprite emptyHeartSprite = null;
#endif
            Assert.That(prefab, Is.Not.Null);
            Assert.That(fullHeartSprite, Is.Not.Null);
            Assert.That(emptyHeartSprite, Is.Not.Null);

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
                Assert.That(row.StatusText, Is.EqualTo("剩余红心 3 / 3"));
                Assert.That(row.GetHeartSprite(0), Is.SameAs(fullHeartSprite));
                Assert.That(row.GetHeartSprite(1), Is.SameAs(fullHeartSprite));
                Assert.That(row.GetHeartSprite(2), Is.SameAs(fullHeartSprite));

                instance.Play();
                yield return new WaitForSecondsRealtime(1.4f);

                Assert.That(continueButton.interactable, Is.True);
                Assert.That(row.StatusText, Is.EqualTo("剩余红心 2 / 3"));
                Assert.That(row.GetHeartSprite(0), Is.SameAs(fullHeartSprite));
                Assert.That(row.GetHeartSprite(1), Is.SameAs(fullHeartSprite));
                Assert.That(row.GetHeartSprite(2), Is.SameAs(emptyHeartSprite));
                Vector3 finalScale = row.GetHeartScale(2);
                Assert.That(finalScale.x, Is.EqualTo(0.84f).Within(0.001f));
                Assert.That(finalScale.y, Is.EqualTo(0.84f).Within(0.001f));
                Assert.That(finalScale.z, Is.EqualTo(0.84f).Within(0.001f));

                int createdSlotCount = row.SlotCount;
                instance.Bind(args);
                Assert.That(row.SlotCount, Is.EqualTo(createdSlotCount));
                Assert.That(row.GetHeartSprite(2), Is.SameAs(fullHeartSprite));
                Assert.That(continueButton.interactable, Is.False);

                instance.Bind(new HeartBreakFormOpenArgs(1, 0, 3, true));
                Assert.That(row.SlotCount, Is.EqualTo(createdSlotCount));
                Assert.That(row.GetHeartSprite(0), Is.SameAs(fullHeartSprite));
                Assert.That(row.GetHeartSprite(1), Is.SameAs(emptyHeartSprite));
                Assert.That(row.GetHeartSprite(2), Is.SameAs(emptyHeartSprite));

                instance.Play();
                yield return new WaitForSecondsRealtime(1.4f);

                Assert.That(continueButton.interactable, Is.True);
                Assert.That(row.StatusText, Is.EqualTo("剩余红心 0 / 3"));
                Assert.That(row.GetHeartSprite(0), Is.SameAs(emptyHeartSprite));
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
