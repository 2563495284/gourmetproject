#if UNITY_EDITOR
using System.Globalization;
using System.IO;
using System.Linq;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ActionHalfCostPresentationTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            string directory = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name =>
                JSON.Parse(File.ReadAllText(Path.Combine(directory, name + ".json"))));
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [TestCase(0, 1.5f, 1.5f, false)]
        [TestCase(1, 1.5f, 0.8f, true)]
        [TestCase(2, 1.5f, 0.8f, true)]
        public void ActionOffer_PreservesPreHalfCostAndAppliesAtMostOneHalf(
            int halfCostStacks,
            float originalCost,
            float expectedCost,
            bool expectedApplied)
        {
            GameRun run = CreateRunWithPendingChoice(originalCost, halfCostStacks);

            ActionChoice choice = ActionOfferService.GetOrRoll(run).Single();

            Assert.That(choice.CostBeforeHalfDays, Is.EqualTo(originalCost).Within(0.0001f));
            Assert.That(choice.CostDays, Is.EqualTo(expectedCost).Within(0.0001f));
            Assert.That(choice.HalfDayBuffApplied, Is.EqualTo(expectedApplied));
        }

        [Test]
        public void PendingChoice_SaveRestoreRebuildsDiscountComparisonWithoutNewSaveField()
        {
            const float originalCost = 1.5f;
            GameRun run = CreateRunWithPendingChoice(originalCost, halfCostStacks: 1);

            RunSaveData saveData = run.ToSaveData();
            Assert.That(saveData.PendingActionChoices, Has.Count.EqualTo(1));
            Assert.That(
                saveData.PendingActionChoices[0].CostDays,
                Is.EqualTo(originalCost).Within(0.0001f));

            GameRun restored = GameRun.FromSaveData(_tables, _database, saveData);
            ActionChoice restoredChoice = ActionOfferService.GetOrRoll(restored).Single();

            Assert.That(restoredChoice.HalfDayBuffApplied, Is.True);
            Assert.That(restoredChoice.CostBeforeHalfDays, Is.EqualTo(originalCost).Within(0.0001f));
            Assert.That(restoredChoice.CostDays, Is.EqualTo(0.8f).Within(0.0001f));
        }

        [Test]
        public void FooterFormatter_UsesPlainTextWithoutHalfCost()
        {
            Assert.That(
                WeekEventCardView.FormatActionCostFooter(1.5f),
                Is.EqualTo("用时：1.5天"));
            Assert.That(
                WeekEventCardView.FormatActionCostFooter(0f),
                Is.Empty);
        }

        [Test]
        public void DiscountFooter_UsesStackedPriceTagAndRealStrike()
        {
            GameObject instance = InstantiateCard();

            try
            {
                WeekEventCardView view = instance.GetComponent<WeekEventCardView>();
                view.SetActionCostFooter(
                    0.8f,
                    halfDayBuffApplied: true,
                    costBeforeHalfDays: 1.5f);

                TMP_Text timeText = FindText(instance, "Time");
                RectTransform halfCostRoot = FindRect(instance, "HalfCostRoot");
                TMP_Text labelText = FindText(instance, "HalfCostLabel");
                TMP_Text originalText = FindText(instance, "HalfCostOriginal");
                TMP_Text effectiveText = FindText(instance, "HalfCostEffective");
                Image strike = FindImage(instance, "HalfCostStrike");

                Assert.That(timeText.gameObject.activeSelf, Is.False);
                Assert.That(halfCostRoot.gameObject.activeSelf, Is.True);
                Assert.That(labelText.text, Is.EqualTo("用时："));
                Assert.That(originalText.text, Is.EqualTo("1.5天"));
                Assert.That(effectiveText.text, Is.EqualTo("0.8天"));
                Assert.That(labelText.fontSize, Is.EqualTo(22f));
                Assert.That(originalText.fontSize, Is.EqualTo(12f));
                Assert.That(effectiveText.fontSize, Is.EqualTo(24f));
                Assert.That(
                    originalText.horizontalAlignment,
                    Is.EqualTo(HorizontalAlignmentOptions.Center));
                Assert.That(
                    effectiveText.horizontalAlignment,
                    Is.EqualTo(HorizontalAlignmentOptions.Center));
                Assert.That(
                    originalText.rectTransform.anchoredPosition.x,
                    Is.EqualTo(effectiveText.rectTransform.anchoredPosition.x).Within(0.01f));
                Assert.That(
                    effectiveText.rectTransform.anchoredPosition.x,
                    Is.EqualTo(20f).Within(0.01f));
                Assert.That(
                    originalText.rectTransform.rect.width,
                    Is.EqualTo(effectiveText.rectTransform.rect.width).Within(0.01f));
                Assert.That(
                    labelText.rectTransform.anchoredPosition.y,
                    Is.EqualTo(effectiveText.rectTransform.anchoredPosition.y).Within(0.01f));
                Assert.That(
                    labelText.rectTransform.anchoredPosition.y,
                    Is.Zero.Within(0.01f));
                Assert.That(originalText.color, Is.EqualTo((Color)new Color32(140, 119, 102, 255)));
                Assert.That(effectiveText.color, Is.EqualTo((Color)new Color32(200, 74, 34, 255)));
                Assert.That(labelText.GetComponent<Outline>(), Is.Null);
                Assert.That(originalText.GetComponent<Outline>(), Is.Null);
                Assert.That(effectiveText.GetComponent<Outline>(), Is.Null);

                effectiveText.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
                labelText.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
                Assert.That(effectiveText.textInfo.characterCount, Is.GreaterThan(0));
                Assert.That(
                    effectiveText.textInfo.meshInfo.Sum(mesh => mesh.vertexCount),
                    Is.GreaterThan(0));
                Assert.That(
                    effectiveText.preferredHeight,
                    Is.LessThanOrEqualTo(effectiveText.rectTransform.rect.height));

                float labelRight = LocalTextEdge(halfCostRoot, labelText, labelText.textBounds.max.x);
                float effectiveLeft = LocalTextEdge(
                    halfCostRoot,
                    effectiveText,
                    effectiveText.textBounds.min.x);
                Assert.That(effectiveLeft - labelRight, Is.InRange(0f, 24f));

                originalText.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
                Assert.That(strike.gameObject.activeSelf, Is.True);
                Assert.That(strike.rectTransform.rect.height, Is.EqualTo(2f).Within(0.01f));
                Assert.That(strike.rectTransform.anchorMin.x, Is.EqualTo(0.5f).Within(0.01f));
                Assert.That(strike.rectTransform.anchorMax.x, Is.EqualTo(0.5f).Within(0.01f));
                Assert.That(strike.rectTransform.pivot.x, Is.EqualTo(0.5f).Within(0.01f));
                Assert.That(strike.rectTransform.anchoredPosition.x, Is.Zero.Within(0.01f));
                Assert.That(
                    strike.rectTransform.rect.width,
                    Is.GreaterThanOrEqualTo(Mathf.Ceil(originalText.preferredWidth)));
                Assert.That(
                    strike.rectTransform.rect.width,
                    Is.LessThanOrEqualTo(originalText.rectTransform.rect.width));

                view.SetActionCostFooter(
                    0.1f,
                    halfDayBuffApplied: true,
                    costBeforeHalfDays: 0.1f);
                Assert.That(timeText.gameObject.activeSelf, Is.True);
                Assert.That(timeText.text, Is.EqualTo("用时：0.1天"));
                Assert.That(halfCostRoot.gameObject.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void FooterFormatter_UsesInvariantOneDecimalFormat()
        {
            CultureInfo previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

                string originalText = WeekEventCardView.FormatActionCostDays(1.5f);
                string effectiveText = WeekEventCardView.FormatActionCostDays(0.8f);

                Assert.That(originalText, Is.EqualTo("1.5天"));
                Assert.That(effectiveText, Is.EqualTo("0.8天"));
                Assert.That(originalText + effectiveText, Does.Not.Contain(","));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }

        [TestCase(0.9f, 0.5f)]
        [TestCase(1.5f, 0.8f)]
        public void DiscountFooter_FitsExistingCardTimeLabel(
            float originalCost,
            float effectiveCost)
        {
            GameObject instance = InstantiateCard();

            try
            {
                WeekEventCardView view = instance.GetComponent<WeekEventCardView>();
                view.SetActionCostFooter(
                    effectiveCost,
                    halfDayBuffApplied: true,
                    costBeforeHalfDays: originalCost);

                RectTransform timeRect = FindText(instance, "Time").rectTransform;
                RectTransform halfCostRoot = FindRect(instance, "HalfCostRoot");
                Assert.That(halfCostRoot.rect.size.x, Is.EqualTo(timeRect.rect.size.x).Within(0.01f));
                Assert.That(halfCostRoot.rect.size.y, Is.EqualTo(timeRect.rect.size.y).Within(0.01f));

                foreach (string name in new[] { "HalfCostLabel", "HalfCostOriginal", "HalfCostEffective" })
                {
                    TMP_Text text = FindText(instance, name);
                    text.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
                    Assert.That(text.isTextOverflowing, Is.False, $"{name}: {text.text}");
                    Assert.That(
                        text.preferredWidth,
                        Is.LessThanOrEqualTo(text.rectTransform.rect.width),
                        $"{name} width: {text.text}");
                    Assert.That(
                        text.preferredHeight,
                        Is.LessThanOrEqualTo(text.rectTransform.rect.height),
                        $"{name} height: {text.text}");
                    Assert.That(
                        text.textInfo.meshInfo.Sum(mesh => mesh.vertexCount),
                        Is.GreaterThan(0),
                        $"{name} mesh: {text.text}");
                    AssertRectInside(halfCostRoot, text.rectTransform, name);
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static GameObject InstantiateCard()
        {
            const string prefabPath =
                "Assets/GameMain/Content/Prefabs/UI/Meta/Events/WeekEventCardView.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = Object.Instantiate(prefab);
            instance.transform.localScale = Vector3.one;
            Canvas canvas = instance.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.ForceUpdateCanvases();
            return instance;
        }

        private static TMP_Text FindText(GameObject root, string name)
        {
            return root
                .GetComponentsInChildren<TMP_Text>(true)
                .Single(text => text.gameObject.name == name);
        }

        private static RectTransform FindRect(GameObject root, string name)
        {
            return root
                .GetComponentsInChildren<RectTransform>(true)
                .Single(rect => rect.gameObject.name == name);
        }

        private static Image FindImage(GameObject root, string name)
        {
            return root
                .GetComponentsInChildren<Image>(true)
                .Single(image => image.gameObject.name == name);
        }

        private static void AssertRectInside(
            RectTransform container,
            RectTransform child,
            string name)
        {
            var corners = new Vector3[4];
            child.GetWorldCorners(corners);
            Rect bounds = container.rect;
            foreach (Vector3 corner in corners)
            {
                Vector3 local = container.InverseTransformPoint(corner);
                Assert.That(local.x, Is.InRange(bounds.xMin - 0.01f, bounds.xMax + 0.01f), name);
                Assert.That(local.y, Is.InRange(bounds.yMin - 0.01f, bounds.yMax + 0.01f), name);
            }
        }

        private static float LocalTextEdge(
            RectTransform container,
            TMP_Text text,
            float localX)
        {
            Vector3 world = text.rectTransform.TransformPoint(new Vector3(localX, 0f, 0f));
            return container.InverseTransformPoint(world).x;
        }

        private GameRun CreateRunWithPendingChoice(float costDays, int halfCostStacks)
        {
            var run = new GameRun(
                _tables,
                _database,
                "glutton_dog",
                $"half-cost-presentation-{halfCostStacks}-{costDays.ToString(CultureInfo.InvariantCulture)}");
            for (int i = 0; i < halfCostStacks; i++)
            {
                run.AddNextDailyActionHalfCostStack();
            }

            cfg.GameAction action = _tables.TbAction.GetOrDefault("act_event");
            Assert.That(action, Is.Not.Null);
            string key = GameRun.BuildActionChoiceKey(
                run.RunActionStepIndex,
                run.WeekIndex,
                run.CurrentDay,
                run.ActionStepIndex);
            run.SetPendingActionChoices(
                key,
                new[]
                {
                    new ActionChoice(
                        action,
                        "half_cost_test",
                        run.ActionStepIndex,
                        run.RunActionStepIndex,
                        costDays),
                });
            return run;
        }
    }
}
#endif
