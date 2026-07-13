using UnityEngine.Scripting;

namespace GourmetProject.Game.Meta.Passives
{
    [Preserve]
    [PassiveItemModel("item_discount_food")]
    public sealed class DiscountFoodModel : ShopDiscountKindModel
    {
        public DiscountFoodModel() : base(ShopEntryKind.Dish)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_discount_fragment")]
    public sealed class DiscountFragmentModel : ShopDiscountKindModel
    {
        public DiscountFragmentModel() : base(ShopEntryKind.Fragment)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_discount_active")]
    public sealed class DiscountActiveModel : ShopDiscountKindModel
    {
        public DiscountActiveModel() : base(ShopEntryKind.ActiveItem)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_discount_passive")]
    public sealed class DiscountPassiveModel : ShopDiscountKindModel
    {
        public DiscountPassiveModel() : base(ShopEntryKind.PassiveItem)
        {
        }
    }

    /// <summary>菜谱本购买折扣：走 ModifyRecipeBookPrice。</summary>
    [Preserve]
    [PassiveItemModel("item_discount_recipe")]
    public sealed class DiscountRecipeModel : PassiveItemModel
    {
        public override float ModifyRecipeBookPrice(float price)
        {
            float d = Value;
            return d > 0f && d < 1f ? price * (1f - d) : price;
        }
    }

    /// <summary>删牌折扣：走 ModifyDeletePrice。</summary>
    [Preserve]
    [PassiveItemModel("item_discount_remove")]
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

        public override float ModifyRecipeBookPrice(float price) => Up(price);

        public override float ModifyDeletePrice(float price) => Up(price);

        private float Up(float price) => Value > 0f ? price * (1f + Value) : price;
    }

    /// <summary>负面「囤积癖」：禁止删除菜品。</summary>
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
        public override bool AutoRestock() => true;
    }
}
