using System.Collections.Generic;

namespace GourmetProject.Gameplay.Tags
{
    /// <summary>
    /// 菜品「技能」与「风味」合成，执行策划文档规则：
    /// 技能数量无上限，全部保留（可被道具追加）；风味单槽，后者替换前者，
    /// 特殊道具可通过 removeFlavorCap 解除「只能存在单个风味」的上限。
    /// </summary>
    public static class TagComposer
    {
        /// <summary>技能合成：按输入顺序保留全部非空技能。</summary>
        public static List<string> ComposeSkills(IEnumerable<string> skillIds)
        {
            var result = new List<string>();
            if (skillIds == null)
            {
                return result;
            }

            foreach (string id in skillIds)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    result.Add(id);
                }
            }

            return result;
        }

        /// <summary>风味合成（单槽）：取最后一个非空风味（后者替换前者）；无则空串。</summary>
        public static string ComposeFlavor(IEnumerable<string> flavorIds)
        {
            string result = string.Empty;
            if (flavorIds == null)
            {
                return result;
            }

            foreach (string id in flavorIds)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    result = id;
                }
            }

            return result;
        }

        /// <summary>
        /// 风味合成（可解除单槽上限）：removeFlavorCap=false 时最多保留最后一个；
        /// 为 true 时（特殊道具）保留全部非空风味。
        /// </summary>
        public static List<string> ComposeFlavors(IEnumerable<string> flavorIds, bool removeFlavorCap = false)
        {
            var all = new List<string>();
            if (flavorIds != null)
            {
                foreach (string id in flavorIds)
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        all.Add(id);
                    }
                }
            }

            if (removeFlavorCap)
            {
                return all;
            }

            var single = new List<string>();
            if (all.Count > 0)
            {
                single.Add(all[all.Count - 1]);
            }

            return single;
        }
    }
}
