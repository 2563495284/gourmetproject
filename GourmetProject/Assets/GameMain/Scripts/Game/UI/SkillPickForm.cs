using System.Collections.Generic;
using GourmetProject.Game.Roguelike;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine.UI;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 到达检查点时弹出的 3 选 1 技能界面。候选由 <see cref="SkillPickData.Options"/> 提供，
    /// 玩家点击后回调 <see cref="SkillPickData.OnPicked"/> 并关闭界面。
    /// </summary>
    public sealed class SkillPickForm : UGuiForm
    {
        private const int MaxOptions = 3;

        private Text _title;
        private readonly Button[] _buttons = new Button[MaxOptions];
        private readonly Text[] _names = new Text[MaxOptions];
        private readonly Text[] _descs = new Text[MaxOptions];
        private readonly Text[] _tags = new Text[MaxOptions];

        private SkillPickData _data;
        private IReadOnlyList<SkillDef> _options;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            _title = CachedTransform.Find("Title").GetComponent<Text>();
            for (int i = 0; i < MaxOptions; i++)
            {
                var opt = CachedTransform.Find("Option" + i);
                _buttons[i] = opt.GetComponent<Button>();
                _names[i] = opt.Find("Name").GetComponent<Text>();
                _descs[i] = opt.Find("Desc").GetComponent<Text>();
                _tags[i] = opt.Find("TagLabel").GetComponent<Text>();

                int index = i;
                _buttons[i].onClick.AddListener(() => OnOptionClicked(index));
            }
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            _data = userData as SkillPickData;
            _options = _data?.Options;
            _title.text = _data != null ? $"选择一个技能  (T{_data.Tier})" : "选择一个技能";

            for (int i = 0; i < MaxOptions; i++)
            {
                bool active = _options != null && i < _options.Count;
                _buttons[i].gameObject.SetActive(active);
                if (!active) continue;

                SkillDef def = _options[i];
                _names[i].text = def.Name;
                _descs[i].text = def.Desc;
                _tags[i].text = TagLabel(def.Tag);
            }
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            _data = null;
            _options = null;
            base.OnClose(isShutdown, userData);
        }

        private void OnOptionClicked(int index)
        {
            if (_options == null || index < 0 || index >= _options.Count) return;

            SkillDef picked = _options[index];
            var callback = _data?.OnPicked;
            GameApp.UI.CloseUIForm(UIForm);
            callback?.Invoke(picked);
        }

        private static string TagLabel(SkillTag tag)
        {
            switch (tag)
            {
                case SkillTag.Move: return "[移动]";
                case SkillTag.Light: return "[光源]";
                case SkillTag.Survival: return "[生存]";
                case SkillTag.Economy: return "[经济]";
                case SkillTag.Monster: return "[怪物]";
                default: return string.Empty;
            }
        }
    }
}
