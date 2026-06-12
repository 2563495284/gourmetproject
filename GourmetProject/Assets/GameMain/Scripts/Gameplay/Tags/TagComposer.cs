using System.Collections.Generic;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Tags
{
    /// <summary>
    /// 标签集合合成，强制执行策划文档的唯一标签规则：
    /// 每个菜品最多带 1 个唯一标签 A、1 个唯一标签 B，重复获得时「替换原来的」（后者生效）。
    /// 固有标签不受限制全部保留。特殊道具可通过 removeUniqueCap 解除上限。
    /// </summary>
    public static class TagComposer
    {
        public static List<string> Compose(IEnumerable<string> tagIds, GameplayDatabase db, bool removeUniqueCap = false)
        {
            var inherent = new List<string>();
            var uniqueA = new List<string>();
            var uniqueB = new List<string>();

            foreach (string id in tagIds)
            {
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                TagDef tag = db.GetTag(id);
                TagCategory category = tag?.Category ?? TagCategory.Inherent;

                switch (category)
                {
                    case TagCategory.UniqueA:
                        AddUnique(uniqueA, id, removeUniqueCap);
                        break;
                    case TagCategory.UniqueB:
                        AddUnique(uniqueB, id, removeUniqueCap);
                        break;
                    default:
                        inherent.Add(id);
                        break;
                }
            }

            var result = new List<string>(inherent);
            result.AddRange(uniqueA);
            result.AddRange(uniqueB);
            return result;
        }

        private static void AddUnique(List<string> bucket, string id, bool removeUniqueCap)
        {
            if (removeUniqueCap)
            {
                bucket.Add(id);
                return;
            }

            // 有上限：后者替换前者。
            bucket.Clear();
            bucket.Add(id);
        }
    }
}
