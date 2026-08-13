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
        private readonly List<SpriteRenderer> _cellPlatePool = new();
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
            int? rotationIndexOverride = null)
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
            int? rotationIndexOverride = null)
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

            BuildDish(displayShape, rotationIndex, sprite, dishPrefab, visualSeed);

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

            BuildBadge(texture, badgePrefab, displayShape, deliciousness);

            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = backgroundColor;
            _camera.aspect = (float)boardWidth / boardHeight;
            FrameCameraToVisibleContent(boardWidth, boardHeight);
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

            Sprite tableSprite = cellPrefab.sprite;
            SpriteRenderer platePrefab = cellPrefab.transform
                .Find("PlateVisual")
                ?.GetComponent<SpriteRenderer>();
            if (platePrefab == null)
            {
                Debug.LogError(
                    "Dish icon preview cell prefab is missing its serialized PlateVisual renderer.",
                    cellPrefab);
                return;
            }

            Sprite plateSprite = platePrefab != null ? platePrefab.sprite : null;
            if (tableSprite == null || plateSprite == null)
            {
                DiningTableCellSprites fallback = DiningTableCellSpriteResources.LoadDefault();
                tableSprite ??= fallback.Table;
                plateSprite ??= fallback.Plate;
            }

            if (tableSprite == null || plateSprite == null)
            {
                Debug.LogError(
                    "Dish icon preview cell prefab has no complete table/plate Sprite pair.",
                    cellPrefab);
                return;
            }

            Transform tableVisualPrefab = cellPrefab.transform;
            Transform plateVisualPrefab = platePrefab.transform;
            IReadOnlyList<GridPos> cells = OccupiedBoardCells(shape);
            for (int i = 0; i < cells.Count; i++)
            {
                GridPos cell = cells[i];
                SpriteRenderer tableRenderer;
                SpriteRenderer plateRenderer;
                if (i < _cellPool.Count)
                {
                    tableRenderer = _cellPool[i];
                    plateRenderer = _cellPlatePool[i];
                }
                else
                {
                    var cellObject = new GameObject($"Cell_{i}")
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                        layer = _previewLayer,
                    };
                    cellObject.transform.SetParent(_boardRoot, false);
                    tableRenderer = cellObject.AddComponent<SpriteRenderer>();

                    var plateObject = new GameObject("PlateVisual")
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                        layer = _previewLayer,
                    };
                    plateObject.transform.SetParent(cellObject.transform, false);
                    plateRenderer = plateObject.AddComponent<SpriteRenderer>();
                    _cellPool.Add(tableRenderer);
                    _cellPlatePool.Add(plateRenderer);
                }

                tableRenderer.gameObject.SetActive(true);
                tableRenderer.transform.localPosition = PreviewTableVisualPosition(
                    tableVisualPrefab,
                    shape,
                    cell);
                tableRenderer.transform.localRotation = tableVisualPrefab.localRotation;
                tableRenderer.transform.localScale = PreviewTableVisualScale(tableVisualPrefab);
                plateRenderer.transform.localPosition = plateVisualPrefab.localPosition;
                plateRenderer.transform.localRotation = plateVisualPrefab.localRotation;
                plateRenderer.transform.localScale = plateVisualPrefab.localScale;

                tableRenderer.sprite = tableSprite;
                tableRenderer.color = Color.white;
                plateRenderer.sprite = plateSprite;
                plateRenderer.color = Color.white;
                SpriteRenderStyle.ApplyUnlitMaterial(tableRenderer);
                SpriteRenderStyle.ApplyUnlitMaterial(plateRenderer);
                BattleSorting.Apply(
                    tableRenderer,
                    BattleSorting.DiningTable,
                    cell.Y * 2);
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
            float visualSeed)
        {
            _dishRenderer.gameObject.SetActive(true);
            _dishRenderer.sprite = sprite;
            _dishRenderer.color = Color.white;
            SpriteRenderStyle.ApplyUnlitMaterial(_dishRenderer);
            BattleSorting.Apply(_dishRenderer, BattleSorting.Pieces, BattleSorting.OrderBody);

            _dishRoot.localPosition = Vector3.zero;
            _dishRoot.localRotation = Quaternion.Euler(0f, 0f, -90f * rotationIndex);
            _dishRoot.localScale = DishVisualLayout.SpriteScale(
                sprite,
                displayShape,
                rotationIndex,
                CellSize,
                Pitch);

            BuildContactShadow(displayShape, dishPrefab);

            FlavorOrganicVisual.ApplyToSpriteRenderer(
                _dishRenderer,
                _flavorScratch,
                ref _dishFlavorBlock,
                visualSeed,
                dishPrefab.PreviewFlavorVisualIntensity,
                useGlobalTime: true);
        }

        private void BuildContactShadow(DishShape displayShape, DishPieceView dishPrefab)
        {
            Vector2 span = DishVisualLayout.FootprintSpan(displayShape, CellSize, Pitch);
            _dishShadowRenderer.gameObject.SetActive(true);
            _dishShadowRenderer.sprite = BattleShadow.SoftShadowSprite;
            _dishShadowRenderer.color = new Color(
                0f,
                0f,
                0f,
                dishPrefab.PreviewShadowBaseAlpha);
            _dishShadowRenderer.transform.localPosition = new Vector3(
                CellSize * dishPrefab.PreviewShadowGroundSide,
                -CellSize * dishPrefab.PreviewShadowGroundDrop,
                0.05f);
            _dishShadowRenderer.transform.localRotation = Quaternion.identity;
            _dishShadowRenderer.transform.localScale = new Vector3(
                span.x * dishPrefab.PreviewShadowGroundScale,
                span.y * dishPrefab.PreviewShadowGroundScale,
                1f);
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
            BigDouble deliciousness)
        {
            if (target == null || badgePrefab == null)
            {
                return;
            }

            PruneBadgeStates();
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
            state.View.transform.localScale = Vector3.one * BadgeScale;
            float badgeTopExtent = state.View.TopExtent
                * Mathf.Abs(state.View.transform.localScale.y);
            state.View.transform.localPosition = DishBadgeLayout.PositionFromShapeCenter(
                displayShape,
                CellSize,
                Pitch,
                badgeTopExtent);
            if (!state.HasValue || state.Value != deliciousness)
            {
                state.Value = deliciousness;
                state.HasValue = true;
                state.View.SetValue(DishValueDisplay.Format(deliciousness));
            }
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
