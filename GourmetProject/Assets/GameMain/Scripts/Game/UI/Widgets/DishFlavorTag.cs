using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Widgets
{
    /// <summary>
    /// 菜品 Tips 顶部框里的风味占位标签（小药丸）。每个风味一个，横向排布，
    /// 不形成列表；对应的风味详细描述在下方以 <see cref="DishInfoCard"/> 逐张展开。
    /// 固定结构在 DishFlavorTag.prefab，文本由 <see cref="Set"/> 数据驱动。
    /// </summary>
    public sealed class DishFlavorTag : MonoBehaviour
    {
        [SerializeField] private TMP_Text _labelText;

        public void Set(string flavorName)
        {
            if (_labelText != null)
            {
                _labelText.text = flavorName;
            }
        }
    }
}
