using System;
using UnityGameFramework.Runtime;

namespace GourmetProject.Runtime.Scene
{
    /// <summary>
    /// 场景转场封装（开箱即用）。透传 GameFramework SceneComponent 的加载/卸载，
    /// 转场进度与完成可通过订阅 GameFramework 的场景事件获取。与玩法无关。
    /// </summary>
    public sealed class SceneLoader
    {
        private readonly SceneComponent _scene;

        public SceneLoader(SceneComponent scene)
        {
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        }

        public void Load(string sceneAssetName)
        {
            _scene.LoadScene(sceneAssetName);
        }

        public void Unload(string sceneAssetName)
        {
            _scene.UnloadScene(sceneAssetName);
        }

        public bool IsLoading(string sceneAssetName)
        {
            return _scene.SceneIsLoading(sceneAssetName);
        }

        public bool IsLoaded(string sceneAssetName)
        {
            return _scene.SceneIsLoaded(sceneAssetName);
        }
    }
}
