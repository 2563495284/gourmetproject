using UnityEngine.Scripting;

namespace GourmetProject.Game.Meta.Passives
{
    [Preserve]
    [PassiveItemModel("item_discount_food")]
    [PassiveItemModel("item_discount_food_festival")]
    public sealed class DiscountFoodModel : ShopDiscountKindModel
    {
        public DiscountFoodModel() : base(ShopEntryKind.Dish)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_discount_fragment")]
    [PassiveItemModel("item_discount_fragment_festival")]
    public sealed class DiscountFragmentModel : ShopDiscountKindModel
    {
        public DiscountFragmentModel() : base(ShopEntryKind.Fragment)
        {
        }
    }

    public abstract class ActiveItemCategoryDiscountModel : PassiveItemModel
    {
        private readonly cfg.ActiveItemCategory _category;

        protected ActiveItemCategoryDiscountModel(cfg.ActiveItemCategory category)
        {
            _category = category;
        }

        public override float ModifyShopPrice(ShopEntryKind kind, float price)
        {
            // 旧重载没有商品 ID，只能维持历史上“任意消耗品均打折”的兼容语义。
            return kind == ShopEntryKind.ActiveItem ? Discount(price) : price;
        }

        public override float ModifyShopPrice(ShopEntryKind kind, string itemId, float price)
        {
            if (kind != ShopEntryKind.ActiveItem)
            {
                return price;
            }

            if (string.IsNullOrEmpty(itemId))
            {
                return Discount(price);
            }

            ItemDefinition item = Run != null
                ? ItemDefinition.Get(Run.Tables, itemId, cfg.ItemKind.Active)
                : null;
            return item != null && item.ActiveItemCategory == _category
                ? Discount(price)
                : price;
        }

        private float Discount(float price)
        {
            float discount = Value;
            return discount > 0f && discount < 1f
                ? price * (1f - discount)
                : price;
        }
    }

    [Preserve]
    [PassiveItemModel("item_discount_active")]
    [PassiveItemModel("item_discount_active_festival")]
    public sealed class DiscountActiveModel : ActiveItemCategoryDiscountModel
    {
        public DiscountActiveModel() : base(cfg.ActiveItemCategory.Strengthen)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_discount_adjust")]
    [PassiveItemModel("item_discount_adjust_festival")]
    public sealed class DiscountAdjustModel : ActiveItemCategoryDiscountModel
    {
        public DiscountAdjustModel() : base(cfg.ActiveItemCategory.Adjust)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_discount_passive")]
    [PassiveItemModel("item_discount_passive_festival")]
    public sealed class DiscountPassiveModel : ShopDiscountKindModel
    {
        public DiscountPassiveModel() : base(ShopEntryKind.PassiveItem)
        {
        }
    }

    /// <summary>删牌折扣：走 ModifyDeletePrice。</summary>
    [Preserve]
    [PassiveItemModel("item_discount_remove")]
    [PassiveItemModel("item_discount_remove_festival")]
    public sealed class DiscountRemoveModel : PassiveItemModel
    {
        public override float ModifyDeletePrice(float price)
        {
            float d = Value;
            return d > 0f && d < 1f ? price * (1f - d) : price;
        }
    }

    /// <summary>删牌固定价（取最低）。</summary>
    [Preserve]
    [PassiveItemModel("item_remove_fixed_30")]
    public sealed class RemovePriceFixedModel : PassiveItemModel
    {
        public override bool TryGetRemovePriceFixed(out int fixedPrice)
        {
            fixedPrice = (int)Value;
            return true;
        }
    }

    /// <summary>负面「涨价」：所有价格通道 ×(1+Value)。</summary>
    [Preserve]
    [PassiveItemModel("item_shop_price_up")]
    public sealed class ShopPriceUpModel : PassiveItemModel
    {
        public override float ModifyShopPrice(ShopEntryKind kind, float price) => Up(price);

        public override float ModifyDeletePrice(float price) => Up(price);

        private float Up(float price) => Value > 0f ? price * (1f + Value) : price;
    }

    /// <summary>负面「囤积癖」：禁止删除食物。</summary>
    [Preserve]
    [PassiveItemModel("item_no_remove")]
    public sealed class NoRemoveDishModel : PassiveItemModel
    {
        public override bool BlockRemoveDish() => true;
    }

    /// <summary>商店自动补货。</summary>
    [Preserve]
    [PassiveItemModel("item_shop_restock")]
    public sealed class ShopRestockModel : PassiveItemModel
    {
        public override bool AutoRestock(ShopEntryKind kind) => kind == ShopEntryKind.Dish;
    }

    [Preserve]
    [PassiveItemModel("item_shop_restock_active")]
    public sealed class ActiveItemRestockModel : PassiveItemModel
    {
        public override bool AutoRestock(ShopEntryKind kind) => kind == ShopEntryKind.ActiveItem;
    }

    [Preserve]
    [PassiveItemModel("item_shop_restock_passive")]
    public sealed class PassiveItemRestockModel : PassiveItemModel
    {
        public override bool AutoRestock(ShopEntryKind kind) => kind == ShopEntryKind.PassiveItem;
    }
}
