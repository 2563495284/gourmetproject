using System;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 商店「出售」单张已持有被动道具卡视图。固定结构在 ShopSellCardView.prefab，
    /// 道具名与售价由 <see cref="Bind"/> 数据驱动填充。
    /// </summary>
    public sealed class ShopSellCardView : MonoBehaviour
    {
        [SerializeField] private Text _nameText;
        [SerializeField] private Button _sellButton;

        public void Bind(cfg.Item item, int price, Action onSell)
        {
            _nameText.text = item.Name;

            Text label = _sellButton.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = $"卖 +{price}";
            }

            _sellButton.onClick.RemoveAllListeners();
            _sellButton.onClick.AddListener(() => onSell?.Invoke());
        }
    }
}
