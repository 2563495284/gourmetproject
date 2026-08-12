using System.Collections.Generic;

namespace GourmetProject.Gameplay.Model
{
    /// <summary>已发布内容 ID 的兼容迁移。所有运行时入口都应先归一化，再保存为新 ID。</summary>
    public static class ContentIdAliases
    {
        public const string LegacyMantouDishId = "custard_bun";
        public const string MantouDishId = "mantou";
        public const string LegacyFreshFlavorId = "t_rust";
        public const string FreshFlavorId = "t_fresh";

        public static string NormalizeDishId(string id)
            => id == LegacyMantouDishId ? MantouDishId : id;

        public static string NormalizeFlavorId(string id)
            => id == LegacyFreshFlavorId ? FreshFlavorId : id;

        public static List<string> NormalizeFlavorIds(IEnumerable<string> ids)
        {
            var result = new List<string>();
            if (ids == null)
            {
                return result;
            }

            foreach (string id in ids)
            {
                string normalized = NormalizeFlavorId(id);
                if (!string.IsNullOrEmpty(normalized))
                {
                    result.Add(normalized);
                }
            }

            return result;
        }
    }
}
