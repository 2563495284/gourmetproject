using GourmetProject.Game.UI.Tooltips;
using UnityEngine;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 经营挑战界面 hover Tip 注册表：持有装饰品和消耗品、时间轴和食物 Tip 预制体，
    /// 惰性实例化到界面根下并缓存，界面关闭时统一隐藏。
    /// </summary>
    public sealed class BattleTipRegistry : MonoBehaviour
    {
        private const string Tag = "Battle";

        [SerializeField] private ItemTipView _itemTipPrefab;
        [SerializeField] private TimelineNodeTipView _timelineNodeTipPrefab;
        [SerializeField] private FoodTipsView _foodTipsPrefab;

        private ItemTipView _itemTip;
        private TimelineNodeTipView _timelineNodeTip;
        private FoodTipsView _foodTip;

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

        public TimelineNodeTipView Timeline
        {
            get
            {
                if (_timelineNodeTip == null)
                {
                    _timelineNodeTip = Create(_timelineNodeTipPrefab, "TimelineNodeTipView_Runtime");
                }

                return _timelineNodeTip;
            }
        }

        public FoodTipsView Food
        {
            get
            {
                if (_foodTip == null)
                {
                    _foodTip = CreateFood(_foodTipsPrefab, "FoodTipsView_Runtime");
                }

                return _foodTip;
            }
        }

        /// <summary>提前实例化全部 Tip（进场时预热，避免首次 hover 卡顿）。</summary>
        public void EnsureAll()
        {
            _ = Item;
            _ = Timeline;
            _ = Food;
        }

        public void HideAll()
        {
            _itemTip?.Hide();
            _timelineNodeTip?.Hide();
            _foodTip?.Hide();
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

        private FoodTipsView CreateFood(FoodTipsView prefab, string viewName)
        {
            if (prefab == null)
            {
                Log.Warning($"{viewName} prefab is not assigned on BattleTipRegistry.", Tag);
                return null;
            }

            FoodTipsView view = Instantiate(prefab, transform, false);
            view.gameObject.name = viewName;
            view.Hide();
            return view;
        }
    }
}
