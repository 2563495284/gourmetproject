using System;
using System.Globalization;
using DG.Tweening;
using GameStartStudio.UI;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Flow;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Game.UI.Widgets;
using TMPro;

namespace GourmetProject.Game.UI.Meta
{
    internal readonly struct WeekEventTitleAnimation
    {
        public WeekEventTitleAnimation(TmpTextAnimationPreset preset, float intensity, float speed)
        {
            Preset = preset;
            Intensity = intensity;
            Speed = speed;
        }

        public TmpTextAnimationPreset Preset { get; }
        public float Intensity { get; }
        public float Speed { get; }
    }

    internal readonly struct WeekEventCardPresentation
    {
        public WeekEventCardPresentation(
            WeekEventTitleAnimation titleAnimation,
            Color titleColor,
            bool isHotBusiness,
            bool isStarEvaluation)
        {
            TitleAnimation = titleAnimation;
            TitleColor = titleColor;
            IsHotBusiness = isHotBusiness;
            IsStarEvaluation = isStarEvaluation;
        }

        public WeekEventTitleAnimation TitleAnimation { get; }
        public Color TitleColor { get; }
        public bool IsHotBusiness { get; }
        public bool IsStarEvaluation { get; }
    }

    /// <summary>
    /// 周地图「n 选一」事件卡视图。固定结构在 WeekEventCardView.prefab，
    /// 文案与点击回调通过 <see cref="Bind"/> 数据驱动填充。
    /// </summary>
    public sealed class WeekEventCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private enum CardSkin
        {
            Event,
            Node,
        }

        private const string NodeEventFooter = "节点行动";
        private const string EventBodyPath = "Sprites/UI/WeekEventCards/card_choice_event_body";
        private const string NodeBodyPath = "Sprites/UI/WeekEventCards/card_choice_node_body";
        private const string EventTitlePath = "Sprites/UI/WeekEventCards/card_choice_event_title";
        private const string NodeTitlePath = "Sprites/UI/WeekEventCards/card_choice_node_title";
        private const string RewardBackingPath = "Sprites/UI/WeekEventCards/card_choice_reward_backing";
        private const string EventFooterPath = "Sprites/UI/WeekEventCards/card_choice_event_footer";
        private const string NodeFooterPath = "Sprites/UI/WeekEventCards/card_choice_node_footer";
        private const string StarBodyPath = "Sprites/UI/WeekEventCards/card_choice_star_event_body";
        private const string StarTitlePath = "Sprites/UI/WeekEventCards/card_choice_star_node_title";
        private const string StarFooterPath = "Sprites/UI/WeekEventCards/card_choice_star_node_footer";
        private const float GlowPadding = 48f;
        private const float DefaultHideDuration = 0.2f;
        private const float DefaultPickEffectHold = 0.5f;
        private const float HalfCostEmphasisDuration = 0.4f;
        private const float RewardDoubleTicketEmphasisDuration = 0.18f;
        private const string RewardDoubleTicketIconPath = "Sprites/Items/active_reroll_action";
        internal const string RewardDoubleTicketText = "待触发";

        private static readonly Color PanelColor = new Color(1f, 0.94f, 0.78f, 0.9f);
        private static readonly Color NodePanelColor = new Color(0.24f, 0.55f, 0.82f, 0.88f);
        private static readonly Color FooterActionColor = new Color(1f, 0.94f, 0.78f, 0.92f);
        private static readonly Color FooterNodeColor = new Color(0.78f, 1f, 0.88f, 0.92f);
        private static readonly Color EventTextColor = new Color32(78, 37, 21, 255);
        private static readonly Color NodeFooterTextColor = Color.white;
        private static readonly Color HotTitleColor = new Color32(200, 74, 34, 255);
        private static readonly Color HotGlowColor = new Color32(255, 106, 37, 255);
        private static readonly Color HotHoverGlowColor = new Color32(255, 177, 59, 255);
        private static readonly Color StarTitleColor = new Color32(243, 211, 138, 255);
        private static readonly Color StarGlowColor = new Color32(255, 183, 35, 255);
        private static readonly Color StarPulseGlowColor = new Color32(196, 70, 255, 255);
        private static readonly Color StarHoverGlowColor = new Color32(255, 242, 164, 255);
        private static readonly Color HalfCostFlashColor = new Color32(255, 231, 214, 255);

        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TmpTextVertexAnimator _nameAnimator;
        [SerializeField] private TMP_Text _descText;
        [SerializeField] private TMP_Text _timeText;

        [Header("Action Cost Discount")]
        [SerializeField] private RectTransform _halfCostRoot;
        [SerializeField] private TMP_Text _halfCostLabelText;
        [SerializeField] private TMP_Text _halfCostOriginalText;
        [SerializeField] private TMP_Text _halfCostEffectiveText;
        [SerializeField] private Image _halfCostStrikeImage;

        [Header("Next Business Reward Double")]
        [SerializeField] private RectTransform _rewardDoubleTicketRoot;
        [SerializeField] private Image _rewardDoubleTicketIconImage;
        [SerializeField] private TMP_Text _rewardDoubleTicketText;

        [SerializeField] private Image _cardBackingImage;
        [SerializeField] private RectTransform _artViewport;
        [SerializeField] private AspectRatioFitter _artAspectFitter;
        [SerializeField] private Image _artImage;
        [SerializeField] private Image _titleBackingImage;
        [SerializeField] private Image _descBackingImage;
        [SerializeField] private Image _footerBackingImage;
        [SerializeField] private Image _rewardBadgeImage;
        [SerializeField] private Image _rewardIconImage;
        [SerializeField] private Button _pickButton;

        [Header("Effects - References")]
        [SerializeField] private Image _glowBorder;
        [SerializeField] private RectTransform _particleContainer;
        [SerializeField] private Image _particleTemplate;
        [SerializeField] private WeekEventCardStarburstGraphic _starburst;

        [Header("Effects - Timing")]
        [SerializeField] private float _showDuration = 0.22f;
        [SerializeField] private float _hideDuration = DefaultHideDuration;
        [SerializeField] private float _selectHold = DefaultPickEffectHold;

        [Header("Effects - Glow")]
        [SerializeField] private Color _hoverColor = Color.blue;
        [SerializeField] private Color _selectedColor = Color.green;
        [SerializeField] private float _glowFadeSpeed = 14f;

        [Header("Effects - Particles")]
        [SerializeField] private int _particleCount = 12;
        [SerializeField] private float _particleLifetime = 0.6f;
        [SerializeField] private float _particleDistance = 60f;
        [SerializeField] private Color _particleColor = new Color(1f, 0.92f, 0.62f, 0.5f);
        [SerializeField] private Vector2 _particleSize = new Vector2(14f, 14f);

