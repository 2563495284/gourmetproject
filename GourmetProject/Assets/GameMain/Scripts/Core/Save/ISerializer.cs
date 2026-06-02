namespace GourmetProject.Core.Save
{
    /// <summary>
    /// 序列化后端抽象。默认提供 JSON 实现（可读、便于调试）；
    /// 未来需要更小更快的存档可替换为二进制实现而不影响上层。
    /// </summary>
    public interface ISerializer
    {
        /// <summary>对象 -> 字节。</summary>
        byte[] Serialize<T>(T value);

        /// <summary>字节 -> 对象。</summary>
        T Deserialize<T>(byte[] data);

        /// <summary>序列化后内容的文件扩展名（不含点），用于落盘命名。</summary>
        string FileExtension { get; }
    }
}
