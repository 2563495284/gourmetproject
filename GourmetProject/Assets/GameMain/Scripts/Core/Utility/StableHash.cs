namespace GourmetProject.Core.Utility
{
    /// <summary>
    /// 跨平台稳定哈希。注意：string.GetHashCode 在不同运行时/平台不保证一致，
    /// 任何参与确定性逻辑（种子派生等）的字符串哈希都必须用本类，绝不能用 GetHashCode。
    /// </summary>
    public static class StableHash
    {
        private const ulong Fnv64OffsetBasis = 14695981039346656037UL;
        private const ulong Fnv64Prime = 1099511628211UL;

        /// <summary>对字符串按 UTF-16 字节做 FNV-1a 64 位哈希。</summary>
        public static ulong Fnv1a64(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Fnv64OffsetBasis;
            }

            unchecked
            {
                ulong hash = Fnv64OffsetBasis;
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    hash ^= (byte)(c & 0xFF);
                    hash *= Fnv64Prime;
                    hash ^= (byte)((c >> 8) & 0xFF);
                    hash *= Fnv64Prime;
                }

                return hash;
            }
        }
    }
}
