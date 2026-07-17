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

        private bool _voidAsPlaceholder;

        [SerializeField] private DiningTableCellView _cellPrefab;

        private readonly Dictionary<GridPos, DiningTableCellView> _cells = new Dictionary<GridPos, DiningTableCellView>();
        private readonly Dictionary<string, Sprite> _materialCellSprites = new Dictionary<string, Sprite>();
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

        public void SetTargetHighlight(GridPos pos, bool selected, bool hovered)
        {
            if (!TryGetCellView(pos, out DiningTableCellView view))
            {
                return;
            }

            Color color = selected
                ? new Color(0.25f, 1f, 0.35f)
                : hovered
                    ? new Color(1f, 0.92f, 0.25f)
                    : new Color(0.25f, 1f, 0.35f);
            view.SetOutline(color, hovered || selected ? 0.08f : 0.045f);
        }

        private void Clear()
        {
            foreach (DiningTableCellView cell in _cells.Values)
            {
                if (cell != null)
                {
                    Destroy(cell.gameObject);
                }
            }

            _cells.Clear();
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
