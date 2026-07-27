using System.Collections.Generic;
using GourmetProject.Core.Utility;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GourmetProject.Game.Presentation.Battle
{
    internal static class DishIconPreviewGridSizing
    {
        public static Vector2Int ExpandedBoardSize(DishShape shape)
        {
            return shape == null
                ? Vector2Int.zero
                : new Vector2Int(
                    ExpandedAxisSize(shape.Width),
                    ExpandedAxisSize(shape.Height));
        }

        private static int ExpandedAxisSize(int foodSize)
        {
            if (foodSize <= 0)
            {
                return 0;
            }

            int boardSize = Mathf.Max(3, foodSize);
            if ((boardSize - foodSize) % 2 != 0)
            {
                boardSize++;
            }

            return boardSize;
        }
    }

    /// <summary>
    /// 在独立运行时场景中复用一套棋盘、菜品和美味值标签，并把结果渲染到每个 UI 预览自己的 RenderTexture。
    /// </summary>
    internal sealed class DishIconPreviewRenderer : MonoBehaviour
    {
        private const string RuntimeSceneName = "DishIconPreviewRuntime";
        private const string PreviewLayerName = "DishIconPreview";
        private const int FallbackPreviewLayer = 31;
        private const float CellSize = 1f;
        private const float DishInset = 0.06f;
        private const float BadgeScale = 0.85f;
        private const float StainScale = 8f;
        private const float StainThreshold = 0.62f;
        private const float StainSoftness = 0.12f;
        private const float StainDarken = 0.12f;
        private static readonly Vector3 StageWorldPosition = new(10000f, 10000f, 0f);
        private const float StageSlotSpacing = 64f;
        private static readonly Color PreviewBackgroundColor = Color.white;

        private static DishIconPreviewRenderer _instance;
        private static int _sceneSerial;

        private readonly List<GameObject> _spawnedObjects = new();
        private readonly List<string> _flavorScratch = new();
        private Camera _camera;
        private int _previewLayer;
        private Transform _boardRoot;
        private Transform _dishRoot;
        private SpriteRenderer _dishRenderer;
        private MaterialPropertyBlock _dishStainBlock;
        private DishValueBadgeView _badge;

        public static RenderTexture Render(
            DishDef dish,
            Sprite sprite,
            int deliciousness,
            IReadOnlyList<string> flavorIds,
            GameObject cellPrefab,
            GameObject badgePrefab,
            int pixelsPerCell,
            DishIconPreviewMode mode)
        {
            if (dish?.Shape == null || sprite == null || cellPrefab == null || badgePrefab == null)
            {
                return null;
            }

            return Instance.RenderInternal(
                dish,
                sprite,
                deliciousness,
                flavorIds,
                cellPrefab,
                badgePrefab,
                Mathf.Clamp(pixelsPerCell, 32, 256),
                mode);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PurgeStaleInstances();
            _instance = null;
            _sceneSerial = 0;
        }

        private static DishIconPreviewRenderer Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                // HideAndDontSave preview rigs can survive a script/domain reload while their
                // runtime-created materials do not. If a new rig is then placed on top of one
                // of those orphaned rigs, the stale SpriteRenderers are drawn as Unity's
                // magenta error material through every transparent part of the new sprites.
                PurgeStaleInstances();

                Scene previousActiveScene = SceneManager.GetActiveScene();
                Scene scene = CreatePreviewScene();
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActiveScene);
                }

                var root = new GameObject(RuntimeSceneName)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                root.transform.position = StagePositionFor(scene);
                SceneManager.MoveGameObjectToScene(root, scene);
                _instance = root.AddComponent<DishIconPreviewRenderer>();
                _instance.BuildRig();
                return _instance;
            }
        }

        private static void PurgeStaleInstances()
        {
            DishIconPreviewRenderer[] instances =
                Resources.FindObjectsOfTypeAll<DishIconPreviewRenderer>();
            for (int i = 0; i < instances.Length; i++)
            {
                DishIconPreviewRenderer renderer = instances[i];
                if (renderer == null)
                {
                    continue;
                }

                GameObject root = renderer.gameObject;
                root.SetActive(false);
                if (Application.isPlaying)
                {
                    Destroy(root);
                }
                else
                {
                    DestroyImmediate(root);
                }
            }
        }

        private static Vector3 StagePositionFor(Scene scene)
        {
            int slot = Mathf.Abs(scene.handle % 1024);
            return StageWorldPosition + new Vector3(
                (slot % 32) * StageSlotSpacing,
                (slot / 32) * StageSlotSpacing,
                0f);
        }

        private void BuildRig()
        {
            _previewLayer = LayerMask.NameToLayer(PreviewLayerName);
            if (_previewLayer < 0)
            {
                _previewLayer = FallbackPreviewLayer;
                Debug.LogWarning(
                    $"Layer '{PreviewLayerName}' is missing. Dish icon preview falls back to layer {_previewLayer}.");
            }

            gameObject.layer = _previewLayer;
            var cameraObject = new GameObject("Dish Icon Camera", typeof(Camera))
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = _previewLayer,
            };
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);
            _camera = cameraObject.GetComponent<Camera>();
            _camera.enabled = false;
            _camera.orthographic = true;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 50f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = PreviewBackgroundColor;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            _camera.cullingMask = 1 << _previewLayer;

            var lightObject = new GameObject("Directional Light", typeof(Light))
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = _previewLayer,
            };
            lightObject.transform.SetParent(transform, false);
            Light mainLight = lightObject.GetComponent<Light>();
            mainLight.type = LightType.Directional;
            mainLight.intensity = 1f;
            mainLight.cullingMask = 1 << _previewLayer;

            _boardRoot = NewRoot("Board");
            _dishRoot = NewRoot("Dish");
            _dishRenderer = _dishRoot.gameObject.AddComponent<SpriteRenderer>();
            SpriteRenderStyle.ApplyUnlitMaterial(_dishRenderer);
            BattleSorting.Apply(_dishRenderer, BattleSorting.Pieces, BattleSorting.OrderBody);
        }

        private Transform NewRoot(string name)
        {
            var child = new GameObject(name)
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = _previewLayer,
            };
            child.transform.SetParent(transform, false);
            return child.transform;
        }

        private RenderTexture RenderInternal(
            DishDef dish,
            Sprite sprite,
            int deliciousness,
            IReadOnlyList<string> flavorIds,
            GameObject cellPrefab,
            GameObject badgePrefab,
            int pixelsPerCell,
            DishIconPreviewMode mode)
        {
            ClearStage();

            ComposeFlavorIds(dish, flavorIds);
            int rotationIndex = FlavorStainPalette.DisplayRotationIndex(
                dish.RotationIndex,
                _flavorScratch);
            DishShape displayShape = dish.Shape.RotatedBy(rotationIndex);
            Vector2Int boardSize = mode == DishIconPreviewMode.Warehouse
                ? new Vector2Int(displayShape.Width, displayShape.Height)
                : DishIconPreviewGridSizing.ExpandedBoardSize(displayShape);
            int boardWidth = boardSize.x;
            int boardHeight = boardSize.y;

            if (mode == DishIconPreviewMode.Card)
            {
                BuildBoard(cellPrefab, boardWidth, boardHeight);
            }

            BuildDish(dish.Shape, rotationIndex, sprite, dish.Id);
            BuildBadge(badgePrefab, displayShape, deliciousness, mode);

            int textureWidth = boardWidth * pixelsPerCell;
            int textureHeight = boardHeight * pixelsPerCell;
            Color backgroundColor = mode == DishIconPreviewMode.Warehouse
                ? Color.clear
                : PreviewBackgroundColor;
            var descriptor = new RenderTextureDescriptor(textureWidth, textureHeight)
            {
                graphicsFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.LDR),
                depthStencilFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil),
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false,
                memoryless = RenderTextureMemoryless.None,
            };
            var texture = new RenderTexture(descriptor)
            {
                name = $"DishIcon_{mode}_{dish.Id}_{boardWidth}x{boardHeight}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.Create();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(true, true, backgroundColor);
            RenderTexture.active = previous;

            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = backgroundColor;
            _camera.aspect = (float)boardWidth / boardHeight;
            _camera.orthographicSize = boardHeight * CellSize * 0.5f;
            var request = new RenderPipeline.StandardRequest
            {
                destination = texture,
            };
            if (RenderPipeline.SupportsRenderRequest(_camera, request))
            {
                RenderPipeline.SubmitRenderRequest(_camera, request);
            }
            else
            {
                _camera.targetTexture = texture;
                _camera.Render();
                _camera.targetTexture = null;
            }

            return texture;
        }

        private void BuildBoard(GameObject cellPrefab, int width, int height)
        {
            Sprite cellSprite = cellPrefab.GetComponent<SpriteRenderer>()?.sprite;
            if (cellSprite == null)
            {
                Sprite[] resourceSprites = Resources.LoadAll<Sprite>("Sprites/UI/board_cell");
                cellSprite = resourceSprites != null && resourceSprites.Length > 0
                    ? resourceSprites[0]
                    : null;
            }

            if (cellSprite == null)
            {
                Debug.LogError("Dish icon preview cell prefab has no SpriteRenderer sprite.", cellPrefab);
                return;
            }

            Vector2 spriteSize = cellSprite.bounds.size;
            float scaleX = spriteSize.x > 0f ? CellSize / spriteSize.x : CellSize;
            float scaleY = spriteSize.y > 0f ? CellSize / spriteSize.y : CellSize;
            float left = -(width - 1) * CellSize * 0.5f;
            float top = (height - 1) * CellSize * 0.5f;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var cellObject = new GameObject($"Cell_{x}_{y}")
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                        layer = _previewLayer,
                    };
                    cellObject.transform.SetParent(_boardRoot, false);
                    cellObject.transform.localPosition = new Vector3(
                        left + x * CellSize,
                        top - y * CellSize,
                        0f);
                    cellObject.transform.localScale = new Vector3(scaleX, scaleY, 1f);

                    SpriteRenderer cellRenderer = cellObject.AddComponent<SpriteRenderer>();
                    cellRenderer.sprite = cellSprite;
                    cellRenderer.color = Color.white;
                    SpriteRenderStyle.ApplyUnlitMaterial(cellRenderer);
                    BattleSorting.Apply(cellRenderer, BattleSorting.DiningTable);
                    _spawnedObjects.Add(cellObject);
                }
            }
        }

        private void BuildDish(
            DishShape originalShape,
            int rotationIndex,
            Sprite sprite,
            string dishId)
        {
            _dishRenderer.gameObject.SetActive(true);
            _dishRenderer.sprite = sprite;
            _dishRenderer.color = Color.white;
            SpriteRenderStyle.ApplyUnlitMaterial(_dishRenderer);
            BattleSorting.Apply(_dishRenderer, BattleSorting.Pieces, BattleSorting.OrderBody);

            Vector2 spriteSize = sprite.bounds.size;
            float contentScale = 1f - DishInset * 2f;
            float scaleX = spriteSize.x > 0f
                ? originalShape.Width * CellSize * contentScale / spriteSize.x
                : 1f;
            float scaleY = spriteSize.y > 0f
                ? originalShape.Height * CellSize * contentScale / spriteSize.y
                : 1f;
            _dishRoot.localPosition = Vector3.zero;
            _dishRoot.localRotation = Quaternion.Euler(0f, 0f, -90f * rotationIndex);
            _dishRoot.localScale = new Vector3(scaleX, scaleY, 1f);

            var stainSettings = new FlavorStainPalette.Settings(
                StainScale,
                StainThreshold,
                StainSoftness,
                StainDarken,
                (float)(StableHash.Fnv1a64(dishId) & 0xFFFFFF));
            FlavorStainPalette.ApplyToSpriteRenderer(
                _dishRenderer,
                _flavorScratch,
                ref _dishStainBlock,
                stainSettings);
        }

        private void ComposeFlavorIds(DishDef dish, IReadOnlyList<string> flavorIds)
        {
            _flavorScratch.Clear();
            if (flavorIds != null)
            {
                _flavorScratch.AddRange(flavorIds);
            }
            else if (dish != null && !string.IsNullOrEmpty(dish.FlavorId))
            {
                _flavorScratch.Add(dish.FlavorId);
            }
        }

        private void BuildBadge(
            GameObject badgePrefab,
            DishShape displayShape,
            int deliciousness,
            DishIconPreviewMode mode)
        {
            DishValueBadgeView prefabView = badgePrefab.GetComponent<DishValueBadgeView>();
            if (prefabView == null)
            {
                return;
            }

            float badgeTopInset = mode == DishIconPreviewMode.Warehouse ? 0.42f : 0.12f;
            float badgeY = displayShape.Height * CellSize * 0.5f - badgeTopInset;
            _badge = Instantiate(prefabView, transform);
            if (_badge == null)
            {
                return;
            }

            _badge.gameObject.hideFlags = HideFlags.HideAndDontSave;
            _badge.transform.localPosition = new Vector3(0f, badgeY, 0f);
            _badge.transform.localScale = Vector3.one * BadgeScale;
            SetLayerRecursively(_badge.gameObject, _previewLayer);
            _badge.SetValue(deliciousness.ToString());
            _spawnedObjects.Add(_badge.gameObject);
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            Transform rootTransform = root.transform;
            for (int i = 0; i < rootTransform.childCount; i++)
            {
                SetLayerRecursively(rootTransform.GetChild(i).gameObject, layer);
            }
        }

        private void ClearStage()
        {
            for (int i = 0; i < _spawnedObjects.Count; i++)
            {
                GameObject spawned = _spawnedObjects[i];
                if (spawned == null)
                {
                    continue;
                }

                spawned.SetActive(false);
                if (Application.isPlaying)
                {
                    Destroy(spawned);
                }
                else
                {
                    DestroyImmediate(spawned);
                }
            }

            _spawnedObjects.Clear();
            _flavorScratch.Clear();
            _badge = null;
            _dishRenderer.SetPropertyBlock(null);
            _dishRenderer.sprite = null;
            _dishRoot.localPosition = Vector3.zero;
            _dishRoot.localRotation = Quaternion.identity;
            _dishRoot.localScale = Vector3.one;
        }

        private static Scene CreatePreviewScene()
        {
            string sceneName;
            do
            {
                sceneName = $"{RuntimeSceneName}_{++_sceneSerial}";
            }
            while (SceneManager.GetSceneByName(sceneName).IsValid());

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Scene editorScene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
                editorScene.name = sceneName;
                return editorScene;
            }
#endif
            return SceneManager.CreateScene(sceneName);
        }
    }
}
