using System.Collections;
using System.Linq;
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
        private const string BattlePrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Battle/BattleForm.prefab";

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

        [UnityTest]
        public IEnumerator StarCardEffects_UseUnscaledTimeAreIdempotentAndClearWhenDisabled()
        {
#if UNITY_EDITOR
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BattlePrefabPath);
#else
            GameObject prefab = null;
#endif
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = Object.Instantiate(prefab);
            Transform starCard = instance.GetComponentsInChildren<Transform>(true)
                .First(child => child.name == "StarCard");
            StarProgressView progress = starCard.GetComponent<StarProgressView>();
            StarCardSparkleGraphic sparkles = progress.Sparkles;
            float originalTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0f;
                progress.Bind(0);
                Assert.That(sparkles.ParticleCount, Is.Zero);

                progress.Bind(1);
                yield return new WaitForSecondsRealtime(0.15f);

                Assert.That(sparkles.BurstInvocationCount, Is.EqualTo(1));
                Assert.That(sparkles.ParticleCount, Is.GreaterThan(0));
                Assert.That(progress.GetStarRect(0).localScale.x, Is.Not.EqualTo(1f).Within(0.001f));

                progress.Bind(1);
                yield return null;
                Assert.That(sparkles.BurstInvocationCount, Is.EqualTo(1));

                starCard.gameObject.SetActive(false);
                Assert.That(sparkles.ParticleCount, Is.Zero);
                Assert.That(progress.GetStarRect(0).localScale, Is.EqualTo(Vector3.one));

                starCard.gameObject.SetActive(true);
                Assert.That(sparkles.BurstInvocationCount, Is.EqualTo(1));
            }
            finally
            {
                Time.timeScale = originalTimeScale;
                DOTween.Kill(progress);
                Object.Destroy(instance);
            }
        }
    }
}
