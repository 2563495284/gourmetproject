using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 菜品详情形状网格的单个格子。固定结构在 DishShapeCell.prefab，
    /// 格子贴图与颜色由详情页按棋盘表现数据驱动。
    /// </summary>
    public sealed class DishShapeCell : MonoBehaviour
    {
        [SerializeField] private Image _image;

        public void SetSprite(Sprite sprite)
        {
            _image.sprite = sprite;
        }

        public void SetColor(Color color)
        {
            _image.color = color;
        }
    }
}
