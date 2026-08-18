using System;
using DG.Tweening;
using GourmetProject.Game.UI.Meta;
using UnityEngine;

namespace GourmetProject.Game.UI.Battle.View
{
    internal interface IBattleInspectionLayer
    {
        bool IsVisible { get; }

        bool IsTransitioning { get; }

        RecipeReadonlyBookView RecipeView { get; }

        ViewTablePanel TablePanel { get; }

        void Initialize();

        void TransitionTo(BattleInspectionView view, Action atSwap = null, Action onShown = null);

        void Hide(Action onHidden = null);

        void ForceHide();
    }

    /// <summary>
    /// BattleForm 中部上方的独立只读查看层。它只负责自身子视图、输入和动画，
    /// 不参与 GameplayPageRouter，也不拥有来源页面状态。
    /// </summary>
    public sealed class BattleInspectionLayer : MonoBehaviour, IBattleInspectionLayer
    {
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private RecipeReadonlyBookView _recipeView;
        [SerializeField] private ViewTablePanel _tablePanel;
        [SerializeField, Min(0f)] private float _fadeOutSeconds = 0.1f;
        [SerializeField, Min(0f)] private float _fadeInSeconds = 0.14f;

        private Tween _transition;
        private readonly PendingPresentationCallbacks _pending = new();
        private bool _forceHiding;

        internal bool IsConfigured => _group != null && _recipeView != null && _tablePanel != null;

        public bool IsVisible => gameObject.activeSelf && _group != null && _group.alpha > 0.999f;

        public bool IsTransitioning => _transition != null && _transition.IsActive();

        public RecipeReadonlyBookView RecipeView => _recipeView;

        public ViewTablePanel TablePanel => _tablePanel;

        public void Initialize()
        {
            _group = _group != null ? _group : GetComponent<CanvasGroup>();
            if (!IsConfigured)
            {
                Debug.LogError($"{nameof(BattleInspectionLayer)} prefab 引用不完整。", this);
            }

            ForceHide();
        }

        void IBattleInspectionLayer.TransitionTo(
            BattleInspectionView view,
            Action atSwap,
            Action onShown)
        {
            TransitionTo(view, atSwap, onShown);
        }

        internal void TransitionTo(BattleInspectionView view, Action atSwap = null, Action onShown = null)
        {
            if (view == BattleInspectionView.None || _group == null)
            {
                onShown?.Invoke();
                return;
            }

            KillTransition();
            _pending.SetShown(onShown);
            bool wasVisible = gameObject.activeSelf;
            gameObject.SetActive(true);
            _group.interactable = false;
            _group.blocksRaycasts = false;

            if (!wasVisible)
            {
                _group.alpha = 0f;
                ApplyView(view);
                atSwap?.Invoke();
                _transition = DOTween.To(
                        () => _group.alpha,
                        value => _group.alpha = value,
                        1f,
                        _fadeInSeconds)
                    .SetEase(Ease.OutSine)
                    .SetUpdate(true)
                    .SetTarget(this)
                    .OnComplete(CompleteShownTransition);
                return;
            }

            _transition = DOTween.Sequence()
                .SetUpdate(true)
                .SetTarget(this)
                .Append(DOTween.To(
                    () => _group.alpha,
                    value => _group.alpha = value,
                    0f,
                    _fadeOutSeconds).SetEase(Ease.InSine))
                .AppendCallback(() =>
                {
                    ApplyView(view);
                    atSwap?.Invoke();
                })
                .Append(DOTween.To(
                    () => _group.alpha,
                    value => _group.alpha = value,
                    1f,
                    _fadeInSeconds).SetEase(Ease.OutSine))
                .OnComplete(CompleteShownTransition);
        }

        public void Hide(Action onHidden = null)
        {
            KillTransition();
            _pending.CompleteShown();
            _pending.SetHidden(onHidden);
            if (!gameObject.activeSelf || _group == null)
            {
                ForceHide();
                return;
            }

            _group.interactable = false;
            _group.blocksRaycasts = false;
            _transition = DOTween.To(
                    () => _group.alpha,
                    value => _group.alpha = value,
                    0f,
                    _fadeOutSeconds)
                .SetEase(Ease.InSine)
                .SetUpdate(true)
                .SetTarget(this)
                .OnComplete(() => ForceHide());
        }

        public void ForceHide()
        {
            if (_forceHiding)
            {
                return;
            }

            _forceHiding = true;
            try
            {
                KillTransition();
                _pending.CompleteAll();
                SetActive(_recipeView, false);
                SetActive(_tablePanel, false);
                if (_group != null)
                {
                    _group.alpha = 0f;
                    _group.interactable = false;
                    _group.blocksRaycasts = false;
                }

                gameObject.SetActive(false);
            }
            finally
            {
                _forceHiding = false;
            }
        }

        private void ApplyView(BattleInspectionView view)
        {
            SetActive(_recipeView, view == BattleInspectionView.Recipe);
            SetActive(_tablePanel, view == BattleInspectionView.Table);
        }

        private void CompleteShownTransition()
        {
            _transition = null;
            if (_group != null)
            {
                _group.alpha = 1f;
                _group.interactable = true;
                _group.blocksRaycasts = true;
            }

            _pending.CompleteShown();
        }

        private void KillTransition()
        {
            if (_transition != null && _transition.IsActive())
            {
                _transition.Kill(complete: false);
            }

            _transition = null;
        }

        private static void SetActive(Component component, bool active)
        {
            if (component != null)
            {
                component.gameObject.SetActive(active);
            }
        }
    }
}
