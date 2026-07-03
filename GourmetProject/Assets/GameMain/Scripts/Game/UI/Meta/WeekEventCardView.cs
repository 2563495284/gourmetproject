using System;
using System.Collections;
using System.Globalization;
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
        [SerializeField] private float _hideDuration = 0.18f;
        [SerializeField] private float _selectHold = 0.08f;

        [Header("Effects - Glow")]
        [SerializeField] private Color _hoverColor = new Color(1f, 0.85f, 0.4f, 0.9f);
        [SerializeField] private Color _selectedColor = new Color(0.4f, 1f, 0.72f, 1f);
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
        private Coroutine _scaleAnim;
        private Material _glowMat;
        private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");

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
            StartCoroutine(ShowRoutine());
        }

        private void OnDisable()
        {
            if (_glowMat != null)
            {
                Destroy(_glowMat);
                _glowMat = null;
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

        /// <summary>反向隐藏后销毁（供持有方清场时调用，选中卡自身已先隐藏）。</summary>
        public void PlayHideThenDestroy()
        {
            if (!isActiveAndEnabled || _isHidden)
            {
                Destroy(gameObject);
                return;
            }

            StartCoroutine(HideThenDestroyRoutine());
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
            StartCoroutine(PickRoutine());
        }

        private IEnumerator PickRoutine()
        {
            yield return WaitUnscaled(_selectHold);
            yield return HideRoutine();
            Action cb = _onPick;
            _onPick = null;
            cb?.Invoke();
        }

        private IEnumerator HideThenDestroyRoutine()
        {
            yield return HideRoutine();
            Destroy(gameObject);
        }

        private IEnumerator ShowRoutine()
        {
            float t = 0f;
            while (t < _showDuration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / _showDuration);
                float y = EaseOutBack(p);
                transform.localScale = new Vector3(1f, y, 1f);
                yield return null;
            }

            transform.localScale = Vector3.one;
        }

        private IEnumerator HideRoutine()
        {
            float startY = transform.localScale.y;
            float t = 0f;
            while (t < _hideDuration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / _hideDuration);
                float y = Mathf.Lerp(startY, 0f, EaseInQuad(p));
                transform.localScale = new Vector3(1f, y, 1f);
                yield return null;
            }

            transform.localScale = new Vector3(1f, 0f, 1f);
            _isHidden = true;
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

            StartCoroutine(ParticleRoutine(rt, img, start, dir));
        }

        private IEnumerator ParticleRoutine(RectTransform rt, Image img, Vector2 start, Vector2 dir)
        {
            Vector2 end = start + dir * _particleDistance;
            float baseScale = UnityEngine.Random.Range(0.6f, 1.25f);
            float t = 0f;
            while (t < _particleLifetime && rt != null)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / _particleLifetime);
                rt.anchoredPosition = Vector2.Lerp(start, end, 1f - (1f - p) * (1f - p));
                float s = baseScale * (1f - 0.5f * p);
                rt.localScale = new Vector3(s, s, 1f);
                if (img != null)
                {
                    Color c = _particleColor;
                    c.a = _particleColor.a * (1f - p);
                    img.color = c;
                }

                yield return null;
            }

            if (rt != null)
            {
                Destroy(rt.gameObject);
            }
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

        private static IEnumerator WaitUnscaled(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private static float EaseOutBack(float p)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float x = p - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        private static float EaseInQuad(float p)
        {
            return p * p;
        }

    }
}
