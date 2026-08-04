namespace GourmetProject.Game.UI.Meta
{
    /// <summary>消耗品商品卡。Prefab 可单独调整消耗品的尺寸和位置。</summary>
    public sealed class ShopActiveItemBuyItemView : ShopBuyItemViewBase
    {
        protected override void ConfigureContent(ShopBuyItemViewContext context)
        {
            SetIcon(context?.Icon);
        }
    }
}
