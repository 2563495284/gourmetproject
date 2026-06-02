using System;

namespace GourmetProject.Core.Rng
{
    /// <summary>
    /// 一条随机流的完整内部状态（xoshiro256** 的 256 位状态）。
    /// 纯数据、可被 JSON / 二进制序列化，用于存档时精确保存与还原随机序列。
    /// </summary>
    [Serializable]
    public struct RngState : IEquatable<RngState>
    {
        public ulong S0;
        public ulong S1;
        public ulong S2;
        public ulong S3;

        public RngState(ulong s0, ulong s1, ulong s2, ulong s3)
        {
            S0 = s0;
            S1 = s1;
            S2 = s2;
            S3 = s3;
        }

        public bool Equals(RngState other)
        {
            return S0 == other.S0 && S1 == other.S1 && S2 == other.S2 && S3 == other.S3;
        }

        public override bool Equals(object obj)
        {
            return obj is RngState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                ulong h = S0 * 31 ^ S1 * 131 ^ S2 * 1313 ^ S3 * 13131;
                return (int)(h ^ (h >> 32));
            }
        }

        public override string ToString()
        {
            return $"RngState({S0:X16},{S1:X16},{S2:X16},{S3:X16})";
        }
    }
}
