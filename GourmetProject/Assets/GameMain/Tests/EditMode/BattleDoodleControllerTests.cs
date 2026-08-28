using System.Collections.Generic;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BattleDoodleControllerTests
    {
        private const string BattleFormPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Battle/BattleForm.prefab";

        [Test]
        public void CanvasSizeForGrid_UsesTargetDensityAndCapsLongestEdge()
        {
            Assert.That(
                BattleDoodleController.CanvasSizeForGrid(18, 12),
                Is.EqualTo(new Vector2Int(1152, 768)));
            Assert.That(
                BattleDoodleController.CanvasSizeForGrid(36, 24),
                Is.EqualTo(new Vector2Int(2048, 1365)));
        }

        [Test]
        public void BrushRadiusPixels_IsExpressedAsCellFraction()
        {
            Assert.That(
                BattleDoodleController.BrushRadiusPixels(60f, BattleDoodleTool.Draw),
                Is.EqualTo(2f).Within(0.0001f));
            Assert.That(
                BattleDoodleController.BrushRadiusPixels(60f, BattleDoodleTool.Erase),
                Is.EqualTo(12f).Within(0.0001f));
        }

        [Test]
        public void CanvasUvForLogicalCell_IsStableWhenTableCellSizeChanges()
        {
            var root = new GameObject("DoodleMapperRoot");
            try
            {
                var large = new DiningTableCoordinateMapper(12, 12, 1.2f, 0f, root.transform);
                var small = new DiningTableCoordinateMapper(12, 12, 0.6f, 0f, root.transform);
                GridPos cell = new(4, 4);

                Vector2 largeUv = BattleDoodleController.CanvasUvForLocalPoint(
                    large,
                    large.CellCenterLocal(cell));
                Vector2 smallUv = BattleDoodleController.CanvasUvForLocalPoint(
                    small,
                    small.CellCenterLocal(cell));

                Assert.That(largeUv.x, Is.EqualTo(0.375f).Within(0.0001f));
                Assert.That(largeUv.y, Is.EqualTo(0.625f).Within(0.0001f));
                Assert.That(smallUv.x, Is.EqualTo(largeUv.x).Within(0.0001f));
                Assert.That(smallUv.y, Is.EqualTo(largeUv.y).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CanvasUvRectForBounds_ExpandsWithoutChangingExistingLogicalCoordinates()
        {
            var root = new GameObject("DoodleBoundsRoot");
            try
            {
                var mapper = new DiningTableCoordinateMapper(12, 12, 1f, 0f, root.transform);
                var oldBounds = new TableFragmentBuilder.PlacementBounds(4, 4, 7, 7);
                var expandedBounds = new TableFragmentBuilder.PlacementBounds(3, 4, 7, 7);

                Rect oldUv = BattleDoodleController.CanvasUvRectForBounds(mapper, oldBounds);
                Rect expandedUv = BattleDoodleController.CanvasUvRectForBounds(mapper, expandedBounds);
                Vector2 existingCellUv = BattleDoodleController.CanvasUvForLocalPoint(
                    mapper,
                    mapper.CellCenterLocal(new GridPos(4, 4)));

                Assert.That(oldUv.xMin, Is.EqualTo(1f / 3f).Within(0.0001f));
                Assert.That(oldUv.xMax, Is.EqualTo(2f / 3f).Within(0.0001f));
                Assert.That(expandedUv.xMin, Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(expandedUv.xMax, Is.EqualTo(oldUv.xMax).Within(0.0001f));
                Assert.That(existingCellUv.x, Is.EqualTo(0.375f).Within(0.0001f));
                Assert.That(existingCellUv.y, Is.EqualTo(0.625f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ProjectVisibleBoundsToScreenRect_FollowsRootMoveAndScale()
        {
            var root = new GameObject("DoodleProjectionRoot");
            var cameraObject = new GameObject("DoodleProjectionCamera");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 5f;
                camera.pixelRect = new Rect(0f, 0f, 1000f, 1000f);
                cameraObject.transform.position = new Vector3(0f, 0f, -10f);

                var mapper = new DiningTableCoordinateMapper(12, 12, 1f, 0f, root.transform);
                var bounds = new TableFragmentBuilder.PlacementBounds(4, 4, 7, 7);
                Rect initial = BattleDoodleController.ProjectVisibleBoundsToScreenRect(
                    mapper,
                    bounds,
                    camera);

                root.transform.position = new Vector3(1f, 0f, 0f);
                Rect moved = BattleDoodleController.ProjectVisibleBoundsToScreenRect(
                    mapper,
                    bounds,
                    camera);
                root.transform.localScale = Vector3.one * 2f;
                Rect scaled = BattleDoodleController.ProjectVisibleBoundsToScreenRect(
                    mapper,
                    bounds,
                    camera);

                Assert.That(moved.center.x - initial.center.x, Is.EqualTo(100f).Within(0.1f));
                Assert.That(moved.width, Is.EqualTo(initial.width).Within(0.1f));
                Assert.That(scaled.center.x, Is.EqualTo(moved.center.x).Within(0.1f));
                Assert.That(scaled.width, Is.EqualTo(initial.width * 2f).Within(0.1f));
                Assert.That(scaled.height, Is.EqualTo(initial.height * 2f).Within(0.1f));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PresentationHide_PreservesUserVisibilityAndCanvas()
        {
            var root = new GameObject("DoodleLifecycleRoot");
            var cameraObject = new GameObject("DoodleLifecycleCamera");
            var controllerObject = new GameObject("DoodleLifecycleController");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                cameraObject.transform.position = new Vector3(0f, 0f, -10f);
                var table = new DiningTable(
                    2,
                    2,
                    new List<GridPos> { new(0, 0), new(1, 0), new(0, 1), new(1, 1) });
                var mapper = new DiningTableCoordinateMapper(2, 2, 1f, 0f, root.transform);
                BattleDoodleController controller =
                    controllerObject.AddComponent<BattleDoodleController>();
                controller.ConfigureTable(mapper, table, camera);
                controller.SetVisible(true);
                controller.SetPresentationActive(true);
                RenderTexture canvas = controller.CanvasTexture;

                controller.SetTool(BattleDoodleTool.Draw);
                controller.SetPresentationActive(false);

                Assert.That(controller.IsVisible, Is.True);
                Assert.That(controller.IsPresentationActive, Is.False);
                Assert.That(controller.Tool, Is.EqualTo(BattleDoodleTool.None));
                Assert.That(controller.CanvasTexture, Is.SameAs(canvas));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BattleFormPrefab_ContainsDoodleCanvasAndTools()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BattleFormPrefabPath);
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
            Assert.That(tools.Find("DoodleDrawButton")?.GetComponent<Button>(), Is.Not.Null);
            Assert.That(tools.Find("DoodleEraseButton")?.GetComponent<Button>(), Is.Not.Null);
            Assert.That(tools.Find("DoodleClearButton")?.GetComponent<Button>(), Is.Not.Null);
            Assert.That(panel.GetComponentInChildren<BattleFoodActionBar>(true), Is.Not.Null);
        }

        [Test]
        public void CanvasShader_IsPackagedAndSupported()
        {
            Shader shader = Resources.Load<Shader>("Shaders/BattleDoodleCanvas");

            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
        }
    }
}
