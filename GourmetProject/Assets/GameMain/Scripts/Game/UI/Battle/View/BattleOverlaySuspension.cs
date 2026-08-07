using System;
using GourmetProject.Game.UI.Hud;
using UnityEngine;

namespace GourmetProject.Game.UI.Battle.View
{
    [Flags]
    internal enum BattleOverlaySuspensionOptions
    {
        None = 0,
        HideActionAxis = 1 << 0,
        HideBackdrop = 1 << 1,
    }

    internal interface IBattleOverlaySuspensionHost
    {
        CanvasGroup Center { get; }

        CanvasGroup ActionAxisGroup { get; }

        GameObject Backdrop { get; }

        GameObject FoodBattlePanel { get; }

        BattleFoodActionBar FoodActionBar { get; }

        ServingOutletView ResolveServingOutlet();

        FoodDiscardBinView ResolveFoodDiscardBin();

        void SetFoodBattlePanelVisible(bool visible);

        void HideAllTips();
    }

    /// <summary>
    /// 独立业务覆盖层共用的来源表现冻结器。它只保存表现状态，不持有页面、奖励或业务回调，
    /// 并保证一次 Suspend 只会对应一次 Restore。
    /// </summary>
    internal sealed class BattleOverlaySuspension
    {
        private readonly IBattleOverlaySuspensionHost _host;
        private CanvasGroupSnapshot _center;
        private CanvasGroupSnapshot _actionAxis;
        private BattleOverlaySuspensionOptions _options;
        private bool _foodBattlePanelActive;
        private bool _foodActionBarActive;
        private ServingOutletView _servingOutlet;
        private bool _servingOutletActive;
        private FoodDiscardBinView _foodDiscardBin;
        private bool _foodDiscardBinActive;
        private bool _backdropActive;

        public BattleOverlaySuspension(IBattleOverlaySuspensionHost host)
        {
            _host = host;
        }

        public bool IsSuspended { get; private set; }

        public bool Suspend(BattleOverlaySuspensionOptions options)
        {
            if (IsSuspended || _host == null)
            {
                return false;
            }

            _options = options;
            _center.Capture(_host.Center);
            if ((options & BattleOverlaySuspensionOptions.HideActionAxis) != 0)
            {
                _actionAxis.Capture(_host.ActionAxisGroup);
            }

            _foodBattlePanelActive = _host.FoodBattlePanel != null
                && _host.FoodBattlePanel.activeSelf;
            _foodActionBarActive = _host.FoodActionBar != null
                && _host.FoodActionBar.gameObject.activeSelf;
            _servingOutlet = _host.ResolveServingOutlet();
            _servingOutletActive = _servingOutlet != null && _servingOutlet.gameObject.activeSelf;
            _foodDiscardBin = _host.ResolveFoodDiscardBin();
            _foodDiscardBinActive = _foodDiscardBin != null && _foodDiscardBin.gameObject.activeSelf;
            _backdropActive = _host.Backdrop != null && _host.Backdrop.activeSelf;
            IsSuspended = true;

            _center.Hide();
            _actionAxis.Hide();
            _host.SetFoodBattlePanelVisible(false);
            if ((options & BattleOverlaySuspensionOptions.HideBackdrop) != 0 && _host.Backdrop != null)
            {
                _host.Backdrop.SetActive(false);
            }

            _host.HideAllTips();
            return true;
        }

        public bool Restore()
        {
            if (!IsSuspended)
            {
                return false;
            }

            IsSuspended = false;
            _center.Restore();
            _actionAxis.Restore();
            if (_host.FoodBattlePanel != null)
            {
                _host.FoodBattlePanel.SetActive(_foodBattlePanelActive);
            }

            _host.FoodActionBar?.SetVisible(_foodActionBarActive);
            _servingOutlet?.SetVisible(_servingOutletActive);
            _foodDiscardBin?.SetVisible(_foodDiscardBinActive);
            _servingOutlet = null;
            _foodDiscardBin = null;

            if ((_options & BattleOverlaySuspensionOptions.HideBackdrop) != 0 && _host.Backdrop != null)
            {
                _host.Backdrop.SetActive(_backdropActive);
            }

            _options = BattleOverlaySuspensionOptions.None;
            return true;
        }

        internal struct CanvasGroupSnapshot
        {
            private CanvasGroup _group;
            private float _alpha;
            private bool _interactable;
            private bool _blocksRaycasts;

            public void Capture(CanvasGroup group)
            {
                _group = group;
                if (_group == null)
                {
                    return;
                }

                _alpha = _group.alpha;
                _interactable = _group.interactable;
                _blocksRaycasts = _group.blocksRaycasts;
            }

            public void Hide()
            {
                if (_group == null)
                {
                    return;
                }

                _group.alpha = 0f;
                _group.interactable = false;
                _group.blocksRaycasts = false;
            }

            public void Restore()
            {
                if (_group == null)
                {
                    return;
                }

                _group.alpha = _alpha;
                _group.interactable = _interactable;
                _group.blocksRaycasts = _blocksRaycasts;
                _group = null;
            }
        }
    }
}
