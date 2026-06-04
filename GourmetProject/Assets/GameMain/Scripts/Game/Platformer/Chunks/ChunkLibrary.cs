using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using UnityEngine;

namespace GourmetProject.Game.Platformer.Chunks
{
    /// <summary>
    /// 预制段库：从 <c>Resources/Chunks</c> 加载所有挂 <see cref="ChunkInfo"/> 的预制体，按角色/难度分桶。
    /// 加载结果按预制体名排序以保证 <see cref="Resources.LoadAll"/> 顺序对随机抽取确定可复现。
    /// </summary>
    public sealed class ChunkLibrary
    {
        public const string ResourceFolder = "Chunks";

        public readonly List<GameObject> Start = new List<GameObject>();
        public readonly List<GameObject> Rest = new List<GameObject>();
        public readonly List<GameObject> End = new List<GameObject>();

        // 索引 1..5 为难度池（0 未用）。
        private readonly List<GameObject>[] _normal = new List<GameObject>[6];

        private ChunkLibrary()
        {
            for (int i = 0; i < _normal.Length; i++) _normal[i] = new List<GameObject>();
        }

        public static ChunkLibrary Load()
        {
            var lib = new ChunkLibrary();

            GameObject[] all = Resources.LoadAll<GameObject>(ResourceFolder);
            Array.Sort(all, (a, b) => string.CompareOrdinal(a != null ? a.name : "", b != null ? b.name : ""));

            foreach (GameObject go in all)
            {
                if (go == null) continue;
                ChunkInfo info = go.GetComponent<ChunkInfo>();
                if (info == null) continue;

                switch (info.Role)
                {
                    case ChunkRole.Start: lib.Start.Add(go); break;
                    case ChunkRole.Rest: lib.Rest.Add(go); break;
                    case ChunkRole.End: lib.End.Add(go); break;
                    default:
                        int d = Mathf.Clamp(info.Difficulty, 1, 5);
                        lib._normal[d].Add(go);
                        break;
                }
            }

            return lib;
        }

        public bool IsEmpty =>
            Start.Count == 0 && Rest.Count == 0 && End.Count == 0 &&
            _normal[1].Count == 0 && _normal[2].Count == 0 && _normal[3].Count == 0 &&
            _normal[4].Count == 0 && _normal[5].Count == 0;

        public GameObject PickStart(IRandomStream rng) => PickFrom(Start, rng, null);
        public GameObject PickRest(IRandomStream rng) => PickFrom(Rest, rng, null);
        public GameObject PickEnd(IRandomStream rng) => PickFrom(End, rng, null);

        /// <summary>抽一个指定难度的 Normal 段；池为空时就近回退到其它难度，尽量避免与 <paramref name="avoid"/> 重复。</summary>
        public GameObject PickNormal(int difficulty, IRandomStream rng, GameObject avoid)
        {
            List<GameObject> pool = ResolveNormalPool(difficulty);
            return PickFrom(pool, rng, avoid);
        }

        private List<GameObject> ResolveNormalPool(int difficulty)
        {
            int d = Mathf.Clamp(difficulty, 1, 5);
            if (_normal[d].Count > 0) return _normal[d];

            // 就近回退：先向低难度找，再向高难度找。
            for (int delta = 1; delta <= 4; delta++)
            {
                int lo = d - delta;
                int hi = d + delta;
                if (lo >= 1 && _normal[lo].Count > 0) return _normal[lo];
                if (hi <= 5 && _normal[hi].Count > 0) return _normal[hi];
            }
            return _normal[d]; // 全空，返回空池（调用方处理 null）
        }

        private static GameObject PickFrom(List<GameObject> pool, IRandomStream rng, GameObject avoid)
        {
            if (pool == null || pool.Count == 0) return null;
            if (pool.Count == 1) return pool[0];

            // 避免与上一个相同：从去重候选里抽。
            if (avoid != null)
            {
                int idx = rng.Range(0, pool.Count - 1); // 在“去掉 avoid”后的等效区间抽
                int picked = 0;
                for (int i = 0; i < pool.Count; i++)
                {
                    if (pool[i] == avoid) continue;
                    if (picked == idx) return pool[i];
                    picked++;
                }
            }

            return pool[rng.Range(0, pool.Count)];
        }
    }
}
