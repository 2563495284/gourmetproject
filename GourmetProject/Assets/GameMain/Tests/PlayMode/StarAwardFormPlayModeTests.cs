using System.Collections;
using DG.Tweening;
using GourmetProject.Game.UI.Common;
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
    public sealed class StarAwardFormPlayModeTests
    {
        private const string PrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Meta/Rewards/StarAwardForm.prefab";

        [UnityTest]
        public IEnumerator Presentation_UsesUnscaledTimeAndUnlocksContinueAfterSingleStarLands()
        {
#if UNITY_EDITOR
            StarAwardForm prefab = AssetDatabase.LoadAssetAtPath<StarAwardForm>(PrefabPath);
#else
            StarAwardForm prefab = null;
#endif
            Assert.That(prefab, Is.Not.Null);
            StarAwardForm instance = Object.Instantiate(prefab);
            StarProgressView progress = instance.GetComponentInChildren<StarProgressView>(true);
            Button continueButton = instance.GetComponentInChildren<Button>(true);
            Image largeStar = instance.transform.Find("AwardPanel/AwardStar").GetComponent<Image>();
            Vector2 largeStarHome = largeStar.rectTransform.anchoredPosition;
            float originalTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0f;
                var args = new StarAwardFormOpenArgs("battle-test", 0, 1);
                instance.Bind(args);
                Assert.That(continueButton.interactable, Is.False);
                Assert.That(progress.EarnedStars, Is.Zero);

                instance.Play(args);
                yield return new WaitForSecondsRealtime(2.2f);

                Assert.That(progress.EarnedStars, Is.EqualTo(1));
                Assert.That(largeStar.gameObject.activeSelf, Is.False);
                Assert.That(continueButton.interactable, Is.True);

                instance.Bind(args);
                Assert.That(largeStar.gameObject.activeSelf, Is.True);
                Assert.That(largeStar.rectTransform.anchoredPosition, Is.EqualTo(largeStarHome));
                Assert.That(progress.EarnedStars, Is.Zero);
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
