using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GourmetProject.Game.Balance
{
    /// <summary>对实际被 Balance Lab 读取的生成 JSON 做稳定内容指纹。</summary>
    [Serializable]
    public sealed class BalanceConfigFingerprint
    {
        public string RootPath = string.Empty;
        public string ContentHash = string.Empty;
        public int FileCount;
        public long TotalBytes;
        public long LatestWriteUtcTicks;
        public string Error = string.Empty;

        public bool IsValid => string.IsNullOrEmpty(Error) && !string.IsNullOrEmpty(ContentHash);

        public string ShortHash => IsValid
            ? ContentHash.Substring(0, Math.Min(12, ContentHash.Length))
            : "不可用";

        public bool HasSameContent(BalanceConfigFingerprint other)
        {
            return other != null
                   && IsValid
                   && other.IsValid
                   && string.Equals(ContentHash, other.ContentHash, StringComparison.OrdinalIgnoreCase);
        }

        public static BalanceConfigFingerprint Compute(string rootPath)
        {
            var result = new BalanceConfigFingerprint { RootPath = rootPath ?? string.Empty };
            try
            {
                if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
                {
                    result.Error = $"配置目录不存在：{rootPath}";
                    return result;
                }

                FileInfo[] files = new DirectoryInfo(rootPath)
                    .GetFiles("*.json", SearchOption.TopDirectoryOnly)
                    .OrderBy(file => file.Name, StringComparer.Ordinal)
                    .ToArray();
                if (files.Length == 0)
                {
                    result.Error = $"配置目录中没有 JSON：{rootPath}";
                    return result;
                }

                using var payload = new MemoryStream();
                foreach (FileInfo file in files)
                {
                    byte[] name = Encoding.UTF8.GetBytes(file.Name);
                    byte[] content = File.ReadAllBytes(file.FullName);
                    WriteLength(payload, name.Length);
                    payload.Write(name, 0, name.Length);
                    WriteLength(payload, content.Length);
                    payload.Write(content, 0, content.Length);
                    result.TotalBytes += content.LongLength;
                    result.LatestWriteUtcTicks = Math.Max(result.LatestWriteUtcTicks, file.LastWriteTimeUtc.Ticks);
                }

                using SHA256 sha = SHA256.Create();
                result.ContentHash = ToHex(sha.ComputeHash(payload.ToArray()));
                result.FileCount = files.Length;
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return result;
        }

        private static void WriteLength(Stream stream, int value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 24));
        }

        private static string ToHex(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) text.Append(value.ToString("x2"));
            return text.ToString();
        }
    }
}
