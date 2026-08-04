namespace GourmetProject.Game.UI.Meta
{
    /// <summary>餐桌格包商品卡。Prefab 可单独调整碎片包图标和购买按钮布局。</summary>
    public sealed class ShopFragmentPackBuyItemView : ShopBuyItemViewBase
    {
        protected override void ConfigureContent(ShopBuyItemViewContext context)
        {
            SetIcon(context?.Icon);
        }
    }
}
