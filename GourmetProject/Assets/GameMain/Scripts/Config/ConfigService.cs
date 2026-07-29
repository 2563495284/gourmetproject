using System;
using System.IO;
using GourmetProject.Core.Diagnostics;
using Luban.SimpleJSON;
using UnityEngine;

namespace GourmetProject.Config
{
    /// <summary>
    /// Luban 配置加载服务（替代 GameFramework 自带 DataTable）。运行时一次性把所有表
    /// 从 StreamingAssets/Config 读入内存。PC 单机可直接读文件；如需移动端再改异步加载。
    /// 这里不预置玩法表，仅提供加载入口与对生成的 cfg.Tables 的访问。
    /// </summary>
    public sealed class ConfigService
    {
        private const string Tag = "Config";
        private const string ConfigFolderName = "Config";

        public cfg.Tables Tables { get; private set; }

        public bool IsLoaded => Tables != null;

        /// <summary>同步加载全部配置表。整个运行期间只需加载一次。</summary>
        public void LoadAll()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, ConfigFolderName);

            Tables = new cfg.Tables(tableName =>
            {
                string path = Path.Combine(dir, tableName + ".json");
                string text = File.ReadAllText(path);
                return JSON.Parse(text);
            });

            Log.Info(
                $"dishBases={Tables.TbDishBase.DataList.Count}, dishFamilies={Tables.TbDishVariant.DataList.Count}, " +
                $"skills={Tables.TbSkill.DataList.Count}, flavors={Tables.TbFlavor.DataList.Count}, materials={Tables.TbMaterial.DataList.Count}, " +
                $"characters={Tables.TbCharacter.DataList.Count}, recipes={Tables.TbRecipe.DataList.Count}, " +
                $"weeks={Tables.TbWeek.DataList.Count}",
                Tag);
        }
    }
}
