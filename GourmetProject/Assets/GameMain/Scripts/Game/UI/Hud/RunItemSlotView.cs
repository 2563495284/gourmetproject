using System;
using DG.Tweening;
using GourmetProject.Game;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Tooltips;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 常驻 HUD 里的道具槽视图（屏幕空间 UGUI 版）：图标 + 名称 + 角标（等级 Lv/份数 xN）+ 品质描边色 + 点击回调。
    /// 固定结构在 RunItemSlotView.prefab，运行时由 BattleForm 的常驻 HUD 数据驱动实例化并 <see cref="Bind"/>。
    /// </summary>
    public sealed class RunItemSlotView : MonoBehaviour
    {
        private static readonly int PulseId = Shader.PropertyToID("_Pulse");
        private static readonly int IsUsedId = Shader.PropertyToID("_IsUsed");
        private static readonly int IsWaxId = Shader.PropertyToID("_IsWax");
        private const string PassiveIconShaderName = "GourmetProject/PassiveItemIcon";
        private const float PassivePulseDuration = 0.55f;
        [SerializeField] private Image _icon;
        [SerializeField] private Button _button;
        [SerializeField] private TipHoverTrigger _tipTrigger;

        [SerializeField] private Text _info;

        private Material _iconEffectMaterial;
        private Tween _pulseTween;
        private PassiveItemModel _boundPassiveModel;
        private string _fallbackInfoText = string.Empty;

        private void Awake()
        {
            EnsureRefs();
        }

        public RectTransform RectTransform => transform as RectTransform;

        public Vector2 IconScreenCenter()
        {
            RectTransform rect = _icon != null ? _icon.rectTransform : RectTransform;
            if (rect == null)
            {
                return Vector2.zero;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.WorldToScreenPoint(cam, rect.TransformPoint(rect.rect.center));
        }

        public bool ContainsScreenPoint(Vector2 screenPoint)
        {
            RectTransform rect = RectTransform;
            if (rect == null)
            {
                return false;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, cam);
        }

        /// <summary>绑定一个有内容的道具槽。</summary>
        public void Bind(
            Sprite icon,
            string name,
            string badge,
            Color qualityColor,
            bool interactable,
            Action onClick,
            RunItemState state = null,
            bool usePassiveShader = false)
        {
            EnsureRefs();
            _fallbackInfoText = badge ?? string.Empty;
            BindPassiveModel(usePassiveShader ? state?.Model : null);
            RefreshInfoText();

            // if (_background != null)
            // {
            //     _background.color = qualityColor;
            // }

            if (_icon != null)
            {
                _icon.enabled = icon != null;
                _icon.sprite = icon;
                ConfigureIconMaterial(usePassiveShader && icon != null, state?.Model);
            }

            if (_button != null)
            {
                _button.onClick.RemoveAllListeners();
                _button.interactable = interactable;
                if (onClick != null)
                {
                    _button.onClick.AddListener(() => onClick());
                }
            }
        }

        /// <summary>把该槽显示为空槽（无图标、不可点）。</summary>
        public void SetEmpty()
        {
            Bind(null, string.Empty, string.Empty, EmptySlotColor, false, null);
            ClearTip();
        }

        /// <summary>把该槽绑定到共享的道具 Tips 实例。</summary>
        public void SetTip(ItemTipView tip, ItemDefinition item)
        {
            EnsureRefs();
            if (_tipTrigger == null || tip == null || item == null)
            {
                ClearTip();
                return;
            }

            // Tips 避让要按整个槽位根宽度计算，而不是某个子 Image；
            // 否则显示在左侧时会从槽位中心向外排布，遮住半个道具。
            _tipTrigger.SetTarget(transform as RectTransform);
            _tipTrigger.SetTip(tip, () => tip.Bind(item));
        }

        /// <summary>清掉悬浮 Tips 绑定，供空槽 / 销毁前使用。</summary>
        public void ClearTip()
        {
            if (_tipTrigger != null)
            {
                _tipTrigger.ClearTip();
            }
        }

        public void HideTip()
        {
            _tipTrigger?.OnPointerExit(null);
        }

        public void PlayPassivePulse()
        {
            EnsureRefs();
            if (_icon == null || !_icon.enabled)
            {
                return;
            }

            Material material = EnsureIconEffectMaterial();
            if (material == null)
            {
                return;
            }

            _icon.material = material;
            RefreshPassiveIconState();
            _pulseTween?.Kill(false);
            material.SetFloat(PulseId, 1f);
            _pulseTween = DOVirtual.DelayedCall(PassivePulseDuration, () =>
            {
                if (material != null)
                {
                    material.SetFloat(PulseId, 0f);
                }
            }, true).SetUpdate(true).SetTarget(this);
        }

        private void OnDestroy()
        {
            BindPassiveModel(null);
            _pulseTween?.Kill(false);
            _pulseTween = null;

            if (_iconEffectMaterial != null)
            {
                Destroy(_iconEffectMaterial);
                _iconEffectMaterial = null;
            }
        }

        private static Color EmptySlotColor => new Color(0.92f, 0.90f, 0.84f, 1f);

        private void EnsureRefs()
        {

            if (_button == null)
            {
                _button = GetComponent<Button>();
            }

            if (_icon == null)
            {
                Transform icon = transform.Find("Image/Icon");
                _icon = icon != null ? icon.GetComponent<Image>() : null;
            }

            if (_tipTrigger == null)
            {
                _tipTrigger = GetComponent<TipHoverTrigger>();
                if (_tipTrigger == null)
                {
                    _tipTrigger = gameObject.AddComponent<TipHoverTrigger>();
                }
            }

            if (_info == null)
            {
                Transform info = transform.Find("Image/Info");
                if (info == null)
                {
                    info = transform.Find("Info");
                }

                _info = info != null ? info.GetComponent<Text>() : null;
            }
        }

        private void ConfigureIconMaterial(bool usePassiveShader, PassiveItemModel model)
        {
            if (_icon == null)
            {
                return;
            }

            if (!usePassiveShader)
            {
                _pulseTween?.Kill(false);
                _icon.material = null;
                if (_iconEffectMaterial != null)
                {
                    _iconEffectMaterial.SetFloat(PulseId, 0f);
                }

                return;
            }

            Material material = EnsureIconEffectMaterial();
            if (material == null)
            {
                _icon.material = null;
                return;
            }

            material.SetFloat(PulseId, 0f);
            material.SetFloat(IsUsedId, model != null && model.IsIconUsed ? 1f : 0f);
            material.SetFloat(IsWaxId, model != null && model.IsIconWax ? 1f : 0f);
            _icon.material = material;
        }

        private Material EnsureIconEffectMaterial()
        {
            if (_iconEffectMaterial != null)
            {
                return _iconEffectMaterial;
            }

            Shader shader = Shader.Find(PassiveIconShaderName);
            if (shader == null)
            {
                return null;
            }

            _iconEffectMaterial = new Material(shader)
            {
                name = $"{name}_PassiveItemIcon",
                hideFlags = HideFlags.DontSave,
            };
            return _iconEffectMaterial;
        }

        private void BindPassiveModel(PassiveItemModel model)
        {
            if (_boundPassiveModel == model)
            {
                return;
            }

            if (_boundPassiveModel != null)
            {
                _boundPassiveModel.Flashed -= OnPassiveModelFlashed;
                _boundPassiveModel.IconStateChanged -= OnPassiveIconStateChanged;
                _boundPassiveModel.InfoTextChanged -= OnPassiveModelInfoTextChanged;
            }

            _boundPassiveModel = model;
            if (_boundPassiveModel != null)
            {
                _boundPassiveModel.Flashed += OnPassiveModelFlashed;
                _boundPassiveModel.IconStateChanged += OnPassiveIconStateChanged;
                _boundPassiveModel.InfoTextChanged += OnPassiveModelInfoTextChanged;
            }
        }

        private void OnPassiveModelFlashed(PassiveItemModel model)
        {
            PlayPassivePulse();
        }

        private void OnPassiveIconStateChanged(PassiveItemModel model)
        {
            RefreshPassiveIconState();
        }

        private void OnPassiveModelInfoTextChanged(PassiveItemModel model)
        {
            RefreshInfoText();
        }

        private void RefreshInfoText()
        {
            if (_info == null)
            {
                return;
            }

            string text = _boundPassiveModel != null ? _boundPassiveModel.InfoText : _fallbackInfoText;
            _info.text = text ?? string.Empty;
        }

        private void RefreshPassiveIconState()
        {
            if (_iconEffectMaterial == null || _boundPassiveModel == null)
            {
                return;
            }

            _iconEffectMaterial.SetFloat(IsUsedId, _boundPassiveModel.IsIconUsed ? 1f : 0f);
            _iconEffectMaterial.SetFloat(IsWaxId, 0f);
        }

        /// <summary>道具品质对应的槽底色（与战斗世界空间槽保持一致）。</summary>
        public static Color QualityColor(cfg.ItemQuality quality)
        {
            switch (quality)
            {
                case cfg.ItemQuality.Uncommon:
                    return new Color(0.50f, 0.86f, 0.46f, 1f);
                case cfg.ItemQuality.Rare:
                    return new Color(0.35f, 0.62f, 1f, 1f);
                case cfg.ItemQuality.Epic:
                    return new Color(0.74f, 0.42f, 1f, 1f);
                case cfg.ItemQuality.Legendary:
                    return new Color(1f, 0.72f, 0.22f, 1f);
                default:
                    return new Color(0.92f, 0.86f, 0.74f, 1f);
            }
        }

        /// <summary>取道具图标（Resources 路径，缺失返回 null）。</summary>
        public static Sprite LoadIcon(ItemDefinition item)
        {
            return ContentIconLoader.LoadItem(item);
        }

        /// <summary>取道具名前两字作为槽内短名（图标缺失时的兜底展示）。</summary>
        public static string ShortName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            return name.Length <= 2 ? name : name.Substring(0, 2);
        }
    }
}
