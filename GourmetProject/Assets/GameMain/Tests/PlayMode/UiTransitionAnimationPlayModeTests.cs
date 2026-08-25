using System;
using System.Collections;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GourmetProject.Tests.PlayMode
{
    public sealed class UiTransitionAnimationPlayModeTests
    {
        private const string RecipeDishPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Meta/Recipes/RecipeEditDishView.prefab";
        private const string ServingOutletPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Hud/ServingOutlet.prefab";

        [UnityTest]
        public IEnumerator RecipeDishRemoval_UsesUnscaledShrinkFade_AndResetsForReuse()
        {
            GameObject canvasObject = CreateCanvas();
            GameObject instance = InstantiatePrefab(RecipeDishPrefabPath, canvasObject.transform);
            float previousTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0f;
                var view = instance.GetComponent<RecipeEditDishView>();
                var group = instance.GetComponent<CanvasGroup>();
                var rect = (RectTransform)instance.transform;
                int completionCount = 0;

                view.PlayPassiveMutationDissolve(() => completionCount++);

                Assert.That(group.interactable, Is.False);
                Assert.That(group.blocksRaycasts, Is.False);
                yield return new WaitForSecondsRealtime(0.12f);
                Assert.That(group.alpha, Is.InRange(0.01f, 0.99f));
                Assert.That(rect.localScale.x, Is.InRange(0.721f, 0.999f));

                yield return new WaitForSecondsRealtime(0.18f);
                Assert.That(group.alpha, Is.EqualTo(0f).Within(0.001f));
                Assert.That(rect.localScale, Is.EqualTo(Vector3.one * 0.72f));
                Assert.That(completionCount, Is.EqualTo(1));

                view.ReleaseForReuse();
                Assert.That(group.alpha, Is.EqualTo(1f).Within(0.001f));
                Assert.That(rect.localScale, Is.EqualTo(Vector3.one));
                Assert.That(group.interactable, Is.False);
                Assert.That(group.blocksRaycasts, Is.False);
            }
            finally
            {
                Time.timeScale = previousTimeScale;
                UnityEngine.Object.Destroy(instance);
                UnityEngine.Object.Destroy(canvasObject);
            }
        }

        [UnityTest]
        public IEnumerator ServingOutlet_FirstBindIsImmediate_AndIdenticalBindDoesNotReplay()
        {
            GameObject canvasObject = CreateCanvas();
            GameObject instance = InstantiatePrefab(ServingOutletPrefabPath, canvasObject.transform);
            try
            {
                ServingOutletView view = instance.GetComponent<ServingOutletView>();
                CanvasGroup group = instance.GetComponent<CanvasGroup>();
                RectTransform preparedRoot = FindRect(instance, "PreparedDish");
                TMP_Text status = FindText(instance, "Status");
                BattleSession ready = CreateReadySession("outlet_first");
                Vector3 baseScale = preparedRoot.localScale;

                Bind(view, ready);

                Assert.That(view.State, Is.EqualTo(ServingOutletState.WaitingForDishDrag));
                Assert.That(group.alpha, Is.EqualTo(1f).Within(0.001f));
                Assert.That(group.interactable, Is.True);
                Assert.That(group.blocksRaycasts, Is.True);
                Assert.That(preparedRoot.gameObject.activeSelf, Is.True);
                Assert.That(preparedRoot.localScale, Is.EqualTo(baseScale));
                Assert.That(status.text, Is.EqualTo("拖到餐桌，或拖进垃圾桶丢弃"));

                Bind(view, ready);
                yield return new WaitForSecondsRealtime(0.05f);

                Assert.That(group.alpha, Is.EqualTo(1f).Within(0.001f));
                Assert.That(group.interactable, Is.True);
                Assert.That(preparedRoot.localScale, Is.EqualTo(baseScale));
            }
            finally
            {
                UnityEngine.Object.Destroy(instance);
                UnityEngine.Object.Destroy(canvasObject);
            }
        }

        [UnityTest]
        public IEnumerator ServingOutlet_TransitionsForNewDishAndStateChanges()
        {
            GameObject canvasObject = CreateCanvas();
            GameObject instance = InstantiatePrefab(ServingOutletPrefabPath, canvasObject.transform);
            try
            {
                ServingOutletView view = instance.GetComponent<ServingOutletView>();
                CanvasGroup group = instance.GetComponent<CanvasGroup>();
                RectTransform preparedRoot = FindRect(instance, "PreparedDish");
                TMP_Text status = FindText(instance, "Status");
                BattleSession firstReady = CreateReadySession("outlet_first_ready");
                BattleSession nextReady = CreateReadySession("outlet_next_ready");
                BattleSession empty = CreateEmptySession();
                Vector3 baseScale = preparedRoot.localScale;

                Bind(view, firstReady);
                Bind(view, nextReady);
                yield return new WaitForSecondsRealtime(0.05f);

                Assert.That(group.alpha, Is.LessThan(1f));
                Assert.That(group.interactable, Is.False);
                Assert.That(group.blocksRaycasts, Is.False);

                yield return new WaitForSecondsRealtime(0.09f);
                Assert.That(view.State, Is.EqualTo(ServingOutletState.WaitingForDishDrag));
                Assert.That(preparedRoot.gameObject.activeSelf, Is.True);
                Assert.That(
                    Mathf.Abs(preparedRoot.localScale.x - baseScale.x),
                    Is.GreaterThan(0.001f));

                yield return new WaitForSecondsRealtime(0.20f);
                Assert.That(group.alpha, Is.EqualTo(1f).Within(0.001f));
                Assert.That(group.interactable, Is.True);
                Assert.That(group.blocksRaycasts, Is.True);
                Assert.That(preparedRoot.localScale, Is.EqualTo(baseScale));

                Bind(view, empty);
                yield return new WaitForSecondsRealtime(0.13f);
                Assert.That(view.State, Is.EqualTo(ServingOutletState.NoDishCanServe));
                Assert.That(preparedRoot.gameObject.activeSelf, Is.False);
                Assert.That(status.text, Is.EqualTo("剩余食物不足"));

                yield return new WaitForSecondsRealtime(0.18f);
                Assert.That(group.alpha, Is.EqualTo(1f).Within(0.001f));
                Assert.That(group.interactable, Is.True);
            }
            finally
            {
                UnityEngine.Object.Destroy(instance);
                UnityEngine.Object.Destroy(canvasObject);
            }
        }

        [UnityTest]
        public IEnumerator ServingOutlet_InterruptedTransitionKeepsLatestState_AndDisableNormalizes()
        {
            GameObject canvasObject = CreateCanvas();
            GameObject instance = InstantiatePrefab(ServingOutletPrefabPath, canvasObject.transform);
            try
            {
                ServingOutletView view = instance.GetComponent<ServingOutletView>();
                CanvasGroup group = instance.GetComponent<CanvasGroup>();
                RectTransform preparedRoot = FindRect(instance, "PreparedDish");
                BattleSession firstReady = CreateReadySession("outlet_interrupt_first");
                BattleSession nextReady = CreateReadySession("outlet_interrupt_latest");
                BattleSession empty = CreateEmptySession();
                Vector3 baseScale = preparedRoot.localScale;

                Bind(view, firstReady);
                Bind(view, empty);
                yield return new WaitForSecondsRealtime(0.04f);
                Bind(view, nextReady);
                yield return new WaitForSecondsRealtime(0.04f);

                Assert.That(group.interactable, Is.False);
                instance.SetActive(false);

                Assert.That(view.State, Is.EqualTo(ServingOutletState.WaitingForDishDrag));
                Assert.That(group.alpha, Is.EqualTo(1f).Within(0.001f));
                Assert.That(group.interactable, Is.True);
                Assert.That(group.blocksRaycasts, Is.True);
                Assert.That(preparedRoot.gameObject.activeSelf, Is.True);
                Assert.That(preparedRoot.localScale, Is.EqualTo(baseScale));

                instance.SetActive(true);
                yield return null;
                Assert.That(group.alpha, Is.EqualTo(1f).Within(0.001f));
                Assert.That(group.interactable, Is.True);
                Assert.That(preparedRoot.localScale, Is.EqualTo(baseScale));
            }
            finally
            {
                UnityEngine.Object.Destroy(instance);
                UnityEngine.Object.Destroy(canvasObject);
            }
        }

        private static BattleSession CreateReadySession(string dishId)
        {
            DishDef dish = CreateDish(dishId);
            var database = new GameplayDatabase(
                new[] { dish },
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var entry = new RecipeSlotEntry(
                dish.Id,
                extraFlavorIds: null,
                extraSkillIds: null,
                scoreMultiplier: 1f,
                scoreFlatBonus: 0f,
                sourceBookIndex: 0,
                sourceDishIndex: 0);
            var session = new BattleSession(
                new DiningTable(2, 2),
                database,
                new Xoshiro256SS(20260824UL),
                new[] { new RecipeSlot("outlet-test-slot", new[] { entry }) },
                requiredScore: 0);
            Assert.That(session.PrepareServe(0).Success, Is.True);
            return session;
        }

        private static BattleSession CreateEmptySession()
        {
            var database = new GameplayDatabase(
                Array.Empty<DishDef>(),
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            return new BattleSession(
                new DiningTable(2, 2),
                database,
                new Xoshiro256SS(20260824UL),
                Array.Empty<RecipeSlot>(),
                requiredScore: 0);
        }

        private static DishDef CreateDish(string id)
        {
            return new DishDef(
                id,
                id,
                deliciousness: 10,
                DishShape.FromRows(new[] { "X" }),
                hiddenMin: 0,
                hiddenMax: 0,
                baseWeight: 1f,
                skillIds: Array.Empty<string>(),
                flavorId: string.Empty);
        }

        private static void Bind(ServingOutletView view, BattleSession session)
        {
            view.Bind(session, null, null, null, null, null);
        }

        private static GameObject CreateCanvas()
        {
            var canvasObject = new GameObject(
                "UiTransitionTestCanvas",
                typeof(RectTransform),
                typeof(Canvas));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            return canvasObject;
        }

        private static GameObject InstantiatePrefab(string path, Transform parent)
        {
#if UNITY_EDITOR
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
#else
            GameObject prefab = null;
#endif
            Assert.That(prefab, Is.Not.Null, path);
            return UnityEngine.Object.Instantiate(prefab, parent, false);
        }

        private static RectTransform FindRect(GameObject root, string name)
        {
            RectTransform result = root
                .GetComponentsInChildren<RectTransform>(true)
                .FirstOrDefault(rect => rect.name == name);
            Assert.That(result, Is.Not.Null, name);
            return result;
        }

        private static TMP_Text FindText(GameObject root, string name)
        {
            TMP_Text result = root
                .GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault(text => text.name == name);
            Assert.That(result, Is.Not.Null, name);
            return result;
        }
    }
}
