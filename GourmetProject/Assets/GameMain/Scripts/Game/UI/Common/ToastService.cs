using GourmetProject.Runtime;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace GourmetProject.Game.UI.Common
{
    /// <summary>全局上浮淡出 Toast。调用方只需传入已经本地化的最终文案。</summary>
    public static class ToastService
    {
        private static ToastQueue s_queue = new ToastQueue();
        private static ToastForm s_form;

        /// <summary>显示一条非阻塞轻提示。空文案以及活动/队列中的重复项会被忽略。</summary>
        public static void Show(string message, ToastKind kind = ToastKind.Info)
        {
            if (!s_queue.TryEnqueue(message, kind))
            {
                return;
            }

            EnsureForm();
            s_form?.NotifyQueueChanged();
        }

        internal static void Prewarm()
        {
            EnsureForm();
        }

        internal static void Attach(ToastForm form)
        {
            if (form == null)
            {
                return;
            }

            s_form = form;
            form.NotifyQueueChanged();
        }

        internal static void Detach(ToastForm form)
        {
            if (s_form != form)
            {
                return;
            }

            s_form = null;
            s_queue.Clear();
        }

        internal static bool TryTake(ToastForm form, out ToastRequest request)
        {
            if (s_form != form)
            {
                request = default;
                return false;
            }

            return s_queue.TryDequeue(out request);
        }

        internal static void CompleteActive(ToastForm form)
        {
            if (s_form == form)
            {
                s_queue.CompleteActive();
            }
        }

        internal static void ResetForTests()
        {
            s_form = null;
            s_queue = new ToastQueue();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ResetForTests();
        }

        private static void EnsureForm()
        {
            UIComponent ui = GameApp.UI;
            if (ui == null || !ui.HasUIGroup(UIForms.GroupTooltip))
            {
                return;
            }

            if (s_form != null)
            {
                return;
            }

            if (ui.HasUIForm(UIForms.Toast))
            {
                UIForm loaded = ui.GetUIForm(UIForms.Toast);
                if (loaded?.Logic is ToastForm form)
                {
                    Attach(form);
                }

                return;
            }

            if (!ui.IsLoadingUIForm(UIForms.Toast))
            {
                ui.OpenUIForm(UIForms.Toast, UIForms.GroupTooltip);
            }
        }
    }
}
