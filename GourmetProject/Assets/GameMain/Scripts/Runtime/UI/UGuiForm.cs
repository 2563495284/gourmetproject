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
