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
        private const string HeartBreakPrefabPath = "Assets/GameMain/Content/Prefabs/UI/HeartBreakForm.prefab";
        private const string RewardPrefabPath = "Assets/GameMain/Content/Prefabs/UI/RewardForm.prefab";
        private const string DefeatPrefabPath = "Assets/GameMain/Content/Prefabs/UI/DefeatForm.prefab";

        [Test]
        public void BattleInfoColumn_HasHeartTextAndNonOverlappingCards()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BattlePrefabPath);
            Assert.That(prefab, Is.Not.Null);

            Transform left = prefab.transform.Find("HudFrame/LeftColumn");
            Assert.That(left, Is.Not.Null);
            BattleInfoColumn column = left.GetComponent<BattleInfoColumn>();
            Assert.That(column, Is.Not.Null);

            var serialized = new SerializedObject(column);
            Text heart = serialized.FindProperty("_heartText").objectReferenceValue as Text;
            Assert.That(heart, Is.Not.Null);
            Assert.That(heart.name, Is.EqualTo("HeartValue"));
            Assert.That(heart.resizeTextForBestFit, Is.True);
            Assert.That(heart.font, Is.Not.Null);
            Assert.That(heart.font.HasCharacter('♥'), Is.True);

            RectTransform week = left.Find("WeekStat") as RectTransform;
            RectTransform gold = left.Find("GoldStat") as RectTransform;
            RectTransform boss = left.Find("BossStat") as RectTransform;
            RectTransform score = left.Find("LeftColumnScoreStat") as RectTransform;
            Assert.That(week, Is.Not.Null);
            Assert.That(gold, Is.Not.Null);
            Assert.That(boss, Is.Not.Null);
            Assert.That(score, Is.Not.Null);
            Assert.That(Top(gold), Is.LessThan(Bottom(week)));
            Assert.That(Top(boss), Is.LessThan(Bottom(gold)));
            Assert.That(Top(score), Is.LessThan(Bottom(boss)));
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

        private static float Top(RectTransform rect)
        {
            return rect.anchoredPosition.y + rect.sizeDelta.y * (1f - rect.pivot.y);
        }

        private static float Bottom(RectTransform rect)
        {
            return rect.anchoredPosition.y - rect.sizeDelta.y * rect.pivot.y;
        }
    }
}
