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
        public void ClearThenSpawnAgain_RecreatesIndexBufferAndRenders()
        {
            SettlementTextBatchHandle first = SpawnStage("第一次");
            _batch.AdvanceForTests(Time.time);
            Mesh mesh = _batch.GetSurfaceMeshForTests(_surfaceParent.transform);
            MeshRenderer renderer =
                _batch.GetSurfaceRendererForTests(_surfaceParent.transform);

            Assert.That(_batch.IsAlive(first), Is.True);
            Assert.That(mesh.subMeshCount, Is.GreaterThan(0));

            _batch.ClearAll();

            Assert.That(mesh.subMeshCount, Is.Zero);
            Assert.That(renderer.enabled, Is.False);

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
