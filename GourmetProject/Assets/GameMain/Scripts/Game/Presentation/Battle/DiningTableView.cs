using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    public sealed class DiningTableView : MonoBehaviour
    {
        // 空格直接露出格子贴图本色（白色不染色）；虚格压暗。
        private static readonly Color EmptyColor = Color.white;
        private static readonly Color VoidColor = new Color(0.07f, 0.04f, 0.03f, 0.0f);

        // 餐桌编辑页：把「胃外虚格」显示为浅色占位（原型里的虚线格），让玩家看到可扩展的最大网格范围。
        private static readonly Color VoidPlaceholderColor = new Color(0.85f, 0.85f, 0.85f, 0.22f);
        private const int DragFeedbackSortingOrder = -80;

        private bool _voidAsPlaceholder;

        [SerializeField] private DiningTableCellView _cellPrefab;
        [SerializeField] private BattleScopeRegionOutlineView _scopeRegionOutlinePrefab;

        private readonly Dictionary<GridPos, DiningTableCellView> _cells = new Dictionary<GridPos, DiningTableCellView>();
        private readonly Dictionary<int, BattleScopeRegionOutlineView> _scopeRegionOutlines = new Dictionary<int, BattleScopeRegionOutlineView>();
        private readonly Dictionary<string, Sprite> _materialCellSprites = new Dictionary<string, Sprite>();
        private readonly List<DiningTableCellView> _dragFeedbackCells = new List<DiningTableCellView>();
        private Sprite _cellSprite;
        private float _cellSize;
        private GpTable _board;
        private Action<GridPos> _clicked;
        private Action<DiningTableCellView> _cellHoverEntered;
        private Action<DiningTableCellView> _cellHoverExited;

        public DiningTableCoordinateMapper Mapper { get; private set; }

        /// <summary>
        /// 生成餐桌格。餐桌以本组件 transform 为局部帧（BoardRoot）：格子挂在其下并以 localPosition 摆放，
        /// 世界摆放/居中/缩放由调用方设置本 transform 的 position/scale 决定。
        /// </summary>
        public void Build(GpTable board, float cellSize, float gap, Action<GridPos> clicked, DiningTableCellView cellPrefab = null)
        {
            Clear();
            _board = board ?? throw new ArgumentNullException(nameof(board));
            _clicked = clicked;
            if (cellPrefab != null)
            {
                _cellPrefab = cellPrefab;
            }

            Mapper = new DiningTableCoordinateMapper(board.Width, board.Height, cellSize, gap, transform);
            _cellSize = cellSize;
            _materialCellSprites.Clear();
            _cellSprite = LoadCellSprite("board_cell") ?? CreatePixelSprite();

            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    var pos = new GridPos(x, y);
                    DiningTableCellView cell = InstantiateCell();
                    if (cell == null)
                    {
                        continue;
                    }

                    cell.Configure(pos, Mapper.CellCenterLocal(pos), cellSize, _cellSprite, _clicked);
                    cell.SetHoverCallbacks(OnCellHoverEntered, OnCellHoverExited);
                    _cells[pos] = cell;
                }
            }

            Sync();
        }

        private DiningTableCellView InstantiateCell()
        {
            if (_cellPrefab != null)
            {
                DiningTableCellView cell = Instantiate(_cellPrefab, transform);
                return cell;
            }

            Debug.LogError($"{nameof(DiningTableView)} 缺少 DiningTableCell prefab。", this);
            return null;
        }

        /// <summary>餐桌编辑页开关：把胃外虚格显示为浅色占位（最大网格提示）。需再次 Sync 生效。</summary>
        public void ShowVoidAsPlaceholders(bool enabled)
        {
            _voidAsPlaceholder = enabled;
            Sync();
        }

        public void Sync()
        {
            if (_board == null)
            {
                return;
            }

            foreach (KeyValuePair<GridPos, DiningTableCellView> kv in _cells)
            {
                GridPos pos = kv.Key;
                DiningTableCellView view = kv.Value;
                view.SetSprite(CellSpriteFor(pos), _cellSize);
                if (!_board.Exists(pos))
                {
                    view.SetColor(_voidAsPlaceholder ? VoidPlaceholderColor : VoidColor);
                    view.SetDebuffed(false);
                }
                else
                {
                    view.SetColor(EmptyColor);
                    view.SetDebuffed(_board.IsDisabled(pos));
                }
            }
        }

        private Sprite CellSpriteFor(GridPos pos)
        {
            if (_board == null || !_board.Exists(pos))
            {
                return _cellSprite;
            }

            IReadOnlyList<string> materials = _board.MaterialsAt(pos);
            for (int i = materials.Count - 1; i >= 0; i--)
            {
                Sprite sprite = LoadMaterialCellSprite(materials[i]);
                if (sprite != null)
                {
                    return sprite;
                }
            }

            return _cellSprite;
        }

        private Sprite LoadMaterialCellSprite(string materialId)
        {
            if (string.IsNullOrEmpty(materialId))
            {
                return null;
            }

            if (_materialCellSprites.TryGetValue(materialId, out Sprite cached))
            {
                return cached;
            }

            Sprite sprite = LoadCellSprite($"board_cell_{materialId}");
            _materialCellSprites[materialId] = sprite;
            return sprite;
        }

        private static Sprite LoadCellSprite(string name)
        {
            string path = $"Sprites/UI/{name}";
            Sprite sprite = Resources.Load<Sprite>(path);
            if (sprite != null)
            {
                return sprite;
            }

            Sprite[] sprites = Resources.LoadAll<Sprite>(path);
            return sprites != null && sprites.Length > 0 ? sprites[0] : null;
        }

        public bool TryGetCellView(GridPos pos, out DiningTableCellView view)
        {
            return _cells.TryGetValue(pos, out view) && view != null;
        }

        public void SetCellHoverCallbacks(Action<DiningTableCellView> entered, Action<DiningTableCellView> exited)
        {
            _cellHoverEntered = entered;
            _cellHoverExited = exited;
            foreach (DiningTableCellView cell in _cells.Values)
            {
                if (cell != null)
                {
                    cell.SetHoverCallbacks(OnCellHoverEntered, OnCellHoverExited);
                }
            }
        }

        public void ClearTargetHighlights()
        {
            foreach (DiningTableCellView view in _cells.Values)
            {
                view?.ClearOutline();
            }

            Sync();
        }

        public void ShowDragPlacementFeedback(DishDragPlacementResult result)
        {
            ShowGridPlacementFeedback(result?.ToGridPlacementFeedback(), dishPlacement: true);
        }

        public void ShowGridPlacementFeedback(GridPlacementFeedback result)
        {
            ShowGridPlacementFeedback(result, dishPlacement: false);
        }

        private void ShowGridPlacementFeedback(
            GridPlacementFeedback result,
            bool dishPlacement)
        {
            if (result == null || Mapper == null)
            {
                ClearDragPlacementFeedback();
                return;
            }

            var display = new Dictionary<GridPos, GridPlacementFeedbackState>();
            foreach (GridPlacementFeedbackCell cell in result.Cells)
            {
                display[cell.Position] = cell.State;
            }

            // 餐桌碎片仍显示中心格的整体状态；食物只映射其实际占用格，
            // 避免中心格覆盖单格反馈或在不规则形状的空洞中多画一格。
            if (!dishPlacement)
            {
                display[result.CenterCell] = result.OverallState;
            }

            EnsureDragFeedbackCount(display.Count);
            if (_dragFeedbackCells.Count < display.Count)
            {
                ClearDragPlacementFeedback();
                return;
            }

            int index = 0;
            foreach (KeyValuePair<GridPos, GridPlacementFeedbackState> entry in display)
            {
                DiningTableCellView overlay = _dragFeedbackCells[index++];
                bool center = !dishPlacement && entry.Key.Equals(result.CenterCell);
                Color color = dishPlacement
                    ? GridPlacementFeedbackPalette.DishColorFor(result.OverallState, entry.Value)
                    : GridPlacementFeedbackPalette.ColorFor(entry.Value);
                overlay.gameObject.SetActive(true);
                overlay.transform.localRotation = Quaternion.identity;
                overlay.Configure(entry.Key, Mapper.CellCenterLocal(entry.Key), _cellSize, _cellSprite, null);
                overlay.name = "DragPlacementFeedback";
                overlay.SetInteractionEnabled(false);
                overlay.SetOutline(
                    color,
                    center ? 0.12f : 0.08f,
                    dishPlacement ? 0f : center ? 0.28f : 0.16f);
                overlay.SetSorting(BattleSorting.Fx, DragFeedbackSortingOrder);
            }

            for (; index < _dragFeedbackCells.Count; index++)
            {
                _dragFeedbackCells[index]?.gameObject.SetActive(false);
            }
        }

        public void ClearDragPlacementFeedback()
        {
            foreach (DiningTableCellView overlay in _dragFeedbackCells)
            {
                if (overlay != null)
                {
                    overlay.gameObject.SetActive(false);
                }
            }
        }

        public void SetTargetHighlight(GridPos pos, bool selected, bool hovered)
        {
            if (!TryGetCellView(pos, out DiningTableCellView view))
            {
                return;
            }

            if (!selected && !hovered)
            {
                view.ClearOutline();
                return;
            }

            view.SetOutline(new Color(0.25f, 1f, 0.35f), selected ? 0.1f : 0.08f);
        }

        public void ClearScopeHighlights(BattleScopeHighlightChannel channel)
        {
            foreach (KeyValuePair<int, BattleScopeRegionOutlineView> pair in _scopeRegionOutlines)
            {
                if (ChannelFromScopeKey(pair.Key) == channel)
                {
                    pair.Value?.Hide();
                }
            }
        }

        public void ClearAllScopeHighlights()
        {
            foreach (BattleScopeRegionOutlineView outline in _scopeRegionOutlines.Values)
            {
                outline?.Hide();
            }
        }

        public void SetScopeRegionHighlight(
            IReadOnlyList<GridPos> cells,
            BattleScopeHighlightChannel channel,
            int layer,
            Color color,
            float width,
            Material materialOverride = null)
        {
            if (cells == null || cells.Count == 0 || Mapper == null)
            {
                return;
            }

            bool hasCell = false;
            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;
            var validCells = new List<GridPos>();
            foreach (GridPos cell in cells)
            {
                if (!_cells.ContainsKey(cell) || _board == null || !_board.Exists(cell))
                {
                    continue;
                }

                hasCell = true;
                validCells.Add(cell);
                minX = Mathf.Min(minX, cell.X);
                minY = Mathf.Min(minY, cell.Y);
                maxX = Mathf.Max(maxX, cell.X);
                maxY = Mathf.Max(maxY, cell.Y);
            }

            if (!hasCell)
            {
                return;
            }

            BattleScopeRegionOutlineView outline = EnsureScopeRegionOutline(channel, layer);
            if (outline == null)
            {
                return;
            }

            Vector3 firstCenter = Mapper.CellCenterLocal(new GridPos(minX, minY));
            Vector3 lastCenter = Mapper.CellCenterLocal(new GridPos(maxX, maxY));
            Vector3 localCenter = (firstCenter + lastCenter) * 0.5f;
            Vector2 localSize = new Vector2(
                Mathf.Abs(lastCenter.x - firstCenter.x) + _cellSize,
                Mathf.Abs(lastCenter.y - firstCenter.y) + _cellSize);
            outline.Show(
                channel,
                layer,
                validCells,
                minX,
                minY,
                maxX,
                maxY,
                localCenter,
                localSize,
                color,
                width,
                materialOverride);
        }

        public void SetAllExistingScopeHighlight(
            BattleScopeHighlightChannel channel,
            int layer,
            Color color,
            float width,
            Material materialOverride = null)
        {
            if (_board == null)
            {
                return;
            }

            SetScopeRegionHighlight(
                _board.ExistingCells(),
                channel,
                layer,
                color,
                width,
                materialOverride);
        }

        private void Clear()
        {
            foreach (DiningTableCellView overlay in _dragFeedbackCells)
            {
                if (overlay != null)
                {
                    Destroy(overlay.gameObject);
                }
            }

            _dragFeedbackCells.Clear();
            foreach (BattleScopeRegionOutlineView outline in _scopeRegionOutlines.Values)
            {
                if (outline != null)
                {
                    Destroy(outline.gameObject);
                }
            }

            _scopeRegionOutlines.Clear();
            foreach (DiningTableCellView cell in _cells.Values)
            {
                if (cell != null)
                {
                    Destroy(cell.gameObject);
                }
            }

            _cells.Clear();
        }

        private void EnsureDragFeedbackCount(int count)
        {
            while (_dragFeedbackCells.Count < count)
            {
                DiningTableCellView overlay = InstantiateCell();
                if (overlay == null)
                {
                    return;
                }

                overlay.name = "DragPlacementFeedback";
                overlay.SetInteractionEnabled(false);
                overlay.gameObject.SetActive(false);
                _dragFeedbackCells.Add(overlay);
            }
        }

        private BattleScopeRegionOutlineView EnsureScopeRegionOutline(
            BattleScopeHighlightChannel channel,
            int layer)
        {
            int key = ScopeLayerKey(channel, layer);
            if (_scopeRegionOutlines.TryGetValue(key, out BattleScopeRegionOutlineView existing)
                && existing != null)
            {
                return existing;
            }

            if (_scopeRegionOutlinePrefab == null)
            {
                Debug.LogError($"{nameof(DiningTableView)} 缺少 ScopeRegionOutline prefab。", this);
                return null;
            }

            BattleScopeRegionOutlineView outline = Instantiate(_scopeRegionOutlinePrefab, transform);
            outline.name = $"ScopeRegion_{channel}_{layer}";
            outline.Hide();
            _scopeRegionOutlines[key] = outline;
            return outline;
        }

        private static int ScopeLayerKey(BattleScopeHighlightChannel channel, int layer)
        {
            return ((int)channel * 1000) + Mathf.Clamp(layer, 0, 999);
        }

        private static BattleScopeHighlightChannel ChannelFromScopeKey(int key)
        {
            return (BattleScopeHighlightChannel)Mathf.Max(0, key / 1000);
        }

        private void OnCellHoverEntered(DiningTableCellView cell)
        {
            _cellHoverEntered?.Invoke(cell);
        }

        private void OnCellHoverExited(DiningTableCellView cell)
        {
            _cellHoverExited?.Invoke(cell);
        }

        private static Sprite CreatePixelSprite()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}
