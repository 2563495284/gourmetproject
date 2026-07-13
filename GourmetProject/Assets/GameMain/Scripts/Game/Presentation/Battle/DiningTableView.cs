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
        // 空格直接露出格子贴图本色（白色不染色）；虚格压暗，强化格暖黄高亮。
        private static readonly Color EmptyColor = Color.white;
        private static readonly Color VoidColor = new Color(0.07f, 0.04f, 0.03f, 0.0f);
        private static readonly Color TagColor = new Color(1f, 0.88f, 0.32f, 0.95f);

        // 餐桌编辑页：把「胃外虚格」显示为浅色占位（原型里的虚线格），让玩家看到可扩展的最大网格范围。
        private static readonly Color VoidPlaceholderColor = new Color(0.85f, 0.85f, 0.85f, 0.22f);

        private bool _voidAsPlaceholder;

        [SerializeField] private DiningTableCellView _cellPrefab;

        private readonly Dictionary<GridPos, DiningTableCellView> _cells = new Dictionary<GridPos, DiningTableCellView>();
        private Sprite _cellSprite;
        private GpTable _board;
        private Action<GridPos> _clicked;

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
            _cellSprite = Resources.Load<Sprite>("Sprites/UI/board_cell") ?? CreatePixelSprite();

            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    var pos = new GridPos(x, y);
                    DiningTableCellView cell = InstantiateCell();
                    cell.Configure(pos, Mapper.CellCenterLocal(pos), cellSize, _cellSprite, _clicked);
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

            // 兜底：无 prefab 时退回脚本根（保持可运行，不应是常态）。
            var go = new GameObject("Cell");
            go.transform.SetParent(transform, false);
            return go.AddComponent<DiningTableCellView>();
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
                if (!_board.Exists(pos))
                {
                    view.SetColor(_voidAsPlaceholder ? VoidPlaceholderColor : VoidColor);
                }
                else if (_board.DishAt(pos) == null && _board.MaterialsAt(pos).Count > 0)
                {
                    // 仅在「空格 + 有强化标签」时给玩法提示色；占用格不再高亮，只露出菜品 sprite。
                    view.SetColor(TagColor);
                }
                else
                {
                    view.SetColor(EmptyColor);
                }
            }
        }

        private void Clear()
        {
            _cells.Clear();
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }
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
