using System;
using System.Collections;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace GourmetProject.Runtime.UI
{
    /// <summary>
    /// UI 界面逻辑基类（开箱即用，不含具体界面）。统一缓存常用引用与提供生命周期钩子，
    /// 玩法层的每个界面继承本类即可。基于 GameFramework UIFormLogic。
    /// </summary>
    public abstract class UGuiForm : UIFormLogic
    {
        private const float DefaultHandoffTimeout = 5f;

        /// <summary>缓存的 Transform。</summary>
        public new Transform CachedTransform { get; private set; }

        /// <summary>本次打开是否承接了同组内另一个仍在等待的界面。</summary>
        protected bool IsHandoffArrival { get; private set; }

        /// <summary>
        /// 当前界面所属的 UI Group 容器。仅需覆盖本组内容的临时 UI 可以挂在这里；
        /// 需要跨 Dialog 显示的 hover Tips 应由玩法层解析到专用 Tooltip Group。
        /// </summary>
        public Transform UIGroupTransform => transform.parent != null ? transform.parent : transform;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            CachedTransform = transform;

            // 界面统一填满所属界面组容器：用 stretch 锚点 + 清零偏移，避免预制体残留的
            // 尺寸/偏移导致界面塌缩或错位。所有界面共用此约定。
            if (CachedTransform is RectTransform rect)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;
                rect.localPosition = Vector3.zero;
            }

            UIButtonSoundFeedback.Install(CachedTransform);
        }

        protected override void OnOpen(object userData)
        {
            UIButtonSoundFeedback.Install(CachedTransform);

            // DefaultUIGroupHelper does not map GameFramework's logical form depth
            // to the uGUI hierarchy. A form restored from the object pool therefore
            // keeps its old sibling index and can render below forms created later.
            // Move it before activation so every newly opened form is immediately
            // rendered at the front of its UI group.
            CachedTransform.SetAsLastSibling();
            base.OnOpen(userData);
            IsHandoffArrival = UIFormHandoff.Complete(this);
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            UIFormHandoff.Cancel(this);
            IsHandoffArrival = false;
            base.OnClose(isShutdown, userData);
        }

        /// <summary>
        /// 保留当前界面，直到同一 UI Group 的下一个界面真正打开后再关闭。
        /// 用于连续弹窗之间维持遮罩，避免异步加载期间短暂露出底层画面。
        /// </summary>
        protected bool HoldUntilNextFormOpens(Action closeAction, float timeout = DefaultHandoffTimeout)
        {
            return UIFormHandoff.Begin(this, closeAction, timeout);
        }

        private static class UIFormHandoff
        {
            private static UGuiForm s_Outgoing;
            private static object s_UIGroup;
            private static Action s_CloseAction;
            private static Coroutine s_TimeoutRoutine;
            private static int s_Generation;

            internal static bool Begin(UGuiForm outgoing, Action closeAction, float timeout)
            {
                if (outgoing == null || outgoing.UIForm == null || closeAction == null)
                {
                    return false;
                }

                if (s_Outgoing != null && !ReferenceEquals(s_Outgoing, outgoing))
                {
                    CloseOutgoing(stopTimeout: true);
                }

                Clear(stopTimeout: true);
                s_Outgoing = outgoing;
                s_UIGroup = outgoing.UIForm.UIGroup;
                s_CloseAction = closeAction;
                int generation = ++s_Generation;
                s_TimeoutRoutine = outgoing.StartCoroutine(
                    CloseAfterTimeout(outgoing, generation, Mathf.Max(0.1f, timeout)));
                return true;
            }

            internal static bool Complete(UGuiForm incoming)
            {
                if (incoming == null
                    || s_Outgoing == null
                    || ReferenceEquals(incoming, s_Outgoing)
                    || incoming.UIForm == null
                    || !ReferenceEquals(incoming.UIForm.UIGroup, s_UIGroup))
                {
                    return false;
                }

                CloseOutgoing(stopTimeout: true);
                return true;
            }

            internal static void Cancel(UGuiForm form)
            {
                if (ReferenceEquals(form, s_Outgoing))
                {
                    Clear(stopTimeout: true);
                }
            }

            private static IEnumerator CloseAfterTimeout(UGuiForm owner, int generation, float timeout)
            {
                float deadline = Time.realtimeSinceStartup + timeout;
                while (Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                if (generation == s_Generation && ReferenceEquals(owner, s_Outgoing))
                {
                    CloseOutgoing(stopTimeout: false);
                }
            }

            private static void CloseOutgoing(bool stopTimeout)
            {
                Action closeAction = s_CloseAction;
                Clear(stopTimeout);
                closeAction?.Invoke();
            }

            private static void Clear(bool stopTimeout)
            {
                if (stopTimeout && s_TimeoutRoutine != null && s_Outgoing != null)
                {
                    s_Outgoing.StopCoroutine(s_TimeoutRoutine);
                }

                s_Outgoing = null;
                s_UIGroup = null;
                s_CloseAction = null;
                s_TimeoutRoutine = null;
            }
        }
    }
}
