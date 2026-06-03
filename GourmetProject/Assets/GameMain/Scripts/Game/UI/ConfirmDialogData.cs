using System;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 通用二次确认弹窗的入参。通过 OpenUIForm 的 userData 传入。
    /// </summary>
    public sealed class ConfirmDialogData
    {
        public string Title = "提示";
        public string Message = string.Empty;
        public string ConfirmText = "确定";
        public string CancelText = "取消";

        /// <summary>点击确定的回调。</summary>
        public Action OnConfirm;

        /// <summary>点击取消的回调（可空）。</summary>
        public Action OnCancel;
    }
}
