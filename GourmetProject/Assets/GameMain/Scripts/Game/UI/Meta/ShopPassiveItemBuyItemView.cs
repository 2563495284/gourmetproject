namespace GourmetProject.Game.UI.Meta
{
    /// <summary>被动道具商品卡。Prefab 可单独调整被动道具的尺寸和位置。</summary>
    public sealed class ShopPassiveItemBuyItemView : ShopBuyItemViewBase
    {
        protected override void ConfigureContent(ShopBuyItemViewContext context)
        {
            SetIcon(context?.Icon);
        }
    }
}
