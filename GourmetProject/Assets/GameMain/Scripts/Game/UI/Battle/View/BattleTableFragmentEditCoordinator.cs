using System;
using System.Collections.Generic;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.UI.Battle.View
{
    internal interface IBattleTableFragmentEditHost
    {
        GameRun Run { get; }

        GameplayView CurrentView { get; }

        IBattleTableFragmentEditLayer FragmentEditLayer { get; }

        bool CanInteract { get; }

        bool CanOpenFragmentEdit { get; }

        IReadOnlyList<int> CandidateRotations { get; }

        void ForceCloseInspection();

        void CancelActiveItemUse();

        bool SuspendFragmentEditSource();

        void RestoreFragmentEditSource();

        void RestoreBattleWorld();

        void BeginTableFragmentChoice(TableFragmentChoiceRequest request);

        void ConfirmTableEditPlacement();

        void SkipTableEditPack();

        void HideTableEditWorld();

        void SetInspectionNavigationBlocked(bool blocked);

        bool OpenRecipeInspection(Action onClosed);

        void RefreshPersistent();

        void NotifyPreparingChild();

        void NotifyChildReady();

        void NotifyPreparingReturn();

        void NotifyParentRestored();
    }

    /// <summary>
    /// 商店与奖励碎片包共用的独立编辑生命周期。来源 GameplayView 始终不变，
    /// 所有业务回调都在来源表现恢复后执行。
    /// </summary>
    internal sealed class BattleTableFragmentEditCoordinator
    {
        private readonly IBattleTableFragmentEditHost _host;
        private GameplayView _sourceView = GameplayView.None;
        private Action<bool> _completed;
        private bool _canConfirm;
        private bool _actionInteractable;
        private bool _finishing;

        public BattleTableFragmentEditCoordinator(IBattleTableFragmentEditHost host)
        {
            _host = host;
        }

        public bool IsActive => _sourceView != GameplayView.None;

        public GameplayView SourceView => _sourceView;

        public bool Open(
            IReadOnlyList<string> candidateIds,
            Action<bool> completed,
            Action onShown = null)
        {
            GameRun run = _host.Run;
            if (IsActive
                || _finishing
                || run == null
                || !_host.CanOpenFragmentEdit
                || _host.CurrentView == GameplayView.None
                || candidateIds == null
                || candidateIds.Count == 0
                || _host.FragmentEditLayer == null
                || !_host.FragmentEditLayer.IsConfigured)
            {
                completed?.Invoke(false);
                onShown?.Invoke();
                return false;
            }

            _host.ForceCloseInspection();
            _host.CancelActiveItemUse();
            _sourceView = _host.CurrentView;
            _completed = completed;
            _canConfirm = false;
            _actionInteractable = false;
            _host.SetInspectionNavigationBlocked(true);
            _host.NotifyPreparingChild();

            if (!_host.SuspendFragmentEditSource())
            {
                ResetState();
                _host.SetInspectionNavigationBlocked(false);
                completed?.Invoke(false);
                onShown?.Invoke();
                return false;
            }

            var request = new TableFragmentChoiceRequest(
                run,
                candidateIds,
                Finish,
                ApplyActionState,
                _host.CandidateRotations);
            _host.BeginTableFragmentChoice(request);
            _host.FragmentEditLayer.Show();
            _host.NotifyChildReady();
            onShown?.Invoke();
            return true;
        }

        public void Confirm()
        {
            if (!CanUseAction() || !_canConfirm)
            {
                return;
            }

            _host.ConfirmTableEditPlacement();
        }

        public void Skip()
        {
            if (!CanUseAction() || _canConfirm)
            {
                return;
            }

            _host.SkipTableEditPack();
        }

        public void ExecuteCurrentAction()
        {
            if (_canConfirm)
            {
                Confirm();
            }
            else
            {
                Skip();
            }
        }

        /// <summary>临时收起碎片编辑层查看菜谱，关闭后继续原选择状态。</summary>
        public void OpenRecipe()
        {
            if (!IsActive
                || _finishing
                || !_host.CanInteract
                || _host.FragmentEditLayer?.IsVisible != true)
            {
                return;
            }

            _host.FragmentEditLayer.HideImmediate();
            if (!_host.OpenRecipeInspection(ResumeAfterRecipeInspection))
            {
                _host.FragmentEditLayer.Show();
            }
        }

        public void ForceClose()
        {
            if (!IsActive)
            {
                _host.FragmentEditLayer?.HideImmediate();
                return;
            }

            _finishing = true;
            _host.HideTableEditWorld();
            _host.RestoreFragmentEditSource();
            _host.SetInspectionNavigationBlocked(false);
            _host.FragmentEditLayer?.HideImmediate();
            ResetState();
        }

        private bool CanUseAction()
        {
            return IsActive
                && !_finishing
                && _host.CanInteract
                && _actionInteractable;
        }

        private void ApplyActionState(TableFragmentEditActionState state)
        {
            _canConfirm = state.CanConfirm;
            _actionInteractable = state.Interactable;
            _host.FragmentEditLayer?.ApplyActionState(state);
        }

        private void ResumeAfterRecipeInspection()
        {
            if (!IsActive || _finishing)
            {
                return;
            }

            _host.FragmentEditLayer?.Show();
            _host.RefreshPersistent();
        }

        private void Finish(bool placed)
        {
            if (!IsActive || _finishing)
            {
                return;
            }

            _finishing = true;
            _host.NotifyPreparingReturn();
            if (_sourceView == GameplayView.Food)
            {
                _host.RestoreBattleWorld();
            }
            else
            {
                _host.HideTableEditWorld();
            }

            _host.RestoreFragmentEditSource();
            _host.SetInspectionNavigationBlocked(false);
            _host.RefreshPersistent();

            Action<bool> completed = _completed;
            _completed = null;
            completed?.Invoke(placed);

            _host.FragmentEditLayer?.HideImmediate();
            ResetState();
            _host.NotifyParentRestored();
        }

        private void ResetState()
        {
            _sourceView = GameplayView.None;
            _completed = null;
            _canConfirm = false;
            _actionInteractable = false;
            _finishing = false;
        }
    }
}
