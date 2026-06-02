using System;
using GameFramework.Resource;
using UnityGameFramework.Runtime;
using Object = UnityEngine.Object;

namespace GourmetProject.Runtime.Resource
{
    /// <summary>
    /// 资源加载封装（开箱即用）。把 GameFramework 基于回调集合的异步加载，包装成
    /// 简洁的 onSuccess / onFailure 委托形式。与玩法无关。
    /// </summary>
    public sealed class ResourceLoader
    {
        private readonly ResourceComponent _resource;

        public ResourceLoader(ResourceComponent resource)
        {
            _resource = resource ?? throw new ArgumentNullException(nameof(resource));
        }

        public void LoadAsset<T>(string assetName, Action<T> onSuccess, Action<string> onFailure = null)
            where T : Object
        {
            var callbacks = new LoadAssetCallbacks(
                (an, asset, duration, userData) => onSuccess?.Invoke(asset as T),
                (an, status, errorMessage, userData) => onFailure?.Invoke(errorMessage));

            _resource.LoadAsset(assetName, typeof(T), callbacks);
        }
    }
}
