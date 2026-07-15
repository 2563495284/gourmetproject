using GourmetProject.Game.UI.Widgets;
using UnityEngine;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>食物商品卡。Prefab 可单独调整食物图标、占格预览和拖拽命中区域。</summary>
    public sealed class ShopFoodBuyItemView : ShopBuyItemViewBase
    {
        [SerializeField] private DishShapePreview _dishShapePreview;

        protected override void ConfigureContent(ShopBuyItemViewContext context)
        {
            SetIcon(context?.Icon);
            _dishShapePreview?.Bind(context?.Dish, context?.Icon);
            UseIconAsHitTargetOnly();
        }
    }
}
