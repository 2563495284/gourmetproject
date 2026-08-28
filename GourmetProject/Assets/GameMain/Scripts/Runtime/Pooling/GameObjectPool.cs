using System;
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
        private readonly Stack<InactiveEntry> _inactive = new Stack<InactiveEntry>();
        private readonly HashSet<int> _inactiveIds = new HashSet<int>();
        private readonly HashSet<int> _ownedIds = new HashSet<int>();
        private readonly Action<GameObject> _onGet;
        private readonly Action<GameObject> _onRelease;
        private readonly Action<GameObject> _onDestroy;
        private readonly Vector3 _prefabLocalPosition;
        private readonly Quaternion _prefabLocalRotation;
        private readonly Vector3 _prefabLocalScale;
        private readonly int _maxInactive;

        private readonly struct InactiveEntry
        {
            public InactiveEntry(GameObject instance)
            {
                Instance = instance;
                InstanceId = instance.GetInstanceID();
            }

            public GameObject Instance { get; }
            public int InstanceId { get; }
        }

        public GameObjectPool(
            GameObject prefab,
            Transform root = null,
            int prewarm = 0,
            int maxInactive = int.MaxValue,
            Action<GameObject> onGet = null,
            Action<GameObject> onRelease = null,
            Action<GameObject> onDestroy = null)
        {
            _prefab = prefab != null ? prefab : throw new ArgumentNullException(nameof(prefab));
            _root = root;
            _maxInactive = Mathf.Max(0, maxInactive);
            _onGet = onGet;
            _onRelease = onRelease;
            _onDestroy = onDestroy;
            Transform prefabTransform = prefab.transform;
            _prefabLocalPosition = prefabTransform.localPosition;
            _prefabLocalRotation = prefabTransform.localRotation;
            _prefabLocalScale = prefabTransform.localScale;

            Prewarm(prewarm);
        }

        public int CountAll => _ownedIds.Count;
        public int CountInactive => _inactiveIds.Count;
        public int CountActive => Mathf.Max(0, CountAll - CountInactive);
        public int PeakActive { get; private set; }

        public GameObject Get(Transform parent = null)
        {
            GameObject go = null;
            while (_inactive.Count > 0 && go == null)
            {
                InactiveEntry entry = _inactive.Pop();
                _inactiveIds.Remove(entry.InstanceId);
                go = entry.Instance;
                if (go == null)
                {
                    _ownedIds.Remove(entry.InstanceId);
                }
            }

            // Unity 已销毁对象仍保留托管壳，null-coalescing 不会把它当成 null；
            // 必须使用 UnityEngine.Object 的重载判断，避免随后访问 transform 抛异常。
            if (go == null)
            {
                go = CreateNew();
            }
            Transform targetParent = parent != null ? parent : _root;
            go.transform.SetParent(targetParent, false);

            ResetTransform(go.transform);
            _onGet?.Invoke(go);
            go.SetActive(true);
            PeakActive = Mathf.Max(PeakActive, CountActive);
            return go;
        }

        public T Get<T>(Transform parent = null) where T : Component
        {
            GameObject go = Get(parent);
            T component = go.GetComponent<T>();
            if (component != null)
            {
                return component;
            }

            Release(go);
            throw new InvalidOperationException(
                $"Pool prefab '{_prefab.name}' does not contain component {typeof(T).Name}.");
        }

        public void Release(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            int instanceId = go.GetInstanceID();
            if (!_ownedIds.Contains(instanceId))
            {
                ReportInvalidRelease($"GameObject '{go.name}' does not belong to pool '{_prefab.name}'.");
                return;
            }

            if (_inactiveIds.Contains(instanceId))
            {
                ReportInvalidRelease($"GameObject '{go.name}' was released to pool '{_prefab.name}' more than once.");
                return;
            }

            _onRelease?.Invoke(go);
            go.SetActive(false);
            go.transform.SetParent(_root, false);

            ResetTransform(go.transform);
            if (_inactiveIds.Count >= _maxInactive)
            {
                _ownedIds.Remove(instanceId);
                _onDestroy?.Invoke(go);
                DestroyObject(go);
                return;
            }

            _inactive.Push(new InactiveEntry(go));
            _inactiveIds.Add(instanceId);
        }

        public void Release(Component component)
        {
            if (component != null)
            {
                Release(component.gameObject);
            }
        }

        public void Prewarm(int count)
        {
            int targetCount = Mathf.Clamp(count, 0, _maxInactive);
            while (_inactiveIds.Count < targetCount)
            {
                GameObject go = CreateNew();
                _onRelease?.Invoke(go);
                go.SetActive(false);
                go.transform.SetParent(_root, false);

                ResetTransform(go.transform);
                _inactive.Push(new InactiveEntry(go));
                _inactiveIds.Add(go.GetInstanceID());
            }
        }

        public void Clear(bool destroy = true)
        {
            if (destroy)
            {
                while (_inactive.Count > 0)
                {
                    InactiveEntry entry = _inactive.Pop();
                    GameObject go = entry.Instance;
                    _inactiveIds.Remove(entry.InstanceId);
                    _ownedIds.Remove(entry.InstanceId);
                    if (go != null)
                    {
                        _onDestroy?.Invoke(go);
                        DestroyObject(go);
                    }
                }
            }
            else
            {
                while (_inactive.Count > 0)
                {
                    InactiveEntry entry = _inactive.Pop();
                    _inactiveIds.Remove(entry.InstanceId);
                    _ownedIds.Remove(entry.InstanceId);
                }

                _inactive.Clear();
            }
        }

        private GameObject CreateNew()
        {
            GameObject go = _root != null
                ? UnityEngine.Object.Instantiate(_prefab, _root)
                : UnityEngine.Object.Instantiate(_prefab);
            _ownedIds.Add(go.GetInstanceID());
            return go;
        }

        private void ResetTransform(Transform target)
        {
            target.localPosition = _prefabLocalPosition;
            target.localRotation = _prefabLocalRotation;
            target.localScale = _prefabLocalScale;
        }

        private static void DestroyObject(GameObject go)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(go);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static void ReportInvalidRelease(string message)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            throw new InvalidOperationException(message);
#else
            Debug.LogError(message);
#endif
        }
    }
}