        private static bool _picking;
        private Action _onPick;
        private bool _hover;
        private bool _selectedGlow;
        private bool _isHidden;
        private Sprite _particleSprite;
        private Tween _scaleTween;
        private Tween _pickDelayTween;
        private Tween _halfCostTween;
        private Tween _rewardDoubleTicketTween;
        private Material _glowMat;
        private ItemTipView _rewardTip;
        private CardSkin _cardSkin;
        private bool _rewardVisible;
        private bool _hasHalfCostPresentation;
        private bool _rewardDoubleTicketVisible;
        private bool _isHotBusiness;
        private bool _isStarEvaluation;
        private bool _effectsVisible;
        private string _footerStableText = string.Empty;
        private Color _footerStableColor = Color.white;
        private WeekEventCardEmberGraphic _hotEmbers;
        private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");

        // 选中特效停留已在 OnPickClicked 内于回调前播放完毕，退场不再额外等待。
        public float PickEffectHold => 0f;
        public RectTransform CardRect => transform as RectTransform;
        public RectTransform RewardRect => _rewardBadgeImage != null
            ? _rewardBadgeImage.rectTransform
            : transform as RectTransform;

        public void SetRewardTip(ItemTipView rewardTip)
        {
            _rewardTip = rewardTip;
        }

        /// <summary>事件「n 选一」单个选项卡：卡名 = 选项文案，页脚标注为节点行动，无耗时行。</summary>
        public void BindEventOption(string optionText, Action onPick)
        {
            ApplyCommon(
                string.IsNullOrWhiteSpace(optionText) ? "选项" : optionText,
                string.Empty,
                onPick,
                formatTitleAsDescription: true);
            ConfigurePresentation(ActionDisplayKind.Event);
            SetFooter(NodeEventFooter, true, FooterNodeColor);
            SetArt(Resources.Load<Sprite>("Sprites/UI/card_action_event"));
            SetRewardBadge(false);
        }

        public void Bind(cfg.TimelineNode node, Action onPick)
        {
            Bind(node, null, null, null, onPick);
        }

        public void Bind(cfg.TimelineNode node, int? interestMaxGain, Action onPick)
        {
            Bind(node, null, null, interestMaxGain, onPick);
        }

        public void Bind(cfg.TimelineNode node, int? interestThreshold, int? interestGoldPer, int? interestMaxGain, Action onPick)
        {
            cfg.GameAction action = node == null ? null : GameApp.Config.Tables.TbAction.GetOrDefault(node.ActionId);
            if (action == null)
            {
                BindNodeCard("事件", string.Empty, "card_action_event", ActionDisplayKind.Event, null, onPick);
                return;
            }

            ActionDisplayKind displayKind = ActionDisplay.KindOf(action);
            cfg.Food food = ResolveFood(action);
            cfg.FoodActionKind? foodKind = food != null ? food.ActionKind : null;
            string artSpriteName = NodeCardSpriteNameFor(action);
            switch (displayKind)
            {
                case ActionDisplayKind.Shop:
                    BindNodeCard("商店", string.Empty, artSpriteName, displayKind, foodKind, onPick);
                    break;
                case ActionDisplayKind.Interest:
                    int threshold = Mathf.Max(0, interestThreshold ?? GameApp.Config.Tables.TbGameBase.InterestThreshold);
                    int configuredGoldPer = interestGoldPer ?? GameApp.Config.Tables.TbGameBase.InterestGoldPer;
                    int goldPer = configuredGoldPer > 0 ? configuredGoldPer : 1;
                    BindNodeCard("收取利息", string.Empty, artSpriteName, displayKind, foodKind, onPick);
                    break;
                case ActionDisplayKind.Boss:
                    cfg.BossDebuff bossDebuff = BossService.PreviewBossDebuff(GameRunContext.Current, node);
                    BindNodeCard(BossTitle(bossDebuff?.Name), string.Empty, artSpriteName, displayKind, foodKind, onPick);
                    break;
                default:
                    BindNodeCard(
                        string.IsNullOrEmpty(action.Name) ? "事件" : action.Name,
                        string.Empty,
                        artSpriteName,
                        displayKind,
                        foodKind,
                        onPick);
                    break;
            }
        }

        public void BindBossNode(cfg.Food boss, string mechanicDesc, long requiredScore, Action onPick)
        {
            string bossName = string.IsNullOrWhiteSpace(boss?.Name) ? "恶魔" : boss.Name;
            string desc = mechanicDesc ?? string.Empty;
            if (requiredScore > 0)
            {
                string scoreLine = $"美味值要求：{requiredScore.ToString("N0", CultureInfo.InvariantCulture)}";
                desc = string.IsNullOrWhiteSpace(desc) ? scoreLine : $"{desc}\n{scoreLine}";
            }

            BindNodeCard(
                $"周末星级评鉴\n{bossName}",
                desc,
                "card_node_boss",
                ActionDisplayKind.Boss,
                boss != null ? boss.ActionKind : cfg.FoodActionKind.Feast,
                onPick);
        }

        public static string BossTitle(string bossDebuffName)
        {
            return string.IsNullOrWhiteSpace(bossDebuffName)
                ? "星级评鉴"
                : $"星级评鉴\n{bossDebuffName}";
        }

        public void BindNodeCard(string title, string desc, string artSpriteName, Action onPick)
        {
            BindNodeCard(title, desc, artSpriteName, ActionDisplayKind.Event, null, onPick);
        }

        private void BindNodeCard(
            string title,
            string desc,
            string artSpriteName,
            ActionDisplayKind displayKind,
            cfg.FoodActionKind? foodKind,
            Action onPick)
        {
            ApplyCommon(title, desc, onPick);
            ApplyCardSkin(CardSkin.Node);
            ConfigurePresentation(displayKind, foodKind);
            SetFooter(NodeEventFooter, true, FooterNodeColor);
            SetArt(Resources.Load<Sprite>("Sprites/UI/card_action_event"));
            SetArtByName(artSpriteName);
            SetRewardBadge(false);
        }

        /// <summary>时间轴「n 选一行动」绑定。</summary>
        public void Bind(cfg.GameAction action, Action onPick)
        {
            if (action == null)
            {
                Bind(string.Empty, string.Empty, 0, onPick);
                SetRewardBadge(false);
                return;
            }

            Bind(CardName(action), string.Empty, action.MinCostDays, onPick);
            ConfigurePresentationFor(action);
            SetArt(CardSpriteFor(action));
            SetFoodRewardBadge(action);
        }

        /// <summary>行动组候选绑定，使用本次选择快照中的耗时。</summary>
        public void Bind(ActionChoice choice, Action onPick)
        {
            Bind(choice, false, onPick);
        }

