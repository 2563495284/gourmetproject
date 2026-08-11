using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Battle.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BattleDoodleConfigurationTests
    {
        private const string PrefabPath = "Assets/GameMain/Content/Prefabs/UI/BattleForm.prefab";

        [Test]
        public void CanvasShader_IsPackagedAndSupported()
        {
            Shader shader = Resources.Load<Shader>("Shaders/BattleDoodleCanvas");

            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
        }

        [Test]
        public void BattleForm_ContainsDoodleCanvasAndFourToolButtons()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);

            Transform panel = prefab.transform.Find("HudFrame/Center/FoodBattlePanel");
            Assert.That(panel, Is.Not.Null);

            Transform canvas = panel.Find("DoodleCanvas");
            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.GetSiblingIndex(), Is.Zero);
            Assert.That(canvas.GetComponent<RawImage>(), Is.Not.Null);
            Assert.That(canvas.GetComponent<RawImage>().raycastTarget, Is.False);

            Transform tools = panel.Find("FoodActions/DoodleTools");
            Assert.That(tools, Is.Not.Null);
            Assert.That(prefab.GetComponent<CanvasGroup>(), Is.Not.Null);
            Assert.That(tools.GetComponent<CanvasGroup>(), Is.Not.Null);
            Assert.That(tools.GetComponent<CanvasGroup>().ignoreParentGroups, Is.True);
            Assert.That(tools.Find("DoodleDrawButton")?.GetComponent<Button>(), Is.Not.Null);
            Assert.That(tools.Find("DoodleEraseButton")?.GetComponent<Button>(), Is.Not.Null);
            Assert.That(tools.Find("DoodleClearButton")?.GetComponent<Button>(), Is.Not.Null);
            Assert.That(tools.Find("DoodleToggleButton")?.GetComponent<Button>(), Is.Not.Null);

            BattleFoodActionBar bar = panel.GetComponentInChildren<BattleFoodActionBar>(true);
            Assert.That(bar, Is.Not.Null);
            Transform recipeInfo = panel.Find("FoodActions/RecipeInfoButton");
            Assert.That(recipeInfo?.GetComponent<Button>(), Is.Not.Null);
            Assert.That(bar.RecipeInfoButtonRect, Is.EqualTo(recipeInfo as RectTransform));
            var serializedBar = new SerializedObject(bar);
            foreach (string fieldName in new[]
                     {
                         "_doodleCanvas",
                         "_recipeInfoButton",
                         "_recipeInfoText",
                         "_doodleDrawButton",
                         "_doodleEraseButton",
                         "_doodleClearButton",
                         "_doodleToggleButton",
                         "_doodleDrawSprite",
                         "_doodleDrawGlowSprite",
                         "_doodleEraseSprite",
                         "_doodleEraseGlowSprite",
                         "_doodleClearSprite",
                     })
            {
                Assert.That(
                    serializedBar.FindProperty(fieldName)?.objectReferenceValue,
                    Is.Not.Null,
                    fieldName);
                }
        }

        [Test]
        public void LocalPointToUv_UsesCanvasRectInsteadOfWholeScreen()
        {
            var rect = new Rect(-640f, -540f, 1280f, 1080f);

            Assert.That(
                BattleDoodleController.LocalPointToUv(rect, new Vector2(-640f, -540f)),
                Is.EqualTo(Vector2.zero));
            Assert.That(
                BattleDoodleController.LocalPointToUv(rect, Vector2.zero),
                Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(
                BattleDoodleController.LocalPointToUv(rect, new Vector2(640f, 540f)),
                Is.EqualTo(Vector2.one));
        }

        [Test]
        public void ToggleTool_IsMutuallyExclusiveAndCanCancelItself()
        {
            var gameObject = new GameObject("BattleDoodleControllerTest");
            try
            {
                BattleDoodleController controller = gameObject.AddComponent<BattleDoodleController>();
                Assert.That(controller.Tool, Is.EqualTo(BattleDoodleTool.None));
                Assert.That(controller.ToggleTool(BattleDoodleTool.Draw), Is.EqualTo(BattleDoodleTool.Draw));
                Assert.That(controller.ToggleTool(BattleDoodleTool.Draw), Is.EqualTo(BattleDoodleTool.None));
                Assert.That(controller.ToggleTool(BattleDoodleTool.Erase), Is.EqualTo(BattleDoodleTool.Erase));
                Assert.That(controller.ToggleTool(BattleDoodleTool.Draw), Is.EqualTo(BattleDoodleTool.Draw));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void DoodleToolbar_AllowsSwitchingToolsWhileOneIsActive()
        {
            var worldObject = new GameObject("BattleWorldControllerTest");
            var actionsObject = new GameObject("FoodActions", typeof(RectTransform));
            try
            {
                BattleDoodleController doodle = worldObject.AddComponent<BattleDoodleController>();
                BattleWorldController world = worldObject.AddComponent<BattleWorldController>();
                var serializedWorld = new SerializedObject(world);
                serializedWorld.FindProperty("_doodle").objectReferenceValue = doodle;
                serializedWorld.ApplyModifiedPropertiesWithoutUndo();

                BattleFoodActionBar bar = actionsObject.AddComponent<BattleFoodActionBar>();
                var toolsObject = new GameObject("DoodleTools", typeof(RectTransform), typeof(CanvasGroup));
                toolsObject.transform.SetParent(actionsObject.transform, false);
                Button drawButton = CreateButton("DoodleDrawButton", toolsObject.transform);
                Button eraseButton = CreateButton("DoodleEraseButton", toolsObject.transform);
                Button clearButton = CreateButton("DoodleClearButton", toolsObject.transform);
                Button toggleButton = CreateButton("DoodleToggleButton", toolsObject.transform);

                var serializedBar = new SerializedObject(bar);
                serializedBar.FindProperty("_doodleDrawButton").objectReferenceValue = drawButton;
                serializedBar.FindProperty("_doodleEraseButton").objectReferenceValue = eraseButton;
                serializedBar.FindProperty("_doodleClearButton").objectReferenceValue = clearButton;
                serializedBar.FindProperty("_doodleToggleButton").objectReferenceValue = toggleButton;
                serializedBar.ApplyModifiedPropertiesWithoutUndo();

                bar.Bind(
                    null,
                    null,
                    () => world.ToggleDoodleTool(BattleDoodleTool.Draw),
                    () => world.ToggleDoodleTool(BattleDoodleTool.Erase),
                    world.ClearDoodle,
                    () => world.ToggleDoodleVisible());

                drawButton.onClick.Invoke();
                bar.Refresh(true, null, world);

                Assert.That(world.DoodleTool, Is.EqualTo(BattleDoodleTool.Draw));
                Assert.That(eraseButton.interactable, Is.True,
                    "画笔激活后仍应能直接点击橡皮。");
                Assert.That(clearButton.interactable, Is.True);
                Assert.That(toggleButton.interactable, Is.True);

                eraseButton.onClick.Invoke();
                bar.Refresh(true, null, world);

                Assert.That(world.DoodleTool, Is.EqualTo(BattleDoodleTool.Erase));
                Assert.That(drawButton.interactable, Is.True,
                    "切到橡皮后仍应能直接切回画笔。");
            }
            finally
            {
                Object.DestroyImmediate(actionsObject);
                Object.DestroyImmediate(worldObject);
            }
        }

        private static Button CreateButton(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Button));
            gameObject.transform.SetParent(parent, false);
            return gameObject.GetComponent<Button>();
        }
    }
}
