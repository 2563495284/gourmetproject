using GourmetProject.Game.UI.Widgets;
using UnityEngine;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>食物商品卡。Prefab 可单独调整食物图标、占格预览和拖拽命中区域。</summary>
    public sealed class ShopFoodBuyItemView : ShopBuyItemViewBase
    {
        [SerializeField] private DishIconRenderTexturePreview _dishIconPreview;

        protected override void ConfigureContent(ShopBuyItemViewContext context)
        {
            SetIcon(context?.Icon);
            if (_dishIconPreview != null && context?.Dish != null)
            {
                _dishIconPreview.Bind(context.Dish, context.Icon, context.Dish.Deliciousness);
            }
            else
            {
                _dishIconPreview?.Hide();
            }

            UseIconAsHitTargetOnly();
        }
    }
}
