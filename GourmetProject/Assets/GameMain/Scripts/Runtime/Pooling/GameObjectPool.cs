using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Runtime.Pooling
{
    /// <summary>
    /// 自包含的预制体对象池（与玩法无关）。GameFramework 的 ObjectPool 面向纯 C# 对象，
    /// 这里补一个面向 GameObject 的轻量池，用于频繁生成/回收的特效、子弹、卡牌视图等。
    /// </summary>
    public sealed class GameObjectPool
    {
        private readonly GameObject _prefab;
        private readonly Transform _root;
        private readonly Stack<GameObject> _inactive = new Stack<GameObject>();

        public GameObjectPool(GameObject prefab, Transform root = null, int prewarm = 0)
        {
            _prefab = prefab != null ? prefab : throw new System.ArgumentNullException(nameof(prefab));
            _root = root;

            for (int i = 0; i < prewarm; i++)
            {
                GameObject go = CreateNew();
                go.SetActive(false);
                _inactive.Push(go);
            }
        }

        public int CountInactive => _inactive.Count;

        public GameObject Get(Transform parent = null)
        {
            GameObject go = _inactive.Count > 0 ? _inactive.Pop() : CreateNew();
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            go.SetActive(true);
            return go;
        }

        public void Release(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            go.SetActive(false);
            if (_root != null)
            {
                go.transform.SetParent(_root, false);
            }

            _inactive.Push(go);
        }

        public void Clear(bool destroy = true)
        {
            if (destroy)
            {
                while (_inactive.Count > 0)
                {
                    GameObject go = _inactive.Pop();
                    if (go != null)
                    {
                        Object.Destroy(go);
                    }
                }
            }
            else
            {
                _inactive.Clear();
            }
        }

        private GameObject CreateNew()
        {
            return _root != null ? Object.Instantiate(_prefab, _root) : Object.Instantiate(_prefab);
        }
    }
}
