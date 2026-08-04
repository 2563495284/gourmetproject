using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Tests.EditMode
{
    public sealed class FoodTipsCountAsTests
    {
        private const string FoodTipsPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/TipsView/FoodTipsView.prefab";

        [Test]
        public void ScoreAndTipsData_UseTheSameLiveEffectiveCountAs()
        {
            GameplayDatabase db = CreateDatabase(out DishDef definition);
            DiningTable table = CreateTableWithDish(definition, out DishInstance dish);
            dish.AddCountAsBonus(1);
            dish.MarkTemporary();

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                db,
                extraCountAsPerDish: 3);
            DishScore score = result.DishScores.Single();
            FoodTipsData tips = FoodTipsDataFactory.Build(dish, table, db, result);

            // 定义 2 + 自身 AddCountAs 2 + 运行时 1 + 全局 3。
            Assert.That(score.EffectiveCountAs, Is.EqualTo(8));
            Assert.That(tips.Summary.CountAs, Is.EqualTo(8));
            Assert.That(tips.Summary.IsTemporaryCopy, Is.True);
        }

        [Test]
        public void CountAsAll_ContributesToEveryFoodCountThreshold()
        {
            GameplayDatabase db = CreateDatabase(out DishDef definition);
            DiningTable table = CreateTableWithDish(definition, out DishInstance dish);
            dish.AddCountAsBonus(1);
            var thresholdSource = new ItemScoreEffectSource(new[]
            {
                new ItemScoreSpec(
                    ItemScoreEffectType.CountThresholdFinalMult,
                    2f,
                    "gte:8",
                    "item_count_ge_mult",
                    "食物数门槛"),
            });

            ScoreResult withoutCountAsAll = new ScoreCalculator().Calculate(
                table,
                db,
                extraSources: new[] { thresholdSource });
            ScoreResult withCountAsAll = new ScoreCalculator().Calculate(
                table,
                db,
                extraSources: new[] { thresholdSource },
                extraCountAsPerDish: 3);

            Assert.That(withoutCountAsAll.FinalMultiplier, Is.EqualTo(1f));
            Assert.That(withCountAsAll.DishScores.Single().EffectiveCountAs, Is.EqualTo(8));
            Assert.That(withCountAsAll.FinalMultiplier, Is.EqualTo(2f));
        }

        [Test]
        public void IntrinsicPreview_ReusesAddCountAsSettlementRules()
        {
            GameplayDatabase db = CreateDatabase(out DishDef definition);

            int countAs = FoodTipsDataFactory.ResolveIntrinsicCountAs(
                definition,
                definition.SkillIds,
                Array.Empty<string>(),
                db);

            Assert.That(countAs, Is.EqualTo(4));
        }

        [Test]
        public void SummaryView_ShowsTemporaryAndRealCount_ThenHidesAtOne()
        {
            GameObject instance = InstantiateFoodTipsPrefab();
            try
            {
                FoodSummaryTipsView view =
                    instance.GetComponentInChildren<FoodSummaryTipsView>(true);
                var serialized = new SerializedObject(view);
                var duplicateView = (RectTransform)serialized
                    .FindProperty("_duplicateView").objectReferenceValue;
                var countAsView = (RectTransform)serialized
                    .FindProperty("_countAsView").objectReferenceValue;
                var countAsText = (TMP_Text)serialized
                    .FindProperty("_countAsText").objectReferenceValue;

                Assert.That(duplicateView, Is.Not.Null);
                Assert.That(countAsView, Is.Not.Null);
                Assert.That(countAsText, Is.Not.Null);

                view.Bind(new FoodSummaryTipsData(
                    "测试食物",
                    Array.Empty<FoodInfoEntry>(),
                    Array.Empty<string>(),
                    isTemporaryCopy: true,
                    countAs: 10));

                Assert.That(duplicateView.gameObject.activeSelf, Is.True);
                Assert.That(countAsView.gameObject.activeSelf, Is.True);
                Assert.That(countAsText.text, Is.EqualTo("10"));

                view.Bind(new FoodSummaryTipsData(
                    "测试食物",
                    Array.Empty<FoodInfoEntry>(),
                    Array.Empty<string>()));

                Assert.That(duplicateView.gameObject.activeSelf, Is.False);
                Assert.That(countAsView.gameObject.activeSelf, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void FoodTips_AppendsExactlyOneFormattedCountAsTermCard()
        {
            GameObject instance = InstantiateFoodTipsPrefab();
            try
            {
                FoodTipsView view = instance.GetComponent<FoodTipsView>();
                var serialized = new SerializedObject(view);
                var specialTagsRoot = (RectTransform)serialized
                    .FindProperty("_specialTagsRoot").objectReferenceValue;
                var summary = new FoodSummaryTipsData(
                    "测试食物",
                    Array.Empty<FoodInfoEntry>(),
                    Array.Empty<string>(),
                    countAs: 4);

                view.Bind(new FoodTipsData(
                    summary,
                    FoodScoreTipsData.Empty,
                    Array.Empty<FoodMaterialTipsEntry>(),
                    Array.Empty<FoodInfoEntry>(),
                    Array.Empty<FoodInfoEntry>(),
                    Array.Empty<FoodInfoEntry>()));

                Assert.That(specialTagsRoot.gameObject.activeSelf, Is.True);
                Assert.That(specialTagsRoot.childCount, Is.EqualTo(1));
                string[] texts = specialTagsRoot
                    .GetComponentsInChildren<TMP_Text>(true)
                    .Select(text => text.text)
                    .ToArray();
                Assert.That(texts, Does.Contain("食物"));
                Assert.That(texts, Does.Contain("视为4 个食物"));

                view.Bind(new FoodTipsData(
                    FoodSummaryTipsData.Empty,
                    FoodScoreTipsData.Empty,
                    Array.Empty<FoodMaterialTipsEntry>(),
                    Array.Empty<FoodInfoEntry>(),
                    Array.Empty<FoodInfoEntry>(),
                    Array.Empty<FoodInfoEntry>()));

                Assert.That(specialTagsRoot.gameObject.activeSelf, Is.False);
                Assert.That(specialTagsRoot.childCount, Is.EqualTo(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static GameplayDatabase CreateDatabase(out DishDef definition)
        {
            var rule = new SkillRuleDef(
                "count_as_self",
                "skill_count_as_self",
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                SkillActionType.AddCountAs,
                SkillScope.Self,
                1,
                new[] { 2f },
                Array.Empty<string>());
            var skill = new SkillDef(
                "skill_count_as_self",
                "",
                "",
                Array.Empty<string>(),
                new[] { rule });
            definition = new DishDef(
                "dish_count_as",
                "测试食物",
                1,
                DishShape.FromRows(new[] { "X" }),
                0,
                0,
                1f,
                new[] { skill.Id },
                string.Empty,
                false,
                countAs: 2);

            return new GameplayDatabase(
                new[] { definition },
                new[] { skill },
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
        }

        private static DiningTable CreateTableWithDish(
            DishDef definition,
            out DishInstance dish)
        {
            var table = new DiningTable(1, 1);
            var placement = new Placement(
                definition.Shape,
                0,
                new GridPos(0, 0));
            dish = new DishInstance(
                1,
                definition,
                placement,
                definition.SkillIds,
                Array.Empty<string>());
            table.Place(dish);
            return table;
        }

        private static GameObject InstantiateFoodTipsPrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodTipsPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            return UnityEngine.Object.Instantiate(prefab);
        }
    }
}
