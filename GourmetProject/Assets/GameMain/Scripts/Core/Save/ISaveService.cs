using System.Collections.Generic;

namespace GourmetProject.Core.Save
{
    /// <summary>
    /// 通用存档服务。能存取任意可被 JSON 序列化的对象，支持多槽位、版本迁移、完整性校验与原子写。
    /// 不关心具体玩法数据结构（RunState 由玩法层自行定义后丢进来即可）。
    /// </summary>
    public interface ISaveService
    {
        /// <summary>把数据写入指定槽位（原子写，失败不破坏旧档）。</summary>
        void Save<T>(string slot, T data);

        /// <summary>尝试从指定槽位读取数据；读取成功返回 true。损坏/校验失败/不存在均返回 false。</summary>
        bool TryLoad<T>(string slot, out T data);

        /// <summary>指定槽位是否存在有效存档文件。</summary>
        bool Has(string slot);

        /// <summary>删除指定槽位的存档（含备份）。</summary>
        void Delete(string slot);

        /// <summary>列出当前所有已存在的槽位名。</summary>
        IEnumerable<string> ListSlots();
    }
}
