using System;
using System.Collections.Generic;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class DishDragInteractionTests
    {
        [Test]
        public void Evaluate_AllCellsAvailable_ReturnsGreenCommitPlacement()
        {
            var root = new GameObject("MapperRoot");
            try
            {
                var table = new DiningTable(3, 2);
                var mapper = new DiningTableCoordinateMapper(3, 2, 1f, 0f, root.transform);
                DishShape shape = DishShape.FromRows(new[] { "XX" });

                DishDragPlacementResult result = DishDragPlacementEvaluator.Evaluate(
                    table,
                    mapper,
                    shape,
                    0,
                    VisualCenter(mapper, shape, new GridPos(0, 0)));

                Assert.That(result.CanCommit, Is.True);
                Assert.That(result.OverallState, Is.EqualTo(DishDragCellState.Valid));
                Assert.That(result.Origin, Is.EqualTo(new GridPos(0, 0)));
                Assert.That(result.Cells, Has.All.Matches<DishDragCellFeedback>(
                    cell => cell.State == DishDragCellState.Valid));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Evaluate_BlockedAndMissingCells_RedWinsOverYellow()
        {
            var root = new GameObject("MapperRoot");
            try
            {
                var existing = new[] { new GridPos(0, 0), new GridPos(1, 0) };
                var table = new DiningTable(3, 1, existing, null);
                table.Place(CreateDish(10, new GridPos(0, 0)));
                var mapper = new DiningTableCoordinateMapper(3, 1, 1f, 0f, root.transform);
                DishShape shape = DishShape.FromRows(new[] { "XXX" });

                DishDragPlacementResult result = DishDragPlacementEvaluator.Evaluate(
                    table,
                    mapper,
                    shape,
                    0,
                    VisualCenter(mapper, shape, new GridPos(0, 0)));

                Assert.That(result.CanCommit, Is.False);
                Assert.That(result.OverallState, Is.EqualTo(DishDragCellState.Blocked));
                Assert.That(result.Cells, Has.Some.Matches<DishDragCellFeedback>(
                    cell => cell.State == DishDragCellState.Blocked));
                Assert.That(result.Cells, Has.Some.Matches<DishDragCellFeedback>(
                    cell => cell.State == DishDragCellState.Missing));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Evaluate_DisabledCell_IsBlocked()
        {
            var root = new GameObject("MapperRoot");
            try
            {
                var table = new DiningTable(2, 1);
                table.SetDisabled(new GridPos(1, 0), true);
                var mapper = new DiningTableCoordinateMapper(2, 1, 1f, 0f, root.transform);
                DishShape shape = DishShape.FromRows(new[] { "XX" });

                DishDragPlacementResult result = DishDragPlacementEvaluator.Evaluate(
                    table,
                    mapper,
                    shape,
                    0,
                    VisualCenter(mapper, shape, new GridPos(0, 0)));

                Assert.That(result.OverallState, Is.EqualTo(DishDragCellState.Blocked));
                Assert.That(result.Cells[1].State, Is.EqualTo(DishDragCellState.Blocked));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Evaluate_OutOfBoundsCell_IsYellowAndKeepsLockedRotation()
        {
            var root = new GameObject("MapperRoot");
            try
            {
                var table = new DiningTable(2, 1);
                var mapper = new DiningTableCoordinateMapper(2, 1, 1f, 0f, root.transform);
                DishShape shape = DishShape.FromRows(new[] { "XX" });

                DishDragPlacementResult result = DishDragPlacementEvaluator.Evaluate(
                    table,
                    mapper,
                    shape,
                    3,
                    VisualCenter(mapper, shape, new GridPos(1, 0)));

                Assert.That(result.OverallState, Is.EqualTo(DishDragCellState.Missing));
                Assert.That(result.Placement.RotationIndex, Is.EqualTo(3));
                Assert.That(result.Placement.Orientation, Is.SameAs(shape));
                Assert.That(result.Cells[1].Position, Is.EqualTo(new GridPos(2, 0)));
                Assert.That(result.Cells[1].State, Is.EqualTo(DishDragCellState.Missing));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DiningTableFeedback_CreatesOverlayOutsideBoardBounds()
        {
            DiningTableCellView cellPrefab = AssetDatabase.LoadAssetAtPath<DiningTableCellView>(
                "Assets/GameMain/Content/Prefabs/Battle/DiningTableCell.prefab");
            var host = new GameObject("DiningTableView");
            DiningTableView view = host.AddComponent<DiningTableView>();
            try
            {
                var table = new DiningTable(2, 1);
                view.Build(table, 1f, 0f, null, cellPrefab);
                DishShape shape = DishShape.FromRows(new[] { "XX" });
                DishDragPlacementResult result = DishDragPlacementEvaluator.Evaluate(
                    table,
                    view.Mapper,
                    shape,
                    0,
                    VisualCenter(view.Mapper, shape, new GridPos(1, 0)));

                view.ShowDragPlacementFeedback(result);

                Vector3 outside = view.Mapper.CellCenterLocal(new GridPos(2, 0));
                bool found = false;
                foreach (DiningTableCellView child in view.GetComponentsInChildren<DiningTableCellView>(true))
                {
                    if (child.name == "DragPlacementFeedback"
                        && child.gameObject.activeSelf
                        && Vector3.Distance(child.transform.localPosition, outside) < 0.001f)
                    {
                        found = true;
                        break;
                    }
                }

                Assert.That(found, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void DragPresentation_PromotesBodyAndShadowsAndRestoresThem()
        {
            DishPieceView prefab = AssetDatabase.LoadAssetAtPath<DishPieceView>(
                "Assets/GameMain/Content/Prefabs/Battle/DishPiece.prefab");
            DishPieceView view = UnityEngine.Object.Instantiate(prefab);
            try
            {
                DishShape shape = DishShape.FromRows(new[] { "XX" });
                DishInstance dish = CreateDish(1, new GridPos(0, 0), shape);
                view.BuildPlaced(dish, null, 1f, 1f, null);

                view.SetDragPresentation(true);

                AssertRendererLayer(view, "Sprite", "PiecesFlying");
                AssertRendererLayer(view, "Shadow", "PiecesFlying");
                AssertRendererLayer(view, "ShadowHalo", "PiecesFlying");
                Assert.That(view.transform.Find("VisualPivot").localScale.x, Is.GreaterThan(1f));

                Vector3 target = new(4f, -2f, 0f);
                view.MoveVisualCenterToWorld(target);
                SpriteRenderer body = FindRenderer(view, "Sprite");
                Assert.That(Vector2.Distance(body.bounds.center, target), Is.LessThan(0.0001f));

                view.SetDragPresentation(false);

                AssertRendererLayer(view, "Sprite", "Pieces");
                AssertRendererLayer(view, "Shadow", "Pieces");
                AssertRendererLayer(view, "ShadowHalo", "Pieces");
                Assert.That(view.transform.Find("VisualPivot").localScale, Is.EqualTo(Vector3.one));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(view.gameObject);
            }
        }

        [Test]
        public void DishPiecePrefab_HasDesignerTunableScopeAffectedShake()
        {
            DishPieceView prefab = AssetDatabase.LoadAssetAtPath<DishPieceView>(
                "Assets/GameMain/Content/Prefabs/Battle/DishPiece.prefab");

            Assert.That(prefab, Is.Not.Null);
            var serialized = new SerializedObject(prefab);
            SerializedProperty distance = serialized.FindProperty("_scopeAffectedShakeCells");
            SerializedProperty duration = serialized.FindProperty("_scopeAffectedShakeDuration");
            SerializedProperty cycles = serialized.FindProperty("_scopeAffectedShakeCycles");

            Assert.That(distance, Is.Not.Null);
            Assert.That(duration, Is.Not.Null);
            Assert.That(cycles, Is.Not.Null);
            Assert.That(distance.floatValue, Is.GreaterThan(0f));
            Assert.That(duration.floatValue, Is.GreaterThan(0f));
            Assert.That(cycles.floatValue, Is.GreaterThan(0f));
        }

        [Test]
        public void DishDropDustPrefab_HasEditableParticleSystemAndProvidedMaterial()
        {
            DishDropDustView prefab = AssetDatabase.LoadAssetAtPath<DishDropDustView>(
                "Assets/GameMain/Content/Prefabs/Battle/DishDropDust.prefab");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/GameMain/Content/Art/Materials/DishDropDust.mat");
            Texture2D expected = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/GameMain/Content/Resources/Particles/Dust1.png");

            Assert.That(prefab, Is.Not.Null);
            Assert.That(material, Is.Not.Null);
            Assert.That(expected, Is.Not.Null);

            var serialized = new SerializedObject(prefab);
            SerializedProperty particlesProperty = serialized.FindProperty("_particles");
            Assert.That(particlesProperty, Is.Not.Null);
            Assert.That(particlesProperty.objectReferenceValue, Is.Not.Null);

            ParticleSystem particles = prefab.GetComponent<ParticleSystem>();
            ParticleSystemRenderer renderer = prefab.GetComponent<ParticleSystemRenderer>();
            Assert.That(particles, Is.Not.Null);
            Assert.That(renderer, Is.Not.Null);
            Assert.That(particles.main.playOnAwake, Is.False);
            Assert.That(particles.emission.burstCount, Is.GreaterThan(0));
            Assert.That(renderer.sharedMaterial, Is.SameAs(material));
            Assert.That(material.GetTexture("_BaseMap"), Is.SameAs(expected));
            Assert.That(renderer.sortingLayerName, Is.EqualTo("Fx"));

            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            Assert.That(velocity.x.mode, Is.EqualTo(velocity.y.mode));
            Assert.That(velocity.z.mode, Is.EqualTo(velocity.x.mode));
        }

        private static Vector3 VisualCenter(
            DiningTableCoordinateMapper mapper,
            DishShape shape,
            GridPos origin)
        {
            Vector3 sum = Vector3.zero;
            foreach (GridPos cell in shape.Cells)
            {
                sum += new Vector3(cell.X * mapper.Pitch, -cell.Y * mapper.Pitch, 0f);
            }

            Vector3 local = mapper.CellCenterLocal(origin) + sum / shape.CellCount;
            return mapper.Root != null ? mapper.Root.TransformPoint(local) : local;
        }

        private static DishInstance CreateDish(int id, GridPos origin, DishShape shape = null)
        {
            shape ??= DishShape.FromRows(new[] { "X" });
            var def = new DishDef(
                $"drag_test_{id}",
                "拖拽测试",
                1,
                shape,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                false);
            return new DishInstance(
                id,
                def,
                new Placement(shape, 0, origin),
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        private static void AssertRendererLayer(DishPieceView view, string name, string layer)
        {
            Assert.That(FindRenderer(view, name)?.sortingLayerName, Is.EqualTo(layer));
        }

        private static SpriteRenderer FindRenderer(DishPieceView view, string objectName)
        {
            foreach (SpriteRenderer renderer in view.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer.name == objectName)
                {
                    return renderer;
                }
            }

            return null;
        }
    }
}
