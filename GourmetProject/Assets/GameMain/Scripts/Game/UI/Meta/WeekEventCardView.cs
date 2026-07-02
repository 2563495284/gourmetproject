using System;
using System.Globalization;
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
        private const string NodeEventFooter = "节点事件";
        private const string DefaultBossTitle = "周末盛宴\n恶魔";

        private static readonly Color PanelColor = new Color(1f, 0.94f, 0.78f, 0.9f);
        private static readonly Color FooterActionColor = new Color(1f, 0.94f, 0.78f, 0.92f);
        private static readonly Color FooterNodeColor = new Color(0.78f, 1f, 0.88f, 0.92f);

        [SerializeField] private Text _nameText;
        [SerializeField] private Text _descText;
        [SerializeField] private Text _timeText;
        [SerializeField] private Image _artImage;
        [SerializeField] private Image _titleBackingImage;
        [SerializeField] private Image _descBackingImage;
        [SerializeField] private Image _footerBackingImage;
        [SerializeField] private Image _rewardBadgeImage;
        [SerializeField] private Image _rewardIconImage;
        [SerializeField] private Text _rewardBadgeText;
        [SerializeField] private Button _pickButton;

        public void Bind(cfg.GameEvent ev, Action onPick)
        {
            BindNodeCard(ev?.Name ?? "事件", ev?.Desc ?? string.Empty, "card_action_event", onPick);
        }

        public void Bind(cfg.TimelineNode node, Action onPick)
        {
            if (node == null)
            {
                BindNodeCard("事件", string.Empty, "card_action_event", onPick);
                return;
            }

            switch (node.NodeType)
            {
                case cfg.TimelineNodeType.Shop:
                    BindNodeCard("商店", string.Empty, "card_node_shop", onPick);
                    break;
                case cfg.TimelineNodeType.Interest:
                    int threshold = Mathf.Max(0, Mathf.RoundToInt(node.PayloadValue));
                    int goldPer = int.TryParse(node.PayloadParam, out int parsedGoldPer) ? parsedGoldPer : 1;
                    BindInterestNode(threshold, goldPer, null, onPick);
                    break;
                case cfg.TimelineNodeType.Boss:
                    BindNodeCard(DefaultBossTitle, string.Empty, "card_node_boss", onPick);
                    break;
                case cfg.TimelineNodeType.Event:
                    BindNodeCard("事件", string.Empty, "card_action_event", onPick);
                    break;
                default:
                    BindNodeCard("事件", string.Empty, "card_action_event", onPick);
                    break;
            }
        }

        public void BindInterestNode(int threshold, int goldPer, int? maxGain, Action onPick)
        {
            string desc = maxGain.HasValue
                ? $"每有{threshold}枚金币，获得{goldPer}枚，最高可获得{maxGain.Value}枚"
                : $"每有{threshold}枚金币，获得{goldPer}枚";
            BindNodeCard("收取利息", desc, "card_node_interest", onPick);
        }

        public void BindBossNode(cfg.Boss boss, string mechanicDesc, long requiredScore, Action onPick)
        {
            string bossName = string.IsNullOrWhiteSpace(boss?.Name) ? "恶魔" : boss.Name;
            string desc = mechanicDesc ?? string.Empty;
            if (requiredScore > 0)
            {
                string scoreLine = $"美味度要求：{requiredScore.ToString("N0", CultureInfo.InvariantCulture)}";
                desc = string.IsNullOrWhiteSpace(desc) ? scoreLine : $"{desc}\n{scoreLine}";
            }

            BindNodeCard($"周末盛宴\n{bossName}", desc, "card_node_boss", onPick);
        }

        public void BindNodeCard(string title, string desc, string artSpriteName, Action onPick)
        {
            ApplyCommon(title, desc, onPick);
            SetFooter(NodeEventFooter, true, FooterNodeColor);
            SetArt(Resources.Load<Sprite>("Sprites/UI/card_action_event"));
            SetArtByName(artSpriteName);
            SetRewardBadge(false);
        }

        /// <summary>行动轴「n 选一行动」绑定。</summary>
        public void Bind(cfg.GameAction action, Action onPick)
        {
            if (action == null)
            {
                Bind(string.Empty, string.Empty, 0, onPick);
                SetRewardBadge(false);
                return;
            }

            Bind(action.Name, string.Empty, action.CostDays, onPick);
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

            Bind(choice.Action.Name, string.Empty, choice.CostDays, onPick);
            SetArt(CardSpriteFor(choice.Action));
            SetRewardBadge(choice.Action.ActionType == cfg.ActionType.Food, choice.Action.RewardKind);
        }

        public void Bind(string name, string desc, float costDays, Action onPick)
        {
            ApplyCommon(name, desc, onPick);
            SetFooter($"用时：{costDays.ToString("0.#", CultureInfo.InvariantCulture)}天", true, FooterActionColor);
        }

        private void ApplyCommon(string title, string desc, Action onPick)
        {
            SetText(_nameText, title);
            SetBacking(_titleBackingImage, true, PanelColor);
            SetDescription(desc);

            if (_pickButton != null)
            {
                _pickButton.onClick.RemoveAllListeners();
                _pickButton.onClick.AddListener(() => onPick?.Invoke());
            }
        }

        private void SetDescription(string desc)
        {
            bool visible = !string.IsNullOrWhiteSpace(desc);
            SetText(_descText, desc);
            if (_descText != null)
            {
                _descText.gameObject.SetActive(visible);
            }

            SetBacking(_descBackingImage, visible, PanelColor);
        }

        private void SetFooter(string text, bool visible, Color backingColor)
        {
            SetText(_timeText, text);
            if (_timeText != null)
            {
                _timeText.gameObject.SetActive(visible);
            }

            SetBacking(_footerBackingImage, visible, backingColor);
        }

        private static void SetText(Text text, string value)
        {
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }

        private static void SetBacking(Image image, bool visible, Color color)
        {
            if (image == null)
            {
                return;
            }

            image.gameObject.SetActive(visible);
            image.color = color;
        }

        private void SetArtByName(string spriteName)
        {
            if (!string.IsNullOrWhiteSpace(spriteName))
            {
                SetArt(Resources.Load<Sprite>($"Sprites/UI/{spriteName}"));
            }
        }

        private void SetArt(Sprite sprite)
        {
            if (_artImage == null)
            {
                return;
            }

            _artImage.enabled = sprite != null;
            _artImage.sprite = sprite;
            _artImage.color = Color.white;
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

            if (_rewardIconImage != null)
            {
                _rewardIconImage.gameObject.SetActive(visible);
                _rewardIconImage.sprite = visible ? RewardIconFor(kind) : null;
                _rewardIconImage.color = Color.white;
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

        private static Sprite RewardIconFor(cfg.RewardKind kind)
        {
            string spriteName;
            switch (kind)
            {
                case cfg.RewardKind.Gold:
                    spriteName = "icon_coin";
                    break;
                case cfg.RewardKind.FragmentChoice:
                    spriteName = "ui_icon_shop_fragment";
                    break;
                case cfg.RewardKind.PassiveItemChoice:
                    spriteName = "ui_icon_shop_passive";
                    break;
                case cfg.RewardKind.ActiveItemGrant:
                    spriteName = "ui_icon_shop_active";
                    break;
                case cfg.RewardKind.DishChoice:
                    spriteName = "ui_icon_shop_food";
                    break;
                default:
                    spriteName = "ui_icon_shop_food";
                    break;
            }

            return Resources.Load<Sprite>($"Sprites/UI/{spriteName}");
        }

    }
}
