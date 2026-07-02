using System;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Flow;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using UnityEngine;
using UnityEngine.UI;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 周地图「n 选一」事件卡视图。固定结构在 WeekEventCardView.prefab，
    /// 文案与点击回调通过 <see cref="Bind"/> 数据驱动填充。
    /// </summary>
    public sealed class WeekEventCardView : MonoBehaviour
    {
        [SerializeField] private Text _nameText;
        [SerializeField] private Text _descText;
        [SerializeField] private Text _timeText;
        [SerializeField] private Image _artImage;
        [SerializeField] private Image _rewardBadgeImage;
        [SerializeField] private Text _rewardBadgeText;
        [SerializeField] private Button _pickButton;

        public void Bind(cfg.GameEvent ev, Action onPick)
        {
            Bind(ev.Name, ev.Desc, ev.TimeCost, onPick);
            SetArt(Resources.Load<Sprite>("Sprites/UI/card_action_event"));
            SetRewardBadge(false);
        }

        /// <summary>行动轴「n 选一行动」绑定。</summary>
        public void Bind(cfg.GameAction action, Action onPick)
        {
            Bind(action.Name, action.Desc, action.CostDays, onPick);
            SetArt(CardSpriteFor(action));
            SetRewardBadge(action.ActionType == cfg.ActionType.Food, action.RewardKind);
        }

        /// <summary>行动组候选绑定，使用本次选择快照中的耗时。</summary>
        public void Bind(ActionChoice choice, Action onPick)
        {
            if (choice == null || choice.Action == null)
            {
                Bind(string.Empty, string.Empty, 0, onPick);
                return;
            }

            string groupName = choice.Group == null ? string.Empty : $"[{choice.Group.Name}] ";
            string desc = $"{groupName}{choice.Action.Desc}\n奖励：{RewardKindText(choice.Action.RewardKind)} · 难度：{choice.Action.FoodDifficulty}";
            Bind(choice.Action.Name, desc, choice.CostDays, onPick);
            SetArt(CardSpriteFor(choice.Action));
            SetRewardBadge(choice.Action.ActionType == cfg.ActionType.Food, choice.Action.RewardKind);
        }

        public void Bind(string name, string desc, int costDays, Action onPick)
        {
            _nameText.text = name;
            _descText.text = desc;
            _timeText.text = $"耗时 {costDays} 天";

            _pickButton.onClick.RemoveAllListeners();
            _pickButton.onClick.AddListener(() => onPick?.Invoke());
        }

        private void SetArt(Sprite sprite)
        {
            if (_artImage == null)
            {
                return;
            }

            _artImage.enabled = sprite != null;
            _artImage.sprite = sprite;
        }

        private void SetRewardBadge(bool visible, cfg.RewardKind kind = default)
        {
            if (_rewardBadgeImage != null)
            {
                _rewardBadgeImage.gameObject.SetActive(visible);
            }

            if (_rewardBadgeText != null)
            {
                _rewardBadgeText.gameObject.SetActive(visible);
                _rewardBadgeText.text = visible ? "!" : string.Empty;
            }
        }

        private static Sprite CardSpriteFor(cfg.GameAction action)
        {
            if (action == null)
            {
                return null;
            }

            string spriteName;
            switch (action.ActionType)
            {
                case cfg.ActionType.Food:
                    spriteName = action.FoodDifficulty == "Hard"
                        ? "card_action_food_hard"
                        : FoodRewardSpriteName(action.RewardKind);
                    break;
                case cfg.ActionType.Event:
                    spriteName = "card_action_event";
                    break;
                case cfg.ActionType.Reward:
                    spriteName = "card_action_reward";
                    break;
                case cfg.ActionType.Negative:
                    spriteName = "card_action_negative";
                    break;
                case cfg.ActionType.Shop:
                    spriteName = "card_action_shop";
                    break;
                default:
                    spriteName = "card_action_event";
                    break;
            }

            return Resources.Load<Sprite>($"Sprites/UI/{spriteName}");
        }

        private static string FoodRewardSpriteName(cfg.RewardKind kind)
        {
            switch (kind)
            {
                case cfg.RewardKind.Gold: return "card_action_food_gold";
                case cfg.RewardKind.FragmentChoice: return "card_action_food_fragment";
                case cfg.RewardKind.PassiveItemChoice: return "card_action_food_passive";
                case cfg.RewardKind.ActiveItemGrant: return "card_action_food_active";
                case cfg.RewardKind.DishChoice: return "card_action_food_dish";
                default: return "card_action_food_dish";
            }
        }

        private static string RewardKindText(cfg.RewardKind kind)
        {
            switch (kind)
            {
                case cfg.RewardKind.DishChoice: return "菜品";
                case cfg.RewardKind.PassiveItemChoice: return "被动道具";
                case cfg.RewardKind.ActiveItemGrant: return "主动道具";
                case cfg.RewardKind.FragmentChoice: return "胃部碎片";
                case cfg.RewardKind.Gold: return "金币";
                default: return kind.ToString();
            }
        }
    }
}
