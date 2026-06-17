using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 菜品详情标签区的单行文本。固定结构在 DishTagLine.prefab，
    /// 行内容由 <see cref="SetText"/> 数据驱动填充。
    /// </summary>
    public sealed class DishTagLine : MonoBehaviour
    {
        [SerializeField] private Text _text;

        public void SetText(string text)
        {
            _text.text = text;
        }
    }
}
