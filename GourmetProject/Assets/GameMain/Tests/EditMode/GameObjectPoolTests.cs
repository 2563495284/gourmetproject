using System;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime.Pooling;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class GameObjectPoolTests
    {
        private GameObject _prefab;
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject("PoolPrefab", typeof(BoxCollider2D));
            _root = new GameObject("PoolRoot");
        }

        [TearDown]
        public void TearDown()
        {
            if (_prefab != null)
            {
                UnityEngine.Object.DestroyImmediate(_prefab);
            }

            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }
        }

        [Test]
        public void PrewarmAndGet_ReusesTypedInstanceAndTracksPeak()
        {
            var pool = new GameObjectPool(_prefab, _root.transform, prewarm: 2, maxInactive: 4);

            Assert.That(pool.CountAll, Is.EqualTo(2));
            Assert.That(pool.CountInactive, Is.EqualTo(2));

            BoxCollider2D first = pool.Get<BoxCollider2D>();
            int firstId = first.gameObject.GetInstanceID();
            pool.Release(first);
            BoxCollider2D reused = pool.Get<BoxCollider2D>();

            Assert.That(reused.gameObject.GetInstanceID(), Is.EqualTo(firstId));
            Assert.That(pool.PeakActive, Is.EqualTo(1));
            pool.Release(reused);
            pool.Clear();
        }

        [Test]
        public void Get_ReparentsAndRestoresPrefabLocalTransform()
        {
            _prefab.transform.localPosition = new Vector3(2f, 3f, 0f);
            _prefab.transform.localScale = new Vector3(0.5f, 0.75f, 1f);
            var parent = new GameObject("RuntimeParent");
            var pool = new GameObjectPool(_prefab, _root.transform, prewarm: 1, maxInactive: 2);

            try
            {
                GameObject instance = pool.Get(parent.transform);
                Assert.That(instance.transform.parent, Is.SameAs(parent.transform));
                Assert.That(instance.transform.localPosition, Is.EqualTo(_prefab.transform.localPosition));
                Assert.That(instance.transform.localScale, Is.EqualTo(_prefab.transform.localScale));
                pool.Release(instance);
                Assert.That(instance.transform.parent, Is.SameAs(_root.transform));
                Assert.That(instance.activeSelf, Is.False);
                pool.Clear();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Release_RejectsDuplicateAndForeignInstances()
        {
            var pool = new GameObjectPool(_prefab, _root.transform, maxInactive: 2);
            var otherPool = new GameObjectPool(_prefab, _root.transform, maxInactive: 2);
            GameObject instance = pool.Get();
            pool.Release(instance);

            Assert.Throws<InvalidOperationException>(() => pool.Release(instance));

            GameObject foreign = otherPool.Get();
            Assert.Throws<InvalidOperationException>(() => pool.Release(foreign));
            otherPool.Release(foreign);
            pool.Clear();
            otherPool.Clear();
        }

        [Test]
        public void Release_DestroysOverflowBeyondMaxInactive()
        {
            var pool = new GameObjectPool(_prefab, _root.transform, maxInactive: 1);
            GameObject first = pool.Get();
            GameObject second = pool.Get();

            pool.Release(first);
            pool.Release(second);

            Assert.That(pool.CountAll, Is.EqualTo(1));
            Assert.That(pool.CountInactive, Is.EqualTo(1));
            pool.Clear();
        }

        [Test]
        public void DestroyCallback_RunsForOverflowAndClear()
        {
            int destroys = 0;
            var pool = new GameObjectPool(
                _prefab,
                _root.transform,
                maxInactive: 1,
                onDestroy: _ => destroys++);
            GameObject first = pool.Get();
            GameObject second = pool.Get();

            pool.Release(first);
            pool.Release(second);
            Assert.That(destroys, Is.EqualTo(1));

            pool.Clear();
            Assert.That(destroys, Is.EqualTo(2));
        }

        [Test]
        public void Callbacks_RunForPrewarmGetAndRelease()
        {
            int gets = 0;
            int releases = 0;
            var pool = new GameObjectPool(
                _prefab,
                _root.transform,
                prewarm: 1,
                maxInactive: 2,
                onGet: _ => gets++,
                onRelease: _ => releases++);

            Assert.That(releases, Is.EqualTo(1));
            GameObject instance = pool.Get();
            pool.Release(instance);

            Assert.That(gets, Is.EqualTo(1));
            Assert.That(releases, Is.EqualTo(2));
            pool.Clear();
        }

        [Test]
        public void ExternallyDestroyedInactiveObject_IsRemovedFromStatisticsOnNextGet()
        {
            var pool = new GameObjectPool(_prefab, _root.transform, prewarm: 1, maxInactive: 2);
            GameObject inactive = _root.transform.GetChild(0).gameObject;
            UnityEngine.Object.DestroyImmediate(inactive);

            GameObject replacement = pool.Get();

            Assert.That(pool.CountAll, Is.EqualTo(1));
            Assert.That(pool.CountInactive, Is.Zero);
            pool.Release(replacement);
            pool.Clear();
        }
    }

    public sealed class BattlePoolingReuseTests
    {
        [Test]
        public void DiningTableBuild_IrregularBoard_UsesOneBatchWithoutStableCellViews()
        {
            DiningTableCellView cellPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/GameMain/Content/Prefabs/Battle/Board/DiningTableCell.prefab")
                ?.GetComponent<DiningTableCellView>();
            Assert.That(cellPrefab, Is.Not.Null);

            var root = new GameObject("DiningTablePoolTest");
            try
            {
                DiningTableView view = root.AddComponent<DiningTableView>();
                var initialCells = new[]
                {
                    new GridPos(4, 5),
                    new GridPos(5, 5),
                    new GridPos(5, 6),
                };
                view.Build(new DiningTable(36, 36, initialCells), 1f, 0.04f, null, cellPrefab);
                DiningTableBatchRenderer batch = root.GetComponent<DiningTableBatchRenderer>();
                GameObjectPool pool = GetPrivateField<GameObjectPool>(view, "_cellPool");
                Mesh mesh = batch.Mesh;

                Assert.That(batch, Is.Not.Null);
                Assert.That(batch.CellCount, Is.EqualTo(initialCells.Length));
                Assert.That(batch.Renderer.enabled, Is.True);
                Assert.That(batch.VertexCount, Is.GreaterThan(0));
                Assert.That(root.GetComponentsInChildren<DiningTableCellView>().Length, Is.Zero);
                Assert.That(pool.CountAll, Is.Zero);

                var expandedCells = new[]
                {
                    new GridPos(4, 5),
                    new GridPos(5, 5),
                    new GridPos(5, 6),
                    new GridPos(6, 6),
                };
                view.Build(new DiningTable(36, 36, expandedCells), 1f, 0.04f, null, cellPrefab);

                Assert.That(batch.CellCount, Is.EqualTo(expandedCells.Length));
                Assert.That(batch.Mesh, Is.SameAs(mesh));
                Assert.That(root.GetComponentsInChildren<DiningTableCellView>().Length, Is.Zero);
                Assert.That(pool.CountAll, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DiningTableTransientPool_ReusedCellClearsVisualAndHoverState()
        {
            DiningTableCellView cellPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/GameMain/Content/Prefabs/Battle/Board/DiningTableCell.prefab")
                ?.GetComponent<DiningTableCellView>();
            Assert.That(cellPrefab, Is.Not.Null);

            var root = new GameObject("DiningTableTransientResetTest");
            try
            {
                DiningTableView view = root.AddComponent<DiningTableView>();
                view.Build(new DiningTable(4, 4), 1f, 0.04f, null, cellPrefab);
                DiningTableCellView first = view.RentCell(root.transform);
                first.Configure(
                    new GridPos(0, 0),
                    Vector3.zero,
                    1f,
                    DiningTableCellSpriteResources.LoadDefault(),
                    null);
                first.SetHoverCallbacks(_ => { }, _ => { });
                first.SetHoveredFromTable(true);
                first.SetPlateFeedbackColor(Color.red);

                view.ReturnCell(first);
                DiningTableCellView reused = view.RentCell(root.transform);

                Assert.That(reused, Is.SameAs(first));
                Assert.That(GetPrivateField<bool>(reused, "_hovered"), Is.False);
                Assert.That(GetPrivateField<bool>(reused, "_plateFeedbackActive"), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TableFragmentEvaluation_ReusesHotPathCollectionsAndResultObjects()
        {
            var mapperRoot = new GameObject("TableFragmentEvaluationBufferTest");
            try
            {
                var table = new DiningTable(4, 4);
                var mapper = new DiningTableCoordinateMapper(4, 4, 1f, 0.04f, mapperRoot.transform);
                var fragment = new TableFragmentDef(
                    "buffer_test",
                    new[] { "XX", "X." },
                    0,
                    0,
                    1f);
                var buffer = new TableFragmentPlacementEvaluationBuffer();

                TableFragmentPlacementEvaluation first = TableFragmentPlacementEvaluator.Evaluate(
                    table,
                    mapper,
                    fragment,
                    mapper.CellCenter(new GridPos(4, 1)),
                    12,
                    12,
                    buffer);
                GridPlacementFeedback feedback = first.Feedback;
                IReadOnlyList<GridPlacementFeedbackCell> cells = feedback.Cells;

                TableFragmentPlacementEvaluation second = TableFragmentPlacementEvaluator.Evaluate(
                    table,
                    mapper,
                    fragment,
                    mapper.CellCenter(new GridPos(4, 2)),
                    12,
                    12,
                    buffer);

                Assert.That(second, Is.SameAs(first));
                Assert.That(second.Feedback, Is.SameAs(feedback));
                Assert.That(second.Feedback.Cells, Is.SameAs(cells));
                Assert.That(second.Feedback.Cells.Count, Is.EqualTo(3));

                Vector3 measuredPosition = mapper.CellCenter(new GridPos(4, 2));
                for (int i = 0; i < 10; i++)
                {
                    TableFragmentPlacementEvaluator.Evaluate(
                        table,
                        mapper,
                        fragment,
                        measuredPosition,
                        12,
                        12,
                        buffer);
                }

                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++)
                {
                    TableFragmentPlacementEvaluator.Evaluate(
                        table,
                        mapper,
                        fragment,
                        measuredPosition,
                        12,
                        12,
                        buffer);
                }
                long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
                Assert.That(allocatedAfter - allocatedBefore, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mapperRoot);
            }
        }

        [Test]
        public void SweetTransfer_RepeatedFlight_DoesNotCreateMoreChildSprites()
        {
            SweetTransferParticleView prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/GameMain/Content/Prefabs/Battle/Effects/SweetTransferParticle.prefab")
                ?.GetComponent<SweetTransferParticleView>();
            Assert.That(prefab, Is.Not.Null);

            var root = new GameObject("SweetTransferPoolTest");
            var pool = new GameObjectPool(
                prefab.gameObject,
                root.transform,
                prewarm: 1,
                maxInactive: 2,
                onGet: go => go.GetComponent<SweetTransferParticleView>()?.PrepareForReuse(),
                onRelease: go => go.GetComponent<SweetTransferParticleView>()?.WarmupForPool());
            try
            {
                SweetTransferParticleView first = SweetTransferParticleView.Begin(
                    prefab,
                    root.transform,
                    Vector3.zero,
                    Vector3.one,
                    0.1f,
                    pool: pool);
                int firstRendererCount = root.GetComponentsInChildren<SpriteRenderer>(true).Length;
                pool.Release(first);

                SweetTransferParticleView second = SweetTransferParticleView.Begin(
                    prefab,
                    root.transform,
                    Vector3.zero,
                    Vector3.one,
                    0.1f,
                    pool: pool);
                int secondRendererCount = root.GetComponentsInChildren<SpriteRenderer>(true).Length;

                Assert.That(firstRendererCount, Is.EqualTo(40));
                Assert.That(secondRendererCount, Is.EqualTo(firstRendererCount));
                Assert.That(second, Is.SameAs(first));
                pool.Release(second);
            }
            finally
            {
                pool.Clear();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DropDust_Replay_UsesPrefabMultipliersInsteadOfCompounding()
        {
            DishDropDustView prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/GameMain/Content/Prefabs/Battle/Effects/DishDropDust.prefab")
                ?.GetComponent<DishDropDustView>();
            Assert.That(prefab, Is.Not.Null);

            DishDropDustView view = UnityEngine.Object.Instantiate(prefab);
            try
            {
                ParticleSystem particles = GetPrivateField<ParticleSystem>(view, "_particles");
                view.PrepareForReuse();
                view.Play(Vector3.zero, new Vector2(2f, 3f), 4, Vector2.zero, _ => { });
                float firstSize = particles.main.startSizeMultiplier;
                float firstVelocityX = particles.velocityOverLifetime.xMultiplier;
                view.ResetForPool();

                view.PrepareForReuse();
                view.Play(Vector3.zero, new Vector2(2f, 3f), 4, Vector2.zero, _ => { });
                Assert.That(particles.main.startSizeMultiplier, Is.EqualTo(firstSize).Within(0.0001f));
                Assert.That(particles.velocityOverLifetime.xMultiplier, Is.EqualTo(firstVelocityX).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(view.gameObject);
            }
        }

        [Test]
        public void SettlementStage_WarmsOnlySpritePoolAndReturnsTransientsOnClear()
        {
            SettlementStageView prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/GameMain/Content/Prefabs/Battle/Settlement/SettlementStage.prefab")
                ?.GetComponent<SettlementStageView>();
            Assert.That(prefab, Is.Not.Null);

            SettlementStageView stage = UnityEngine.Object.Instantiate(prefab);
            try
            {
                MethodInfo ensurePools = typeof(SettlementStageView).GetMethod(
                    "EnsurePools",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(ensurePools, Is.Not.Null);
                ensurePools.Invoke(stage, null);
                GameObjectPool spritePool = GetPrivateField<GameObjectPool>(stage, "_spritePool");
                Assert.That(spritePool.CountInactive, Is.EqualTo(12));
                Assert.That(
                    typeof(SettlementStageView).GetField(
                        "_labelPool",
                        BindingFlags.Instance | BindingFlags.NonPublic),
                    Is.Null);
                Assert.That(
                    typeof(SettlementStageView).GetField(
                        "_finaleLabelPool",
                        BindingFlags.Instance | BindingFlags.NonPublic),
                    Is.Null);

                MethodInfo createSprite = typeof(SettlementStageView).GetMethod(
                    "CreateSprite",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(createSprite, Is.Not.Null);
                createSprite.Invoke(stage, new object[]
                {
                    "PoolTestSprite",
                    Vector3.zero,
                    Color.white,
                    0,
                });

                Assert.That(spritePool.CountActive, Is.EqualTo(1));
                stage.ClearImmediate();
                Assert.That(spritePool.CountActive, Is.Zero);
                Assert.That(spritePool.CountAll, Is.EqualTo(12));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(stage.gameObject);
            }
        }

        [Test]
        public void BattleWorld_ClearDuringTemporaryFlyInInvalidatesOwnershipAndReleasesView()
        {
            var worldObject = new GameObject("BattleWorldFlyInPoolTest");
            worldObject.SetActive(false);
            BattleWorldController world = worldObject.AddComponent<BattleWorldController>();
            var pieceObject = new GameObject("TemporaryFlyInPiece");
            DishPieceView piece = pieceObject.AddComponent<DishPieceView>();
            Tween tween = DOTween.Sequence()
                .Append(piece.transform.DOMove(Vector3.one, 10f))
                .SetLink(pieceObject);

            try
            {
                Dictionary<int, DishPieceView> dishViews =
                    GetPrivateField<Dictionary<int, DishPieceView>>(world, "_dishViewsById");
                dishViews[42] = piece;
                SetPrivateField(world, "_temporaryAreaFlyInPiece", piece);
                SetPrivateField(world, "_temporaryAreaFlyInTween", tween);

                MethodInfo clear = typeof(BattleWorldController).GetMethod(
                    "ClearPlacedPieces",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(clear, Is.Not.Null);
                clear.Invoke(world, null);

                Assert.That(piece == null, Is.True);
                Assert.That(
                    GetPrivateField<DishPieceView>(world, "_temporaryAreaFlyInPiece"),
                    Is.Null);
                Assert.That(
                    GetPrivateField<Tween>(world, "_temporaryAreaFlyInTween"),
                    Is.Null);
                Assert.That(
                    GetPrivateField<int>(world, "_temporaryAreaFlyInVersion"),
                    Is.GreaterThan(0));
                Assert.That(dishViews, Is.Empty);
            }
            finally
            {
                tween?.Kill(false);
                if (pieceObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(pieceObject);
                }
                UnityEngine.Object.DestroyImmediate(worldObject);
            }
        }

        [Test]
        public void DishPiece_ReusedGeometryRefreshesFlavorMaterial()
        {
            DishPieceView prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/GameMain/Content/Prefabs/Battle/Dishes/DishPiece.prefab")
                ?.GetComponent<DishPieceView>();
            Assert.That(prefab, Is.Not.Null);

            DishPieceView view = UnityEngine.Object.Instantiate(prefab);
            var texture = new Texture2D(2, 2);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), Vector2.one * 0.5f);
            try
            {
                DishShape shape = DishShape.FromRows(new[] { "X" });
                var definition = new DishDef(
                    "pool_refresh_dish",
                    "Pool Refresh Dish",
                    10,
                    shape,
                    0,
                    0,
                    1f,
                    Array.Empty<string>(),
                    string.Empty);
                var dish = new DishInstance(
                    7,
                    definition,
                    new Placement(shape, 0, new GridPos(0, 0)),
                    Array.Empty<string>(),
                    Array.Empty<string>());

                view.BuildPlaced(dish, sprite, 1f, 1.04f, null);
                SpriteRenderer renderer = GetPrivateField<SpriteRenderer>(view, "_spriteRenderer");
                Assert.That(renderer.sharedMaterial, Is.Not.SameAs(SpriteRenderStyle.SpriteFlavorOrganicMaterial));

                dish.AddFlavor("t_sweet");
                view.PrepareForReuse();
                view.BuildPlaced(dish, sprite, 1f, 1.04f, null);

                Assert.That(renderer.sharedMaterial, Is.SameAs(SpriteRenderStyle.SpriteFlavorOrganicMaterial));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(view.gameObject);
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static T GetPrivateField<T>(object owner, string fieldName)
        {
            FieldInfo field = owner.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(owner);
        }

        private static void SetPrivateField(object owner, string fieldName, object value)
        {
            FieldInfo field = owner.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(owner, value);
        }
    }
}
