using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine.UI;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 通用二次确认弹窗。由调用方通过 <see cref="ConfirmDialogData"/> 传入标题、文案与回调。
    /// </summary>
    public sealed class ConfirmDialogForm : UGuiForm
    {
        private Text _titleText;
        private Text _messageText;
        private Button _confirmButton;
        private Button _cancelButton;
        private Text _confirmLabel;
        private Text _cancelLabel;

        private ConfirmDialogData _data;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            _titleText = CachedTransform.Find("Window/Title").GetComponent<Text>();
            _messageText = CachedTransform.Find("Window/Message").GetComponent<Text>();
            _confirmButton = CachedTransform.Find("Window/ConfirmButton").GetComponent<Button>();
            _cancelButton = CachedTransform.Find("Window/CancelButton").GetComponent<Button>();
            _confirmLabel = _confirmButton.transform.Find("Text").GetComponent<Text>();
            _cancelLabel = _cancelButton.transform.Find("Text").GetComponent<Text>();

            _confirmButton.onClick.AddListener(OnConfirmClicked);
            _cancelButton.onClick.AddListener(OnCancelClicked);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            _data = userData as ConfirmDialogData ?? new ConfirmDialogData();
            _titleText.text = _data.Title;
            _messageText.text = _data.Message;
            _confirmLabel.text = _data.ConfirmText;
            _cancelLabel.text = _data.CancelText;

            // 取消文案为空时作为单按钮通知弹窗（隐藏取消按钮）。
            _cancelButton.gameObject.SetActive(!string.IsNullOrEmpty(_data.CancelText));
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            _data = null;
            base.OnClose(isShutdown, userData);
        }

        private void OnConfirmClicked()
        {
            var callback = _data?.OnConfirm;
            GameApp.UI.CloseUIForm(UIForm);
            callback?.Invoke();
        }

        private void OnCancelClicked()
        {
            var callback = _data?.OnCancel;
            GameApp.UI.CloseUIForm(UIForm);
            callback?.Invoke();
        }
    }
}
