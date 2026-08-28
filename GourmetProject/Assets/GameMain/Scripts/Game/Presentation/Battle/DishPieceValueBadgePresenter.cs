using DG.Tweening;
using BreakInfinity;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>负责世界食物 Badge 的实例、布局、数值覆盖、排序与反馈动画。</summary>
    public sealed class DishPieceValueBadgePresenter : MonoBehaviour
    {
        [SerializeField] private DishValueBadgeView _badgePrefab;

        private DishValueBadgeView _badge;
        private DishInstance _instance;
        private Vector3 _baseScale = Vector3.one;
        private float _visualScale = 1f;
        private BigDouble? _valueOverride;
        private bool _flying;
        private int _sortingOrderOffset;
        private bool _visible = true;
        private bool _chapterFocused;
        private Tween _valueFadeTween;

        internal DishValueBadgeView View => _badge;

        internal Vector3 WorldPosition => _badge != null
            ? _badge.transform.position
            : transform.position;

        internal void Bind(DishInstance instance)
        {
            _instance = instance;
            _valueOverride = null;
            _chapterFocused = false;
            _badge?.SetChapterFocused(false);
        }

        internal void ResetForReuse(bool clearBinding = true)
        {
            KillValueFade();
            _valueOverride = null;
            if (clearBinding)
            {
                _instance = null;
            }
            _flying = false;
            _sortingOrderOffset = 0;
            _visible = true;
            _chapterFocused = false;
            if (_badge == null)
            {
                return;
            }

            _badge.transform.DOKill(false);
            _badge.SetChapterFocused(false);
            _badge.SetDimmed(false);
            _badge.SetAlpha(1f);
            _badge.gameObject.SetActive(true);
        }

        internal void UpdateLayout(DishShape shape, float cellSize, float pitch)
        {
            if (shape == null || _badgePrefab == null)
            {
                return;
            }

            if (_badge == null)
            {
                _badge = Instantiate(_badgePrefab, transform);
                _badge.name = "DishValueBadge";
                _badge.transform.localRotation = Quaternion.identity;
                _baseScale = _badge.transform.localScale;
            }

            _visualScale = DiningTableLayout.VisualScaleForCellSize(cellSize);
            _badge.transform.DOKill(false);
            _badge.transform.localScale = PresentedBaseScale;
            _badge.SetChapterFocused(_chapterFocused);

            float badgeTopExtent = _badge.TopExtent
                * Mathf.Abs(_badge.transform.localScale.y);
            _badge.transform.localPosition =
                DishBadgeLayout.PositionFromTopLeftCellOrigin(
                    shape,
                    cellSize,
                    pitch,
                    badgeTopExtent);
            ApplySorting();
            ApplyVisible();
            Refresh();
        }

        internal void SetVisible(bool visible)
        {
            KillValueFade();
            _visible = visible;
            ApplyVisible();
            if (visible)
            {
                _badge?.SetAlpha(1f);
            }
        }

        internal void Fade(bool visible, float duration, System.Action onComplete = null)
        {
            KillValueFade();
            if (_badge == null || !_visible)
            {
                onComplete?.Invoke();
                return;
            }

            float targetAlpha = visible ? 1f : 0f;
            if (duration <= 0.0001f
                || Mathf.Approximately(_badge.CurrentAlpha, targetAlpha))
            {
                _badge.SetAlpha(targetAlpha);
                onComplete?.Invoke();
                return;
            }

            _valueFadeTween = DOTween.To(
                    () => _badge != null ? _badge.CurrentAlpha : targetAlpha,
                    alpha => _badge?.SetAlpha(alpha),
                    targetAlpha,
                    duration)
                .SetEase(visible ? Ease.OutQuad : Ease.InQuad)
                .SetUpdate(true)
                .SetLink(_badge.gameObject)
                .OnComplete(() =>
                {
                    _valueFadeTween = null;
                    _badge?.SetAlpha(targetAlpha);
                    onComplete?.Invoke();
                });
        }

        internal void SetDimmed(bool dimmed)
        {
            _badge?.SetDimmed(dimmed);
        }

        internal void Refresh()
        {
            if (_badge == null || _instance == null)
            {
                return;
            }

            BigDouble value = _valueOverride
                ?? DishValueDisplay.CurrentContribution(_instance);
            _badge.SetValue(DishValueDisplay.Format(value));
        }

        internal void SetOverride(BigDouble value)
        {
            _valueOverride = value;
            Refresh();
        }

        internal void ClearOverride()
        {
            _valueOverride = null;
            Refresh();
        }

        internal void SetSorting(bool flying, int sortingOrderOffset)
        {
            _flying = flying;
            _sortingOrderOffset = sortingOrderOffset;
            ApplySorting();
        }

        internal void Punch(float scale, float duration)
        {
            if (_badge == null || !_visible)
            {
                return;
            }

            Transform badgeTransform = _badge.transform;
            badgeTransform.DOKill(false);
            badgeTransform.localScale = PresentedBaseScale;
            badgeTransform.DOPunchScale(
                    Vector3.one * (scale * _visualScale),
                    duration,
                    1,
                    0.45f)
                .SetLink(_badge.gameObject);
        }

        internal void SetChapterFocused(bool focused, float duration)
        {
            _chapterFocused = focused;
            if (_badge == null)
            {
                return;
            }

            _badge.SetChapterFocused(focused);
            Transform badgeTransform = _badge.transform;
            badgeTransform.DOKill(false);
            if (duration <= 0.0001f)
            {
                badgeTransform.localScale = PresentedBaseScale;
                return;
            }

            badgeTransform.DOScale(PresentedBaseScale, duration)
                .SetEase(focused ? Ease.OutBack : Ease.OutCubic)
                .SetLink(_badge.gameObject);
        }

        private Vector3 ScaledBaseScale => _baseScale * _visualScale;

        private Vector3 PresentedBaseScale => ScaledBaseScale * (_chapterFocused ? 1.12f : 1f);

        private void ApplyVisible()
        {
            if (_badge != null && _badge.gameObject.activeSelf != _visible)
            {
                _badge.gameObject.SetActive(_visible);
            }
        }

        private void ApplySorting()
        {
            if (_badge == null)
            {
                return;
            }

            string layer = _flying
                ? BattleSorting.PiecesFlying
                : BattleSorting.WorldUi;
            int order = _flying
                ? BattleSorting.OrderBody + 5 + _sortingOrderOffset
                : BattleSorting.OrderDishBadge + _sortingOrderOffset;
            _badge.ConfigureSorting(layer, order);
        }

        private void KillValueFade()
        {
            if (_valueFadeTween == null)
            {
                return;
            }

            _valueFadeTween.Kill();
            _valueFadeTween = null;
        }

        private void OnDisable()
        {
            KillValueFade();
            _badge?.SetAlpha(1f);
        }
    }
}
