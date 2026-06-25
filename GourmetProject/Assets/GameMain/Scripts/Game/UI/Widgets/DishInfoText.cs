using System.Collections.Generic;
using System.Text;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;

namespace GourmetProject.Game.UI.Widgets
{
    /// <summary>
    /// 菜品信息文本的共享生成逻辑：标签行、专有名词解释、hover tooltip 汇总。
    /// 由菜品详情界面（DishDetailForm）与菜单书 hover tips（DishTooltipView）共用，避免两处实现漂移。
    /// </summary>
    public static class DishInfoText
    {
        /// <summary>生成标签展示行（格式「【名称】描述」），并输出去重后的关联专有名词 id。</summary>
        public static List<string> TagLines(IReadOnlyList<string> tagIds, GameplayDatabase db, out List<string> termIds)
        {
            var lines = new List<string>();
            termIds = new List<string>();
            if (tagIds == null || db == null)
            {
                return lines;
            }

            foreach (string tagId in tagIds)
            {
                TagDef tag = db.GetTag(tagId);
                if (tag == null)
                {
                    continue;
                }

                lines.Add($"【{tag.Name}】{tag.Desc}");
                if (tag.HasTerm && !termIds.Contains(tag.TermId))
                {
                    termIds.Add(tag.TermId);
                }
            }

            return lines;
        }

        /// <summary>生成专有名词解释块（每行「※ 名称：描述」）。无名词时返回空串。</summary>
        public static string TermBlock(IReadOnlyList<string> termIds)
        {
            if (termIds == null || termIds.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (string termId in termIds)
            {
                cfg.Term term = GameApp.Config.Tables.TbTerm.GetOrDefault(termId);
                if (term != null)
                {
                    sb.AppendLine($"※ {term.Name}：{term.Desc}");
                }
            }

            return sb.ToString();
        }

        /// <summary>生成菜品 hover tooltip 的完整文本：名称、美味度、形状、标签、名词。</summary>
        public static string Tooltip(DishDef def, IReadOnlyList<string> tagIds, GameplayDatabase db)
        {
            if (def == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.AppendLine(def.Name);
            sb.AppendLine($"美味度 {def.Deliciousness}　形状 {def.Shape.Width}x{def.Shape.Height}");

            List<string> tagLines = TagLines(tagIds ?? def.InherentTags, db, out List<string> termIds);
            foreach (string line in tagLines)
            {
                sb.AppendLine(line);
            }

            string terms = TermBlock(termIds);
            if (!string.IsNullOrEmpty(terms))
            {
                sb.AppendLine();
                sb.Append(terms);
            }

            return sb.ToString().TrimEnd();
        }
    }
}
