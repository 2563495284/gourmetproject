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
        public Transform CachedTransform { get; private set; }

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
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            base.OnClose(isShutdown, userData);
        }
    }
}
