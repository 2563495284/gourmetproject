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
        public DishDetailData(DishDef def, GameplayDatabase db, System.Collections.Generic.IReadOnlyList<string> skillIds, string flavorId)
        {
            Def = def;
            Database = db;
            SkillIds = skillIds ?? def.SkillIds;
            FlavorId = flavorId ?? def.FlavorId;
        }

        public DishDef Def { get; }

        public GameplayDatabase Database { get; }

        public System.Collections.Generic.IReadOnlyList<string> SkillIds { get; }

        public string FlavorId { get; }
    }
}
