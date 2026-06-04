using System;
using System.Collections.Generic;
using GourmetProject.Game.Roguelike;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 技能 3 选 1 界面入参（通过 OpenUIForm 的 userData 传入）。
    /// </summary>
    public sealed class SkillPickData
    {
        /// <summary>本次选择对应的段位（仅用于标题展示）。</summary>
        public int Tier;

        /// <summary>候选技能（1-3 个；不足 3 个时界面自动隐藏多余按钮）。</summary>
        public IReadOnlyList<SkillDef> Options;

        /// <summary>玩家选定某个技能后的回调。</summary>
        public Action<SkillDef> OnPicked;
    }
}
