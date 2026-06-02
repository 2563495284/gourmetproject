namespace GourmetProject.Core.Utility
{
    /// <summary>
    /// 轻量校验和（FNV-1a 64 位）。用于检测存档是否损坏或被手改，配合可选盐值提高篡改门槛。
    /// 注意：这是完整性校验而非加密，不能抵御有意的逆向，仅用于阻止随手改档。
    /// </summary>
    public static class Checksum
    {
        private const ulong Fnv64OffsetBasis = 14695981039346656037UL;
        private const ulong Fnv64Prime = 1099511628211UL;

        public static ulong Compute(byte[] data, string salt = null)
        {
            unchecked
            {
                ulong hash = Fnv64OffsetBasis;

                if (!string.IsNullOrEmpty(salt))
                {
                    for (int i = 0; i < salt.Length; i++)
                    {
                        hash ^= (byte)salt[i];
                        hash *= Fnv64Prime;
                    }
                }

                if (data != null)
                {
                    for (int i = 0; i < data.Length; i++)
                    {
                        hash ^= data[i];
                        hash *= Fnv64Prime;
                    }
                }

                return hash;
            }
        }

        public static string ComputeHex(byte[] data, string salt = null)
        {
            return Compute(data, salt).ToString("X16");
        }
    }
}
