using System;
using System.Collections.Generic;
using DG.Tweening;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Widgets
{
    /// <summary>
    /// UI 侧的统一菜品图标：显示矩形棋盘、居中的菜品和 DishValueBadge 美味值。
    /// 实际世界对象由共享的独立预览场景渲染，本组件只持有自己的 RenderTexture。
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public sealed class DishIconRenderTexturePreview : MonoBehaviour
    {
        private const float TransformInDuration = 0.14f;
        private const float TransformOutDuration = 0.2f;
        private const float TransformHoldDuration = 0.5f;
        private const float PrefabGridSize = 3f;
        private static readonly Color TransformFlashColor = new(1.85f, 1.85f, 1.85f, 1f);

        [SerializeField] private RawImage _targetImage;
        [SerializeField] private GameObject _cellPrefab;
        [SerializeField] private GameObject _badgePrefab;
        [SerializeField, Range(32, 256)] private int _pixelsPerCell = 96;
        [SerializeField, HideInInspector] private Vector2 _prefabThreeByThreeSize;

        private RenderTexture _renderTexture;
        private Sequence _transformSequence;
        private RectTransform _displaySizeTarget;

        public RenderTexture CurrentTexture => _renderTexture;

        public void SetRaycastTarget(bool value)
        {
            EnsureRefs();
            if (_targetImage != null)
            {
                _targetImage.raycastTarget = value;
            }
        }

        public static Vector2Int ExpandedBoardSize(DishShape shape)
        {
            return shape == null
                ? Vector2Int.zero
                : new Vector2Int(shape.Width + 2, shape.Height + 2);
        }

        public static Vector2 DisplaySizeForGrid(
            Vector2 prefabThreeByThreeSize,
            Vector2Int gridSize)
        {
            if (gridSize.x <= 0 || gridSize.y <= 0)
            {
                return prefabThreeByThreeSize;
            }

            return new Vector2(
                prefabThreeByThreeSize.x * gridSize.x / PrefabGridSize,
                prefabThreeByThreeSize.y * gridSize.y / PrefabGridSize);
        }

        public void Bind(
            DishDef dish,
            Sprite spriteOverride = null,
            int? deliciousnessOverride = null,
            IReadOnlyList<string> flavorIds = null)
        {
            EnsureRefs();
            ReleaseTexture();

            if (dish?.Shape == null || _targetImage == null)
            {
                Hide();
                return;
            }

            Sprite sprite = spriteOverride ?? ContentIconLoader.LoadDish(dish);
            if (sprite == null || _cellPrefab == null || _badgePrefab == null)
            {
                Hide();
                return;
            }

            _renderTexture = DishIconPreviewRenderer.Render(
                dish,
                sprite,
                deliciousnessOverride ?? dish.Deliciousness,
                flavorIds,
                _cellPrefab,
                _badgePrefab,
                _pixelsPerCell);

            _targetImage.texture = _renderTexture;
            _targetImage.color = Color.white;
            _targetImage.enabled = _renderTexture != null;
            gameObject.SetActive(_renderTexture != null);

            if (_renderTexture != null)
            {
                Vector2Int renderedGridSize = new(
                    Mathf.Max(1, _renderTexture.width / _pixelsPerCell),
                    Mathf.Max(1, _renderTexture.height / _pixelsPerCell));
                ApplyDisplaySize(renderedGridSize);

                AspectRatioFitter fitter = GetComponent<AspectRatioFitter>();
                if (fitter == null)
                {
                    fitter = gameObject.AddComponent<AspectRatioFitter>();
                }

                fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fitter.aspectRatio = (float)_renderTexture.width / _renderTexture.height;
            }
        }

        private void OnEnable()
        {
            EnsureRefs();
            CapturePrefabSize();
        }

        public void PlayTransformTo(
            DishDef dish,
            IReadOnlyList<string> flavorIds,
            Action onComplete)
        {
            EnsureRefs();
            KillTransformSequence();
            if (dish?.Shape == null || _targetImage == null)
            {
                Bind(dish, flavorIds: flavorIds);
                onComplete?.Invoke();
                return;
            }

            Transform target = transform;
            _targetImage.color = Color.white;
            _transformSequence = DOTween.Sequence()
                .Append(target.DOPunchScale(Vector3.one * 0.08f, 0.24f, vibrato: 6, elasticity: 0.6f))
                .InsertCallback(TransformInDuration, () =>
                {
                    Bind(dish, flavorIds: flavorIds);
                    if (_targetImage != null)
                    {
                        _targetImage.color = TransformFlashColor;
                    }
                })
                .Insert(
                    TransformInDuration,
                    DOVirtual.Float(
                            0f,
                            1f,
                            TransformOutDuration,
                            progress =>
                            {
                                if (_targetImage != null)
                                {
                                    _targetImage.color = Color.Lerp(
                                        TransformFlashColor,
                                        Color.white,
                                        progress);
                                }
                            })
                        .SetEase(Ease.InOutQuad))
                .AppendInterval(TransformHoldDuration)
                .SetUpdate(true)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    _transformSequence = null;
                    onComplete?.Invoke();
                });
        }

        public void Hide()
        {
            EnsureRefs();
            KillTransformSequence();
            ReleaseTexture();
            if (_targetImage != null)
            {
                _targetImage.texture = null;
                _targetImage.enabled = false;
            }

            RestorePrefabSize();
        }

        private void OnDestroy()
        {
            KillTransformSequence();
            ReleaseTexture();
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
            if (_targetImage == null)
            {
                _targetImage = GetComponent<RawImage>();
            }

            if (_displaySizeTarget == null)
            {
                _displaySizeTarget =
                    transform.parent as RectTransform ??
                    transform as RectTransform;
            }
        }

        private void CapturePrefabSize()
        {
            if (_prefabThreeByThreeSize.x > 0f
                && _prefabThreeByThreeSize.y > 0f)
            {
                return;
            }

            EnsureRefs();
            if (_displaySizeTarget == null)
            {
                return;
            }

            Vector2 size = _displaySizeTarget.rect.size;
            if (size.x <= 0f || size.y <= 0f)
            {
                size = new Vector2(
                    Mathf.Abs(_displaySizeTarget.sizeDelta.x),
                    Mathf.Abs(_displaySizeTarget.sizeDelta.y));
            }

            if (size.x > 0f && size.y > 0f)
            {
                _prefabThreeByThreeSize = size;
            }
        }

        private void ApplyDisplaySize(Vector2Int gridSize)
        {
            CapturePrefabSize();
            if (_displaySizeTarget == null
                || _prefabThreeByThreeSize.x <= 0f
                || _prefabThreeByThreeSize.y <= 0f)
            {
                return;
            }

            Vector2 displaySize = DisplaySizeForGrid(
                _prefabThreeByThreeSize,
                gridSize);
            _displaySizeTarget.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                displaySize.x);
            _displaySizeTarget.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                displaySize.y);
            LayoutRebuilder.MarkLayoutForRebuild(_displaySizeTarget);
        }

        private void RestorePrefabSize()
        {
            if (_displaySizeTarget == null
                || _prefabThreeByThreeSize.x <= 0f
                || _prefabThreeByThreeSize.y <= 0f)
            {
                return;
            }

            _displaySizeTarget.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                _prefabThreeByThreeSize.x);
            _displaySizeTarget.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                _prefabThreeByThreeSize.y);
            LayoutRebuilder.MarkLayoutForRebuild(_displaySizeTarget);
        }

        private void ReleaseTexture()
        {
            if (_renderTexture == null)
            {
                return;
            }

            if (_targetImage != null && _targetImage.texture == _renderTexture)
            {
                _targetImage.texture = null;
            }

            _renderTexture.Release();
            if (Application.isPlaying)
            {
                Destroy(_renderTexture);
            }
            else
            {
                DestroyImmediate(_renderTexture);
            }

            _renderTexture = null;
        }
    }
}
