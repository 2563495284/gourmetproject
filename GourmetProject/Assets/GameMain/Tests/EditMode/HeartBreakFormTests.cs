using GourmetProject.Game.UI.Meta;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class HeartBreakFormTests
    {
        private const string PrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Meta/Rewards/HeartBreakForm.prefab";
        private const string FullHeartSpritePath =
            "Assets/GameMain/Content/Resources/Sprites/UI/FengKuangCanTing/Icons/icon_heart_active.png";
        private const string EmptyHeartSpritePath =
            "Assets/GameMain/Content/Resources/Sprites/UI/FengKuangCanTing/Icons/icon_heart_empty.png";

        [TestCase(3, 2, 3, false, 3, 2, 3, 1)]
        [TestCase(4, 3, 5, false, 4, 3, 5, 1)]
        [TestCase(1, 0, 3, true, 1, 0, 3, 1)]
        [TestCase(0, 0, 3, true, 1, 0, 3, 1)]
        [TestCase(9, -2, 5, false, 5, 0, 5, 5)]
        [TestCase(1, 4, 3, false, 3, 3, 3, 0)]
        public void Normalize_ClampsCountsAndRebuildsLegacyTerminalLoss(
            int before,
            int after,
            int capacity,
            bool isTerminal,
            int expectedBefore,
            int expectedAfter,
            int expectedCapacity,
            int expectedLost)
        {
            HeartBreakHeartRowState state = HeartBreakHeartRowState.Normalize(
                before,
                after,
                capacity,
                isTerminal);

            Assert.That(state.BeforeHeartCount, Is.EqualTo(expectedBefore));
            Assert.That(state.AfterHeartCount, Is.EqualTo(expectedAfter));
            Assert.That(state.Capacity, Is.EqualTo(expectedCapacity));
            Assert.That(state.LostHeartCount, Is.EqualTo(expectedLost));
            Assert.That(state.IsTerminal, Is.EqualTo(isTerminal));
        }

        [Test]
        public void Prefab_HasHeartRowReferencesAndNoLegacyBreakObjects()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);

            HeartBreakForm form = prefab.GetComponent<HeartBreakForm>();
            HeartBreakHeartRow row = prefab.GetComponentInChildren<HeartBreakHeartRow>(true);
            Assert.That(form, Is.Not.Null);
            Assert.That(row, Is.Not.Null);

            var formObject = new SerializedObject(form);
            Assert.That(formObject.FindProperty("_heartRow").objectReferenceValue, Is.SameAs(row));
            Assert.That(formObject.FindProperty("_continueButton").objectReferenceValue, Is.Not.Null);
            Assert.That(formObject.FindProperty("_transitionGroup").objectReferenceValue, Is.Not.Null);
            Assert.That(formObject.FindProperty("_transitionPanel").objectReferenceValue, Is.Not.Null);

            var rowObject = new SerializedObject(row);
            Sprite fullHeartSprite = AssetDatabase.LoadAssetAtPath<Sprite>(FullHeartSpritePath);
            Sprite emptyHeartSprite = AssetDatabase.LoadAssetAtPath<Sprite>(EmptyHeartSpritePath);
            Assert.That(fullHeartSprite, Is.Not.Null);
            Assert.That(emptyHeartSprite, Is.Not.Null);
            Assert.That(rowObject.FindProperty("_heartTemplate").objectReferenceValue, Is.TypeOf<Image>());
            Assert.That(rowObject.FindProperty("_statusText").objectReferenceValue, Is.TypeOf<TextMeshProUGUI>());
            Assert.That(rowObject.FindProperty("_fullHeartSprite").objectReferenceValue, Is.SameAs(fullHeartSprite));
            Assert.That(rowObject.FindProperty("_emptyHeartSprite").objectReferenceValue, Is.SameAs(emptyHeartSprite));

            Transform heartRow = prefab.transform.Find("Panel/Content/HeartRow");
            Assert.That(heartRow, Is.Not.Null);
            Assert.That(heartRow.Find("HeartTemplate"), Is.Not.Null);
            Assert.That(heartRow.Find("LeftHalf"), Is.Null);
            Assert.That(heartRow.Find("RightHalf"), Is.Null);
            Assert.That(heartRow.Find("Crack"), Is.Null);
        }
    }
}