        /// <summary>行动组候选绑定，并按本次运行态标记外挂的奖励翻倍票券。</summary>
        public void Bind(ActionChoice choice, bool showRewardDoubleTicket, Action onPick)
        {
            if (choice == null || choice.Action == null)
            {
                Bind(string.Empty, string.Empty, 0, onPick);
                return;
            }

            Bind(CardName(choice.Action), string.Empty, choice.CostDays, onPick);
            SetActionCostFooter(
                choice.CostDays,
                choice.HalfDayBuffApplied,
                choice.CostBeforeHalfDays);
            ConfigurePresentationFor(choice.Action);
            SetArt(CardSpriteFor(choice.Action));
            SetFoodRewardBadge(choice.Action);
            SetNextBusinessRewardDoubleTicket(showRewardDoubleTicket);
        }

        private static cfg.Food ResolveFood(cfg.GameAction action) => FoodService.Resolve(GameApp.Config.Tables, action);

        /// <summary>卡面标题：Food 用食物明细名，其余用行动名。</summary>
        private static string CardName(cfg.GameAction action)
        {
            cfg.Food food = ResolveFood(action);
            return food != null ? food.Name : action.Name;
        }

        private void SetFoodRewardBadge(cfg.GameAction action)
        {
            cfg.Food food = action?.Behavior == cfg.ActionBehavior.Food ? ResolveFood(action) : null;
            cfg.FoodActionKind actionKind = food?.ActionKind ?? cfg.FoodActionKind.Normal;
            SetRewardBadge(
                food != null,
                food?.RewardKind ?? default,
                actionKind,
                action);
        }

        public void Bind(string name, string desc, float costDays, Action onPick)
        {
            ApplyCommon(name, desc, onPick);
            ConfigurePresentation(ActionDisplayKind.Event);
            SetActionCostFooter(costDays, false, costDays);
        }

        private void ApplyCommon(
            string title,
            string desc,
            Action onPick,
            bool formatTitleAsDescription = false)
        {
            ResetHalfCostPresentation();
            SetNextBusinessRewardDoubleTicket(false);
            ApplyCardSkin(CardSkin.Event);
            if (formatTitleAsDescription)
            {
                if (_nameText != null)
                {
                    SemanticDescriptionFormatter.Set(_nameText, title);
                }
            }
            else
            {
                SetText(_nameText, title);
            }
            SetDescription(desc);

            _onPick = onPick;
            if (_pickButton != null)
            {
                _pickButton.onClick.RemoveAllListeners();
                _pickButton.onClick.AddListener(OnPickClicked);
            }
        }

        internal void SetNextBusinessRewardDoubleTicket(bool visible)
        {
            _rewardDoubleTicketVisible = visible;
            KillRewardDoubleTicketTween();

            if (_rewardDoubleTicketRoot == null)
            {
                return;
            }

            _rewardDoubleTicketRoot.gameObject.SetActive(visible);
            _rewardDoubleTicketRoot.localScale = Vector3.one;
            SetText(_rewardDoubleTicketText, RewardDoubleTicketText);

            if (_rewardDoubleTicketIconImage != null)
            {
                if (visible && _rewardDoubleTicketIconImage.sprite == null)
                {
                    _rewardDoubleTicketIconImage.sprite =
                        Resources.Load<Sprite>(RewardDoubleTicketIconPath);
                }

                _rewardDoubleTicketIconImage.color = Color.white;
            }
        }

        private void ConfigurePresentationFor(cfg.GameAction action)
        {
            ActionDisplayKind displayKind = ActionDisplay.KindOf(action);
            cfg.Food food = action != null ? ResolveFood(action) : null;
            ConfigurePresentation(displayKind, food != null ? food.ActionKind : null);
        }

        private void ConfigurePresentation(
            ActionDisplayKind displayKind,
            cfg.FoodActionKind? foodKind = null)
        {
            WeekEventCardPresentation presentation = ResolvePresentation(displayKind, foodKind);
            _isHotBusiness = presentation.IsHotBusiness;
            _isStarEvaluation = presentation.IsStarEvaluation;
            ApplyPresentationSkin();

            if (_nameText != null)
            {
                _nameText.color = presentation.TitleColor;
            }

            EnsurePersistentEffectsReferences();
            _hotEmbers?.SetHot(_isHotBusiness && _effectsVisible && isActiveAndEnabled);
            _starburst?.SetStarEvaluation(_isStarEvaluation && _effectsVisible && isActiveAndEnabled);
            _starburst?.SetHighlighted(_isStarEvaluation && (_hover || _selectedGlow));

            if (_nameAnimator == null && _nameText != null)
            {
                _nameAnimator = _nameText.GetComponent<TmpTextVertexAnimator>();
            }

            if (_nameAnimator == null)
            {
                return;
            }

            WeekEventTitleAnimation animation = presentation.TitleAnimation;
            _nameAnimator.SetPreset(animation.Preset, animation.Intensity, animation.Speed);
            _nameAnimator.Rebuild();
        }

        internal static WeekEventCardPresentation ResolvePresentation(
            ActionDisplayKind displayKind,
            cfg.FoodActionKind? foodKind = null)
        {
            bool isHotBusiness = displayKind == ActionDisplayKind.Food
                && foodKind == cfg.FoodActionKind.Super;
            bool isStarEvaluation = displayKind == ActionDisplayKind.Boss;
            return new WeekEventCardPresentation(
                ResolveTitleAnimation(displayKind, foodKind),
                isStarEvaluation ? StarTitleColor : isHotBusiness ? HotTitleColor : EventTextColor,
                isHotBusiness,
                isStarEvaluation);
        }

        internal static WeekEventTitleAnimation ResolveTitleAnimation(
            ActionDisplayKind displayKind,
            cfg.FoodActionKind? foodKind = null)
        {
            if (displayKind == ActionDisplayKind.Food)
            {
                switch (foodKind)
                {
                    case cfg.FoodActionKind.Super:
                        return new WeekEventTitleAnimation(TmpTextAnimationPreset.HotFood, 1f, 1f);
                    case cfg.FoodActionKind.Feast:
                        return new WeekEventTitleAnimation(TmpTextAnimationPreset.Boss, 1.2f, 1f);
                    default:
                        return new WeekEventTitleAnimation(TmpTextAnimationPreset.Food, 0.75f, 0.83f);
                }
            }

            if (displayKind == ActionDisplayKind.Boss)
            {
                return new WeekEventTitleAnimation(TmpTextAnimationPreset.StarEvaluation, 1.35f, 1.08f);
            }

            TmpTextAnimationPreset preset = displayKind switch
            {
                ActionDisplayKind.Reward => TmpTextAnimationPreset.Reward,
                ActionDisplayKind.Negative => TmpTextAnimationPreset.Negative,
                ActionDisplayKind.Shop => TmpTextAnimationPreset.Shop,
                ActionDisplayKind.Interest => TmpTextAnimationPreset.Interest,
                ActionDisplayKind.Slot => TmpTextAnimationPreset.Slot,
                _ => TmpTextAnimationPreset.Event,
            };
            return new WeekEventTitleAnimation(preset, 1f, 1f);
        }

