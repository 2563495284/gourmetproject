using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 菜品详情形状网格的单个格子。固定结构在 DishShapeCell.prefab，
    /// 是否填充由 <see cref="SetColor"/> 数据驱动着色。
    /// </summary>
    public sealed class DishShapeCell : MonoBehaviour
    {
        [SerializeField] private Image _image;

        public void SetColor(Color color)
        {
            _image.color = color;
        }
    }
}
