using UnityEngine;
using UnityEngine.UI;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;
using TMPro;

namespace GourmetProject.Game.UI.Widgets
{
    /// <summary>
    /// 菜品详情标签区的单行文本。固定结构在 DishTagLine.prefab，
    /// 行内容由 <see cref="SetText"/> 数据驱动填充。
    /// </summary>
    public sealed class DishTagLine : MonoBehaviour
    {
        [SerializeField] private TMP_Text _text;

        public void SetText(string text)
        {
            _text.text = text;
        }
    }
}
