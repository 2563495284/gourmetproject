using UnityEngine;

namespace GourmetProject.Runtime.Common
{
    /// <summary>
    /// 通用 MonoBehaviour 单例基类。用于少量确实需要场景驻留 + Update 的全局对象。
    /// 注意：GameFramework 的各模块已是单例式组件，优先使用 GameApp 暴露的入口，
    /// 仅在框架未覆盖的场景才用本基类。
    /// </summary>
    public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
    {
        private static T _instance;
        private static bool _applicationQuitting;

        public static T Instance
        {
            get
            {
                if (_applicationQuitting)
                {
                    return null;
                }

                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<T>();
                    if (_instance == null)
                    {
                        var go = new GameObject(typeof(T).Name);
                        _instance = go.AddComponent<T>();
                        DontDestroyOnLoad(go);
                    }
                }

                return _instance;
            }
        }

        public static bool HasInstance => _instance != null && !_applicationQuitting;

        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = (T)this;
            DontDestroyOnLoad(gameObject);
            OnSingletonAwake();
        }

        protected virtual void OnSingletonAwake()
        {
        }

        protected virtual void OnApplicationQuit()
        {
            _applicationQuitting = true;
        }

        protected virtual void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
