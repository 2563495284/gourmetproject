using DG.Tweening;
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
        private float? _valueOverride;
        private bool _flying;
        private int _sortingOrderOffset;

        internal DishValueBadgeView View => _badge;

        internal Vector3 WorldPosition => _badge != null
            ? _badge.transform.position
            : transform.position;

        internal void Bind(DishInstance instance)
        {
            _instance = instance;
            _valueOverride = null;
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

            float badgeTopExtent = _badge.TopExtent
                * Mathf.Abs(_badge.transform.localScale.y);
            _badge.transform.localPosition =
                DishBadgeLayout.PositionFromTopLeftCellOrigin(
                    shape,
                    cellSize,
                    pitch,
                    badgeTopExtent);
            ApplySorting();
            Refresh();
        }

        internal void Refresh()
        {
            if (_badge == null || _instance == null)
            {
                return;
            }

            float value = _valueOverride
                ?? DishValueDisplay.CurrentContribution(_instance);
            _badge.SetValue(DishValueDisplay.Format(value));
        }

        internal void SetOverride(float value)
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
            if (_badge == null)
            {
                return;
            }

            Transform badgeTransform = _badge.transform;
            badgeTransform.DOKill(false);
            badgeTransform.localScale = _baseScale;
            badgeTransform.DOPunchScale(
                    Vector3.one * scale,
                    duration,
                    1,
                    0.45f)
                .SetLink(_badge.gameObject);
        }

        private void ApplySorting()
        {
            if (_badge == null)
            {
                return;
            }

            string layer = _flying
                ? BattleSorting.PiecesFlying
                : BattleSorting.Fx;
            int order = _flying
                ? BattleSorting.OrderBody + 5 + _sortingOrderOffset
                : BattleSorting.OrderFloatingText + _sortingOrderOffset;
            _badge.ConfigureSorting(layer, order);
        }
    }
}
