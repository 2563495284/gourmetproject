using Newtonsoft.Json.Linq;

namespace GourmetProject.Core.Save
{
    /// <summary>
    /// 单步存档迁移：把 FromVersion 版本的存档数据原地升级到 FromVersion + 1。
    /// 多步迁移由 SaveService 自动按版本链依次调用，改了玩法结构后老存档不会直接崩。
    /// </summary>
    public interface ISaveMigration
    {
        /// <summary>本迁移处理的来源版本号（处理 FromVersion -> FromVersion + 1）。</summary>
        int FromVersion { get; }

        /// <summary>对存档的 data 区块做结构升级，返回升级后的 data。</summary>
        JToken Migrate(JToken data);
    }
}
