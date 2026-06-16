using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using GpBoard = GourmetProject.Gameplay.Board.Board;

namespace GourmetProject.Game.Gameplay.Presentation
{
    public sealed class BoardView : MonoBehaviour
    {
        // 空格直接露出格子贴图本色（白色不染色）；虚格压暗，强化格暖黄高亮。
        private static readonly Color EmptyColor = Color.white;
        private static readonly Color VoidColor = new Color(0.07f, 0.04f, 0.03f, 0.0f);
        private static readonly Color TagColor = new Color(1f, 0.88f, 0.32f, 0.95f);

        private readonly Dictionary<GridPos, BoardCellView> _cells = new Dictionary<GridPos, BoardCellView>();
        private Sprite _cellSprite;
        private GpBoard _board;
        private Action<GridPos> _clicked;

        public BoardCoordinateMapper Mapper { get; private set; }

        public void Build(GpBoard board, float cellSize, float gap, Vector3 center, Action<GridPos> clicked)
        {
            Clear();
            _board = board ?? throw new ArgumentNullException(nameof(board));
            _clicked = clicked;
            Mapper = new BoardCoordinateMapper(board.Width, board.Height, cellSize, gap, center);
            _cellSprite = Resources.Load<Sprite>("Sprites/UI/board_cell") ?? CreatePixelSprite();

            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    var pos = new GridPos(x, y);
                    BoardCellView cell = BoardCellView.Create(
                        transform,
                        pos,
                        Mapper.CellCenter(pos),
                        cellSize,
                        _cellSprite,
                        _clicked);
                    _cells[pos] = cell;
                }
            }

            Sync();
        }

        public void Sync()
        {
            if (_board == null)
            {
                return;
            }

            foreach (KeyValuePair<GridPos, BoardCellView> kv in _cells)
            {
                GridPos pos = kv.Key;
                BoardCellView view = kv.Value;
                if (!_board.Exists(pos))
                {
                    view.SetColor(VoidColor);
                }
                else if (_board.DishAt(pos) == null && _board.TagsAt(pos).Count > 0)
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
