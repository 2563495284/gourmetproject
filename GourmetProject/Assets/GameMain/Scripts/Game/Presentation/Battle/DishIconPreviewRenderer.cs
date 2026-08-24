using System.Collections.Generic;
using BreakInfinity;
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
        private const float Pitch = CellSize
                                    + DiningTableLayout.Gap / DiningTableLayout.MaxCellSize;
        private const float BadgeScale = 1f;
        private const float CameraPadding = 0.025f;
        private static readonly Vector3 StageWorldPosition = new(10000f, 10000f, 0f);
        private const float StageSlotSpacing = 64f;
        private static readonly Color PreviewBackgroundColor = Color.clear;

        private static DishIconPreviewRenderer _instance;

        private readonly List<SpriteRenderer> _cellPool = new();
        private readonly List<string> _flavorScratch = new();
        private readonly Dictionary<RenderTexture, BadgePreviewState> _badgesByTarget = new();
        private readonly List<RenderTexture> _staleBadgeTargets = new();
        private Camera _camera;
        private int _previewLayer;
        private Transform _boardRoot;
        private Transform _dishRoot;
        private SpriteRenderer _dishRenderer;
        private SpriteRenderer _dishShadowRenderer;
        private MaterialPropertyBlock _dishFlavorBlock;

        private sealed class BadgePreviewState
        {
            public DishValueBadgeView View;
            public DishValueBadgeView SourcePrefab;
            public BigDouble Value;
            public bool HasValue;
            public DishIconPreviewMode Mode;
            public Vector2Int ShapeSize;
            public float Scale = BadgeScale;
        }

        public static RenderTexture Render(
            DishDef dish,
            Sprite sprite,
            BigDouble deliciousness,
            IReadOnlyList<string> flavorIds,
            SpriteRenderer cellPrefab,
            DishPieceView dishPrefab,
            DishValueBadgeView badgePrefab,
            int pixelsPerCell,
            DishIconPreviewMode mode,
            float visualSeed,
            int? rotationIndexOverride = null,
            bool showValueBadge = true,
            float valueBadgeAlpha = 1f)
        {
            if (dish?.Shape == null
                || sprite == null
                || cellPrefab == null
                || dishPrefab == null
                || badgePrefab == null)
            {
                return null;
            }

            return Instance.RenderInternal(
                dish,
                sprite,
                deliciousness,
                flavorIds,
                cellPrefab,
                dishPrefab,
                badgePrefab,
                Mathf.Clamp(pixelsPerCell, 32, 256),
                mode,
                visualSeed,
                rotationIndexOverride,
                showValueBadge,
                valueBadgeAlpha,
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
            DishPieceView dishPrefab,
            DishValueBadgeView badgePrefab,
            int pixelsPerCell,
            DishIconPreviewMode mode,
            float visualSeed,
            int? rotationIndexOverride = null,
            bool showValueBadge = true,
            float valueBadgeAlpha = 1f)
        {
            if (target == null
                || !target.IsCreated()
                || dish?.Shape == null
                || sprite == null
                || cellPrefab == null
                || dishPrefab == null
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
                dishPrefab,
                badgePrefab,
                Mathf.Clamp(pixelsPerCell, 32, 256),
                mode,
                visualSeed,
                rotationIndexOverride,
                showValueBadge,
                valueBadgeAlpha,
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
            Transform shadowRoot = NewRoot("DishShadow");
            _dishShadowRenderer = shadowRoot.gameObject.AddComponent<SpriteRenderer>();
            SpriteRenderStyle.ApplyUnlitMaterial(_dishShadowRenderer);
            BattleSorting.Apply(
                _dishShadowRenderer,
                BattleSorting.Pieces,
                BattleSorting.OrderShadow);
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
            DishPieceView dishPrefab,
            DishValueBadgeView badgePrefab,
            int pixelsPerCell,
            DishIconPreviewMode mode,
            float visualSeed,
            int? rotationIndexOverride,
            bool showValueBadge,
            float valueBadgeAlpha,
            RenderTexture existingTarget)
        {
            MoveRigToActiveScene();
            ClearStage();

            ComposeFlavorIds(dish, flavorIds);
            int rotationIndex = rotationIndexOverride
                ?? FlavorStainPalette.DisplayRotationIndex(
                    0,
                    _flavorScratch);
            DishShape displayShape = dish.Shape.RotatedBy(rotationIndex);
            Vector2Int boardSize = new(displayShape.Width, displayShape.Height);
            int boardWidth = boardSize.x;
            int boardHeight = boardSize.y;

            if (mode == DishIconPreviewMode.Card)
            {
                BuildBoard(cellPrefab, displayShape);
            }

            BuildDish(
                displayShape,
                rotationIndex,
                sprite,
                dishPrefab,
                mode,
                visualSeed);

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

            BuildBadge(
                texture,
                badgePrefab,
                displayShape,
                deliciousness,
                mode,
                showValueBadge,
                valueBadgeAlpha);

            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = backgroundColor;
            _camera.aspect = (float)boardWidth / boardHeight;
            if (mode == DishIconPreviewMode.Warehouse)
            {
                FrameCameraToFootprint(displayShape);
            }
            else
            {
                FrameCameraToVisibleContent(boardWidth, boardHeight);
            }
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
                DiningTableCellSprites fallback = DiningTableCellSpriteResources.LoadDefault();
                cellSprite = fallback.Plate;
            }

            if (cellSprite == null)
            {
                Debug.LogError(
                    "Dish icon preview cell prefab has no PlateVisual Sprite.",
                    cellPrefab);
                return;
            }

            Transform plateVisualPrefab = cellPrefab.transform;
            IReadOnlyList<GridPos> cells = OccupiedBoardCells(shape);
            for (int i = 0; i < cells.Count; i++)
            {
                GridPos cell = cells[i];
                SpriteRenderer plateRenderer;
                if (i < _cellPool.Count)
                {
                    plateRenderer = _cellPool[i];
                }
                else
                {
                    var cellObject = new GameObject($"Cell_{i}")
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                        layer = _previewLayer,
                    };
                    cellObject.transform.SetParent(_boardRoot, false);
                    plateRenderer = cellObject.AddComponent<SpriteRenderer>();
                    _cellPool.Add(plateRenderer);
                }

                plateRenderer.gameObject.SetActive(true);
                plateRenderer.transform.localPosition = PreviewTableVisualPosition(
                    plateVisualPrefab,
                    shape,
                    cell);
                plateRenderer.transform.localRotation = plateVisualPrefab.localRotation;
                plateRenderer.transform.localScale = PreviewTableVisualScale(plateVisualPrefab);

                plateRenderer.sprite = cellSprite;
                plateRenderer.color = Color.white;
                SpriteRenderStyle.ApplyUnlitMaterial(plateRenderer);
                BattleSorting.Apply(
                    plateRenderer,
                    BattleSorting.DiningTable,
                    cell.Y * 2 + 1);
            }
        }

        internal static Vector3 PreviewTableVisualPosition(
            Transform tableVisualPrefab,
            DishShape shape,
            GridPos cell)
        {
            float left = -(shape.Width - 1) * Pitch * 0.5f;
            float top = (shape.Height - 1) * Pitch * 0.5f;
            return new Vector3(
                       left + cell.X * Pitch,
                       top - cell.Y * Pitch,
                       0f)
                   + tableVisualPrefab.localPosition * CellSize;
        }

        internal static Vector3 PreviewTableVisualScale(Transform tableVisualPrefab)
        {
            return tableVisualPrefab.localScale * CellSize;
        }

        private void BuildDish(
            DishShape displayShape,
            int rotationIndex,
            Sprite sprite,
            DishPieceView dishPrefab,
            DishIconPreviewMode mode,
            float visualSeed)
        {
            _dishRenderer.gameObject.SetActive(true);
            _dishRenderer.sprite = sprite;
            _dishRenderer.color = Color.white;
            SpriteRenderStyle.ApplyUnlitMaterial(_dishRenderer);
            BattleSorting.Apply(_dishRenderer, BattleSorting.Pieces, BattleSorting.OrderBody);

            bool useTightMeshBounds = mode == DishIconPreviewMode.Warehouse;
            Quaternion rotation = Quaternion.Euler(0f, 0f, -90f * rotationIndex);
            Vector3 scale = DishVisualLayout.SpriteScale(
                sprite,
                displayShape,
                rotationIndex,
                CellSize,
                Pitch,
                useTightMeshBounds);
            if (useTightMeshBounds)
            {
                // 无棋盘底图的仓库/出餐口预览不能分别拉满宽高，否则原画会变形。
                // 取较小轴的缩放值，让可见 Sprite 等比放入占格包围盒。
                float uniformScale = Mathf.Min(scale.x, scale.y);
                scale = new Vector3(uniformScale, uniformScale, 1f);
            }

            _dishRoot.localRotation = rotation;
            _dishRoot.localScale = scale;
            _dishRoot.localPosition = useTightMeshBounds
                ? DishVisualLayout.PositionToCenterSpriteBounds(
                    DishVisualLayout.SpriteMeshBounds(sprite),
                    scale,
                    rotation)
                : Vector3.zero;

            if (mode == DishIconPreviewMode.Card)
            {
                BuildContactShadow(dishPrefab);
            }

            FlavorOrganicVisual.ApplyToSpriteRenderer(
                _dishRenderer,
                _flavorScratch,
                ref _dishFlavorBlock,
                visualSeed,
                dishPrefab.PreviewFlavorVisualIntensity,
                useGlobalTime: true);
        }

        private void BuildContactShadow(DishPieceView dishPrefab)
        {
            _dishShadowRenderer.gameObject.SetActive(true);
            _dishShadowRenderer.sprite = _dishRenderer.sprite;
            _dishShadowRenderer.color = new Color(
                0f,
                0f,
                0f,
                dishPrefab.PreviewShadowBaseAlpha);
            float scale = Mathf.Max(0.0001f, dishPrefab.PreviewShadowGroundScale);
            _dishShadowRenderer.transform.localPosition = new Vector3(
                _dishRoot.localPosition.x + CellSize * dishPrefab.PreviewShadowGroundSide,
                _dishRoot.localPosition.y - CellSize * dishPrefab.PreviewShadowGroundDrop,
                0.05f);
            _dishShadowRenderer.transform.localRotation = _dishRoot.localRotation;
            _dishShadowRenderer.transform.localScale = Vector3.Scale(
                _dishRoot.localScale,
                new Vector3(scale, scale, 1f));
            _dishShadowRenderer.flipX = _dishRenderer.flipX;
            _dishShadowRenderer.flipY = _dishRenderer.flipY;
            SpriteRenderStyle.ApplyUnlitMaterial(_dishShadowRenderer);
            BattleSorting.Apply(
                _dishShadowRenderer,
                BattleSorting.Pieces,
                BattleSorting.OrderShadow);
        }

        private void FrameCameraToVisibleContent(int boardWidth, int boardHeight)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(includeInactive: false);
            bool hasBounds = false;
            Bounds visibleBounds = default;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    visibleBounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    visibleBounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
            {
                _camera.transform.localPosition = new Vector3(0f, 0f, -10f);
                _camera.orthographicSize = boardHeight * CellSize * 0.5f;
                return;
            }

            Vector3 center = transform.InverseTransformPoint(visibleBounds.center);
            float width = Mathf.Max(CellSize, visibleBounds.size.x) * (1f + CameraPadding * 2f);
            float height = Mathf.Max(CellSize, visibleBounds.size.y) * (1f + CameraPadding * 2f);
            float aspect = Mathf.Max(0.0001f, (float)boardWidth / boardHeight);
            _camera.transform.localPosition = new Vector3(center.x, center.y, -10f);
            _camera.orthographicSize = Mathf.Max(height * 0.5f, width / (aspect * 2f));
        }

        private void FrameCameraToFootprint(DishShape displayShape)
        {
            _camera.transform.localPosition = new Vector3(0f, 0f, -10f);
            _camera.orthographicSize = PreviewOrthographicSize(
                displayShape,
                _camera.aspect);
        }

        internal static float PreviewOrthographicSize(
            DishShape displayShape,
            float aspect)
        {
            Vector2 span = DishVisualLayout.FootprintSpan(
                displayShape,
                CellSize,
                Pitch);
            float safeAspect = Mathf.Max(0.0001f, aspect);
            float size = Mathf.Max(
                span.y * 0.5f,
                span.x / (safeAspect * 2f));
            return size * (1f + CameraPadding * 2f);
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
            RenderTexture target,
            DishValueBadgeView badgePrefab,
            DishShape displayShape,
            BigDouble deliciousness,
            DishIconPreviewMode mode,
            bool visible,
            float valueAlpha)
        {
            if (target == null || badgePrefab == null)
            {
                return;
            }

            PruneBadgeStates();
            if (!visible)
            {
                return;
            }

            if (!_badgesByTarget.TryGetValue(target, out BadgePreviewState state)
                || state?.View == null
                || !ReferenceEquals(state.SourcePrefab, badgePrefab))
            {
                if (state?.View != null)
                {
                    DestroyBadge(state.View);
                }

                DishValueBadgeView view = Instantiate(badgePrefab, transform);
                if (view == null)
                {
                    return;
                }

                view.gameObject.hideFlags = HideFlags.HideAndDontSave;
                SetLayerRecursively(view.gameObject, _previewLayer);
                state = new BadgePreviewState
                {
                    View = view,
                    SourcePrefab = badgePrefab,
                };
                _badgesByTarget[target] = state;
            }

            state.View.gameObject.SetActive(true);
            state.View.SetValueAlpha(valueAlpha);
            Vector2Int shapeSize = new(displayShape.Width, displayShape.Height);
            bool layoutChanged = state.Mode != mode || state.ShapeSize != shapeSize;
            state.View.transform.localScale = Vector3.one * BadgeScale;
            if (!state.HasValue || state.Value != deliciousness)
            {
                state.Value = deliciousness;
                state.HasValue = true;
                state.View.SetValue(
                    DishValueDisplay.Format(deliciousness),
                    forceMeshUpdate: true);
                layoutChanged = true;
            }

            if (layoutChanged)
            {
                state.Mode = mode;
                state.ShapeSize = shapeSize;
                state.Scale = mode == DishIconPreviewMode.Warehouse
                    ? WarehouseBadgeScale(state.View, displayShape)
                    : BadgeScale;
            }

            state.View.transform.localScale = Vector3.one * state.Scale;
            float badgeTopExtent = state.View.TopExtent
                * Mathf.Abs(state.View.transform.localScale.y);
            Vector3 badgePosition = DishBadgeLayout.PositionFromShapeCenter(
                displayShape,
                CellSize,
                Pitch,
                badgeTopExtent);
            state.View.transform.localPosition = badgePosition;
            if (mode == DishIconPreviewMode.Warehouse)
            {
                CenterWarehouseBadgeHorizontally(state.View, badgePosition);
            }
        }

        private static float WarehouseBadgeScale(
            DishValueBadgeView view,
            DishShape displayShape)
        {
            if (view == null || displayShape == null)
            {
                return BadgeScale;
            }

            if (!TryGetRendererBounds(view, out Bounds badgeBounds)
                || badgeBounds.size.x <= 0.0001f)
            {
                return BadgeScale;
            }

            DishBadgeLayout.GridRun run =
                DishBadgeLayout.FindTopContinuousRun(displayShape.Cells);
            float availableWidth =
                (run.EndX - run.StartX) * Pitch + CellSize;
            return Mathf.Min(
                BadgeScale,
                availableWidth / badgeBounds.size.x);
        }

        private void CenterWarehouseBadgeHorizontally(
            DishValueBadgeView view,
            Vector3 desiredPosition)
        {
            if (!TryGetRendererBounds(view, out Bounds badgeBounds))
            {
                return;
            }

            float visibleCenterX = transform
                .InverseTransformPoint(badgeBounds.center)
                .x;
            desiredPosition.x += desiredPosition.x - visibleCenterX;
            view.transform.localPosition = desiredPosition;
        }

        private static bool TryGetRendererBounds(
            DishValueBadgeView view,
            out Bounds bounds)
        {
            bounds = default;
            if (view == null)
            {
                return false;
            }

            Renderer[] renderers = view.GetComponentsInChildren<Renderer>(false);
            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }

        private void PruneBadgeStates()
        {
            _staleBadgeTargets.Clear();
            foreach (KeyValuePair<RenderTexture, BadgePreviewState> entry in _badgesByTarget)
            {
                if (entry.Key == null || !entry.Key.IsCreated() || entry.Value?.View == null)
                {
                    if (entry.Value?.View != null)
                    {
                        DestroyBadge(entry.Value.View);
                    }
                    _staleBadgeTargets.Add(entry.Key);
                }
            }

            for (int i = 0; i < _staleBadgeTargets.Count; i++)
            {
                _badgesByTarget.Remove(_staleBadgeTargets[i]);
            }
            _staleBadgeTargets.Clear();
        }

        private static void DestroyBadge(DishValueBadgeView badge)
        {
            if (badge == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(badge.gameObject);
            }
            else
            {
                DestroyImmediate(badge.gameObject);
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
            foreach (BadgePreviewState state in _badgesByTarget.Values)
            {
                if (state?.View != null)
                {
                    state.View.gameObject.SetActive(false);
                }
            }

            _dishRenderer.SetPropertyBlock(null);
            _dishRenderer.sprite = null;
            _dishShadowRenderer.sprite = null;
            _dishShadowRenderer.gameObject.SetActive(false);
            _dishRoot.localPosition = Vector3.zero;
            _dishRoot.localRotation = Quaternion.identity;
            _dishRoot.localScale = Vector3.one;
        }

    }
}
