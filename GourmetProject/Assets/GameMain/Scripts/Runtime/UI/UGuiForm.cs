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
        /// <summary>缓存的 Transform。</summary>
        public new Transform CachedTransform { get; private set; }

        /// <summary>
        /// 当前界面所属的 UI Group 容器。需要覆盖本组全部界面的临时 UI（例如 hover Tip）
        /// 应挂在这里，避免直接挂到最外层 Canvas 后越过更高层的 UI Group。
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
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            base.OnClose(isShutdown, userData);
        }
    }
}
