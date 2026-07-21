using System;
using System.Collections.Generic;
using DG.Tweening;
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

        private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        private static readonly int BoingId = Shader.PropertyToID("_Boing");
        private static readonly int EdgeClampPointId = Shader.PropertyToID("_EdgeClampPoint");
        private const float TransformInDuration = 0.14f;
        private const float TransformOutDuration = 0.2f;
        private const float TransformHoldDuration = 0.5f;
        private static readonly Color TransformFlashColor = new(1.85f, 1.85f, 1.85f, 1f);

        private readonly List<GameObject> _spawnedCells = new();
        private readonly List<string> _flavorScratch = new();
        private readonly DishSpriteProvider _spriteProvider = new();
        private Material _stainMaterial;
        private Material _transformMaterial;
        private Sequence _transformSequence;

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
            ComposeFlavorScratch(def, flavorIds);

            int rotationIndex = DisplayRotationIndex(def);
            DishShape shape = def.Shape.RotatedBy(rotationIndex);
            int dim = Mathf.Max(shape.Width, shape.Height);
            float offX = (dim - shape.Width) * 0.5f;
            float offY = (dim - shape.Height) * 0.5f;

            BuildBoardGrid(shape, dim, offX, offY);
            BuildDishImage(def, spriteOverride, rotationIndex, shape, dim, offX, offY);
        }

        public void Hide()
        {
            EnsureRefs();
            KillTransformSequence();
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

        public void PlayTransformTo(DishDef def, IReadOnlyList<string> flavorIds, Action onComplete)
        {
            EnsureRefs();
            KillTransformSequence();
            if (_dishImage == null)
            {
                Bind(def, flavorIds: flavorIds);
                onComplete?.Invoke();
                return;
            }

            if (SpriteRenderStyle.SpriteTransformMaterial == null)
            {
                _transformSequence = DOTween.Sequence()
                    .Append(_dishImage.rectTransform.DOPunchScale(Vector3.one * 0.08f, 0.24f, vibrato: 6, elasticity: 0.6f))
                    .InsertCallback(0.12f, () => Bind(def, flavorIds: flavorIds))
                    .AppendInterval(TransformHoldDuration)
                    .SetUpdate(true)
                    .SetLink(gameObject)
                    .OnComplete(() =>
                    {
                        _transformSequence = null;
                        onComplete?.Invoke();
                    });
                return;
            }

            EnsureTransformMaterial();
            _dishImage.material = _transformMaterial;
            ApplyTransformEffect(0f);
            _transformSequence = DOTween.Sequence()
                .Append(DOTween.To(() => 0f, ApplyTransformEffect, 1f, TransformInDuration).SetEase(Ease.OutQuad))
                .AppendCallback(() =>
                {
                    Bind(def, flavorIds: flavorIds);
                    _dishImage.color = TransformFlashColor;
                })
                .Append(DOTween.To(() => _dishImage.color, value => _dishImage.color = value, Color.white, TransformOutDuration).SetEase(Ease.InOutQuad))
                .AppendInterval(TransformHoldDuration)
                .SetUpdate(true)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    _transformSequence = null;
                    onComplete?.Invoke();
                });
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
            int rotationIndex,
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
            ApplyFlavorStain(def);

            RectTransform rect = _dishImage.rectTransform;
            ApplyDishImageRect(rect, rotationIndex, shape, dim, offX, offY);
            rect.localRotation = Quaternion.Euler(0f, 0f, -90f * rotationIndex);
            rect.anchoredPosition = Vector2.zero;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.SetAsLastSibling();
        }

        private void ComposeFlavorScratch(DishDef def, IReadOnlyList<string> flavorIds)
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
        }

        private int DisplayRotationIndex(DishDef def)
        {
            return FlavorStainPalette.DisplayRotationIndex(def != null ? def.RotationIndex : 0, _flavorScratch);
        }

        private void ApplyDishImageRect(RectTransform rect, int rotationIndex, DishShape shape, int dim, float offX, float offY)
        {
            int rot = ((rotationIndex % 4) + 4) % 4;
            bool swapped = (rot % 2) == 1;
            float imageW = swapped ? shape.Height : shape.Width;
            float imageH = swapped ? shape.Width : shape.Height;
            float centerX = (offX + shape.Width * 0.5f) / dim;
            float centerY = 1f - (offY + shape.Height * 0.5f) / dim;
            float halfW = imageW * 0.5f / dim;
            float halfH = imageH * 0.5f / dim;

            rect.anchorMin = new Vector2(centerX - halfW, centerY - halfH);
            rect.anchorMax = new Vector2(centerX + halfW, centerY + halfH);
        }

        private void ApplyFlavorStain(DishDef def)
        {
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
            KillTransformSequence();
            FlavorStainPalette.ReleaseMaterial(ref _stainMaterial);
            FlavorStainPalette.ReleaseMaterial(ref _transformMaterial);
        }

        private void EnsureTransformMaterial()
        {
            if (_transformMaterial != null || SpriteRenderStyle.SpriteTransformMaterial == null)
            {
                return;
            }

            _transformMaterial = new Material(SpriteRenderStyle.SpriteTransformMaterial)
            {
                name = "RuntimeUISpriteTransform",
            };
        }

        private void ApplyTransformEffect(float amount)
        {
            if (_transformMaterial == null)
            {
                return;
            }

            float t = Mathf.Clamp01(amount);
            _transformMaterial.SetFloat(BrightnessId, t);
            _transformMaterial.SetVector(BoingId, new Vector4(0.2f * t, -0.14f * t, 0f, 0f));
            _transformMaterial.SetVector(EdgeClampPointId, new Vector4(0.24f, 0.24f, 0f, 0f));
        }

        private void KillTransformSequence()
        {
            if (_transformSequence == null)
            {
                return;
            }

            _transformSequence.Kill();
            _transformSequence = null;
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
