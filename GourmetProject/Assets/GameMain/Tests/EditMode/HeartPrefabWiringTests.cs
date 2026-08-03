using GourmetProject.Game.UI.Battle.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class HeartPrefabWiringTests
    {
        private const string BattlePrefabPath = "Assets/GameMain/Content/Prefabs/UI/BattleForm.prefab";
        private const string BattleHeartItemPrefabPath = "Assets/GameMain/Content/Prefabs/UI/Hud/BattleHeartItem.prefab";
        private const string HeartBreakPrefabPath = "Assets/GameMain/Content/Prefabs/UI/HeartBreakForm.prefab";
        private const string RewardPrefabPath = "Assets/GameMain/Content/Prefabs/UI/RewardForm.prefab";
        private const string DefeatPrefabPath = "Assets/GameMain/Content/Prefabs/UI/DefeatForm.prefab";

        [Test]
        public void BattleInfoColumn_HasStaticReferenceLayoutAndDynamicHeartPrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BattlePrefabPath);
            Assert.That(prefab, Is.Not.Null);

            RectTransform left = prefab.transform.Find("HudFrame/LeftColumn") as RectTransform;
            Assert.That(left, Is.Not.Null);
            BattleInfoColumn column = left.GetComponent<BattleInfoColumn>();
            Assert.That(column, Is.Not.Null);

            var serialized = new SerializedObject(column);
            string[] references =
            {
                "_weekText",
                "_goldText",
                "_heartContainer",
                "_heartItemPrefab",
                "_heartActiveSprite",
                "_heartEmptySprite",
                "_scoreCurrentText",
                "_scoreRequiredText",
                "_viewRecipeButton",
                "_viewRecipeCountText",
                "_viewTableButton",
                "_viewTableLabelText",
                "_viewTableCountText",
                "_discardCountText",
                "_settingsButton",
                "_scoreFire",
                "_scoreTitlePanel",
                "_bossStat",
                "_bossTitleText",
                "_bossSkillText",
            };
            foreach (string reference in references)
            {
                SerializedProperty property = serialized.FindProperty(reference);
                Assert.That(property, Is.Not.Null, $"BattleInfoColumn 缺少序列化字段 {reference}。");
                Assert.That(property.objectReferenceValue, Is.Not.Null, $"BattleInfoColumn 缺少 {reference} 引用。");
            }

            RectTransform heartContainer = left.Find("HeartContainer") as RectTransform;
            Assert.That(heartContainer, Is.Not.Null);
            HorizontalLayoutGroup heartLayout = heartContainer.GetComponent<HorizontalLayoutGroup>();
            Assert.That(heartLayout, Is.Not.Null);
            Assert.That(heartLayout.spacing, Is.EqualTo(20f));

            GameObject heartPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BattleHeartItemPrefabPath);
            Assert.That(heartPrefab, Is.Not.Null);
            Image heartImage = heartPrefab.GetComponent<Image>();
            Assert.That(heartImage, Is.Not.Null);
            Assert.That(heartImage.sprite, Is.Not.Null);
            Assert.That(heartImage.raycastTarget, Is.False);
            Assert.That((heartPrefab.transform as RectTransform).sizeDelta, Is.EqualTo(new Vector2(48f, 45f)));

            RectTransform background = left.Find("Background") as RectTransform;
            RectTransform week = left.Find("WeekCard") as RectTransform;
            RectTransform gold = left.Find("CurrencyCard") as RectTransform;
            RectTransform scoreTitle = left.Find("ScoreTitlePanel") as RectTransform;
            RectTransform boss = left.Find("BossStat") as RectTransform;
            RectTransform score = left.Find("ScoreSection") as RectTransform;
            RectTransform stats = left.Find("StatsSection") as RectTransform;
            RectTransform settings = left.Find("SettingsCard") as RectTransform;
            Assert.That(background, Is.Not.Null);
            Assert.That(week, Is.Not.Null);
            Assert.That(gold, Is.Not.Null);
            Assert.That(scoreTitle, Is.Not.Null);
            Assert.That(boss, Is.Not.Null);
            Assert.That(score, Is.Not.Null);
            Assert.That(stats, Is.Not.Null);
            Assert.That(settings, Is.Not.Null);
            Assert.That(week.anchoredPosition.y, Is.EqualTo(483f).Within(0.01f));
            Assert.That(gold.anchoredPosition.y, Is.EqualTo(353.5f).Within(0.01f));
            Assert.That(scoreTitle.anchoredPosition.y, Is.EqualTo(190f).Within(0.01f));
            Assert.That(score.anchoredPosition.y, Is.EqualTo(40.5f).Within(0.01f));
            Assert.That(stats.anchoredPosition.y, Is.EqualTo(-289.5f).Within(0.01f));
            Assert.That(settings.anchoredPosition.y, Is.EqualTo(-534f).Within(0.01f));

            Assert.That(boss.gameObject.activeSelf, Is.False);
            Assert.That(boss.pivot, Is.EqualTo(new Vector2(0.27f, 0f)));
            Assert.That(boss.anchoredPosition.x + boss.sizeDelta.x * (1f - boss.pivot.x),
                Is.GreaterThan((left.rect.width * 0.5f)), "Boss 卡应允许越过 LeftColumn 右边界。");
        }

        [Test]
        public void HeartBreakForm_HasAllPresetVisualReferences()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HeartBreakPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Component form = prefab.GetComponent("HeartBreakForm");
            Assert.That(form, Is.Not.Null);

            var serialized = new SerializedObject(form);
            string[] references =
            {
                "_titleText",
                "_heartCountText",
                "_centerHeartText",
                "_leftHalf",
                "_rightHalf",
                "_crackGroup",
                "_contentGroup",
                "_continueButton",
                "_transitionGroup",
                "_transitionPanel",
            };
            foreach (string reference in references)
            {
                Assert.That(
                    serialized.FindProperty(reference).objectReferenceValue,
                    Is.Not.Null,
                    $"HeartBreakForm 缺少 {reference} 引用。");
            }

            Assert.That(prefab.transform.Find("Dim"), Is.Not.Null);
            Assert.That(prefab.transform.Find("Panel/Content/HeartStage/LeftHalf/HeartGlyph"), Is.Not.Null);
            Assert.That(prefab.transform.Find("Panel/Content/HeartStage/RightHalf/HeartGlyph"), Is.Not.Null);
            Transform crack = prefab.transform.Find("Panel/Content/HeartStage/Crack");
            Assert.That(crack, Is.Not.Null);
            Assert.That(crack.childCount, Is.EqualTo(3));

            Button continueButton = prefab.transform.Find("Panel/Content/Continue")?.GetComponent<Button>();
            Assert.That(continueButton, Is.Not.Null);
            Assert.That(continueButton.GetComponentInChildren<Text>(true).text, Is.EqualTo("继续"));
        }

        [Test]
        public void RewardForm_HasPopupTransitionReferences()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RewardPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Component form = prefab.GetComponent("RewardForm");
            Assert.That(form, Is.Not.Null);

            var serialized = new SerializedObject(form);
            Assert.That(serialized.FindProperty("_transitionGroup").objectReferenceValue, Is.Not.Null);
            Assert.That(serialized.FindProperty("_transitionPanel").objectReferenceValue, Is.Not.Null);
        }

        [Test]
        public void DefeatForm_HasPopupTransitionReferences()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefeatPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Component form = prefab.GetComponent("DefeatForm");
            Assert.That(form, Is.Not.Null);

            var serialized = new SerializedObject(form);
            Assert.That(serialized.FindProperty("_transitionGroup").objectReferenceValue, Is.Not.Null);
            Assert.That(serialized.FindProperty("_transitionPanel").objectReferenceValue, Is.Not.Null);
        }

    }
}
