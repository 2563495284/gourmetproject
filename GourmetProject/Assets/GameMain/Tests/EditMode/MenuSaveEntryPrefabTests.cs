using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class MenuSaveEntryPrefabTests
    {
        private const string MainMenuPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/MainMenuForm.prefab";
        private const string CharacterSelectPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/CharacterSelectForm.prefab";

        [Test]
        public void MainMenu_HasFixedStartEntryAndNoAbandonButton()
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(MainMenuPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            Button startButton = FindDescendant(prefab.transform, "StartButton")
                ?.GetComponent<Button>();
            Assert.That(startButton, Is.Not.Null);
            Assert.That(
                startButton.GetComponentInChildren<Text>(true).text,
                Is.EqualTo("开始游戏"));
            Assert.That(
                FindDescendant(prefab.transform, "AbandonButton"),
                Is.Null);
        }

        [Test]
        public void CharacterSelect_HasHiddenContinueButtonBesideConfirmButton()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                CharacterSelectPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            Transform continueTransform =
                FindDescendant(prefab.transform, "ContinueButton");
            Transform confirmTransform =
                FindDescendant(prefab.transform, "ConfirmButton");
            Assert.That(continueTransform, Is.Not.Null);
            Assert.That(confirmTransform, Is.Not.Null);
            Assert.That(continueTransform.gameObject.activeSelf, Is.False);

            Button continueButton = continueTransform.GetComponent<Button>();
            Button confirmButton = confirmTransform.GetComponent<Button>();
            Assert.That(continueButton, Is.Not.Null);
            Assert.That(confirmButton, Is.Not.Null);
            Assert.That(
                continueButton.GetComponentInChildren<Text>(true).text,
                Is.EqualTo("继续游戏"));
            Assert.That(
                confirmButton.GetComponentInChildren<Text>(true).text,
                Is.EqualTo("开始游戏"));

            var continueRect = (RectTransform)continueTransform;
            var confirmRect = (RectTransform)confirmTransform;
            Assert.That(
                continueRect.anchoredPosition.y,
                Is.EqualTo(confirmRect.anchoredPosition.y));
            Assert.That(
                continueRect.anchoredPosition.x,
                Is.LessThan(confirmRect.anchoredPosition.x));

            Component form = prefab.GetComponent("CharacterSelectForm");
            Assert.That(form, Is.Not.Null);
            var serialized = new SerializedObject(form);
            Assert.That(
                serialized.FindProperty("_continueButton").objectReferenceValue,
                Is.SameAs(continueButton));
        }

        private static Transform FindDescendant(
            Transform root,
            string objectName)
        {
            foreach (Transform child in
                     root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == objectName)
                {
                    return child;
                }
            }

            return null;
        }
    }
}
