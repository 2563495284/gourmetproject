using System;
using System.Globalization;
using DG.Tweening;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Flow;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using UnityEngine;
using UnityEngine.EventSystems;
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
    public sealed class WeekEventCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const string NodeEventFooter = "节点事件";
        private const string DefaultBossTitle = "周末盛宴\n恶魔";
        private const float GlowPadding = 48f;
        private const float DefaultHideDuration = 0.2f;
        private const float DefaultPickEffectHold = 0.2f;

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

        [Header("Effects - References")]
        [SerializeField] private Image _glowBorder;
        [SerializeField] private RectTransform _particleContainer;

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
        private Material _glowMat;
        private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");

        public float PickEffectHold => Mathf.Max(DefaultPickEffectHold, _selectHold);

        public void Bind(cfg.GameEvent ev, Action onPick)
        {
            BindNodeCard(ev?.Name ?? "事件", ev?.Desc ?? string.Empty, "card_action_event", onPick);
        }

        /// <summary>事件「n 选一」单个选项卡：卡名 = 选项文案，页脚标注为节点事件，无耗时行。</summary>
        public void BindEventOption(string optionText, Action onPick)
        {
            ApplyCommon(string.IsNullOrWhiteSpace(optionText) ? "选项" : optionText, string.Empty, onPick);
            SetFooter(NodeEventFooter, true, FooterNodeColor);
            SetArt(Resources.Load<Sprite>("Sprites/UI/card_action_event"));
            SetRewardBadge(false);
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

            _onPick = onPick;
            if (_pickButton != null)
            {
                _pickButton.onClick.RemoveAllListeners();
                _pickButton.onClick.AddListener(OnPickClicked);
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

        // —— 表现：出现/隐藏动画、hover 发亮、选中变色 + 四周粒子 ——

        private void OnEnable()
        {
            _picking = false;
            _hover = false;
            _selectedGlow = false;
            _isHidden = false;
            EnsureRefs();
            ResetGlow();
            transform.localScale = new Vector3(1f, 0f, 1f);
            UpdateGlowQuadSize();
        }

        private void OnDisable()
        {
            KillScaleTween();

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

        private void Update()
        {
            if (_glowBorder == null)
            {
                return;
            }

            UpdateGlowQuadSize();

            Color target = _selectedGlow ? _selectedColor : _hoverColor;
            if (!_selectedGlow && !_hover)
            {
                target.a = 0f;
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
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hover = false;
        }

        // 入场：Y 方向 0→1 弹出，由持有方在转场完成后显式触发。
        public Tween PlayShow()
        {
            KillScaleTween();
            _isHidden = false;
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
            SetPickInteractable(false);
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
            EmitParticles();

            Action cb = _onPick;
            _onPick = null;
            cb?.Invoke();
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
            var go = new GameObject("Particle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_particleContainer, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = _particleSize;
            rt.anchoredPosition = start;
            rt.localScale = Vector3.one;

            var img = go.GetComponent<Image>();
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
                var go = new GameObject("GlowBorder", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var rt = (RectTransform)go.transform;
                rt.SetParent(transform, false);
                StretchFull(rt);
                // 向外扩出 padding，让光晕能画到卡牌矩形之外；放到最底层，只在 Art 外圈显形。
                rt.offsetMin = new Vector2(-GlowPadding, -GlowPadding);
                rt.offsetMax = new Vector2(GlowPadding, GlowPadding);
                rt.SetAsFirstSibling();
                var img = go.GetComponent<Image>();
                img.material = Resources.Load<Material>("Materials/UIOuterGlow");
                img.raycastTarget = false;
                Color c = _hoverColor;
                c.a = 0f;
                img.color = c;
                _glowBorder = img;
            }

            if (_particleContainer == null)
            {
                var go = new GameObject("Particles", typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(transform, false);
                StretchFull(rt);
                rt.SetAsLastSibling();
                _particleContainer = rt;
            }

            EnsureGlowMaterial();
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
