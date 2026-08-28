using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class DiningTableBatchRendererTests
    {
        private const string CellPrefabPath =
            "Assets/GameMain/Content/Prefabs/Battle/Board/DiningTableCell.prefab";

        private GameObject _root;
        private DiningTableView _view;
        private DiningTableCellView _cellPrefab;

        [SetUp]
        public void SetUp()
        {
            _cellPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CellPrefabPath)
                ?.GetComponent<DiningTableCellView>();
            Assert.That(_cellPrefab, Is.Not.Null);
            _root = new GameObject("DiningTableBatchTest");
            _view = _root.AddComponent<DiningTableView>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        [Test]
        public void Build_UsesExistingCellsOnlyAndMatchesSourceSpriteGeometry()
        {
            var positions = new[]
            {
                new GridPos(10, 12),
                new GridPos(11, 12),
                new GridPos(11, 13),
            };
            _view.Build(new DiningTable(36, 36, positions), 0.8f, 0.04f, null, _cellPrefab);

            DiningTableBatchRenderer batch = _root.GetComponent<DiningTableBatchRenderer>();
            Sprite sprite = DiningTableCellSpriteResources.LoadDefault().Plate;
            Mesh mesh = batch.Mesh;

            Assert.That(batch.CellCount, Is.EqualTo(positions.Length));
            Assert.That(batch.VertexCount, Is.EqualTo(sprite.vertices.Length * positions.Length));
            Assert.That(batch.IndexCount, Is.EqualTo(sprite.triangles.Length * positions.Length));
            Assert.That(_root.GetComponents<MeshRenderer>().Length, Is.EqualTo(1));
            Assert.That(_root.GetComponentsInChildren<SpriteRenderer>().Length, Is.Zero);
            Assert.That(_root.GetComponentsInChildren<BoxCollider2D>().Length, Is.Zero);

            Assert.That(_view.TryGetCellWorldPosition(positions[0], out Vector3 center), Is.True);
            Assert.That(_view.TryGetCellWorldBounds(positions[0], out Bounds bounds), Is.True);
            Assert.That(bounds.Contains(center), Is.True);
            Assert.That(_view.Mapper.ContainsWorldPoint(positions[0], center), Is.True);
            Assert.That(_view.TryGetCellWorldPosition(new GridPos(0, 0), out _), Is.False);

            Vector2[] sourceVertices = sprite.vertices;
            Vector2[] sourceUv = sprite.uv;
            Vector3 expectedFirstVertex = _view.Mapper.CellCenterLocal(positions[0])
                + _cellPrefab.BatchVisualLocalMatrix.MultiplyPoint3x4(sourceVertices[0]) * 0.8f;
            Assert.That(Vector3.Distance(mesh.vertices[0], expectedFirstVertex), Is.LessThan(0.0001f));
            Assert.That(Vector2.Distance(mesh.uv[0], sourceUv[0]), Is.LessThan(0.0001f));
            Assert.That(mesh.colors32[0], Is.EqualTo(new Color32(255, 255, 255, 255)));

            int geometryRevision = batch.GeometryRevision;
            int uploads = batch.StreamUploadCount;
            _view.Sync();
            batch.FlushPendingChanges();
            Assert.That(batch.GeometryRevision, Is.EqualTo(geometryRevision));
            Assert.That(batch.StreamUploadCount, Is.EqualTo(uploads));
        }

        [Test]
        public void PlaceholderMode_KeepsOneBatchWhileCoveringFullLogicalBounds()
        {
            _view.Build(
                new DiningTable(3, 2, new[] { new GridPos(1, 1) }),
                1f,
                0.04f,
                null,
                _cellPrefab);
            DiningTableBatchRenderer batch = _root.GetComponent<DiningTableBatchRenderer>();

            Assert.That(batch.CellCount, Is.EqualTo(1));
            _view.ShowVoidAsPlaceholders(true);
            Assert.That(batch.CellCount, Is.EqualTo(6));
            Assert.That(_root.GetComponents<MeshRenderer>().Length, Is.EqualTo(1));

            _view.ShowVoidAsPlaceholders(false);
            Assert.That(batch.CellCount, Is.EqualTo(1));
        }

        [Test]
        public void BossPresentation_PreservesRemovedTombstoneAndRevealsAddedCell()
        {
            var kept = new GridPos(0, 0);
            var added = new GridPos(1, 0);
            var removed = new GridPos(2, 0);
            var board = new DiningTable(3, 1, new[] { kept, added });
            _view.Build(board, 1f, 0.04f, null, _cellPrefab);
            var plan = new BossDebuffPresentationPlan("test", null);
            plan.AddedCells.Add(added);
            plan.RemovedCells.Add(removed);

            _view.StageBossPresentation(plan);
            DiningTableBatchRenderer batch = _root.GetComponent<DiningTableBatchRenderer>();

            Assert.That(batch.CellCount, Is.EqualTo(3));
            Assert.That(batch.TryGetCellState(added, out Color addedColor, out _, out _, out _), Is.True);
            Assert.That(addedColor.a, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(batch.TryGetCellState(removed, out _, out bool removedDebuffed, out _, out _), Is.True);
            Assert.That(removedDebuffed, Is.False);

            _view.RevealAddedCell(added);
            _view.RevealRemovedCell(removed);
            Assert.That(batch.TryGetCellState(added, out addedColor, out _, out _, out _), Is.True);
            Assert.That(addedColor.a, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(batch.TryGetCellState(removed, out _, out removedDebuffed, out _, out _), Is.True);
            Assert.That(removedDebuffed, Is.True);

            _view.FinishBossPresentation();
            Assert.That(batch.CellCount, Is.EqualTo(3));
            Assert.That(batch.Contains(removed), Is.True);
        }

        [Test]
        public void TargetFeedbackAndPulse_UpdateCellStreamWithoutChangingTopology()
        {
            var position = new GridPos(0, 0);
            _view.Build(new DiningTable(1, 1), 1f, 0.04f, null, _cellPrefab);
            DiningTableBatchRenderer batch = _root.GetComponent<DiningTableBatchRenderer>();
            int vertices = batch.VertexCount;
            int indices = batch.IndexCount;

            _view.SetTargetHighlight(position, selected: true, hovered: false);
            batch.SetPulse(position, 1f);
            batch.FlushPendingChanges();

            Assert.That(batch.TryGetCellState(position, out _, out _, out bool feedback, out float pulse), Is.True);
            Assert.That(feedback, Is.True);
            Assert.That(pulse, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(batch.VertexCount, Is.EqualTo(vertices));
            Assert.That(batch.IndexCount, Is.EqualTo(indices));

            _view.ClearTargetHighlights();
            batch.ClearPulses();
            batch.FlushPendingChanges();
            Assert.That(batch.TryGetCellState(position, out _, out _, out feedback, out pulse), Is.True);
            Assert.That(feedback, Is.False);
            Assert.That(pulse, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void CancelPulseAndDestroy_ReleasesRuntimeResources()
        {
            var position = new GridPos(0, 0);
            _view.Build(new DiningTable(1, 1), 1f, 0.04f, null, _cellPrefab);
            DiningTableBatchRenderer batch = _root.GetComponent<DiningTableBatchRenderer>();
            Mesh mesh = batch.Mesh;
            Material material = batch.Renderer.sharedMaterial;

            batch.SetPulse(position, 1f);
            _view.CancelSettlementPulses();
            Assert.That(batch.TryGetCellState(position, out _, out _, out _, out float pulse), Is.True);
            Assert.That(pulse, Is.EqualTo(0f).Within(0.0001f));

            batch.ReleaseRuntimeResources(destroyImmediately: true);
            Assert.That(mesh == null, Is.True);
            Assert.That(material == null, Is.True);
            Object.DestroyImmediate(_root);
            _root = null;
        }
    }
}
