#if UNITY_EDITOR
using System.Collections.Generic;
using GourmetProject.Game.Presentation.Battle;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.Rendering;

namespace GourmetProject.Tests.EditMode
{
    public sealed class DishBatchingTests
    {
        private const string DishPiecePrefabPath =
            "Assets/GameMain/Content/Prefabs/Battle/Dishes/DishPiece.prefab";
        private const string BadgePrefabPath =
            "Assets/GameMain/Content/Prefabs/Battle/Dishes/DishValueBadge.prefab";
        private const string BadgeAtlasPath =
            "Assets/GameMain/Content/SpriteAtlases/DishValueBadge.spriteatlasv2";
        private const string DishFolder =
            "Assets/GameMain/Content/Resources/Sprites/Dishes";

        private readonly List<Object> _ownedObjects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _ownedObjects.Count - 1; i >= 0; i--)
            {
                if (_ownedObjects[i] != null)
                {
                    Object.DestroyImmediate(_ownedObjects[i]);
                }
            }

            _ownedObjects.Clear();
        }

        [Test]
        public void ShadowBatch_DifferentTexturesStillUseOneRendererMaterialAndSubmesh()
        {
            DishShadowBatchRenderer batch = CreateBatchRoot();
            Sprite firstSprite = CreateSprite(8, 8, new Color32(255, 255, 255, 255));
            Sprite secondSprite = CreateSprite(12, 6, new Color32(255, 255, 255, 255));
            DishPieceView first = CreatePiece("First", firstSprite);
            DishPieceView second = CreatePiece("Second", secondSprite);

            SpriteRenderer firstHalo = FindRenderer(first, "ShadowHalo");
            SpriteRenderer firstCore = FindRenderer(first, "Shadow");
            firstHalo.transform.localPosition = new Vector3(0.3f, -0.2f, 0f);
            firstHalo.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            firstHalo.transform.localScale = new Vector3(1.2f, 0.8f, 1f);
            firstHalo.flipX = true;
            firstHalo.color = new Color(0f, 0f, 0f, 0.07f);
            firstCore.color = new Color(0f, 0f, 0f, 0.28f);

            first.ConfigureShadowBatch(batch);
            second.ConfigureShadowBatch(batch);
            batch.FlushPendingChanges();

            Assert.That(batch.RegisteredCount, Is.EqualTo(2));
            Assert.That(batch.GetComponents<MeshRenderer>(), Has.Length.EqualTo(1));
            Assert.That(batch.Renderer.sharedMaterials, Has.Length.EqualTo(1));
            Assert.That(batch.Mesh.subMeshCount, Is.EqualTo(1));
            Assert.That(
                batch.VertexCount,
                Is.EqualTo((firstSprite.vertices.Length + secondSprite.vertices.Length) * 2));
            Assert.That(
                batch.IndexCount,
                Is.EqualTo((firstSprite.triangles.Length + secondSprite.triangles.Length) * 2));
            Assert.That(firstHalo.forceRenderingOff, Is.True);
            Assert.That(firstCore.forceRenderingOff, Is.True);

            Vector2 source = firstSprite.vertices[0];
            source.x = -source.x;
            Vector3 expected = batch.transform.worldToLocalMatrix.MultiplyPoint3x4(
                firstHalo.transform.TransformPoint(source));
            Assert.That(Vector3.Distance(batch.Mesh.vertices[0], expected), Is.LessThan(0.0001f));
            Assert.That(batch.Mesh.colors32[0], Is.EqualTo((Color32)firstHalo.color));
        }

        [Test]
        public void ShadowBatch_StreamChangesUploadOnceWithoutChangingTopology()
        {
            DishShadowBatchRenderer batch = CreateBatchRoot();
            Sprite sprite = CreateSprite(8, 8, new Color32(255, 255, 255, 255));
            DishPieceView piece = CreatePiece("Moving", sprite);
            piece.ConfigureShadowBatch(batch);
            batch.FlushPendingChanges();
            int topology = batch.TopologyRevision;
            int uploads = batch.StreamUploadCount;

            FindRenderer(piece, "Shadow").transform.localPosition += new Vector3(0.25f, 0.1f, 0f);
            FindRenderer(piece, "ShadowHalo").color = new Color(0f, 0f, 0f, 0.12f);
            batch.MarkStreamDirty(piece);
            batch.MarkStreamDirty(piece);
            batch.FlushPendingChanges();

            Assert.That(batch.TopologyRevision, Is.EqualTo(topology));
            Assert.That(batch.StreamUploadCount, Is.EqualTo(uploads + 1));

            Sprite replacement = CreateSprite(10, 10, new Color32(255, 255, 255, 255));
            FindRenderer(piece, "Shadow").sprite = replacement;
            FindRenderer(piece, "ShadowHalo").sprite = replacement;
            batch.MarkTopologyDirty(piece);
            batch.FlushPendingChanges();
            Assert.That(batch.TopologyRevision, Is.EqualTo(topology + 1));
        }

        [Test]
        public void ShadowBatch_UnregisterAndReleaseRestoreFallbackAndOwnedResources()
        {
            DishShadowBatchRenderer batch = CreateBatchRoot();
            Sprite sprite = CreateSprite(8, 8, new Color32(255, 255, 255, 255));
            DishPieceView piece = CreatePiece("Pooled", sprite);
            SpriteRenderer core = FindRenderer(piece, "Shadow");
            SpriteRenderer halo = FindRenderer(piece, "ShadowHalo");
            piece.ConfigureShadowBatch(batch);
            batch.FlushPendingChanges();

            piece.ConfigureShadowBatch(null);
            Assert.That(batch.RegisteredCount, Is.Zero);
            Assert.That(core.forceRenderingOff, Is.False);
            Assert.That(halo.forceRenderingOff, Is.False);

            piece.ConfigureShadowBatch(batch);
            batch.FlushPendingChanges();
            batch.ReleaseRuntimeResources(destroyImmediately: true);
            Assert.That(batch.Mesh, Is.Null);
            Assert.That(batch.Renderer.sharedMaterial, Is.Null);
            Assert.That(core.forceRenderingOff, Is.False);
            Assert.That(halo.forceRenderingOff, Is.False);
        }

        [Test]
        public void Badge_DecorationsShareV2AtlasMaterial_AndRenderInExplicitOrder()
        {
            SpriteAtlasAsset atlas = SpriteAtlasAsset.Load(BadgeAtlasPath);
            Assert.That(atlas, Is.Not.Null);
            var atlasSerialized = new SerializedObject(atlas);
            SerializedProperty packables = atlasSerialized.FindProperty("m_ImporterData.packables");
            Assert.That(packables, Is.Not.Null);
            Assert.That(packables.arraySize, Is.EqualTo(2));

            var importer = AssetImporter.GetAtPath(BadgeAtlasPath) as SpriteAtlasImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.includeInBuild, Is.True);
            Assert.That(importer.packingSettings.enableRotation, Is.False);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BadgePrefabPath);
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = Object.Instantiate(prefab);
            _ownedObjects.Add(instance);
            DishValueBadgeView badge = instance.GetComponent<DishValueBadgeView>();
            Assert.That(badge, Is.Not.Null);
            badge.ConfigureSorting(BattleSorting.WorldUi, BattleSorting.OrderDishBadge);

            SpriteRenderer[] decorations = instance.GetComponentsInChildren<SpriteRenderer>(true);
            Transform valueBackingTransform = instance.transform.Find("ValueBacking");
            Transform iconTransform = instance.transform.Find("DishValueIcon");
            TextMeshPro valueText = instance.GetComponentInChildren<TextMeshPro>(true);
            Assert.That(decorations, Has.Length.EqualTo(2));
            Assert.That(valueBackingTransform, Is.Not.Null);
            Assert.That(iconTransform, Is.Not.Null);
            SpriteRenderer valueBacking = valueBackingTransform.GetComponent<SpriteRenderer>();
            SpriteRenderer icon = iconTransform.GetComponent<SpriteRenderer>();
            Assert.That(valueBacking, Is.Not.Null);
            Assert.That(icon, Is.Not.Null);
            Assert.That(valueText, Is.Not.Null);
            for (int i = 0; i < decorations.Length; i++)
            {
                Assert.That(decorations[i].sharedMaterial, Is.SameAs(decorations[0].sharedMaterial));
                Assert.That(decorations[i].shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
                Assert.That(decorations[i].receiveShadows, Is.False);
                Assert.That(decorations[i].lightProbeUsage, Is.EqualTo(LightProbeUsage.Off));
                Assert.That(decorations[i].reflectionProbeUsage, Is.EqualTo(ReflectionProbeUsage.Off));
                Assert.That(
                    decorations[i].motionVectorGenerationMode,
                    Is.EqualTo(MotionVectorGenerationMode.ForceNoMotion));
            }

            Assert.That(valueBacking.sortingOrder, Is.EqualTo(BattleSorting.OrderDishBadge));
            Assert.That(icon.sortingOrder, Is.EqualTo(BattleSorting.OrderDishBadge + 1));
            Assert.That(valueText.renderer.sortingOrder, Is.EqualTo(BattleSorting.OrderDishBadge + 2));
            var textRenderer = valueText.renderer as MeshRenderer;
            Assert.That(textRenderer, Is.Not.Null);
            Assert.That(textRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
            Assert.That(textRenderer.receiveShadows, Is.False);
            Assert.That(textRenderer.lightProbeUsage, Is.EqualTo(LightProbeUsage.Off));
            Assert.That(textRenderer.reflectionProbeUsage, Is.EqualTo(ReflectionProbeUsage.Off));
            Assert.That(
                textRenderer.motionVectorGenerationMode,
                Is.EqualTo(MotionVectorGenerationMode.ForceNoMotion));

            RenderPipelineAsset pipeline =
                AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/UniversalRP.asset");
            Assert.That(pipeline, Is.Not.Null);
            var pipelineSerialized = new SerializedObject(pipeline);
            SerializedProperty dynamicBatching =
                pipelineSerialized.FindProperty("m_SupportsDynamicBatching");
            Assert.That(dynamicBatching, Is.Not.Null);
            Assert.That(dynamicBatching.boolValue, Is.True);
        }

        [Test]
        public void AllDishSpritesUseTightMeshImport()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { DishFolder });
            Assert.That(guids, Has.Length.GreaterThan(0));
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null || importer.textureType != TextureImporterType.Sprite)
                {
                    continue;
                }

                var serializedImporter = new SerializedObject(importer);
                SerializedProperty meshType = serializedImporter.FindProperty("m_SpriteMeshType");
                Assert.That(meshType, Is.Not.Null, path);
                Assert.That(meshType.intValue, Is.EqualTo((int)SpriteMeshType.Tight), path);
            }
        }

        private DishShadowBatchRenderer CreateBatchRoot()
        {
            var root = new GameObject("DishShadowBatchTest");
            _ownedObjects.Add(root);
            return root.AddComponent<DishShadowBatchRenderer>();
        }

        private DishPieceView CreatePiece(string name, Sprite sprite)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DishPiecePrefabPath);
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = Object.Instantiate(prefab);
            instance.name = name;
            _ownedObjects.Add(instance);
            DishPieceView piece = instance.GetComponent<DishPieceView>();
            Assert.That(piece, Is.Not.Null);
            FindRenderer(piece, "Shadow").sprite = sprite;
            FindRenderer(piece, "ShadowHalo").sprite = sprite;
            return piece;
        }

        private Sprite CreateSprite(int width, int height, Color32 fill)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = fill;
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                16f,
                0,
                SpriteMeshType.Tight);
            _ownedObjects.Add(sprite);
            _ownedObjects.Add(texture);
            return sprite;
        }

        private static SpriteRenderer FindRenderer(DishPieceView piece, string name)
        {
            SpriteRenderer[] renderers = piece.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].name == name)
                {
                    return renderers[i];
                }
            }

            Assert.Fail($"Missing renderer '{name}'.");
            return null;
        }
    }
}
#endif
