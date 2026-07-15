namespace GourmetProject.Game.UI.Meta
{
    /// <summary>主动道具商品卡。Prefab 可单独调整主动道具的尺寸和位置。</summary>
    public sealed class ShopActiveItemBuyItemView : ShopBuyItemViewBase
    {
        protected override void ConfigureContent(ShopBuyItemViewContext context)
        {
            SetIcon(context?.Icon);
        }
    }
}
