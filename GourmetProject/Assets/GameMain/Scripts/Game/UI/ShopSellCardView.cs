using System;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 商店「处理」单张已持有道具/菜谱菜品卡视图。固定结构在 ShopSellCardView.prefab，
    /// 道具名与售价由 <see cref="Bind"/> 数据驱动填充。
    /// </summary>
    public sealed class ShopSellCardView : MonoBehaviour
    {
        [SerializeField] private Text _nameText;
        [SerializeField] private Button _sellButton;

        public void Bind(cfg.Item item, int price, Action onSell)
        {
            Bind(item.Name, $"卖 +{price}", onSell);
        }

        /// <summary>通用「处理」绑定（出售道具 / 菜谱管理删除菜品）。</summary>
        public void Bind(string name, string buttonLabel, Action onClick)
        {
            _nameText.text = name;

            Text label = _sellButton.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = buttonLabel;
            }

            _sellButton.onClick.RemoveAllListeners();
            _sellButton.onClick.AddListener(() => onClick?.Invoke());
        }
    }
}
