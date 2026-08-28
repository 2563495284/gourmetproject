#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Common;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SettlementTextBatchRendererTests
    {
        private const string EffectPrefabPath =
            "Assets/GameMain/Content/Prefabs/Battle/Settlement/SettlementEffectLabel.prefab";
        private const string StagePrefabPath =
            "Assets/GameMain/Content/Prefabs/Battle/Settlement/SettlementStageLabel.prefab";
        private const string FinalePrefabPath =
            "Assets/GameMain/Content/Prefabs/Battle/Settlement/SettlementFinaleLabel.prefab";

        private GameObject _host;
        private GameObject _surfaceParent;
        private SettlementTextBatchRenderer _batch;
        private FloatingTextView _effectPrefab;
        private SettlementStageLabelView _stagePrefab;
        private SettlementStageLabelView _finalePrefab;

        [SetUp]
        public void SetUp()
        {
            _effectPrefab = LoadComponent<FloatingTextView>(EffectPrefabPath);
            _stagePrefab = LoadComponent<SettlementStageLabelView>(StagePrefabPath);
            _finalePrefab = LoadComponent<SettlementStageLabelView>(FinalePrefabPath);
            _host = new GameObject("Settlement Text Batch Test Host");
            _surfaceParent = new GameObject("Settlement Text Batch Test Surface");
            _surfaceParent.transform.SetParent(_host.transform, false);
            _batch = _host.AddComponent<SettlementTextBatchRenderer>();

            Assert.That(
                _batch.Initialize(_effectPrefab, _stagePrefab, _finalePrefab),
                Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                UnityEngine.Object.DestroyImmediate(_host);
            }
        }

        [Test]
        public void AnimationSamples_PreserveStageAndFloatingCurves()
        {
            const float start = 10f;
            const float duration = 2f;

            SettlementTextAnimationSample stageStart =
                SettlementTextBatchRenderer.EvaluateAnimation(
                    SettlementTextAnimationKind.Stage,
                    start,
                    start,
                    duration,
                    1f,
                    0.10f,
                    hold: false);
            SettlementTextAnimationSample stageSettled =
                SettlementTextBatchRenderer.EvaluateAnimation(
                    SettlementTextAnimationKind.Stage,
                    start + duration * 0.48f,
                    start,
                    duration,
                    1f,
                    -0.10f,
                    hold: false);
            SettlementTextAnimationSample stageEnd =
                SettlementTextBatchRenderer.EvaluateAnimation(
                    SettlementTextAnimationKind.Stage,
                    start + duration,
                    start,
                    duration,
                    1f,
                    0.10f,
                    hold: false);
            SettlementTextAnimationSample heldEnd =
                SettlementTextBatchRenderer.EvaluateAnimation(
                    SettlementTextAnimationKind.Stage,
                    start + duration,
                    start,
                    duration,
                    1f,
                    0.10f,
                    hold: true);

            Assert.That(stageStart.Scale, Is.EqualTo(0.80f).Within(0.0001f));
            Assert.That(stageStart.VerticalOffset, Is.Zero.Within(0.0001f));
            Assert.That(stageStart.Alpha, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(stageSettled.Scale, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(stageSettled.VerticalOffset, Is.EqualTo(-0.10f).Within(0.0001f));
            Assert.That(stageEnd.Alpha, Is.Zero.Within(0.0001f));
            Assert.That(heldEnd.Alpha, Is.EqualTo(1f).Within(0.0001f));

            SettlementTextAnimationSample delayed =
                SettlementTextBatchRenderer.EvaluateAnimation(
                    SettlementTextAnimationKind.Floating,
                    start - 0.1f,
                    start,
                    duration,
                    1.5f,
                    0.8f,
                    hold: false);
            SettlementTextAnimationSample floatingHalf =
                SettlementTextBatchRenderer.EvaluateAnimation(
                    SettlementTextAnimationKind.Floating,
                    start + duration * 0.5f,
                    start,
                    duration,
                    1.5f,
                    0.8f,
                    hold: false);

            Assert.That(delayed.Alpha, Is.Zero.Within(0.0001f));
            Assert.That(delayed.VerticalOffset, Is.Zero.Within(0.0001f));
            Assert.That(floatingHalf.Scale, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(floatingHalf.VerticalOffset, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(floatingHalf.Alpha, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void SlotReuse_RejectsAStaleHandleAndClearReleasesEverything()
        {
            SettlementTextBatchHandle first = SpawnStage("第一次");
            _batch.AdvanceForTests(Time.time);
            Assert.That(_batch.IsAlive(first), Is.True);

            _batch.Release(first);
            _batch.AdvanceForTests(Time.time);
            SettlementTextBatchHandle reused = SpawnStage("第二次");

            Assert.That(reused.Slot, Is.EqualTo(first.Slot));
            Assert.That(reused.Generation, Is.GreaterThan(first.Generation));
            _batch.Release(first);
            Assert.That(_batch.IsAlive(reused), Is.True);

            _batch.ClearAll();
            Assert.That(_batch.ActiveLabelCount, Is.Zero);
            Assert.That(_batch.VisibleVertexCount, Is.Zero);
        }

        [Test]
        public void ClearThenSpawnAgain_RetainsBuffersAndRenders()
        {
            SettlementTextBatchHandle first = SpawnStage("第一次");
            _batch.AdvanceForTests(Time.time);
            Mesh mesh = _batch.GetSurfaceMeshForTests(_surfaceParent.transform);
            MeshRenderer renderer =
                _batch.GetSurfaceRendererForTests(_surfaceParent.transform);
            int vertexCapacity = _batch.VertexCapacity;
            int indexCapacity = _batch.IndexCapacity;
            int subMeshCount = mesh.subMeshCount;

            Assert.That(_batch.IsAlive(first), Is.True);
            Assert.That(subMeshCount, Is.GreaterThan(0));

            _batch.ClearAll();

            Assert.That(_batch.VisibleVertexCount, Is.Zero);
            Assert.That(renderer.enabled, Is.False);
            Assert.That(_batch.VertexCapacity, Is.EqualTo(vertexCapacity));
            Assert.That(_batch.IndexCapacity, Is.EqualTo(indexCapacity));
            Assert.That(mesh.subMeshCount, Is.EqualTo(subMeshCount));
            Assert.That(renderer.sharedMaterials, Is.Empty);

            SettlementTextBatchHandle second = SpawnStage("第二次");
            Assert.DoesNotThrow(() => _batch.AdvanceForTests(Time.time));

            Assert.That(_batch.IsAlive(second), Is.True);
            Assert.That(mesh.subMeshCount, Is.GreaterThan(0));
            Assert.That(
                Enumerable.Range(0, mesh.subMeshCount)
                    .Sum(index => (long)mesh.GetIndexCount(index)),
                Is.GreaterThan(0));
            Assert.That(renderer.enabled, Is.True);
        }

        [Test]
        public void DisableEnableThenSpawn_ForcesFreshMaterialLayoutAndUpload()
        {
            SettlementTextBatchHandle first = SpawnStage("禁用前");
            _batch.AdvanceForTests(Time.time);
            MeshRenderer renderer =
                _batch.GetSurfaceRendererForTests(_surfaceParent.transform);
            Assert.That(first.IsValid, Is.True);
            Assert.That(renderer.enabled, Is.True);

            _host.SetActive(false);
            _host.SetActive(true);

            SettlementTextBatchHandle second = SpawnStage("恢复后");
            _batch.AdvanceForTests(Time.time + 0.01f);

            Assert.That(second.IsValid, Is.True);
            Assert.That(renderer.enabled, Is.True);
            Assert.That(_batch.VisibleVertexCount, Is.GreaterThan(0));
            Assert.That(renderer.sharedMaterials, Is.Not.Empty);
            Assert.That(renderer.sharedMaterials.All(material => material != null), Is.True);
        }

        [Test]
        public void GeometryCache_ReusesIdenticalVisualsAndSeparatesDifferentStyles()
        {
            int bakeCount = _batch.GeometryBakeCount;
            SettlementTextBatchHandle first = _batch.SpawnFloating(
                _surfaceParent.transform,
                Vector3.zero,
                "来源",
                "分数 [score]+10[/score]",
                Color.white,
                rise: 0.9f,
                duration: 10f,
                delay: 0f,
                visualScale: 1f,
                sortingOrder: 20);
            SettlementTextBatchHandle repeated = _batch.SpawnFloating(
                _surfaceParent.transform,
                Vector3.one,
                "来源",
                "分数 [score]+10[/score]",
                Color.white,
                rise: 0.5f,
                duration: 2f,
                delay: 0.2f,
                visualScale: 1.5f,
                sortingOrder: 30);

            Assert.That(first.IsValid, Is.True);
            Assert.That(repeated.IsValid, Is.True);
            Assert.That(_batch.GeometryBakeCount, Is.EqualTo(bakeCount + 1));
            Assert.That(_batch.GeometryCacheHitCount, Is.EqualTo(1));

            _batch.SpawnFloating(
                _surfaceParent.transform,
                Vector3.zero,
                "来源",
                "分数 [score]+10[/score]",
                Color.red,
                rise: 0.9f,
                duration: 10f,
                delay: 0f,
                visualScale: 1f,
                sortingOrder: 40);

            Assert.That(_batch.GeometryBakeCount, Is.EqualTo(bakeCount + 2));
            Assert.That(_batch.GeometryCacheCount, Is.EqualTo(2));

            int defaultColorBakeCount = _batch.GeometryBakeCount;
            _batch.SpawnFloating(
                _surfaceParent.transform,
                Vector3.zero,
                string.Empty,
                "默认色共享",
                effectColor: null,
                rise: 0f,
                duration: 10f,
                delay: 0f,
                visualScale: 1f,
                sortingOrder: 50);
            FloatingTextView layoutTemplate = _batch
                .GetComponentsInChildren<FloatingTextView>(true)
                .Single();
            _batch.SpawnFloating(
                _surfaceParent.transform,
                Vector3.one,
                string.Empty,
                "默认色共享",
                layoutTemplate.EffectText.color,
                rise: 1f,
                duration: 1f,
                delay: 1f,
                visualScale: 2f,
                sortingOrder: 60);
            Assert.That(_batch.GeometryBakeCount, Is.EqualTo(defaultColorBakeCount + 1));

            int stageBakeCount = _batch.GeometryBakeCount;
            SpawnStage("同一舞台");
            _batch.SpawnStage(
                _surfaceParent.transform,
                Vector3.one,
                "技能触发",
                "同一舞台",
                Color.white,
                finalStamp: false,
                duration: 1f,
                visualScale: 2f,
                holdUntilCleared: false,
                headerSemanticColor: null,
                sortingOrder: 80,
                verticalDriftDirection: -1f,
                impactScale: 2f);
            Assert.That(_batch.GeometryBakeCount, Is.EqualTo(stageBakeCount + 1));

            _batch.SpawnStage(
                _surfaceParent.transform,
                Vector3.zero,
                "技能触发",
                "同一舞台",
                Color.white,
                finalStamp: true,
                duration: 1f,
                visualScale: 1f,
                holdUntilCleared: false,
                headerSemanticColor: null,
                sortingOrder: 90,
                verticalDriftDirection: 1f,
                impactScale: 1f);
            Assert.That(_batch.GeometryBakeCount, Is.EqualTo(stageBakeCount + 2));
        }

        [Test]
        public void GeometryCache_UsesBoundedLruAndTemplateChangeInvalidatesIt()
        {
            for (int i = 0; i <= SettlementTextBatchRenderer.GeometryCacheCapacity; i++)
            {
                _batch.SpawnFloating(
                    _surfaceParent.transform,
                    Vector3.zero,
                    string.Empty,
                    $"唯一效果 {i}",
                    effectColor: null,
                    rise: 0f,
                    duration: 10f,
                    delay: 0f,
                    visualScale: 1f,
                    sortingOrder: i);
            }

            Assert.That(
                _batch.GeometryCacheCount,
                Is.EqualTo(SettlementTextBatchRenderer.GeometryCacheCapacity));
            int bakeCount = _batch.GeometryBakeCount;
            _batch.SpawnFloating(
                _surfaceParent.transform,
                Vector3.zero,
                string.Empty,
                "唯一效果 0",
                effectColor: null,
                rise: 0f,
                duration: 10f,
                delay: 0f,
                visualScale: 1f,
                sortingOrder: 1000);
            Assert.That(_batch.GeometryBakeCount, Is.EqualTo(bakeCount + 1));

            _batch.AdvanceForTests(Time.time);
            int vertexCapacity = _batch.VertexCapacity;
            int indexCapacity = _batch.IndexCapacity;
            Assert.That(_batch.RetainedMaterialGroupCount, Is.GreaterThan(0));

            FloatingTextView alternateEffect = UnityEngine.Object.Instantiate(_effectPrefab);
            try
            {
                Assert.That(
                    _batch.Initialize(alternateEffect, _stagePrefab, _finalePrefab),
                    Is.True);
                Assert.That(_batch.GeometryCacheCount, Is.Zero);
                Assert.That(_batch.GeometryCacheBytes, Is.Zero);
                Assert.That(_batch.GeometryBakeCount, Is.Zero);
                Assert.That(_batch.RetainedMaterialGroupCount, Is.Zero);
                Assert.That(_batch.VertexCapacity, Is.EqualTo(vertexCapacity));
                Assert.That(_batch.IndexCapacity, Is.EqualTo(indexCapacity));

                _batch.SpawnFloating(
                    _surfaceParent.transform,
                    Vector3.zero,
                    string.Empty,
                    "重初始化后",
                    effectColor: null,
                    rise: 0f,
                    duration: 10f,
                    delay: 0f,
                    visualScale: 1f,
                    sortingOrder: 20);
                _batch.AdvanceForTests(Time.time);
                Assert.That(_batch.RetainedMaterialGroupCount, Is.GreaterThan(0));
                Assert.That(_batch.VertexCapacity, Is.EqualTo(vertexCapacity));
                Assert.That(_batch.IndexCapacity, Is.EqualTo(indexCapacity));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(alternateEffect.gameObject);
            }
        }

        [Test]
        public void GeometryCache_OwnsStableMaterialsAcrossTemplateRebinds()
        {
            const string cachedText = "[strong]稳定缓存[/strong] [multmul]×2[/multmul]";
            SettlementTextBatchHandle first = _batch.SpawnFloating(
                _surfaceParent.transform,
                Vector3.zero,
                "来源",
                cachedText,
                effectColor: null,
                rise: 0f,
                duration: 10f,
                delay: 0f,
                visualScale: 1f,
                sortingOrder: 20);
            _batch.AdvanceForTests(Time.time);
            _batch.Release(first);

            int registeredAfterFirstBake = _batch.RegisteredMaterialCount;
            for (int i = 0; i < 32; i++)
            {
                SettlementTextBatchHandle transient = _batch.SpawnFloating(
                    _surfaceParent.transform,
                    Vector3.zero,
                    string.Empty,
                    $"变化文本 {i}",
                    effectColor: null,
                    rise: 0f,
                    duration: 10f,
                    delay: 0f,
                    visualScale: 1f,
                    sortingOrder: 30 + i);
                _batch.Release(transient);
            }

            SettlementTextBatchHandle cached = _batch.SpawnFloating(
                _surfaceParent.transform,
                Vector3.zero,
                "来源",
                cachedText,
                effectColor: null,
                rise: 0f,
                duration: 10f,
                delay: 0f,
                visualScale: 1f,
                sortingOrder: 100);
            _batch.AdvanceForTests(Time.time + 0.01f);
            MeshRenderer renderer =
                _batch.GetSurfaceRendererForTests(_surfaceParent.transform);

            Assert.That(cached.IsValid, Is.True);
            Assert.That(_batch.GeometryCacheHitCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(_batch.RegisteredMaterialCount, Is.GreaterThanOrEqualTo(registeredAfterFirstBake));
            Assert.That(renderer.enabled, Is.True);
            Assert.That(renderer.sharedMaterials, Is.Not.Empty);
            Assert.That(renderer.sharedMaterials.All(material => material != null), Is.True);
        }

        [Test]
        public void GeometryCache_UsesByteBudgetForLargeEntries()
        {
            string longText = new string('分', SettlementTextBatchRenderer.InitialGlyphCapacity);
            _batch.SpawnFloating(
                _surfaceParent.transform,
                Vector3.zero,
                string.Empty,
                $"0{longText}",
                effectColor: null,
                rise: 0f,
                duration: 10f,
                delay: 0f,
                visualScale: 1f,
                sortingOrder: 20);
            long firstEntryBytes = _batch.GeometryCacheBytes;

            Assert.That(firstEntryBytes, Is.GreaterThan(100_000L));
            int entryCount = (int)(
                SettlementTextBatchRenderer.GeometryCacheByteCapacity / firstEntryBytes) + 2;
            Assert.That(entryCount, Is.LessThan(SettlementTextBatchRenderer.GeometryCacheCapacity));
            for (int i = 1; i < entryCount; i++)
            {
                _batch.SpawnFloating(
                    _surfaceParent.transform,
                    Vector3.zero,
                    string.Empty,
                    $"{i}{longText}",
                    effectColor: null,
                    rise: 0f,
                    duration: 10f,
                    delay: 0f,
                    visualScale: 1f,
                    sortingOrder: 20 + i);
            }

            Assert.That(
                _batch.GeometryCacheBytes,
                Is.LessThanOrEqualTo(SettlementTextBatchRenderer.GeometryCacheByteCapacity));
            Assert.That(_batch.GeometryCacheCount, Is.LessThan(entryCount));
        }

        [Test]
        public void FloatingLifetime_IncludesDelayAndExpiresWithoutDroppingEarly()
        {
            float beforeSpawn = Time.time;
            SettlementTextBatchHandle handle = _batch.SpawnFloating(
                _surfaceParent.transform,
                Vector3.zero,
                "来源",
                "分数 +10",
                Color.white,
                rise: 0.9f,
                duration: 0.5f,
                delay: 0.25f,
                visualScale: 1f,
                sortingOrder: 20);

            _batch.AdvanceForTests(beforeSpawn + 0.20f);
            Assert.That(_batch.IsAlive(handle), Is.True);
            _batch.AdvanceForTests(beforeSpawn + 0.80f);
            Assert.That(_batch.IsAlive(handle), Is.False);
            Assert.That(_batch.ActiveLabelCount, Is.Zero);
        }

        [Test]
        public void SettledAndDelayedLabels_SkipUploadsUntilTimeStateChanges()
        {
            float beforeSpawn = Time.time;
            SettlementTextBatchHandle held = _batch.SpawnStage(
                _surfaceParent.transform,
                Vector3.zero,
                "技能触发",
                "稳定标签",
                Color.white,
                finalStamp: false,
                duration: 0.5f,
                visualScale: 1f,
                holdUntilCleared: true,
                headerSemanticColor: null,
                sortingOrder: 20,
                verticalDriftDirection: 1f,
                impactScale: 1f);

            _batch.AdvanceForTests(beforeSpawn + 0.1f);
            int initialUploads = _batch.UploadCount;
            _batch.AdvanceForTests(beforeSpawn + 0.3f);
            Assert.That(_batch.UploadCount, Is.EqualTo(initialUploads + 1));
            _batch.AdvanceForTests(beforeSpawn + 0.7f);
            int settledUploads = _batch.UploadCount;
            _batch.AdvanceForTests(beforeSpawn + 1.2f);
            _batch.AdvanceForTests(beforeSpawn + 2f);

            Assert.That(_batch.IsAlive(held), Is.True);
            Assert.That(_batch.UploadCount, Is.EqualTo(settledUploads));

            // 延迟标签使用新的时间线，避免把模拟到未来的 held 时间倒退回 Time.time。
            _batch.ClearAll();
            _batch.SpawnFloating(
                _surfaceParent.transform,
                Vector3.zero,
                string.Empty,
                "延迟效果",
                effectColor: null,
                rise: 0.5f,
                duration: 0.5f,
                delay: 1f,
                visualScale: 1f,
                sortingOrder: 30);
            float delayedSpawn = Time.time;
            _batch.AdvanceForTests(delayedSpawn + 0.1f);
            int hiddenUploads = _batch.UploadCount;
            _batch.AdvanceForTests(delayedSpawn + 0.5f);
            Assert.That(_batch.UploadCount, Is.EqualTo(hiddenUploads));
            _batch.AdvanceForTests(delayedSpawn + 1.1f);
            Assert.That(_batch.UploadCount, Is.EqualTo(hiddenUploads + 1));
        }

        [Test]
        public void LongUptime_DistinctFrameTimesDoNotCollapse()
        {
            const float fiveHours = 5f * 60f * 60f;
            float nextFrame = fiveHours + 1f / 60f;

            Assert.That(Mathf.Approximately(fiveHours, nextFrame), Is.True);
            Assert.That(
                SettlementTextBatchRenderer.IsSameEvaluationTime(fiveHours, nextFrame),
                Is.False);
            Assert.That(
                SettlementTextBatchRenderer.IsSameEvaluationTime(fiveHours, fiveHours),
                Is.True);
        }

        [Test]
        public void CacheHitSpawnAndRelease_ReusesSlotsWithoutManagedAllocation()
        {
            SettlementTextBatchHandle warm = _batch.SpawnFloating(
                _surfaceParent.transform,
                Vector3.zero,
                string.Empty,
                "缓存命中",
                effectColor: null,
                rise: 0f,
                duration: 10f,
                delay: 0f,
                visualScale: 1f,
                sortingOrder: 20);
            _batch.Release(warm);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 64; i++)
            {
                SettlementTextBatchHandle handle = _batch.SpawnFloating(
                    _surfaceParent.transform,
                    Vector3.zero,
                    string.Empty,
                    "缓存命中",
                    effectColor: null,
                    rise: 0f,
                    duration: 10f,
                    delay: 0f,
                    visualScale: 1f,
                    sortingOrder: 20);
                _batch.Release(handle);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.Zero);
        }

        [Test]
        public void SameFrameStructureChanges_RebuildOnlyOncePerAdvance()
        {
            var handles = new SettlementTextBatchHandle[64];
            for (int i = 0; i < handles.Length; i++)
            {
                handles[i] = _batch.SpawnFloating(
                    _surfaceParent.transform,
                    Vector3.zero,
                    string.Empty,
                    "同帧效果",
                    effectColor: null,
                    rise: 0f,
                    duration: 10f,
                    delay: 0f,
                    visualScale: 1f,
                    sortingOrder: 20 + i);
            }

            Assert.That(_batch.RebuildCount, Is.Zero);
            _batch.AdvanceForTests(Time.time);
            Assert.That(_batch.RebuildCount, Is.EqualTo(1));
            for (int i = 0; i < handles.Length; i++)
            {
                _batch.Release(handles[i]);
            }

            Assert.That(_batch.RebuildCount, Is.EqualTo(1));
            _batch.AdvanceForTests(Time.time);
            Assert.That(_batch.RebuildCount, Is.EqualTo(2));
        }

        [Test]
        public void RichTextAndSecondAtlas_CopyGeometryUvColorsAndMaterialGroups()
        {
            TMP_FontAsset font = _effectPrefab.EffectText.font;
            TMP_Character secondAtlasCharacter = font.characterTable.FirstOrDefault(
                character => character.glyph != null && character.glyph.atlasIndex > 0);
            Assert.That(secondAtlasCharacter, Is.Not.Null, "测试字体必须包含第二张 Atlas。 ");
            string secondAtlasGlyph = char.ConvertFromUtf32((int)secondAtlasCharacter.unicode);
            string text = $"普通{secondAtlasGlyph}[strong]粗体[/strong] [multmul]×2[/multmul]";

            SettlementTextBatchHandle handle = _batch.SpawnFloating(
                _surfaceParent.transform,
                Vector3.zero,
                string.Empty,
                text,
                effectColor: null,
                rise: 0f,
                duration: 10000f,
                delay: 0f,
                visualScale: 1f,
                sortingOrder: 25);
            _batch.AdvanceForTests(Time.time);

            Assert.That(handle.IsValid, Is.True);
            FloatingTextView layoutTemplate = _batch
                .GetComponentsInChildren<FloatingTextView>(true)
                .Single();
            ExpectedGeometry expected = CaptureExpectedFloatingGeometry(layoutTemplate);
            Mesh mesh = _batch.GetSurfaceMeshForTests(_surfaceParent.transform);
            MeshRenderer renderer = _batch.GetSurfaceRendererForTests(_surfaceParent.transform);

            Assert.That(mesh, Is.Not.Null);
            Assert.That(renderer, Is.Not.Null);
            Assert.That(_batch.VisibleVertexCount, Is.EqualTo(expected.Positions.Count));
            Assert.That(
                Enumerable.Range(0, mesh.subMeshCount)
                    .Sum(index => (long)mesh.GetIndexCount(index)),
                Is.EqualTo(expected.IndexCount));
            Assert.That(renderer.sharedMaterials, Has.Length.EqualTo(expected.MaterialCount));
            Assert.That(renderer.sharedMaterials.Any(material =>
                    material != null
                    && material.name.Contains(SemanticDescriptionFormatter.MultiplyMaterialName)),
                Is.True);
            Assert.That(expected.MaterialCount, Is.GreaterThanOrEqualTo(4));

            var actualPositions = new List<Vector3>();
            var actualUv = new List<Vector4>();
            var actualColors = new List<Color32>();
            mesh.GetVertices(actualPositions);
            mesh.GetUVs(0, actualUv);
            mesh.GetColors(actualColors);
            for (int i = 0; i < expected.Positions.Count; i++)
            {
                Assert.That(
                    Vector3.Distance(actualPositions[i], expected.Positions[i]),
                    Is.LessThan(0.0001f),
                    $"顶点位置不一致：{i}");
                Assert.That(
                    Vector4.Distance(actualUv[i], expected.Uv[i]),
                    Is.LessThan(0.0001f),
                    $"UV 不一致：{i}");
                Assert.That(actualColors[i].r, Is.EqualTo(expected.Colors[i].r));
                Assert.That(actualColors[i].g, Is.EqualTo(expected.Colors[i].g));
                Assert.That(actualColors[i].b, Is.EqualTo(expected.Colors[i].b));
                Assert.That(
                    actualColors[i].a,
                    Is.EqualTo(expected.Colors[i].a).Within(1));
            }
        }

        [Test]
        public void SixtyFourLabelsAnd2048Glyphs_UseOneRendererThenDoubleCapacity()
        {
            string thirtyTwoGlyphs = new('测', 32);
            int childCountBefore = _host.GetComponentsInChildren<Transform>(true).Length;
            for (int i = 0; i < SettlementTextBatchRenderer.InitialLabelCapacity; i++)
            {
                _batch.SpawnFloating(
                    _surfaceParent.transform,
                    Vector3.zero,
                    string.Empty,
                    thirtyTwoGlyphs,
                    effectColor: null,
                    rise: 0f,
                    duration: 10f,
                    delay: 0f,
                    visualScale: 1f,
                    sortingOrder: 100 + i);
            }

            _batch.AdvanceForTests(Time.time);
            int spriteVertexCount = _effectPrefab.Background.sprite.vertices.Length;
            int expectedVertices = SettlementTextBatchRenderer.InitialLabelCapacity
                * (32 * 4 + spriteVertexCount);
            Assert.That(_batch.ActiveLabelCount, Is.EqualTo(64));
            Assert.That(_batch.VisibleVertexCount, Is.EqualTo(expectedVertices));
            Assert.That(
                _batch.VertexCapacity,
                Is.EqualTo(SettlementTextBatchRenderer.InitialVertexCapacity));
            Assert.That(_batch.SurfaceCount, Is.EqualTo(1));
            Assert.That(_batch.BatchRendererCount, Is.EqualTo(1));

            int childCountAtCapacity = _host.GetComponentsInChildren<Transform>(true).Length;
            Assert.That(childCountAtCapacity, Is.EqualTo(childCountBefore + 1));
            _batch.SpawnFloating(
                _surfaceParent.transform,
                Vector3.zero,
                string.Empty,
                thirtyTwoGlyphs,
                effectColor: null,
                rise: 0f,
                duration: 10f,
                delay: 0f,
                visualScale: 1f,
                sortingOrder: 200);
            _batch.AdvanceForTests(Time.time);

            Assert.That(_batch.ActiveLabelCount, Is.EqualTo(65));
            Assert.That(
                _batch.VertexCapacity,
                Is.EqualTo(SettlementTextBatchRenderer.InitialVertexCapacity * 2));
            Assert.That(
                _host.GetComponentsInChildren<Transform>(true).Length,
                Is.EqualTo(childCountAtCapacity));
            Assert.That(_batch.BatchRendererCount, Is.EqualTo(1));
        }

        [Test]
        public void MoreLabelsWithSameVisualMaterials_DoNotAddDrawCallGroups()
        {
            for (int i = 0; i < 32; i++)
            {
                _batch.SpawnFloating(
                    _surfaceParent.transform,
                    Vector3.zero,
                    "来源",
                    $"分数 [score]+{i}[/score]",
                    effectColor: null,
                    rise: 0f,
                    duration: 10f,
                    delay: 0f,
                    visualScale: 1f,
                    sortingOrder: 20 + i);
            }

            _batch.AdvanceForTests(Time.time);
            int materialGroups = _batch.MaterialGroupCount;
            Assert.That(materialGroups, Is.GreaterThan(0));
            Assert.That(_batch.BatchRendererCount, Is.EqualTo(1));

            for (int i = 0; i < 96; i++)
            {
                _batch.SpawnFloating(
                    _surfaceParent.transform,
                    Vector3.zero,
                    "来源",
                    $"分数 [score]+{i % 10}[/score]",
                    effectColor: null,
                    rise: 0f,
                    duration: 10f,
                    delay: 0f,
                    visualScale: 1f,
                    sortingOrder: 100 + i);
            }

            _batch.AdvanceForTests(Time.time + 0.01f);
            Assert.That(_batch.ActiveLabelCount, Is.EqualTo(128));
            Assert.That(_batch.BatchRendererCount, Is.EqualTo(1));
            Assert.That(_batch.MaterialGroupCount, Is.EqualTo(materialGroups));
        }

        private SettlementTextBatchHandle SpawnStage(string body)
        {
            return _batch.SpawnStage(
                _surfaceParent.transform,
                Vector3.zero,
                "技能触发",
                body,
                Color.white,
                finalStamp: false,
                duration: 1f,
                visualScale: 1f,
                holdUntilCleared: false,
                headerSemanticColor: null,
                sortingOrder: 20,
                verticalDriftDirection: 1f,
                impactScale: 1f);
        }

        private static T LoadComponent<T>(string path) where T : Component
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            T component = prefab.GetComponent<T>();
            Assert.That(component, Is.Not.Null, path);
            return component;
        }

        private static ExpectedGeometry CaptureExpectedFloatingGeometry(FloatingTextView template)
        {
            var result = new ExpectedGeometry();
            SpriteRenderer background = template.Background;
            Matrix4x4 spriteMatrix = template.transform.worldToLocalMatrix
                * background.transform.localToWorldMatrix;
            Vector2[] spritePositions = background.sprite.vertices;
            Vector2[] spriteUv = background.sprite.uv;
            for (int i = 0; i < spritePositions.Length; i++)
            {
                result.Positions.Add(spriteMatrix.MultiplyPoint3x4(spritePositions[i]));
                result.Uv.Add(new Vector4(spriteUv[i].x, spriteUv[i].y, 0f, 0f));
                result.Colors.Add(background.color);
            }

            result.IndexCount += background.sprite.triangles.Length;
            result.MaterialCount++;
            AppendExpectedText(template.transform, template.EffectText, result);
            return result;
        }

        private static void AppendExpectedText(
            Transform root,
            TextMeshPro text,
            ExpectedGeometry result)
        {
            Matrix4x4 relative = root.worldToLocalMatrix * text.transform.localToWorldMatrix;
            TMP_MeshInfo[] meshInfos = text.textInfo.meshInfo;
            for (int meshIndex = 0; meshIndex < meshInfos.Length; meshIndex++)
            {
                TMP_MeshInfo meshInfo = meshInfos[meshIndex];
                if (meshInfo.vertexCount <= 0 || meshInfo.material == null)
                {
                    continue;
                }

                result.MaterialCount++;
                result.IndexCount += meshInfo.vertexCount / 4 * 6;
                for (int i = 0; i < meshInfo.vertexCount; i++)
                {
                    result.Positions.Add(relative.MultiplyPoint3x4(meshInfo.vertices[i]));
                    result.Uv.Add(meshInfo.uvs0[i]);
                    result.Colors.Add(meshInfo.colors32[i]);
                }
            }
        }

        private sealed class ExpectedGeometry
        {
            public readonly List<Vector3> Positions = new();
            public readonly List<Vector4> Uv = new();
            public readonly List<Color32> Colors = new();
            public int IndexCount;
            public int MaterialCount;
        }
    }
}
#endif
