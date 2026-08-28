using System;
using System.Collections.Generic;
using System.Reflection;
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
        public void DiningTableBuild_GrowAndShrink_ReusesCellsAndKeepsCountStable()
        {
            DiningTableCellView cellPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/GameMain/Content/Prefabs/Battle/Board/DiningTableCell.prefab")
                ?.GetComponent<DiningTableCellView>();
            Assert.That(cellPrefab, Is.Not.Null);

            var root = new GameObject("DiningTablePoolTest");
            try
            {
                DiningTableView view = root.AddComponent<DiningTableView>();
                view.Build(new DiningTable(4, 4), 1f, 0.04f, null, cellPrefab);
                Dictionary<GridPos, DiningTableCellView> cells = GetPrivateField<
                    Dictionary<GridPos, DiningTableCellView>>(view, "_cells");
                DiningTableCellView origin = cells[new GridPos(0, 0)];

                view.Build(new DiningTable(12, 12), 1f, 0.04f, null, cellPrefab);
                GameObjectPool pool = GetPrivateField<GameObjectPool>(view, "_cellPool");
                Assert.That(cells.Count, Is.EqualTo(144));
                Assert.That(pool.CountAll, Is.EqualTo(144));
                Assert.That(cells[new GridPos(0, 0)], Is.SameAs(origin));

                view.Build(new DiningTable(4, 4), 1f, 0.04f, null, cellPrefab);
                Assert.That(cells.Count, Is.EqualTo(16));
                Assert.That(pool.CountAll, Is.EqualTo(144));
                Assert.That(pool.CountInactive, Is.EqualTo(128));
                Assert.That(cells[new GridPos(0, 0)], Is.SameAs(origin));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
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

        private static T GetPrivateField<T>(object owner, string fieldName)
        {
            FieldInfo field = owner.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(owner);
        }
    }
}
