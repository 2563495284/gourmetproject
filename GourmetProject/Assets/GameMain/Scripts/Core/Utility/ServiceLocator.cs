using System;
using System.Collections.Generic;

namespace GourmetProject.Core.Utility
{
    /// <summary>
    /// 极轻量服务定位器。用于在不引入完整 DI 容器的前提下，集中注册/获取与玩法无关的服务，
    /// 避免到处使用静态单例。GameEntry 在启动时注册各服务，其它地方按接口取用。
    /// </summary>
    public sealed class ServiceLocator
    {
        private static readonly ServiceLocator _instance = new ServiceLocator();
        public static ServiceLocator Instance => _instance;

        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        public void Register<T>(T service) where T : class
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _services[typeof(T)] = service;
        }

        public bool TryGet<T>(out T service) where T : class
        {
            if (_services.TryGetValue(typeof(T), out object obj))
            {
                service = (T)obj;
                return true;
            }

            service = null;
            return false;
        }

        public T Get<T>() where T : class
        {
            if (_services.TryGetValue(typeof(T), out object obj))
            {
                return (T)obj;
            }

            throw new KeyNotFoundException($"Service of type {typeof(T).FullName} is not registered.");
        }

        public bool Contains<T>() where T : class
        {
            return _services.ContainsKey(typeof(T));
        }

        public void Unregister<T>() where T : class
        {
            _services.Remove(typeof(T));
        }

        public void Clear()
        {
            _services.Clear();
        }
    }
}
