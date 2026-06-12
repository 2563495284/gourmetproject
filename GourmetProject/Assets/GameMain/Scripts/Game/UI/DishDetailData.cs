using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.UI
{
    /// <summary>打开菜品详情界面（image2）所需的数据：菜品定义 + 用于查标签/名词的数据库 + 该实例的最终标签。</summary>
    public sealed class DishDetailData
    {
        public DishDetailData(DishDef def, GameplayDatabase db, System.Collections.Generic.IReadOnlyList<string> tagIds)
        {
            Def = def;
            Database = db;
            TagIds = tagIds ?? def.InherentTags;
        }

        public DishDef Def { get; }

        public GameplayDatabase Database { get; }

        public System.Collections.Generic.IReadOnlyList<string> TagIds { get; }
    }
}
