using System;
using GourmetProject.Game;
using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Model;
using UnityEngine;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 旧版通用购买卡的兼容组件。新商店请使用 ShopFood/Passive/Active/FragmentPack 四个具体子类 prefab。
    /// </summary>
    public sealed class ShopBuyCardView : ShopBuyItemViewBase
    {
        [SerializeField] private DishIconRenderTexturePreview _dishIconPreview;

        public void Bind(ItemDefinition item, int price, bool affordable, Action onBuy)
        {
            Sprite icon = ContentIconLoader.LoadItem(item);
            var entry = new ShopEntry(ShopEntryKind.PassiveItem, item?.Id, item?.Name, item?.Desc, price, price);
            Bind(new ShopBuyItemViewContext(
                null,
                entry,
                affordable,
                icon,
                null,
                (_, _) =>
                {
                    onBuy?.Invoke();
                    return true;
                }));
        }

        public void Bind(
            string name,
            string desc,
            int price,
            bool affordable,
            Sprite icon,
            Func<ShopBuyCardView, bool> onBuy,
            Action<ShopBuyCardView> onTargetPointerDown = null,
            Action<ShopBuyCardView, Vector2> onTargetPointerUp = null)
        {
            var entry = new ShopEntry(
                ShopEntryKind.PassiveItem,
                "legacy_shop_card",
                name,
                desc,
                price,
                price);
            Bind(new ShopBuyItemViewContext(
                null,
                entry,
                affordable,
                icon,
                null,
                (_, view) => onBuy?.Invoke((ShopBuyCardView)view) == true,
                onTargetPointerDown == null ? null : (view, _) => onTargetPointerDown.Invoke((ShopBuyCardView)view),
                onTargetPointerUp == null ? null : (view, _, screenPoint) => onTargetPointerUp.Invoke((ShopBuyCardView)view, screenPoint)));
        }

        public void BindDish(
            string name,
            string desc,
            int price,
            bool affordable,
            Sprite icon,
            DishDef dish,
            Func<ShopBuyCardView, bool> onBuy,
            Action<ShopBuyCardView> onTargetPointerDown = null,
            Action<ShopBuyCardView, Vector2> onTargetPointerUp = null)
        {
            var entry = new ShopEntry(ShopEntryKind.Dish, dish?.Id, name, desc, price, price);
            Bind(new ShopBuyItemViewContext(
                null,
                entry,
                affordable,
                icon,
                dish,
                (_, view) => onBuy?.Invoke((ShopBuyCardView)view) == true,
                onTargetPointerDown == null ? null : (view, _) => onTargetPointerDown.Invoke((ShopBuyCardView)view),
                onTargetPointerUp == null ? null : (view, _, screenPoint) => onTargetPointerUp.Invoke((ShopBuyCardView)view, screenPoint)));
        }

        protected override void ConfigureContent(ShopBuyItemViewContext context)
        {
            SetIcon(context?.Icon);
            if (context?.Dish == null)
            {
                _dishIconPreview?.Hide();
                return;
            }

            _dishIconPreview?.Bind(
                DishPreviewRequest.FromDefinition(context.Dish, context.Icon));
            UseIconAsHitTargetOnly();
        }
    }
}
