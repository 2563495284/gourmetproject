using GourmetProject.Runtime.UI;
using UnityEngine;

namespace GourmetProject.Game.UI.Common
{
    /// <summary>Tooltip UI Group 中常驻、无输入阻塞的 Toast 门面。</summary>
    public sealed class ToastForm : UGuiForm
    {
        [SerializeField] private ToastPresenter _presenter;

        internal ToastPresenter Presenter => _presenter;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            if (_presenter == null)
            {
                throw new MissingReferenceException(
                    "ToastForm requires serialized reference '_presenter'.");
            }

            _presenter.ResetVisual();
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            _presenter.Cancel(false);
            ToastService.Attach(this);
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            _presenter.Cancel(false);
            ToastService.Detach(this);
            base.OnClose(isShutdown, userData);
        }

        internal void NotifyQueueChanged()
        {
            if (!isActiveAndEnabled || _presenter.IsPlaying)
            {
                return;
            }

            CachedTransform.SetAsLastSibling();
            PlayNext();
        }

        private void PlayNext()
        {
            if (!ToastService.TryTake(this, out ToastRequest request))
            {
                _presenter.ResetVisual();
                return;
            }

            CachedTransform.SetAsLastSibling();
            _presenter.Play(request, OnPresentationComplete);
        }

        private void OnPresentationComplete()
        {
            ToastService.CompleteActive(this);
            PlayNext();
        }
    }
}