        private void SetDescription(string desc)
        {
            bool visible = !string.IsNullOrWhiteSpace(desc);
            if (_descText != null)
            {
                SemanticDescriptionFormatter.Set(_descText, desc);
                _descText.gameObject.SetActive(visible);
            }

            SetBacking(_descBackingImage, visible, _cardSkin == CardSkin.Node ? NodePanelColor : PanelColor);
        }

        private void SetFooter(string text, bool visible, Color backingColor)
        {
            _hasHalfCostPresentation = false;
            SetHalfCostRootActive(false);
            _footerStableText = text ?? string.Empty;
            SetText(_timeText, _footerStableText);
            if (_timeText != null)
            {
                _timeText.gameObject.SetActive(visible);
                _timeText.color = _cardSkin == CardSkin.Node ? NodeFooterTextColor : EventTextColor;
            }

            SetFooterBacking(visible, backingColor);
        }

        private void SetFooterBacking(bool visible, Color backingColor)
        {
            if (_footerBackingImage == null)
            {
                return;
            }

            _footerBackingImage.sprite = LoadSkinSprite(
                _isStarEvaluation
                    ? StarFooterPath
                    : _cardSkin == CardSkin.Node ? NodeFooterPath : EventFooterPath);
            _footerBackingImage.type = Image.Type.Sliced;
            _footerStableColor = _footerBackingImage.sprite != null
                ? Color.white
                : backingColor;
            SetBacking(
                _footerBackingImage,
                visible,
                _footerStableColor);
        }

        internal void SetActionCostFooter(
            float costDays,
            bool halfDayBuffApplied,
            float costBeforeHalfDays)
        {
            string text = FormatActionCostFooter(costDays);
            bool visible = !string.IsNullOrEmpty(text);
            bool showHalfCost = visible
                && halfDayBuffApplied
                && HasReducedActionCost(costDays, costBeforeHalfDays);
            if (!showHalfCost || !CanShowHalfCostFooter())
            {
                SetFooter(text, visible, FooterActionColor);
                return;
            }

            _footerStableText = string.Empty;
            _hasHalfCostPresentation = true;
            SetText(_timeText, string.Empty);
            if (_timeText != null)
            {
                _timeText.gameObject.SetActive(false);
            }

            SetHalfCostRootActive(true);
            SetText(_halfCostLabelText, "用时：");
            SetText(_halfCostOriginalText, FormatActionCostDays(costBeforeHalfDays));
            SetText(_halfCostEffectiveText, FormatActionCostDays(costDays));
            UpdateHalfCostStrikeWidth();
            SetFooterBacking(true, FooterActionColor);
        }

        internal static string FormatActionCostFooter(float costDays)
        {
            string costText = FormatActionCostDays(costDays);
            return string.IsNullOrEmpty(costText)
                ? string.Empty
                : $"用时：{costText}";
        }

        internal static string FormatActionCostDays(float costDays)
        {
            float cost = TimelineMath.Quantize(Mathf.Max(0f, costDays));
            if (cost <= 0f)
            {
                return string.Empty;
            }

            return $"{cost.ToString("0.#", CultureInfo.InvariantCulture)}天";
        }

        private static bool HasReducedActionCost(float costDays, float costBeforeHalfDays)
        {
            float effectiveCost = TimelineMath.Quantize(Mathf.Max(0f, costDays));
            float originalCost = TimelineMath.Quantize(Mathf.Max(0f, costBeforeHalfDays));
            return originalCost > effectiveCost;
        }

        private bool CanShowHalfCostFooter()
        {
            return _halfCostRoot != null
                && _halfCostLabelText != null
                && _halfCostOriginalText != null
                && _halfCostEffectiveText != null
                && _halfCostStrikeImage != null;
        }

        private void SetHalfCostRootActive(bool active)
        {
            if (_halfCostRoot != null)
            {
                _halfCostRoot.gameObject.SetActive(active);
            }
        }

