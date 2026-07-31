using GourmetProject.Game.UI.Widgets;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>食物商品卡。Prefab 可单独调整食物图标、占格预览和拖拽命中区域。</summary>
    public sealed class ShopFoodBuyItemView : ShopBuyItemViewBase
    {
        [SerializeField] private DishIconRenderTexturePreview _dishIconPreview;

        public override RectTransform PurchaseFlySource =>
            _dishIconPreview != null
                ? _dishIconPreview.transform as RectTransform
                : base.PurchaseFlySource;

        public override RectTransform TipPlacementTarget =>
            _dishIconPreview != null
                ? _dishIconPreview.transform as RectTransform
                : base.TipPlacementTarget;

        public override RenderTexture CapturePurchaseFlyTexture()
        {
            return _dishIconPreview != null
                ? _dishIconPreview.CopyCurrentTexture()
                : null;
        }

        protected override void ConfigureContent(ShopBuyItemViewContext context)
        {
            SetIcon(context?.Icon);
            RawImage interactionGraphic = null;
            if (_dishIconPreview != null && context?.Dish != null)
            {
                _dishIconPreview.Bind(
                    DishPreviewRequest.FromDefinition(context.Dish, context.Icon));
                _dishIconPreview.SetRaycastTarget(true);
                interactionGraphic = _dishIconPreview.GetComponent<RawImage>();
            }
            else
            {
                _dishIconPreview?.Hide();
            }

            UseIconAsHitTargetOnly();
            UseGraphicAsInteractionFeedback(interactionGraphic);
        }
    }
}
