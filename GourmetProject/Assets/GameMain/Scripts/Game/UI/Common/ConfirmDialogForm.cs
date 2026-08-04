using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;
using TMPro;

namespace GourmetProject.Game.UI.Common
{
    /// <summary>
    /// 通用二次确认弹窗。由调用方通过 <see cref="ConfirmDialogData"/> 传入标题、文案与回调。
    /// </summary>
    public sealed class ConfirmDialogForm : UGuiForm
    {
        private TMP_Text _titleText;
        private TMP_Text _messageText;
        private Button _confirmButton;
        private Button _cancelButton;
        private TMP_Text _confirmLabel;
        private TMP_Text _cancelLabel;
        private RectTransform _confirmButtonRect;
        private Vector2 _confirmTwoButtonPosition;

        private ConfirmDialogData _data;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            _titleText = CachedTransform.Find("Window/Title").GetComponent<TMP_Text>();
            _messageText = CachedTransform.Find("Window/Message").GetComponent<TMP_Text>();
            _confirmButton = CachedTransform.Find("Window/ConfirmButton").GetComponent<Button>();
            _cancelButton = CachedTransform.Find("Window/CancelButton").GetComponent<Button>();
            _confirmLabel = _confirmButton.transform.Find("TMP_Text").GetComponent<TMP_Text>();
            _cancelLabel = _cancelButton.transform.Find("TMP_Text").GetComponent<TMP_Text>();
            _confirmButtonRect = (RectTransform)_confirmButton.transform;
            _confirmTwoButtonPosition = _confirmButtonRect.anchoredPosition;

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

            // 取消文案为空时作为单按钮通知弹窗，并将确认按钮居中。
            bool showCancelButton = !string.IsNullOrEmpty(_data.CancelText);
            _cancelButton.gameObject.SetActive(showCancelButton);
            _confirmButtonRect.anchoredPosition = showCancelButton
                ? _confirmTwoButtonPosition
                : new Vector2(0f, _confirmTwoButtonPosition.y);
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
