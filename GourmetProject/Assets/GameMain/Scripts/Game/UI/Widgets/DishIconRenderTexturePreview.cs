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
        private static readonly Color TransformFlashColor = new(1.85f, 1.85f, 1.85f, 1f);

        [SerializeField] private RawImage _targetImage;
        [SerializeField] private GameObject _cellPrefab;
        [SerializeField] private GameObject _badgePrefab;
        [SerializeField, Range(32, 256)] private int _pixelsPerCell = 96;

        private RenderTexture _renderTexture;
        private Sequence _transformSequence;

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
                AspectRatioFitter fitter = GetComponent<AspectRatioFitter>();
                if (fitter == null)
                {
                    fitter = gameObject.AddComponent<AspectRatioFitter>();
                }

                fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fitter.aspectRatio = (float)_renderTexture.width / _renderTexture.height;
            }
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
