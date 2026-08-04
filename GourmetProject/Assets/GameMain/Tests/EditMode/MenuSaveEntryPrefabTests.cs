using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Tests.EditMode
{
    public sealed class MenuSaveEntryPrefabTests
    {
        private const string MainMenuPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/MainMenuForm.prefab";
        private const string CharacterSelectPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/CharacterSelectForm.prefab";
        private const string ConfirmDialogPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/ConfirmDialogForm.prefab";

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
                startButton.GetComponentInChildren<TMP_Text>(true).text,
                Is.EqualTo("开始游戏"));
            Assert.That(
                FindDescendant(prefab.transform, "AbandonButton"),
                Is.Null);
        }

        [Test]
        public void CharacterSelect_HasHiddenContinueButtonReference()
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
                continueButton.GetComponentInChildren<TMP_Text>(true).text,
                Is.EqualTo("继续游戏"));
            Assert.That(
                confirmButton.GetComponentInChildren<TMP_Text>(true).text,
                Is.EqualTo("开始游戏"));

            Component form = prefab.GetComponent("CharacterSelectForm");
            Assert.That(form, Is.Not.Null);
            var serialized = new SerializedObject(form);
            Assert.That(
                serialized.FindProperty("_continueButton").objectReferenceValue,
                Is.SameAs(continueButton));

            string[] requiredReferenceProperties =
            {
                "_nameText",
                "_descText",
                "_leftArrow",
                "_rightArrow",
                "_confirmButton",
                "_continueButton",
                "_backButton",
                "_confirmLabel",
                "_recipeReadonlyBookView",
                "_recipeViewButton",
                "_recipeViewGlow",
                "_foodTipsPrefab",
            };
            foreach (string propertyName in requiredReferenceProperties)
            {
                Assert.That(
                    serialized.FindProperty(propertyName).objectReferenceValue,
                    Is.Not.Null,
                    propertyName);
            }

            SerializedProperty dots = serialized.FindProperty("_dots");
            Assert.That(dots.arraySize, Is.GreaterThan(0));
            for (int i = 0; i < dots.arraySize; i++)
            {
                Assert.That(
                    dots.GetArrayElementAtIndex(i).objectReferenceValue,
                    Is.Not.Null,
                    $"_dots[{i}]");
            }
        }

        [Test]
        public void CharacterSelect_ResolvesAllRequiredReferences()
        {
            GameObject prefab = PrefabUtility.LoadPrefabContents(
                CharacterSelectPrefabPath);
            try
            {
                Component form = prefab.GetComponent("CharacterSelectForm");
                Assert.That(form, Is.Not.Null);

                System.Reflection.MethodInfo ensureReferences =
                    form.GetType().GetMethod(
                        "EnsureReferences",
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic);
                Assert.That(ensureReferences, Is.Not.Null);
                Assert.DoesNotThrow(
                    () => ensureReferences.Invoke(form, null));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
        }

        [Test]
        public void ConfirmDialog_HasAllSerializedReferences()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ConfirmDialogPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            Component form = prefab.GetComponent("ConfirmDialogForm");
            Assert.That(form, Is.Not.Null);
            var serialized = new SerializedObject(form);

            string[] requiredReferenceProperties =
            {
                "_titleText",
                "_messageText",
                "_confirmButton",
                "_cancelButton",
                "_confirmLabel",
                "_cancelLabel",
                "_confirmButtonRect",
            };
            foreach (string propertyName in requiredReferenceProperties)
            {
                Assert.That(
                    serialized.FindProperty(propertyName).objectReferenceValue,
                    Is.Not.Null,
                    propertyName);
            }
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
