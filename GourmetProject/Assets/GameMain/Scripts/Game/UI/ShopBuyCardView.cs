using System;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 商店「进货」单张被动道具卡视图。固定结构在 ShopBuyCardView.prefab，
    /// 道具信息、价格与可购买状态由 <see cref="Bind"/> 数据驱动填充。
    /// </summary>
    public sealed class ShopBuyCardView : MonoBehaviour
    {
        [SerializeField] private Text _nameText;
        [SerializeField] private Text _descText;
        [SerializeField] private Button _buyButton;

        public void Bind(cfg.Item item, int price, bool affordable, Action onBuy)
        {
            Bind(item.Name, item.Desc, price, affordable, onBuy);
        }

        /// <summary>通用商品绑定（被动道具 / 菜品 / 胃部碎片）。</summary>
        public void Bind(string name, string desc, int price, bool affordable, Action onBuy)
        {
            _nameText.text = name;
            _descText.text = desc;

            Text label = _buyButton.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = $"购买 {price}";
            }

            _buyButton.interactable = affordable;
            _buyButton.onClick.RemoveAllListeners();
            _buyButton.onClick.AddListener(() => onBuy?.Invoke());
        }
    }
}
