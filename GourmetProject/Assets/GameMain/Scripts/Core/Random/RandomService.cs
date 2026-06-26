using System;
using System.Collections.Generic;
using GourmetProject.Core.Utility;

namespace GourmetProject.Core.Rng
{
    /// <summary>
    /// 确定性随机系统的总入口。一个主种子派生出多条互相独立的命名随机流，
    /// 某条流多消耗/少消耗随机数都不会污染其它流，这是 roguelike 可复现的关键。
    /// 通过 Capture/Restore 与存档系统对接。
    /// </summary>
    public sealed class RandomService
    {
        /// <summary>域流缓存键里「域名」与「实例 key」之间的分隔符，取不可见控制字符避免与普通流名冲突。</summary>
        private const char DomainKeySeparator = '\u0001';

        private readonly Dictionary<string, Xoshiro256SS> _streams = new Dictionary<string, Xoshiro256SS>(StringComparer.Ordinal);

        /// <summary>原始种子文本（仅用于展示/分享）。</summary>
        public string SeedText { get; private set; }

        /// <summary>解析后的 64 位主种子。</summary>
        public ulong MasterSeed { get; private set; }

        public bool IsInitialized { get; private set; }

        /// <summary>用字符串种子初始化。空字符串会生成一个随机种子文本。</summary>
        public void Init(string seedText)
        {
            if (string.IsNullOrWhiteSpace(seedText))
            {
                seedText = GenerateRandomSeedText();
            }

            SeedText = seedText;
            MasterSeed = ParseSeed(seedText);
            _streams.Clear();
            IsInitialized = true;
        }

        /// <summary>用数值种子初始化。</summary>
        public void Init(ulong masterSeed, string seedText = null)
        {
            MasterSeed = masterSeed;
            SeedText = seedText ?? masterSeed.ToString();
            _streams.Clear();
            IsInitialized = true;
        }

        /// <summary>按名取流；首次访问时基于主种子与流名稳定派生该流的初始状态。</summary>
        public IRandomStream Stream(string name)
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException("RandomService.Init must be called before accessing streams.");
            }

            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Stream name must not be empty.", nameof(name));
            }

            if (!_streams.TryGetValue(name, out Xoshiro256SS stream))
            {
                stream = new Xoshiro256SS(DeriveStreamSeed(MasterSeed, name));
                _streams.Add(name, stream);
            }

            return stream;
        }

        /// <summary>
        /// 取「系统域」内的实例流：主种子 --域名--> 域种子 --实例 key--> 实例流，两段派生。
        /// 相比把域名与实例信息拼成单个流名，两段派生让域之间的隔离更强、命名更规范。
        /// domain 取 <see cref="SeedDomains"/> 常量，instanceKey 用上下文（如 $"w{Week}_d{Day}"）。
        /// </summary>
        public IRandomStream DomainStream(string domain, string instanceKey)
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException("RandomService.Init must be called before accessing streams.");
            }

            if (string.IsNullOrEmpty(domain))
            {
                throw new ArgumentException("Domain must not be empty.", nameof(domain));
            }

            if (string.IsNullOrEmpty(instanceKey))
            {
                throw new ArgumentException("Instance key must not be empty.", nameof(instanceKey));
            }

            string cacheKey = domain + DomainKeySeparator + instanceKey;
            if (!_streams.TryGetValue(cacheKey, out Xoshiro256SS stream))
            {
                ulong domainSeed = DeriveStreamSeed(MasterSeed, domain);
                ulong instanceSeed = DeriveStreamSeed(domainSeed, instanceKey);
                stream = new Xoshiro256SS(instanceSeed);
                _streams.Add(cacheKey, stream);
            }

            return stream;
        }

        /// <summary>
        /// 取表现域（<see cref="SeedDomains.Cosmetic"/>）实例流。表现层随机专用，
        /// 不参与玩法复现、不入存档（见 <see cref="Capture"/>）。
        /// </summary>
        public IRandomStream Cosmetic(string instanceKey)
        {
            return DomainStream(SeedDomains.Cosmetic, instanceKey);
        }

        /// <summary>按权重从候选中取一个元素。</summary>
        public T WeightedPick<T>(string streamName, IReadOnlyList<T> items, IReadOnlyList<float> weights)
        {
            if (items == null || weights == null || items.Count != weights.Count)
            {
                throw new ArgumentException("items and weights must be non-null and of equal length.");
            }

            int index = Stream(streamName).WeightedPickIndex(weights);
            return items[index];
        }

        /// <summary>捕获当前所有玩法流状态，用于写入存档；表现域（cosmetic）刻意排除，避免污染玩法复现。</summary>
        public RandomSnapshot Capture()
        {
            var snapshot = new RandomSnapshot
            {
                SeedText = SeedText,
                MasterSeed = MasterSeed,
                Streams = new Dictionary<string, RngState>(_streams.Count, StringComparer.Ordinal),
            };

            string cosmeticPrefix = SeedDomains.Cosmetic + DomainKeySeparator;
            foreach (KeyValuePair<string, Xoshiro256SS> pair in _streams)
            {
                if (pair.Key.StartsWith(cosmeticPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                snapshot.Streams[pair.Key] = pair.Value.State;
            }

            return snapshot;
        }

        /// <summary>从存档快照还原主种子与所有流状态。</summary>
        public void Restore(RandomSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            SeedText = snapshot.SeedText;
            MasterSeed = snapshot.MasterSeed;
            _streams.Clear();
            IsInitialized = true;

            if (snapshot.Streams == null)
            {
                return;
            }

            foreach (KeyValuePair<string, RngState> pair in snapshot.Streams)
            {
                _streams[pair.Key] = new Xoshiro256SS(pair.Value);
            }
        }

        /// <summary>把字符串种子解析为 64 位主种子：纯数字按数值，否则取稳定哈希。</summary>
        public static ulong ParseSeed(string seedText)
        {
            if (string.IsNullOrEmpty(seedText))
            {
                return 0UL;
            }

            if (ulong.TryParse(seedText, out ulong numeric))
            {
                return numeric;
            }

            return StableHash.Fnv1a64(seedText);
        }

        private static ulong DeriveStreamSeed(ulong masterSeed, string name)
        {
            ulong nameHash = StableHash.Fnv1a64(name);
            unchecked
            {
                // boost::hash_combine 风格混合，再过一遍 SplitMix64 充分打散。
                ulong combined = masterSeed;
                combined ^= nameHash + 0x9E3779B97F4A7C15UL + (combined << 6) + (combined >> 2);
                return SplitMix64.Mix(combined);
            }
        }

        private static string GenerateRandomSeedText()
        {
            // 仅用于"未指定种子"时生成一个可分享的种子文本，本身不要求确定性。
            ulong t = (ulong)DateTime.UtcNow.Ticks;
            ulong mixed = SplitMix64.Mix(t ^ 0xD1B54A32D192ED03UL);
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            char[] buffer = new char[8];
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = alphabet[(int)(mixed % (ulong)alphabet.Length)];
                mixed /= (ulong)alphabet.Length;
                if (mixed == 0UL)
                {
                    mixed = SplitMix64.Mix(t + (ulong)i + 1UL);
                }
            }

            return new string(buffer);
        }
    }
}
