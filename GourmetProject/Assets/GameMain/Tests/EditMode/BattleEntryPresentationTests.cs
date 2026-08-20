using System;
using System.Threading;
using DG.Tweening;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Gameplay.Scoring;
using Luban.SimpleJSON;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BattleEntryPresentationTests
    {
        private const string OverlayPath =
            "Assets/GameMain/Content/Resources/Prefabs/UI/Battle/BattleEntryPresentationOverlay.prefab";
        private const string BattleFormPath =
            "Assets/GameMain/Content/Prefabs/UI/Battle/BattleForm.prefab";

        [Test]
        public void Prefab_HasCompleteBindings_AndIsNestedUnderHudFrame()
        {
            GameObject overlayPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OverlayPath);
            Assert.That(overlayPrefab, Is.Not.Null);
            BattleEntryPresentationView view = overlayPrefab.GetComponent<BattleEntryPresentationView>();
            Assert.That(view, Is.Not.Null);

            var serializedView = new SerializedObject(view);
            string[] requiredBindings =
            {
                "_overlay", "_inputGroup", "_contentGroup", "_targetGroup", "_targetScoreText",
                "_ruleGroup", "_ruleGroupCanvas", "_ruleIcon", "_ruleNameText",
                "_ruleDescriptionText", "_timelineTheme",
            };
            foreach (string propertyName in requiredBindings)
            {
                Assert.That(
                    serializedView.FindProperty(propertyName)?.objectReferenceValue,
                    Is.Not.Null,
                    propertyName);
            }

            Image inputCatcher = overlayPrefab.GetComponent<Image>();
            CanvasGroup inputGroup = overlayPrefab.GetComponent<CanvasGroup>();
            Assert.That(inputCatcher, Is.Not.Null);
            Assert.That(inputCatcher.raycastTarget, Is.True);
            Assert.That(inputCatcher.color.a, Is.Zero.Within(0.001f));
            Assert.That(inputGroup.blocksRaycasts, Is.False);

            RectTransform content = overlayPrefab.transform.Find("CenterViewport/Content") as RectTransform;
            Assert.That(content, Is.Not.Null);
            Assert.That(content.localScale.x, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(content.localScale.y, Is.EqualTo(0.5f).Within(0.001f));

            foreach (TMP_Text text in overlayPrefab.GetComponentsInChildren<TMP_Text>(true))
            {
                Assert.That(text.text, Does.Not.Contain("Boss").IgnoreCase, text.name);
                Assert.That(text.text, Does.Not.Contain("Debuff").IgnoreCase, text.name);
            }

            GameObject battleFormPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BattleFormPath);
            Assert.That(battleFormPrefab, Is.Not.Null);
            BattleForm battleForm = battleFormPrefab.GetComponent<BattleForm>();
            Assert.That(battleForm, Is.Not.Null);
            var serializedForm = new SerializedObject(battleForm);
            var nestedView = serializedForm.FindProperty("_battleEntryPresentation")
                ?.objectReferenceValue as BattleEntryPresentationView;
            Assert.That(nestedView, Is.Not.Null);
            Assert.That(nestedView.transform.parent.name, Is.EqualTo("HudFrame"));

            RectTransform nestedRect = nestedView.transform as RectTransform;
            Assert.That(nestedRect.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(nestedRect.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(nestedRect.sizeDelta, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void Prepare_NormalChallenge_ShowsOnlyFinalTargetScore()
        {
            BattleEntryPresentationView view = InstantiateView();
            try
            {
                const int finalTarget = 12_345_678;
                view.Prepare(finalTarget, null);

                Assert.That(
                    view.DisplayedTargetScore,
                    Does.Contain(ScoreNumberFormatter.Format(finalTarget)));
                Assert.That(view.DisplayedTargetScore, Does.Not.Contain("Boss").IgnoreCase);
                Assert.That(view.DisplayedTargetScore, Does.Not.Contain("Debuff").IgnoreCase);
                Assert.That(view.ShowsRule, Is.False);
                Assert.That(view.DisplayedRuleName, Is.Empty);
                Assert.That(view.DisplayedRuleDescription, Is.Empty);

                Tween presentation = view.BuildPresentationTween();
                Assert.That(presentation.Duration(), Is.EqualTo(1.37f).Within(0.001f));
                presentation.Complete(withCallbacks: true);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(view.gameObject);
            }
        }

        [Test]
        public void Prepare_StarEvaluation_ShowsIconNameAndSemanticMultilineDescription()
        {
            BattleEntryPresentationView view = InstantiateView();
            try
            {
                const string description = "每次品尝后，[score]目标美味值[/score]提高。\n连续品尝会进一步提高。";
                cfg.BossDebuff rule = Rule("debuff_tasting", "主厨品鉴", description);
                view.Prepare(8888, rule);

                Assert.That(view.ShowsRule, Is.True);
                Assert.That(view.DisplayedRuleIcon, Is.Not.Null);
                Assert.That(view.DisplayedRuleName, Is.EqualTo("主厨品鉴"));
                Assert.That(
                    view.DisplayedRuleDescription,
                    Is.EqualTo(SemanticDescriptionFormatter.Format(description)));
                Assert.That(view.DisplayedRuleDescription, Does.Contain("\n"));

                Tween presentation = view.BuildPresentationTween();
                Assert.That(presentation.Duration(), Is.EqualTo(1.64f).Within(0.001f));
                presentation.Complete(withCallbacks: true);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(view.gameObject);
            }
        }

        [Test]
        public void Presentation_ClickSkipsOnce_AndResetClearsInputBlocker()
        {
            BattleEntryPresentationView view = InstantiateView();
            Tween presentation = null;
            try
            {
                view.Prepare(1234, null);
                presentation = view.BuildPresentationTween();
                int skips = 0;
                view.BindSkipHandler(() => skips++);
                presentation.GotoWithCallbacks(0.01f, andPlay: false);

                Assert.That(view.BlocksInput, Is.True);
                view.OnPointerClick(null);
                view.OnPointerClick(null);
                Assert.That(skips, Is.EqualTo(1));

                view.BindSkipHandler(null);
                Assert.That(view.BlocksInput, Is.False);
                Assert.That(view.gameObject.activeSelf, Is.False);
            }
            finally
            {
                presentation?.Kill(complete: false);
                UnityEngine.Object.DestroyImmediate(view.gameObject);
            }
        }

        [Test]
        public void CoverSwap_CoveredSkipCompletionAndCancellation_CleanUpExactlyOnce()
        {
            var coverObject = new GameObject("Cover", typeof(RectTransform), typeof(CanvasGroup));
            CanvasGroup cover = coverObject.GetComponent<CanvasGroup>();
            Tween transition = null;
            Tween cancelled = null;
            CancellationTokenSource cancellation = null;
            try
            {
                int swaps = 0;
                int completions = 0;
                Action skip = null;
                transition = UITransition.CoverSwap(
                    cover,
                    () => swaps++,
                    0.10f,
                    0.02f,
                    0.10f,
                    () => DOTween.Sequence().AppendInterval(1f),
                    handler => skip = handler,
                    CancellationToken.None,
                    () => completions++);

                transition.GotoWithCallbacks(0.14f, andPlay: false);
                Assert.That(swaps, Is.EqualTo(1));
                Assert.That(skip, Is.Not.Null);
                skip.Invoke();
                Assert.That(skip, Is.Null, "Skip should unbind before the reveal starts.");
                transition.Complete(withCallbacks: true);
                transition.Kill(complete: false);

                Assert.That(completions, Is.EqualTo(1));
                Assert.That(cover.alpha, Is.Zero.Within(0.001f));
                Assert.That(cover.blocksRaycasts, Is.False);
                Assert.That(cover.interactable, Is.False);

                int cancelledCompletions = 0;
                int cancelledSwaps = 0;
                skip = null;
                cancellation = new CancellationTokenSource();
                cancelled = UITransition.CoverSwap(
                    cover,
                    () => cancelledSwaps++,
                    0.10f,
                    0.02f,
                    0.10f,
                    () => DOTween.Sequence().AppendInterval(1f),
                    handler => skip = handler,
                    cancellation.Token,
                    () => cancelledCompletions++);
                cancellation.Cancel();
                cancelled.Kill(complete: false);
                cancelled.GotoWithCallbacks(0.01f, andPlay: false);
                // DOTween 会把回调内部的 Kill 延迟到当前 seek 结束；此时已完成 startup，
                // Complete 只用于把测试自身的取消序列从 DOTween 注册表移除；取消 token
                // 会阻止 swap / onDone / skip 绑定，因此不改变被测取消语义。
                cancelled.Complete(withCallbacks: true);

                Assert.That(cancelledCompletions, Is.Zero);
                Assert.That(cancelledSwaps, Is.Zero);
                Assert.That(skip, Is.Null, "Cancellation should unbind the covered presentation.");
                Assert.That(cancelled.IsActive(), Is.False);
                Assert.That(cover.alpha, Is.Zero.Within(0.001f));
                Assert.That(cover.blocksRaycasts, Is.False);
                Assert.That(cover.interactable, Is.False);
            }
            finally
            {
                transition?.Kill(complete: false);
                cancelled?.Kill(complete: false);
                cancellation?.Dispose();
                DOTween.Kill(cover, complete: false);
                UnityEngine.Object.DestroyImmediate(coverObject);
            }
        }

        [Test]
        public void CoverSwap_ExistingOverload_KeepsOriginalCompletionContract()
        {
            var coverObject = new GameObject("Cover", typeof(RectTransform), typeof(CanvasGroup));
            CanvasGroup cover = coverObject.GetComponent<CanvasGroup>();
            Tween transition = null;
            try
            {
                int swaps = 0;
                int completions = 0;
                transition = UITransition.CoverSwap(
                    cover,
                    () => swaps++,
                    0.10f,
                    0.03f,
                    0.10f,
                    () => completions++);
                Assert.That(transition.Duration(), Is.EqualTo(0.23f).Within(0.001f));

                transition.Complete(withCallbacks: true);
                transition.Kill(complete: false);
                Assert.That(swaps, Is.EqualTo(1));
                Assert.That(completions, Is.EqualTo(1));
                Assert.That(cover.alpha, Is.Zero.Within(0.001f));
                Assert.That(cover.blocksRaycasts, Is.False);
            }
            finally
            {
                transition?.Kill(complete: false);
                UnityEngine.Object.DestroyImmediate(coverObject);
            }
        }

        private static BattleEntryPresentationView InstantiateView()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OverlayPath);
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            BattleEntryPresentationView view = instance.GetComponent<BattleEntryPresentationView>();
            Assert.That(view, Is.Not.Null);
            view.EnsureBuilt();
            return view;
        }

        private static cfg.BossDebuff Rule(string id, string name, string description)
        {
            string json = "{"
                          + $"\"id\":\"{Escape(id)}\","
                          + $"\"name\":\"{Escape(name)}\","
                          + $"\"desc\":\"{Escape(description)}\","
                          + "\"weight\":1,"
                          + "\"unlockCondition\":\"\","
                          + "\"targetScoreHiddenOffset\":0,"
                          + "\"dialogues\":[]"
                          + "}";
            return new cfg.BossDebuff(JSON.Parse(json));
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
        }
    }
}
