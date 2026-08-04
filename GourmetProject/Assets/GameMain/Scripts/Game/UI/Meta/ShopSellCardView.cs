using System;
using GourmetProject.Game.Meta;
using UnityEngine;
using UnityEngine.UI;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;
using TMPro;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 商店「处理」单张已持有装饰品和消耗品/食谱食物卡视图。固定结构在 ShopSellCardView.prefab，
    /// 装饰品和消耗品名与售价由 <see cref="Bind"/> 数据驱动填充。
    /// </summary>
    public sealed class ShopSellCardView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private Button _sellButton;

        public void Bind(ItemDefinition item, int price, Action onSell)
        {
            Bind(item.Name, $"卖 +{price}", onSell);
        }

        /// <summary>通用「处理」绑定（出售装饰品和消耗品 / 食谱管理删除食物）。</summary>
        public void Bind(string name, string buttonLabel, Action onClick)
        {
            _nameText.text = name;

            TMP_Text label = _sellButton.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.text = buttonLabel;
            }

            _sellButton.onClick.RemoveAllListeners();
            _sellButton.onClick.AddListener(() => onClick?.Invoke());
        }
    }
}
