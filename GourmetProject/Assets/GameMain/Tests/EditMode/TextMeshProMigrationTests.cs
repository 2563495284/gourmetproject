using System;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class TextMeshProMigrationTests
    {
        private const string ContentRoot = "Assets/GameMain/Content";
        private const string SceneRoot = "Assets/GameMain/Content/Scenes";
        private const string AlimamaFontPath =
            "Assets/GameMain/Content/Resources/Fonts/AlimamaShuHeiTi-Bold SDF.asset";

        [Test]
        public void BusinessAssetsContainNoLegacyTextOrDropdownComponents()
        {
            int legacyUi = 0;
            int legacyWorld = 0;
            int legacyDropdown = 0;
            int missingScripts = 0;

            foreach (string path in FindAssets("t:Prefab", ContentRoot))
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    legacyUi += root.GetComponentsInChildren<Text>(true).Length;
                    legacyWorld += root.GetComponentsInChildren<TextMesh>(true).Length;
                    legacyDropdown += root.GetComponentsInChildren<Dropdown>(true).Length;
                    missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(root);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            string activeScenePath = SceneManager.GetActiveScene().path;
            try
            {
                foreach (string path in FindAssets("t:Scene", SceneRoot))
                {
                    Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        legacyUi += root.GetComponentsInChildren<Text>(true).Length;
                        legacyWorld += root.GetComponentsInChildren<TextMesh>(true).Length;
                        legacyDropdown += root.GetComponentsInChildren<Dropdown>(true).Length;
                        missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(root);
                    }
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(activeScenePath))
                {
                    EditorSceneManager.OpenScene(activeScenePath, OpenSceneMode.Single);
                }
            }

            Assert.That(legacyUi, Is.Zero, "Found legacy UnityEngine.UI.Text components.");
            Assert.That(legacyWorld, Is.Zero, "Found legacy UnityEngine.TextMesh components.");
            Assert.That(legacyDropdown, Is.Zero, "Found Dropdown components that still require legacy Text.");
            Assert.That(missingScripts, Is.Zero, "Found missing MonoBehaviour scripts after migration.");
        }

        [Test]
        public void TmpDropdownsHaveCompleteTemplateAndTextReferences()
        {
            foreach (string path in FindAssets("t:Prefab", ContentRoot))
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (TMP_Dropdown dropdown in root.GetComponentsInChildren<TMP_Dropdown>(true))
                    {
                        Assert.That(dropdown.template, Is.Not.Null, $"{path}: {dropdown.name} template");
                        Toggle itemToggle = dropdown.template.GetComponentInChildren<Toggle>(true);
                        Assert.That(itemToggle, Is.Not.Null, $"{path}: {dropdown.name} template item Toggle");
                        Assert.That(
                            itemToggle.transform,
                            Is.Not.SameAs(dropdown.template),
                            $"{path}: {dropdown.name} item Toggle must be a template child");
                        Assert.That(
                            itemToggle.transform.parent,
                            Is.InstanceOf<RectTransform>(),
                            $"{path}: {dropdown.name} item Toggle parent RectTransform");
                        Assert.That(dropdown.captionText, Is.Not.Null, $"{path}: {dropdown.name} captionText");
                        Assert.That(dropdown.itemText, Is.Not.Null, $"{path}: {dropdown.name} itemText");
                        Assert.That(
                            dropdown.itemText.transform.IsChildOf(itemToggle.transform),
                            Is.True,
                            $"{path}: {dropdown.name} itemText must belong to the item Toggle");
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        [Test]
        public void TmpSettingsUseAlimamaAsFallbackFont()
        {
            TMP_FontAsset alimama = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AlimamaFontPath);
            Assert.That(alimama, Is.Not.Null);
            Assert.That(TMP_Settings.fallbackFontAssets, Does.Contain(alimama));
        }

        private static string[] FindAssets(string filter, string folder)
        {
            return AssetDatabase.FindAssets(filter, new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
    }
}
