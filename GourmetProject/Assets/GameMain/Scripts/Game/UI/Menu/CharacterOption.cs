using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;
namespace GourmetProject.Game.UI.Menu
{
    /// <summary>
    /// 角色选择界面的占位角色数据。后续接入 Luban 角色配置表后，
    /// 用查表替换 <see cref="CharacterOptions.All"/> 即可，界面逻辑无需改动。
    /// </summary>
    public sealed class CharacterOption
    {
        public CharacterOption(string id, string displayName, string description, string portraitResource)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            PortraitResource = portraitResource;
        }

        /// <summary>角色唯一标识。</summary>
        public string Id { get; }

        /// <summary>显示名。</summary>
        public string DisplayName { get; }

        /// <summary>简介文案（杀戮尖塔风格的一句话定位）。</summary>
        public string Description { get; }

        /// <summary>立绘资源路径（相对 Resources，传给 Resources.Load）。</summary>
        public string PortraitResource { get; }
    }

    /// <summary>
    /// 占位角色列表。数量与原型图的翻页圆点一致（3 个）。
    /// </summary>
    public static class CharacterOptions
    {
        public static readonly CharacterOption[] All =
        {
            new(
                id: "glutton_dog",
                displayName: "大胃汪",
                description: "暴食流。胃口越大，桌上的菜越不够看。",
                portraitResource: "Sprites/Characters/char_glutton_dog"),
            new(
                id: "wok_cat",
                displayName: "铁锅喵",
                description: "料理流。一手颠勺，一手挥刀，火候即是胜负。",
                portraitResource: "Sprites/Characters/char_wok_cat"),
            new(
                id: "chili_rat",
                displayName: "辣魔鼠",
                description: "火辣流。越吃越上头，辣到对手举旗投降。",
                portraitResource: "Sprites/Characters/char_chili_rat"),
        };
    }
}
