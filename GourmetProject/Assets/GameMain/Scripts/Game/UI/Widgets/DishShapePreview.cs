using System.Collections.Generic;
using GourmetProject.Core.Utility;
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
        [Header("风味脏印（程序化噪声，仅作用于菜品图）")]
        [SerializeField] private float _stainScale = 8f;
        [SerializeField, Range(0f, 1f)] private float _stainThreshold = 0.62f;
        [SerializeField, Range(0.001f, 0.5f)] private float _stainSoftness = 0.12f;
        [SerializeField, Range(0f, 1f)] private float _stainDarken = 0.12f;

        private readonly List<GameObject> _spawnedCells = new();
        private readonly List<string> _flavorScratch = new();
        private readonly DishSpriteProvider _spriteProvider = new();
        private Material _stainMaterial;

        public void Bind(DishDef def, Sprite spriteOverride = null, IReadOnlyList<string> flavorIds = null)
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
            BuildDishImage(def, spriteOverride, flavorIds, shape, dim, offX, offY);
        }

        public void Hide()
        {
            EnsureRefs();
            ClearCells();

            if (_contentRoot != null)
            {
                _contentRoot.gameObject.SetActive(false);
            }

            if (_dishImage != null)
            {
                _dishImage.material = null;
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

        private void BuildDishImage(
            DishDef def,
            Sprite spriteOverride,
            IReadOnlyList<string> flavorIds,
            DishShape shape,
            int dim,
            float offX,
            float offY)
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
            ApplyFlavorStain(def, flavorIds);

            RectTransform rect = _dishImage.rectTransform;
            rect.anchorMin = new Vector2(offX / dim, 1f - (offY + shape.Height) / dim);
            rect.anchorMax = new Vector2((offX + shape.Width) / dim, 1f - offY / dim);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.SetAsLastSibling();
        }

        private void ApplyFlavorStain(DishDef def, IReadOnlyList<string> flavorIds)
        {
            _flavorScratch.Clear();
            if (flavorIds != null)
            {
                _flavorScratch.AddRange(flavorIds);
            }
            else if (def != null && !string.IsNullOrEmpty(def.FlavorId))
            {
                _flavorScratch.Add(def.FlavorId);
            }

            var settings = new FlavorStainPalette.Settings(
                _stainScale,
                _stainThreshold,
                _stainSoftness,
                _stainDarken,
                def != null ? (float)(StableHash.Fnv1a64(def.Id) & 0xFFFFFF) : 0f);
            FlavorStainPalette.ApplyToGraphic(_dishImage, _flavorScratch, ref _stainMaterial, settings);
        }

        private void OnDestroy()
        {
            FlavorStainPalette.ReleaseMaterial(ref _stainMaterial);
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