        private void UpdateHalfCostStrikeWidth()
        {
            if (_halfCostOriginalText == null || _halfCostStrikeImage == null)
            {
                return;
            }

            _halfCostOriginalText.ForceMeshUpdate(
                ignoreActiveState: true,
                forceTextReparsing: true);
            float availableWidth = Mathf.Max(4f, _halfCostOriginalText.rectTransform.rect.width);
            float preferredWidth = Mathf.Ceil(_halfCostOriginalText.preferredWidth) + 4f;
            float strikeWidth = Mathf.Clamp(preferredWidth, 4f, availableWidth);
            _halfCostStrikeImage.rectTransform.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                strikeWidth);
        }

        private void ApplyCardSkin(CardSkin skin)
        {
            _cardSkin = skin;

            if (_cardBackingImage != null)
            {
                _cardBackingImage.sprite = LoadSkinSprite(
                    skin == CardSkin.Node ? NodeBodyPath : EventBodyPath);
                _cardBackingImage.type = Image.Type.Sliced;
                _cardBackingImage.color = Color.white;
                _cardBackingImage.enabled = _cardBackingImage.sprite != null;
            }

            if (_titleBackingImage != null)
            {
                _titleBackingImage.sprite = LoadSkinSprite(
                    skin == CardSkin.Node ? NodeTitlePath : EventTitlePath);
                _titleBackingImage.type = Image.Type.Sliced;
                _titleBackingImage.color = Color.white;
                _titleBackingImage.enabled = _titleBackingImage.sprite != null;
                _titleBackingImage.gameObject.SetActive(true);
            }

            if (_nameText != null)
            {
                _nameText.color = EventTextColor;
            }

            if (_descBackingImage != null && _descBackingImage.gameObject.activeSelf)
            {
                _descBackingImage.color = skin == CardSkin.Node ? NodePanelColor : PanelColor;
            }

            if (_rewardBadgeImage != null)
            {
                _rewardBadgeImage.sprite = LoadSkinSprite(RewardBackingPath);
                _rewardBadgeImage.type = Image.Type.Sliced;
                _rewardBadgeImage.color = Color.white;
            }

            UpdateArtViewport();
        }

        private static Sprite LoadSkinSprite(string path)
        {
            return Resources.Load<Sprite>(path);
        }

        private void ApplyPresentationSkin()
        {
            if (!_isStarEvaluation)
            {
                return;
            }

            if (_cardBackingImage != null)
            {
                _cardBackingImage.sprite = LoadSkinSprite(StarBodyPath);
                _cardBackingImage.type = Image.Type.Sliced;
                _cardBackingImage.color = Color.white;
                _cardBackingImage.enabled = _cardBackingImage.sprite != null;
            }

            if (_titleBackingImage != null)
            {
                _titleBackingImage.sprite = LoadSkinSprite(StarTitlePath);
                _titleBackingImage.type = Image.Type.Sliced;
                _titleBackingImage.color = Color.white;
                _titleBackingImage.enabled = _titleBackingImage.sprite != null;
            }

            if (_footerBackingImage != null)
            {
                _footerBackingImage.sprite = LoadSkinSprite(StarFooterPath);
                _footerBackingImage.type = Image.Type.Sliced;
                _footerBackingImage.color = Color.white;
            }
        }

        private void UpdateArtViewport()
        {
            if (_artViewport == null)
            {
                return;
            }

            // PSD 的顶部 80px 是标题色带；事件卡有奖励时为奖励槽留出中段空间。
            float bottom = _cardSkin == CardSkin.Event && _rewardVisible ? 0.44f : 0.19f;
            _artViewport.anchorMin = new Vector2(0f, bottom);
            _artViewport.anchorMax = new Vector2(1f, 0.855f);
            _artViewport.offsetMin = Vector2.zero;
            _artViewport.offsetMax = Vector2.zero;
        }

        private static void SetText(TMP_Text text, string value)
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
            if (_artAspectFitter != null && sprite != null && sprite.rect.height > 0f)
            {
                _artAspectFitter.aspectRatio = sprite.rect.width / sprite.rect.height;
            }
        }

        private void SetRewardBadge(
            bool visible,
            cfg.RewardKind kind = default,
            cfg.FoodActionKind actionKind = cfg.FoodActionKind.Normal,
            cfg.GameAction action = null)
        {
            _rewardVisible = visible;
            if (_rewardBadgeImage != null)
            {
                _rewardBadgeImage.gameObject.SetActive(visible);
            }

            if (_rewardIconImage != null)
            {
                _rewardIconImage.gameObject.SetActive(visible);
                _rewardIconImage.sprite = visible ? RewardIconFor(actionKind, kind) : null;
                _rewardIconImage.color = Color.white;
                BindRewardTip(visible, action);
            }

            UpdateArtViewport();
        }

        private void BindRewardTip(bool visible, cfg.GameAction action)
        {
            if (_rewardIconImage == null)
            {
                return;
            }

            TipHoverTrigger legacyTrigger = _rewardIconImage.GetComponent<TipHoverTrigger>();
            legacyTrigger?.ClearTip();

            GameObject triggerObject = _pickButton != null
                ? _pickButton.gameObject
                : _rewardIconImage.gameObject;
            TipHoverTrigger trigger = triggerObject.GetComponent<TipHoverTrigger>();
            if (!visible
                || _rewardTip == null
                || action == null
                || string.IsNullOrWhiteSpace(action.RewardTitle))
            {
                trigger?.ClearTip();
                return;
            }

            if (trigger == null)
            {
                trigger = triggerObject.AddComponent<TipHoverTrigger>();
            }

            trigger.SetTarget(_rewardIconImage.rectTransform);
            trigger.SetHoverRegion(_rewardIconImage.rectTransform);
            trigger.SetTip(
                _rewardTip,
                () => _rewardTip.Bind(action.RewardTitle, action.RewardDesc));
        }

        private static Sprite CardSpriteFor(cfg.GameAction action)
        {
            string spriteName = CardSpriteNameFor(action);
            return string.IsNullOrEmpty(spriteName)
                ? null
                : Resources.Load<Sprite>($"Sprites/UI/{spriteName}");
        }

        internal static string CardSpriteNameFor(cfg.GameAction action)
        {
            if (action == null)
            {
                return string.Empty;
            }

            string configuredSpriteName = ConfiguredActionSpriteNameFor(action.Id);
            if (!string.IsNullOrEmpty(configuredSpriteName))
            {
                return configuredSpriteName;
            }

            string spriteName;
            switch (action.Behavior)
            {
                case cfg.ActionBehavior.Food:
                    if (FoodService.IsBossAction(null, action))
                    {
                        spriteName = "card_node_boss";
                    }
                    else
                    {
                        cfg.Food food = ResolveFood(action);
                        spriteName = FoodRewardSpriteName(
                            food?.ActionKind ?? cfg.FoodActionKind.Normal,
                            food?.RewardKind ?? cfg.RewardKind.Gold);
                    }

                    break;
                case cfg.ActionBehavior.Event:
                    spriteName = "card_action_event";
                    break;
                case cfg.ActionBehavior.Slot:
                    spriteName = "card_action_slot";
                    break;
                case cfg.ActionBehavior.Reward:
                    spriteName = "card_action_reward";
                    break;
                case cfg.ActionBehavior.Negative:
                case cfg.ActionBehavior.Effect:
                    spriteName = "card_action_negative";
                    break;
                case cfg.ActionBehavior.Shop:
                    spriteName = "card_action_shop";
                    break;
                case cfg.ActionBehavior.Interest:
                    spriteName = "card_node_interest";
                    break;
                default:
                    spriteName = "card_action_generic";
                    break;
            }

            return spriteName;
        }

        internal static string NodeCardSpriteNameFor(cfg.GameAction action)
        {
            if (action == null)
            {
                return "card_action_event";
            }

            string configuredSpriteName = ConfiguredActionSpriteNameFor(action.Id);
            if (!string.IsNullOrEmpty(configuredSpriteName))
            {
                return configuredSpriteName;
            }

            switch (ActionDisplay.KindOf(action))
            {
                case ActionDisplayKind.Food:
                    return CardSpriteNameFor(action);
                case ActionDisplayKind.Boss:
                    return "card_node_boss";
                case ActionDisplayKind.Reward:
                    return "card_action_reward";
                case ActionDisplayKind.Negative:
                    return "card_action_negative";
                case ActionDisplayKind.Shop:
                    return "card_action_shop";
                case ActionDisplayKind.Interest:
                    return "card_node_interest";
                case ActionDisplayKind.Slot:
                    return "card_action_slot";
                case ActionDisplayKind.Event:
                default:
                    return "card_action_event";
            }
        }

        internal static string ConfiguredActionSpriteNameFor(string actionId)
        {
            switch (actionId)
            {
                case "act_food_gold":
                    return "card_action_food_normal_gold";
                case "act_food_fragment":
                    return "card_action_food_normal_fragment";
                case "act_food_passive":
                    return "card_action_food_normal_passive";
                case "act_food_active_strengthen":
                    return "card_action_food_normal_active_strengthen";
                case "act_food_active_adjust":
                    return "card_action_food_normal_active_adjust";
                case "act_food_hard_gold":
                    return "card_action_food_hard_gold";
                case "act_food_hard_fragment":
                    return "card_action_food_hard_fragment";
                case "act_food_hard_passive":
                    return "card_action_food_hard_passive";
                case "act_food_hard_active_strengthen":
                    return "card_action_food_hard_active_strengthen";
                case "act_food_hard_active_adjust":
                    return "card_action_food_hard_active_adjust";
                case "act_event":
                    return "card_action_event";
                case "act_reward":
                    return "card_action_reward";
                case "act_shop":
                    return "card_action_shop";
                case "act_interest":
                    return "card_node_interest";
                case "act_boss":
                    return "card_node_boss";
                case "act_slot":
                    return "card_action_slot";
                case "act_loan_repay":
                    return "card_action_loan_repay";
                case "act_gold_clear":
                    return "card_action_gold_clear";
                case "act_restore_heart":
                    return "card_action_restore_heart";
                default:
                    return string.Empty;
            }
        }

        internal static string FoodRewardSpriteName(
            cfg.FoodActionKind actionKind,
            cfg.RewardKind rewardKind)
        {
            string actionSuffix;
            switch (actionKind)
            {
                case cfg.FoodActionKind.Normal:
                    actionSuffix = "normal";
                    break;
                case cfg.FoodActionKind.Super:
                    actionSuffix = "hard";
                    break;
                default:
                    return LegacyFoodRewardSpriteName(rewardKind);
            }

            string rewardSuffix;
            switch (rewardKind)
            {
                case cfg.RewardKind.Gold:
                    rewardSuffix = "gold";
                    break;
                case cfg.RewardKind.FragmentChoice:
                    rewardSuffix = "fragment";
                    break;
                case cfg.RewardKind.PassiveItemChoice:
                    rewardSuffix = "passive";
                    break;
                case cfg.RewardKind.ActiveItemStrengthen:
                    rewardSuffix = "active_strengthen";
                    break;
                case cfg.RewardKind.ActiveItemAdjust:
                    rewardSuffix = "active_adjust";
                    break;
                default:
                    return LegacyFoodRewardSpriteName(rewardKind);
            }

            return $"card_action_food_{actionSuffix}_{rewardSuffix}";
        }

        private static string LegacyFoodRewardSpriteName(cfg.RewardKind kind)
        {
            switch (kind)
            {
                case cfg.RewardKind.Gold: return "card_action_food_gold";
                case cfg.RewardKind.FragmentChoice: return "card_action_food_fragment";
                case cfg.RewardKind.PassiveItemChoice: return "card_action_food_passive";
                case cfg.RewardKind.ActiveItemGrant:
                case cfg.RewardKind.ActiveItemStrengthen:
                case cfg.RewardKind.ActiveItemAdjust:
                    return "card_action_food_active";
                case cfg.RewardKind.DishChoice: return "card_action_food_dish";
                default: return "card_action_food_dish";
            }
        }

        internal static string RewardIconSpriteName(
            cfg.FoodActionKind actionKind,
            cfg.RewardKind kind)
        {
            return RewardBadgeResolver.SpriteNameFor(actionKind, kind);
        }

        private static Sprite RewardIconFor(
            cfg.FoodActionKind actionKind,
            cfg.RewardKind kind)
        {
            string spriteName = RewardIconSpriteName(actionKind, kind);
            return Resources.Load<Sprite>($"Sprites/UI/{spriteName}");
        }

        // —— 表现：出现/隐藏动画、hover 发亮、选中变色 + 四周粒子 ——

        private void OnEnable()
        {
            KillHalfCostTween();
            KillRewardDoubleTicketTween();
            _picking = false;
            _hover = false;
            _selectedGlow = false;
            _isHidden = false;
            _effectsVisible = true;
            EnsureRefs();
            _hotEmbers?.SetHot(_isHotBusiness);
            _starburst?.SetStarEvaluation(_isStarEvaluation);
            _starburst?.SetHighlighted(false);
            ResetGlow();
            transform.localScale = new Vector3(1f, 0f, 1f);
            UpdateGlowQuadSize();
        }

        private void OnDisable()
        {
            KillScaleTween();
            KillPickDelayTween();
            KillHalfCostTween();
            KillRewardDoubleTicketTween();
            _effectsVisible = false;
            _hotEmbers?.SetHot(false);
            _starburst?.SetStarEvaluation(false);

            if (_glowMat != null)
            {
                Destroy(_glowMat);
                _glowMat = null;
            }
        }

        private void KillScaleTween()
        {
            if (_scaleTween != null)
            {
                _scaleTween.Kill();
                _scaleTween = null;
            }
        }

        private void KillPickDelayTween()
        {
            if (_pickDelayTween != null)
            {
                _pickDelayTween.Kill();
                _pickDelayTween = null;
            }
        }

        private void ResetHalfCostPresentation()
        {
            KillHalfCostTween();
            _hasHalfCostPresentation = false;
            SetHalfCostRootActive(false);
        }

        private void KillHalfCostTween()
        {
            if (_halfCostTween != null)
            {
                _halfCostTween.Kill();
                _halfCostTween = null;
            }

            if (_timeText != null)
            {
                _timeText.rectTransform.localScale = Vector3.one;
                SetText(_timeText, _footerStableText);
            }

            if (_halfCostEffectiveText != null)
            {
                _halfCostEffectiveText.rectTransform.localScale = Vector3.one;
            }

            if (_halfCostStrikeImage != null)
            {
                _halfCostStrikeImage.rectTransform.localScale = Vector3.one;
            }

            if (_footerBackingImage != null)
            {
                _footerBackingImage.color = _footerStableColor;
            }
        }

        private void KillRewardDoubleTicketTween()
        {
            if (_rewardDoubleTicketTween != null)
            {
                _rewardDoubleTicketTween.Kill();
                _rewardDoubleTicketTween = null;
            }

            if (_rewardDoubleTicketRoot != null)
            {
                _rewardDoubleTicketRoot.localScale = Vector3.one;
            }

            if (_rewardDoubleTicketIconImage != null)
            {
                _rewardDoubleTicketIconImage.color = Color.white;
            }
        }

        private void PlayRewardDoubleTicketEmphasis()
        {
            if (!_rewardDoubleTicketVisible
                || _rewardDoubleTicketRoot == null
                || !_rewardDoubleTicketRoot.gameObject.activeInHierarchy)
            {
                return;
            }

            KillRewardDoubleTicketTween();
            _rewardDoubleTicketRoot.localScale = Vector3.one * 0.88f;
            if (_rewardDoubleTicketIconImage != null)
            {
                _rewardDoubleTicketIconImage.color = new Color(1f, 0.9f, 0.45f, 0.48f);
            }

            Sequence sequence = DOTween.Sequence().SetUpdate(true);
            sequence.Append(_rewardDoubleTicketRoot
                .DOScale(1f, RewardDoubleTicketEmphasisDuration)
                .SetEase(Ease.OutBack));
            if (_rewardDoubleTicketIconImage != null)
            {
                sequence.Join(_rewardDoubleTicketIconImage
                    .DOColor(Color.white, RewardDoubleTicketEmphasisDuration)
                    .SetEase(Ease.OutQuad));
            }

            _rewardDoubleTicketTween = sequence.OnComplete(() =>
            {
                if (_rewardDoubleTicketRoot != null)
                {
                    _rewardDoubleTicketRoot.localScale = Vector3.one;
                }

                if (_rewardDoubleTicketIconImage != null)
                {
                    _rewardDoubleTicketIconImage.color = Color.white;
                }

                _rewardDoubleTicketTween = null;
            });
        }

        private void PlayHalfCostEmphasis()
        {
            if (!_hasHalfCostPresentation
                || _halfCostRoot == null
                || !_halfCostRoot.gameObject.activeInHierarchy
                || _halfCostEffectiveText == null
                || _halfCostStrikeImage == null)
            {
                return;
            }

            KillHalfCostTween();
            RectTransform effectiveRect = _halfCostEffectiveText.rectTransform;
            RectTransform strikeRect = _halfCostStrikeImage.rectTransform;
            effectiveRect.localScale = Vector3.one * 0.92f;
            strikeRect.localScale = new Vector3(0f, 1f, 1f);

            bool hasFooter = _footerBackingImage != null
                && _footerBackingImage.gameObject.activeInHierarchy;
            if (hasFooter)
            {
                _footerBackingImage.color = HalfCostFlashColor;
            }

            const float strikeDuration = 0.12f;
            const float riseDuration = 0.12f;
            float settleDuration = HalfCostEmphasisDuration - strikeDuration - riseDuration;
            Sequence sequence = DOTween.Sequence().SetUpdate(true);
            sequence.Append(strikeRect
                .DOScaleX(1f, strikeDuration)
                .SetEase(Ease.OutQuad));
            sequence.Append(effectiveRect
                .DOScale(1.08f, riseDuration)
                .SetEase(Ease.OutQuad));
            sequence.Append(effectiveRect
                .DOScale(1f, settleDuration)
                .SetEase(Ease.OutBack));
            if (hasFooter)
            {
                sequence.Insert(0f, _footerBackingImage
                    .DOColor(_footerStableColor, HalfCostEmphasisDuration)
                    .SetEase(Ease.OutQuad));
            }

            _halfCostTween = sequence.OnComplete(() =>
            {
                effectiveRect.localScale = Vector3.one;
                strikeRect.localScale = Vector3.one;
                if (_footerBackingImage != null)
                {
                    _footerBackingImage.color = _footerStableColor;
                }

                _halfCostTween = null;
            });
        }

        private void Update()
        {
            if (_glowBorder == null)
            {
                return;
            }

            UpdateGlowQuadSize();

            Color target;
            if (_selectedGlow)
            {
                target = _selectedColor;
            }
            else if (_effectsVisible && _isStarEvaluation)
            {
                if (_hover)
                {
                    target = StarHoverGlowColor;
                    target.a = 1f;
                }
                else
                {
                    float phase = Mathf.Repeat(Time.unscaledTime / 1.08f, 1f);
                    float pulse = 0.5f - Mathf.Cos(phase * Mathf.PI * 2f) * 0.5f;
                    target = Color.Lerp(StarGlowColor, StarPulseGlowColor, pulse);
                    target.a = Mathf.Lerp(0.48f, 0.72f, pulse);
                }
            }
            else if (_effectsVisible && _isHotBusiness)
            {
                if (_hover)
                {
                    target = HotHoverGlowColor;
                    target.a = 0.85f;
                }
                else
                {
                    float phase = Mathf.Repeat(Time.unscaledTime / 1.8f, 1f);
                    float pulse = 0.5f - Mathf.Cos(phase * Mathf.PI * 2f) * 0.5f;
                    target = HotGlowColor;
                    target.a = Mathf.Lerp(0.2f, 0.34f, pulse);
                }
            }
            else
            {
                target = _hoverColor;
                if (!_hover || !_effectsVisible)
                {
                    target.a = 0f;
                }
            }

            float k = 1f - Mathf.Exp(-_glowFadeSpeed * Time.unscaledDeltaTime);
            _glowBorder.color = Color.Lerp(_glowBorder.color, target, k);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_picking)
            {
                return;
            }

            _hover = true;
            _starburst?.SetHighlighted(_isStarEvaluation);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hover = false;
            _starburst?.SetHighlighted(_isStarEvaluation && _selectedGlow);
        }

        // 入场：Y 方向 0→1 弹出，由持有方在转场完成后显式触发。
        public Tween PlayShow()
        {
            KillScaleTween();
            KillHalfCostTween();
            _isHidden = false;
            _effectsVisible = true;
            _hotEmbers?.SetHot(_isHotBusiness);
            _starburst?.SetStarEvaluation(_isStarEvaluation);
            _starburst?.SetHighlighted(false);
            transform.localScale = new Vector3(1f, 0f, 1f);
            SetPickInteractable(false);
            _scaleTween = transform.DOScaleY(1f, Mathf.Max(0.01f, _showDuration))
                .SetEase(Ease.OutBack)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    transform.localScale = Vector3.one;
                    SetPickInteractable(true);
                    _scaleTween = null;
                    PlayHalfCostEmphasis();
                    PlayRewardDoubleTicketEmphasis();
                });
            return _scaleTween;
        }

        // 隐藏：Y 方向收拢到 0，完成后回调。
        private Tween PlayHide(Action onComplete, float delay = 0f)
        {
            if (_isHidden)
            {
                onComplete?.Invoke();
                return null;
            }

            KillScaleTween();
            KillHalfCostTween();
            KillRewardDoubleTicketTween();
            SetPickInteractable(false);
            _effectsVisible = false;
            _hotEmbers?.SetHot(false);
            _starburst?.SetStarEvaluation(false);
            Sequence seq = DOTween.Sequence().SetUpdate(true);
            if (delay > 0f)
            {
                seq.AppendInterval(delay);
            }

            float hideDuration = Mathf.Max(DefaultHideDuration, _hideDuration);
            seq.AppendCallback(() =>
            {
                _selectedGlow = false;
                _hover = false;
            });
            seq.Append(transform.DOScaleY(0f, hideDuration).SetEase(Ease.InQuad));
            if (_glowBorder != null)
            {
                float startAlpha = _glowBorder.color.a;
                seq.Join(DOVirtual.Float(startAlpha, 0f, hideDuration, alpha =>
                {
                    if (_glowBorder == null)
                    {
                        return;
                    }

                    Color c = _glowBorder.color;
                    c.a = alpha;
                    _glowBorder.color = c;
                }).SetEase(Ease.InQuad));
            }

            _scaleTween = seq
                .OnComplete(() =>
                {
                    transform.localScale = new Vector3(1f, 0f, 1f);
                    _isHidden = true;
                    _scaleTween = null;
                    onComplete?.Invoke();
                });
            return _scaleTween;
        }

        /// <summary>反向隐藏后销毁；delay 由持有方统一传入，保证同组卡牌同一时间开始隐藏。</summary>
        public Tween PlayHideThenDestroy(float delay = 0f)
        {
            if (!isActiveAndEnabled || _isHidden)
            {
                Destroy(gameObject);
                return null;
            }

            return PlayHide(() => Destroy(gameObject), delay);
        }

        private void OnPickClicked()
        {
            if (_picking)
            {
                return;
            }

            _picking = true;
            _selectedGlow = true;
            _hover = false;
            _starburst?.SetHighlighted(_isStarEvaluation);
            if (_isStarEvaluation)
            {
                _starburst?.TriggerBurst();
            }
            SetPickInteractable(false);
            EmitParticles();

            Action cb = _onPick;
            _onPick = null;

            KillPickDelayTween();
            float delay = Mathf.Max(0.01f, _selectHold);
            _pickDelayTween = DOVirtual.DelayedCall(delay, () =>
            {
                _pickDelayTween = null;
                cb?.Invoke();
            }).SetUpdate(true);
        }

        private void EmitParticles()
        {
            EnsureRefs();
            if (_particleContainer == null)
            {
                return;
            }

            Sprite dot = ParticleSprite();
            var selfRect = (RectTransform)transform;
            Vector2 half = selfRect.rect.size * 0.5f;
            for (int i = 0; i < _particleCount; i++)
            {
                float ang = (i / (float)_particleCount) * Mathf.PI * 2f + UnityEngine.Random.Range(-0.2f, 0.2f);
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Vector2 start = new Vector2(dir.x * half.x, dir.y * half.y);
                SpawnParticle(dot, start, dir);
            }
        }

        private void SpawnParticle(Sprite dot, Vector2 start, Vector2 dir)
        {
            if (_particleTemplate == null)
            {
                Debug.LogError($"{nameof(WeekEventCardView)} prefab 缺少 Particle template。", this);
                return;
            }

            Image img = Instantiate(_particleTemplate, _particleContainer);
            img.gameObject.name = "Particle";
            img.gameObject.SetActive(true);
            var rt = (RectTransform)img.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = _particleSize;
            rt.anchoredPosition = start;
            rt.localScale = Vector3.one;

            img.sprite = dot;
            img.raycastTarget = false;
            img.color = _particleColor;

            PlayParticleTween(rt, img, start, dir);
        }

        private void PlayParticleTween(RectTransform rt, Image img, Vector2 start, Vector2 dir)
        {
            Vector2 end = start + dir * _particleDistance;
            float baseScale = UnityEngine.Random.Range(0.6f, 1.25f);
            float lifetime = Mathf.Max(0.01f, _particleLifetime);
            DOVirtual.Float(0f, 1f, lifetime, p =>
                {
                    if (rt == null)
                    {
                        return;
                    }

                    rt.anchoredPosition = Vector2.Lerp(start, end, 1f - (1f - p) * (1f - p));
                    float s = baseScale * (1f - 0.5f * p);
                    rt.localScale = new Vector3(s, s, 1f);
                    if (img != null)
                    {
                        Color c = _particleColor;
                        c.a = _particleColor.a * (1f - p);
                        img.color = c;
                    }
                })
                .SetEase(Ease.Linear)
                .SetUpdate(true)
                .SetTarget(rt)
                .OnComplete(() =>
                {
                    if (rt != null)
                    {
                        Destroy(rt.gameObject);
                    }
                });
        }

        private Sprite ParticleSprite()
        {
            if (_particleSprite == null)
            {
                _particleSprite = Resources.Load<Sprite>("Sprites/UI/white");
            }

            return _particleSprite;
        }

        private void ResetGlow()
        {
            if (_glowBorder == null)
            {
                return;
            }

            Color c = _hoverColor;
            c.a = 0f;
            _glowBorder.color = c;
        }

        private void EnsureRefs()
        {
            if (_glowBorder == null)
            {
                Debug.LogError($"{nameof(WeekEventCardView)} prefab 缺少 GlowBorder。", this);
            }

            if (_particleContainer == null)
            {
                Debug.LogError($"{nameof(WeekEventCardView)} prefab 缺少 Particles 容器。", this);
            }

            if (_particleTemplate != null)
            {
                _particleTemplate.gameObject.SetActive(false);
            }

            EnsurePersistentEffectsReferences();
            EnsureGlowMaterial();
        }

        private void EnsurePersistentEffectsReferences()
        {
            if (_hotEmbers == null && _particleContainer != null)
            {
                _hotEmbers = _particleContainer.GetComponent<WeekEventCardEmberGraphic>();
            }

            if (_starburst == null && _particleContainer != null)
            {
                _starburst = _particleContainer.GetComponentInChildren<WeekEventCardStarburstGraphic>(true);
            }
        }

        private void SetPickInteractable(bool interactable)
        {
            if (_pickButton != null)
            {
                _pickButton.interactable = interactable;
            }
        }

        // 外发光 shader 需要知道本实例 quad 的真实像素尺寸；实际游戏里卡牌会被布局改尺寸，
        // 因此不能沿用材质里写死的 _QuadSize，改用每实例材质并逐帧回填真实 rect 尺寸。
        private void EnsureGlowMaterial()
        {
            if (_glowBorder == null || _glowMat != null)
            {
                return;
            }

            Material baseMat = _glowBorder.material;
            if (baseMat == null || baseMat.shader == null || baseMat.shader.name != "GourmetProject/UIOuterGlow")
            {
                baseMat = Resources.Load<Material>("Materials/UIOuterGlow");
            }

            if (baseMat == null)
            {
                return;
            }

            _glowMat = new Material(baseMat);
            _glowBorder.material = _glowMat;
        }

        private void UpdateGlowQuadSize()
        {
            if (_glowMat == null || _glowBorder == null)
            {
                return;
            }

            Rect r = _glowBorder.rectTransform.rect;
            _glowMat.SetVector(QuadSizeId, new Vector4(r.width, r.height, 0f, 0f));
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

    }
}
