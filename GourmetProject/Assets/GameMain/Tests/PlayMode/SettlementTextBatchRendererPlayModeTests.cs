#if UNITY_EDITOR
using System.Collections;
using GourmetProject.Game.Presentation.Battle;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class SettlementTextBatchRendererPlayModeTests
    {
        private const string EffectPrefabPath =
            "Assets/GameMain/Content/Prefabs/Battle/Settlement/SettlementEffectLabel.prefab";
        private const string StagePrefabPath =
            "Assets/GameMain/Content/Prefabs/Battle/Settlement/SettlementStageLabel.prefab";
        private const string FinalePrefabPath =
            "Assets/GameMain/Content/Prefabs/Battle/Settlement/SettlementFinaleLabel.prefab";

        [UnityTest]
        public IEnumerator CompleteSettlementLabels_RenderAfterDisableEnableAndClear()
        {
            FloatingTextView effect = LoadComponent<FloatingTextView>(EffectPrefabPath);
            SettlementStageLabelView stage = LoadComponent<SettlementStageLabelView>(StagePrefabPath);
            SettlementStageLabelView finale = LoadComponent<SettlementStageLabelView>(FinalePrefabPath);
            var host = new GameObject("Settlement Batch PlayMode Host");
            var surfaceParent = new GameObject("Settlement Batch PlayMode Surface");
            surfaceParent.transform.SetParent(host.transform, false);
            SettlementTextBatchRenderer batch = host.AddComponent<SettlementTextBatchRenderer>();
            Assert.That(batch.Initialize(effect, stage, finale), Is.True);

            var cameraObject = new GameObject("Settlement Batch PlayMode Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 3f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            var target = new RenderTexture(512, 256, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;

            try
            {
                host.SetActive(false);
                yield return null;
                host.SetActive(true);
                yield return null;

                SettlementTextBatchHandle floating = batch.SpawnFloating(
                    surfaceParent.transform,
                    Vector3.zero,
                    "技能来源",
                    "分数 [score]+123[/score]  倍率 [multmul]×2[/multmul]",
                    effectColor: null,
                    rise: 0.2f,
                    duration: 2f,
                    delay: 0f,
                    visualScale: 1f,
                    sortingOrder: 20);
                Assert.That(floating.IsValid, Is.True);
                batch.AdvanceForTests(Time.time + 0.1f);
                AssertVisible(batch, surfaceParent.transform, camera, target, "技能/来源飘字");

                batch.ClearAll();
                SettlementTextBatchHandle result = batch.SpawnStage(
                    surfaceParent.transform,
                    Vector3.zero,
                    "技能触发",
                    "本次分数 [score]+456[/score]",
                    Color.white,
                    finalStamp: false,
                    duration: 2f,
                    visualScale: 1f,
                    holdUntilCleared: true,
                    headerSemanticColor: null,
                    sortingOrder: 30,
                    verticalDriftDirection: 1f,
                    impactScale: 1f);
                Assert.That(result.IsValid, Is.True);
                batch.AdvanceForTests(Time.time + 0.1f);
                AssertVisible(batch, surfaceParent.transform, camera, target, "阶段分数飘字");

                batch.ClearAll();
                SettlementTextBatchHandle final = batch.SpawnStage(
                    surfaceParent.transform,
                    Vector3.zero,
                    "本桌结算",
                    "总分 12345",
                    Color.white,
                    finalStamp: true,
                    duration: 2f,
                    visualScale: 1f,
                    holdUntilCleared: true,
                    headerSemanticColor: null,
                    sortingOrder: 40,
                    verticalDriftDirection: 1f,
                    impactScale: 1f);
                Assert.That(final.IsValid, Is.True);
                batch.AdvanceForTests(Time.time + 0.1f);
                AssertVisible(batch, surfaceParent.transform, camera, target, "最终总分飘字");
            }
            finally
            {
                camera.targetTexture = null;
                Object.Destroy(target);
                Object.Destroy(cameraObject);
                Object.Destroy(host);
            }

            yield return null;
        }

        private static void AssertVisible(
            SettlementTextBatchRenderer batch,
            Transform surfaceParent,
            Camera camera,
            RenderTexture target,
            string label)
        {
            MeshRenderer renderer = batch.GetSurfaceRendererForTests(surfaceParent);
            Assert.That(renderer, Is.Not.Null, label);
            Assert.That(renderer.enabled, Is.True, label);
            Assert.That(batch.VisibleVertexCount, Is.GreaterThan(0), label);
            Assert.That(renderer.sharedMaterials, Is.Not.Empty, label);

            RenderTexture previous = RenderTexture.active;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                Color32[] colors = pixels.GetPixels32();
                int visiblePixels = 0;
                for (int i = 0; i < colors.Length; i++)
                {
                    if (colors[i].a > 8)
                    {
                        visiblePixels++;
                    }
                }

                Assert.That(visiblePixels, Is.GreaterThan(16), label);
            }
            finally
            {
                RenderTexture.active = previous;
                Object.Destroy(pixels);
            }
        }

        private static T LoadComponent<T>(string path) where T : Component
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            T component = prefab.GetComponent<T>();
            Assert.That(component, Is.Not.Null, path);
            return component;
        }
    }
}
#endif
