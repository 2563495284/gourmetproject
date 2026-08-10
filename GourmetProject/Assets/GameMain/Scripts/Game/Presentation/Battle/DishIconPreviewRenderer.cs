using System.Collections.Generic;
using BreakInfinity;
using GourmetProject.Core.Utility;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 在当前活动场景的隐藏运行时 Rig 中复用一套棋盘、食物和美味值标签，
    /// 并把结果渲染到每个 UI 预览自己的 RenderTexture。
    /// </summary>
    internal sealed class DishIconPreviewRenderer : MonoBehaviour
    {
        private const string RuntimeSceneName = "DishIconPreviewRuntime";
        private const string PreviewLayerName = "DishIconPreview";
        private const int FallbackPreviewLayer = 31;
        private const float CellSize = 1f;
        private const float DishInset = 0.06f;
        private const float BadgeScale = 0.85f;
        private static readonly Vector3 StageWorldPosition = new(10000f, 10000f, 0f);
        private const float StageSlotSpacing = 64f;
        private static readonly Color PreviewBackgroundColor = Color.clear;

        private static DishIconPreviewRenderer _instance;

        private readonly List<SpriteRenderer> _cellPool = new();
        private readonly List<string> _flavorScratch = new();
        private Camera _camera;
        private int _previewLayer;
        private Transform _boardRoot;
        private Transform _dishRoot;
        private SpriteRenderer _dishRenderer;
        private MaterialPropertyBlock _dishFlavorBlock;
        private DishValueBadgeView _badge;
        private DishValueBadgeView _badgeSourcePrefab;
        private BigDouble _badgeValue;
        private string _badgeText;
        private bool _hasBadgeValue;

        public static RenderTexture Render(
            DishDef dish,
            Sprite sprite,
            BigDouble deliciousness,
            IReadOnlyList<string> flavorIds,
            SpriteRenderer cellPrefab,
            DishValueBadgeView badgePrefab,
            int pixelsPerCell,
            DishIconPreviewMode mode,
            int? rotationIndexOverride = null)
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
                mode,
                rotationIndexOverride,
                null);
        }

        /// <summary>
        /// 在已有 RT 中重绘。实时风味预览逐帧调用此入口，不创建新的相机、材质或 RT。
        /// </summary>
        public static bool RenderInto(
            RenderTexture target,
            DishDef dish,
            Sprite sprite,
            BigDouble deliciousness,
            IReadOnlyList<string> flavorIds,
            SpriteRenderer cellPrefab,
            DishValueBadgeView badgePrefab,
            int pixelsPerCell,
            DishIconPreviewMode mode,
            int? rotationIndexOverride = null)
        {
            if (target == null
                || !target.IsCreated()
                || dish?.Shape == null
                || sprite == null
                || cellPrefab == null
                || badgePrefab == null)
            {
                return false;
            }

            RenderTexture rendered = Instance.RenderInternal(
                dish,
                sprite,
                deliciousness,
                flavorIds,
                cellPrefab,
                badgePrefab,
                Mathf.Clamp(pixelsPerCell, 32, 256),
                mode,
                rotationIndexOverride,
                target);
            return ReferenceEquals(rendered, target);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PurgeStaleInstances();
            _instance = null;
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

                var root = new GameObject(RuntimeSceneName)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                root.transform.position =
                    StagePositionFor(SceneManager.GetActiveScene());
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
            var cameraObject = new GameObject("Dish Icon Camera")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = _previewLayer,
            };
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.orthographic = true;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 50f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = PreviewBackgroundColor;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            _camera.cullingMask = 1 << _previewLayer;

            var lightObject = new GameObject("Directional Light")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = _previewLayer,
            };
            lightObject.transform.SetParent(transform, false);
            Light mainLight = lightObject.AddComponent<Light>();
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
            BigDouble deliciousness,
            IReadOnlyList<string> flavorIds,
            SpriteRenderer cellPrefab,
            DishValueBadgeView badgePrefab,
            int pixelsPerCell,
            DishIconPreviewMode mode,
            int? rotationIndexOverride,
            RenderTexture existingTarget)
        {
            MoveRigToActiveScene();
            ClearStage();

            ComposeFlavorIds(dish, flavorIds);
            int rotationIndex = rotationIndexOverride
                ?? FlavorStainPalette.DisplayRotationIndex(
                    dish.RotationIndex,
                    _flavorScratch);
            DishShape displayShape = dish.Shape.RotatedBy(rotationIndex);
            Vector2Int boardSize = new(displayShape.Width, displayShape.Height);
            int boardWidth = boardSize.x;
            int boardHeight = boardSize.y;

            if (mode == DishIconPreviewMode.Card)
            {
                BuildBoard(cellPrefab, displayShape);
            }

            BuildDish(dish.Shape, rotationIndex, sprite, dish.Id);
            BuildBadge(badgePrefab, displayShape, deliciousness);

            int textureWidth = boardWidth * pixelsPerCell;
            int textureHeight = boardHeight * pixelsPerCell;
            Color backgroundColor = mode == DishIconPreviewMode.Warehouse
                ? Color.clear
                : PreviewBackgroundColor;
            RenderTexture texture = existingTarget;
            if (texture != null
                && (texture.width != textureWidth || texture.height != textureHeight))
            {
                return null;
            }

            if (texture == null)
            {
                var descriptor = new RenderTextureDescriptor(textureWidth, textureHeight)
                {
                    graphicsFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.LDR),
                    depthStencilFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil),
                    msaaSamples = 1,
                    useMipMap = false,
                    autoGenerateMips = false,
                    memoryless = RenderTextureMemoryless.None,
                };
                texture = new RenderTexture(descriptor)
                {
                    name = $"DishIcon_{mode}_{dish.Id}_{boardWidth}x{boardHeight}",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                texture.Create();
            }

            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = backgroundColor;
            _camera.aspect = (float)boardWidth / boardHeight;
            _camera.orthographicSize = boardHeight * CellSize * 0.5f;
            _camera.targetTexture = texture;
            _camera.Render();
            _camera.targetTexture = null;

            return texture;
        }

        private void MoveRigToActiveScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid()
                || !activeScene.isLoaded
                || gameObject.scene == activeScene)
            {
                return;
            }

            SceneManager.MoveGameObjectToScene(gameObject, activeScene);
            transform.position = StagePositionFor(activeScene);
        }

        internal static IReadOnlyList<GridPos> OccupiedBoardCells(
            DishShape shape)
        {
            return shape?.Cells ?? System.Array.Empty<GridPos>();
        }

        private void BuildBoard(
            SpriteRenderer cellPrefab,
            DishShape shape)
        {
            if (shape == null)
            {
                return;
            }

            Sprite cellSprite = cellPrefab.sprite;
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
            float left = -(shape.Width - 1) * CellSize * 0.5f;
            float top = (shape.Height - 1) * CellSize * 0.5f;
            IReadOnlyList<GridPos> cells = OccupiedBoardCells(shape);
            for (int i = 0; i < cells.Count; i++)
            {
                GridPos cell = cells[i];
                SpriteRenderer cellRenderer;
                if (i < _cellPool.Count)
                {
                    cellRenderer = _cellPool[i];
                }
                else
                {
                    var cellObject = new GameObject($"Cell_{i}")
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                        layer = _previewLayer,
                    };
                    cellObject.transform.SetParent(_boardRoot, false);
                    cellRenderer = cellObject.AddComponent<SpriteRenderer>();
                    _cellPool.Add(cellRenderer);
                }

                cellRenderer.gameObject.SetActive(true);
                cellRenderer.transform.localPosition = new Vector3(
                    left + cell.X * CellSize,
                    top - cell.Y * CellSize,
                    0f);
                cellRenderer.transform.localScale = new Vector3(scaleX, scaleY, 1f);

                cellRenderer.sprite = cellSprite;
                cellRenderer.color = Color.white;
                SpriteRenderStyle.ApplyUnlitMaterial(cellRenderer);
                BattleSorting.Apply(cellRenderer, BattleSorting.DiningTable);
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

            FlavorOrganicVisual.ApplyToSpriteRenderer(
                _dishRenderer,
                _flavorScratch,
                ref _dishFlavorBlock,
                (float)(StableHash.Fnv1a64(dishId) & 0xFFFFFF),
                FlavorOrganicVisual.DefaultIntensity,
                useGlobalTime: true);
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
            DishValueBadgeView badgePrefab,
            DishShape displayShape,
            BigDouble deliciousness)
        {
            if (badgePrefab == null)
            {
                return;
            }

            if (_badge == null || !ReferenceEquals(_badgeSourcePrefab, badgePrefab))
            {
                if (_badge != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(_badge.gameObject);
                    }
                    else
                    {
                        DestroyImmediate(_badge.gameObject);
                    }
                }

                _badge = Instantiate(badgePrefab, transform);
                _badgeSourcePrefab = badgePrefab;
                _hasBadgeValue = false;
                if (_badge == null)
                {
                    return;
                }

                _badge.gameObject.hideFlags = HideFlags.HideAndDontSave;
                SetLayerRecursively(_badge.gameObject, _previewLayer);
            }

            _badge.gameObject.SetActive(true);
            _badge.transform.localScale = Vector3.one * BadgeScale;
            float badgeTopExtent = _badge.TopExtent
                * Mathf.Abs(_badge.transform.localScale.y);
            _badge.transform.localPosition = DishBadgeLayout.PositionFromShapeCenter(
                displayShape,
                CellSize,
                CellSize,
                badgeTopExtent);
            if (!_hasBadgeValue || _badgeValue != deliciousness)
            {
                _badgeValue = deliciousness;
                _badgeText = DishValueDisplay.Format(deliciousness);
                _hasBadgeValue = true;
                _badge.SetValue(_badgeText);
            }
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
            for (int i = 0; i < _cellPool.Count; i++)
            {
                SpriteRenderer cell = _cellPool[i];
                if (cell == null)
                {
                    continue;
                }

                cell.gameObject.SetActive(false);
            }

            _flavorScratch.Clear();
            if (_badge != null)
            {
                _badge.gameObject.SetActive(false);
            }

            _dishRenderer.SetPropertyBlock(null);
            _dishRenderer.sprite = null;
            _dishRoot.localPosition = Vector3.zero;
            _dishRoot.localRotation = Quaternion.identity;
            _dishRoot.localScale = Vector3.one;
        }

    }
}
