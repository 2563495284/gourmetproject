namespace GourmetProject.Core.Save
{
    /// <summary>存档服务的可调行为。</summary>
    public sealed class SaveServiceOptions
    {
        /// <summary>当前存档结构版本号。写档时记录，读档时据此触发迁移。</summary>
        public int CurrentVersion = 1;

        /// <summary>是否写入并校验完整性校验和（检测损坏/手改）。</summary>
        public bool EnableChecksum = true;

        /// <summary>校验和盐值，提高随手改档的门槛（非加密）。</summary>
        public string ChecksumSalt = "GourmetProject";

        /// <summary>写新档时是否保留上一份为 .bak 备份。</summary>
        public bool KeepBackup = true;

        /// <summary>存档文件扩展名（不含点）。</summary>
        public string FileExtension = "sav";

        /// <summary>JSON 是否缩进（开发期便于阅读，发布可关以减小体积）。</summary>
        public bool Indented = true;
    }
}
