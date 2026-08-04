namespace GourmetProject.Game.UI.Meta
{
    /// <summary>装饰品商品卡。Prefab 可单独调整装饰品的尺寸和位置。</summary>
    public sealed class ShopPassiveItemBuyItemView : ShopBuyItemViewBase
    {
        protected override void ConfigureContent(ShopBuyItemViewContext context)
        {
            SetIcon(context?.Icon);
        }
    }
}
