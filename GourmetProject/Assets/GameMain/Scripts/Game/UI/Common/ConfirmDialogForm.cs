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
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _messageText;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private TMP_Text _confirmLabel;
        [SerializeField] private TMP_Text _cancelLabel;
        [SerializeField] private RectTransform _confirmButtonRect;
        private Vector2 _confirmTwoButtonPosition;

        private ConfirmDialogData _data;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            EnsureReferences();
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

        private void EnsureReferences()
        {
            RequireReference(_titleText, nameof(_titleText));
            RequireReference(_messageText, nameof(_messageText));
            RequireReference(_confirmButton, nameof(_confirmButton));
            RequireReference(_cancelButton, nameof(_cancelButton));
            RequireReference(_confirmLabel, nameof(_confirmLabel));
            RequireReference(_cancelLabel, nameof(_cancelLabel));
            RequireReference(_confirmButtonRect, nameof(_confirmButtonRect));
        }

        private static void RequireReference(
            Object reference,
            string fieldName)
        {
            if (reference == null)
            {
                throw new MissingReferenceException(
                    $"ConfirmDialogForm requires serialized reference '{fieldName}'.");
            }
        }
    }
}
