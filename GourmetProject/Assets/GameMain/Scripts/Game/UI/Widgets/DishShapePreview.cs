using System.Collections.Generic;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Widgets
{
    /// <summary>
    /// 小尺寸菜品形状预览：根据菜品占格生成背景网格，并在对应形状范围内铺菜品图。
    /// 固定结构由 prefab 提供，格子数量随菜品形状数据动态实例化。
    /// </summary>
    public sealed class DishShapePreview : MonoBehaviour
    {
        [SerializeField] private RectTransform _contentRoot;
        [SerializeField] private Image _dishImage;
        [SerializeField] private DishShapeCell _cellPrefab;
        [SerializeField] private Sprite _cellSprite;
        [SerializeField] private Color _filledCellColor = Color.white;
        [SerializeField] private Color _emptyCellColor = new Color(1f, 1f, 1f, 0.35f);
        [SerializeField] private float _cellPadding = 3f;

        private readonly List<GameObject> _spawnedCells = new();
        private readonly DishSpriteProvider _spriteProvider = new();

        public void Bind(DishDef def, Sprite spriteOverride = null)
        {
            EnsureRefs();
            ClearCells();

            if (def?.Shape == null || _contentRoot == null)
            {
                Hide();
                return;
            }

            _contentRoot.gameObject.SetActive(true);

            DishShape shape = def.Shape.RotatedBy(def.RotationIndex);
            int dim = Mathf.Max(shape.Width, shape.Height);
            float offX = (dim - shape.Width) * 0.5f;
            float offY = (dim - shape.Height) * 0.5f;

            BuildBoardGrid(shape, dim, offX, offY);
            BuildDishImage(def, spriteOverride, shape, dim, offX, offY);
        }

        public void Hide()
        {
            EnsureRefs();
            ClearCells();

            if (_contentRoot != null)
            {
                _contentRoot.gameObject.SetActive(false);
            }
        }

        private void BuildBoardGrid(DishShape shape, int dim, float offX, float offY)
        {
            if (_cellPrefab == null || _contentRoot == null)
            {
                return;
            }

            for (int y = 0; y < shape.Height; y++)
            {
                for (int x = 0; x < shape.Width; x++)
                {
                    float minX = (offX + x) / dim;
                    float maxX = (offX + x + 1f) / dim;
                    float minY = 1f - (offY + y + 1f) / dim;
                    float maxY = 1f - (offY + y) / dim;

                    DishShapeCell cell = Instantiate(_cellPrefab, _contentRoot);
                    var rect = (RectTransform)cell.transform;
                    rect.anchorMin = new Vector2(minX, minY);
                    rect.anchorMax = new Vector2(maxX, maxY);
                    rect.offsetMin = new Vector2(_cellPadding, _cellPadding);
                    rect.offsetMax = new Vector2(-_cellPadding, -_cellPadding);
                    rect.localScale = Vector3.one;
                    cell.SetSprite(CellSprite);
                    cell.SetColor(IsFilled(shape, x, y) ? _filledCellColor : _emptyCellColor);
                    _spawnedCells.Add(cell.gameObject);
                }
            }
        }

        private void BuildDishImage(DishDef def, Sprite spriteOverride, DishShape shape, int dim, float offX, float offY)
        {
            if (_dishImage == null)
            {
                return;
            }

            _dishImage.gameObject.SetActive(true);
            _dishImage.sprite = spriteOverride != null ? spriteOverride : _spriteProvider.Get(def);
            _dishImage.color = Color.white;
            _dishImage.raycastTarget = false;
            _dishImage.preserveAspect = false;

            RectTransform rect = _dishImage.rectTransform;
            rect.anchorMin = new Vector2(offX / dim, 1f - (offY + shape.Height) / dim);
            rect.anchorMax = new Vector2((offX + shape.Width) / dim, 1f - offY / dim);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.SetAsLastSibling();
        }

        private void EnsureRefs()
        {
            if (_contentRoot == null)
            {
                _contentRoot = transform as RectTransform;
            }
        }

        private Sprite CellSprite
        {
            get
            {
                if (_cellSprite == null)
                {
                    _cellSprite = Resources.Load<Sprite>("Sprites/UI/board_cell");
                }

                return _cellSprite;
            }
        }

        private static bool IsFilled(DishShape shape, int x, int y)
        {
            if (x < 0 || y < 0 || x >= shape.Width || y >= shape.Height)
            {
                return false;
            }

            var pos = new GridPos(x, y);
            for (int i = 0; i < shape.Cells.Count; i++)
            {
                if (shape.Cells[i].Equals(pos))
                {
                    return true;
                }
            }

            return false;
        }

        private void ClearCells()
        {
            foreach (GameObject cell in _spawnedCells)
            {
                if (cell != null)
                {
                    Destroy(cell);
                }
            }

            _spawnedCells.Clear();
        }
    }
}
