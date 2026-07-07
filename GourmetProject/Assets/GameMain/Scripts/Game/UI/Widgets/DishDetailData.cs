using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;

namespace GourmetProject.Game.UI.Widgets
{
    /// <summary>打开菜品详情界面（image2）所需的数据：菜品定义 + 用于查技能/风味/名词的数据库 + 该实例的技能与风味。</summary>
    public sealed class DishDetailData
    {
        public DishDetailData(
            DishDef def,
            GameplayDatabase db,
            System.Collections.Generic.IReadOnlyList<string> skillIds,
            string flavorId,
            System.Collections.Generic.IReadOnlyDictionary<string, string> skillSources = null)
        {
            Def = def;
            Database = db;
            SkillIds = skillIds ?? def.SkillIds;
            FlavorId = flavorId ?? def.FlavorId;
            SkillSources = skillSources;
        }

        public DishDef Def { get; }

        public GameplayDatabase Database { get; }

        public System.Collections.Generic.IReadOnlyList<string> SkillIds { get; }

        public string FlavorId { get; }

        /// <summary>该实例技能的来源标签（skillId → 「源名&lt;甜蜜传递&gt;」等）；无来源信息时为 null。</summary>
        public System.Collections.Generic.IReadOnlyDictionary<string, string> SkillSources { get; }
    }
}
