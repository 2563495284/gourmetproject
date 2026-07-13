using System.Collections.Generic;
using System.Text;
using GourmetProject.Gameplay.Board;
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
    /// 菜品信息文本的共享生成逻辑：技能/风味行、专有名词解释、hover tooltip 汇总。
    /// 由菜品详情界面（DishDetailForm）与菜单书 hover tips（DishTooltipView）共用，避免两处实现漂移。
    /// </summary>
    public static class DishInfoText
    {
        /// <summary>
        /// 生成技能/风味展示行（格式「【名称】描述」，技能在前、风味在后），并输出去重后的关联专有名词 id。
        /// 技能走 <see cref="GameplayDatabase.GetSkill"/>、风味走 <see cref="GameplayDatabase.GetFlavor"/>。
        /// </summary>
        public static List<string> TagLines(IReadOnlyList<string> skillIds, IReadOnlyList<string> flavorIds, GameplayDatabase db, out List<string> termIds, IReadOnlyDictionary<string, string> skillSources = null, IReadOnlyList<TransferredSkill> transferredSkills = null)
        {
            var lines = new List<string>();
            termIds = new List<string>();
            if (db == null)
            {
                return lines;
            }

            if (skillIds != null)
            {
                foreach (string skillId in skillIds)
                {
                    string sourceLabel = null;
                    skillSources?.TryGetValue(skillId, out sourceLabel);
                    AppendEffect(db.GetSkill(skillId), lines, termIds, sourceLabel);
                }
            }

            // 甜蜜传递获得的外来子技能：每条按「【源名<甜蜜传递>】子技能描述」单独成行。
            if (transferredSkills != null)
            {
                foreach (TransferredSkill t in transferredSkills)
                {
                    string desc = t?.Desc;
                    if (string.IsNullOrEmpty(desc))
                    {
                        continue;
                    }

                    lines.Add(string.IsNullOrEmpty(t.SourceLabel) ? desc : $"【{t.SourceLabel}】{desc}");
                }
            }

            if (flavorIds != null)
            {
                foreach (string flavorId in flavorIds)
                {
                    AppendEffect(db.GetFlavor(flavorId), lines, termIds);
                }
            }

            return lines;
        }

        private static void AppendEffect(IEffectDef def, List<string> lines, List<string> termIds)
        {
            if (def == null)
            {
                return;
            }

            lines.Add($"【{def.Name}】{def.Desc}");
            if (def.HasTerm && !termIds.Contains(def.TermId))
            {
                termIds.Add(def.TermId);
            }
        }

        private static void AppendEffect(SkillDef def, List<string> lines, List<string> termIds, string sourceLabel = null)
        {
            if (def == null)
            {
                return;
            }

            string title = string.IsNullOrEmpty(sourceLabel) ? def.Name : sourceLabel;
            lines.Add(string.IsNullOrEmpty(title) ? def.Desc : $"【{title}】{def.Desc}");
            if (def.HasTerm && !termIds.Contains(def.TermId))
            {
                termIds.Add(def.TermId);
            }
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

        /// <summary>生成菜品 hover tooltip 的完整文本：名称、美味度、形状、技能/风味、名词。</summary>
        public static string Tooltip(DishDef def, IReadOnlyList<string> skillIds, string flavorId, GameplayDatabase db)
        {
            if (def == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.AppendLine(def.Name);
            sb.AppendLine($"美味度 {def.Deliciousness}　形状 {def.Shape.Width}x{def.Shape.Height}");

            IReadOnlyList<string> flavorIds = string.IsNullOrEmpty(flavorId)
                ? System.Array.Empty<string>()
                : new[] { flavorId };
            List<string> tagLines = TagLines(skillIds, flavorIds, db, out List<string> termIds);
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
