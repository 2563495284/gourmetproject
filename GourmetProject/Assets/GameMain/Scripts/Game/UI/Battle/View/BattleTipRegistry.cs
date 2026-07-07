using GourmetProject.Game.UI.Tooltips;
using UnityEngine;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 战斗界面 hover Tip 注册表：持有 4 类 Tip 预制体，惰性实例化到界面根下并缓存。
    /// 供右栏道具（ItemTip）与行动轴（Shop/Interest/Boss 节点 Tip）按需取用，界面关闭时统一隐藏。
    /// </summary>
    public sealed class BattleTipRegistry : MonoBehaviour
    {
        private const string Tag = "Battle";

        [SerializeField] private ItemTipView _itemTipPrefab;
        [SerializeField] private ShopNodeTipView _shopNodeTipPrefab;
        [SerializeField] private InterestNodeTipView _interestNodeTipPrefab;
        [SerializeField] private BossFeastTipView _bossFeastTipPrefab;

        private ItemTipView _itemTip;
        private ShopNodeTipView _shopTip;
        private InterestNodeTipView _interestTip;
        private BossFeastTipView _bossTip;

        public ItemTipView Item
        {
            get
            {
                if (_itemTip == null)
                {
                    _itemTip = Create(_itemTipPrefab, "ItemTipView_Runtime");
                }

                return _itemTip;
            }
        }

        public ShopNodeTipView Shop
        {
            get
            {
                if (_shopTip == null)
                {
                    _shopTip = Create(_shopNodeTipPrefab, "ShopNodeTipView_Runtime");
                }

                return _shopTip;
            }
        }

        public InterestNodeTipView Interest
        {
            get
            {
                if (_interestTip == null)
                {
                    _interestTip = Create(_interestNodeTipPrefab, "InterestNodeTipView_Runtime");
                }

                return _interestTip;
            }
        }

        public BossFeastTipView Boss
        {
            get
            {
                if (_bossTip == null)
                {
                    _bossTip = Create(_bossFeastTipPrefab, "BossFeastTipView_Runtime");
                }

                return _bossTip;
            }
        }

        /// <summary>提前实例化全部 Tip（进场时预热，避免首次 hover 卡顿）。</summary>
        public void EnsureAll()
        {
            _ = Item;
            _ = Shop;
            _ = Interest;
            _ = Boss;
        }

        public void HideAll()
        {
            _itemTip?.Hide();
            _shopTip?.Hide();
            _interestTip?.Hide();
            _bossTip?.Hide();
        }

        private T Create<T>(T prefab, string viewName) where T : ActionTipView
        {
            if (prefab == null)
            {
                Log.Warning($"{viewName} prefab is not assigned on BattleTipRegistry.", Tag);
                return null;
            }

            T view = Instantiate(prefab, transform, false);
            view.gameObject.name = viewName;
            view.Hide();
            return view;
        }
    }
}
