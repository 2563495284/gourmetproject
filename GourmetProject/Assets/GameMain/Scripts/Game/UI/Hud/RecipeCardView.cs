using System;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 扇形菜谱条里的一本菜谱本卡：显示容量文本（如 10/12）+ 点击回调（战斗态用于上菜，非战斗态置灰）。
    /// 固定结构在 Recipe.prefab，由 <see cref="RecipeView"/> 数据驱动实例化并做扇形排布/补间动画。
    /// </summary>
    public sealed class RecipeCardView : MonoBehaviour
    {
        private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");
        private static readonly int PaddingId = Shader.PropertyToID("_Padding");

        private const float GlowPadding = 28f;

        [SerializeField] private Text _capacityText;
        [SerializeField] private Button _button;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Image _glowBorder;

        private RectTransform _rect;
        private Material _glowMaterial;

        public RectTransform Rect
        {
            get
            {
                if (_rect == null)
                {
                    _rect = (RectTransform)transform;
                }

                return _rect;
            }
        }

        public CanvasGroup CanvasGroup
        {
            get
            {
                if (_canvasGroup == null)
                {
                    _canvasGroup = GetComponent<CanvasGroup>();
                    if (_canvasGroup == null)
                    {
                        _canvasGroup = gameObject.AddComponent<CanvasGroup>();
                    }
                }

                return _canvasGroup;
            }
        }

        public void OnDestroy()
        {
            if (_glowMaterial != null)
            {
                Destroy(_glowMaterial);
                _glowMaterial = null;
            }
        }

        public void Bind(string capacity, bool interactable, Action onClick)
        {
            if (_capacityText != null)
            {
                _capacityText.text = capacity ?? string.Empty;
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

        public bool ContainsScreenPoint(Vector2 screenPoint, Camera eventCamera)
        {
            return RectTransformUtility.RectangleContainsScreenPoint(Rect, screenPoint, eventCamera);
        }

        public Vector2 CenterScreenPoint(Camera eventCamera)
        {
            return RectTransformUtility.WorldToScreenPoint(eventCamera, Rect.TransformPoint(Rect.rect.center));
        }

        public void SetTargetHighlight(bool visible, bool emphasized)
        {
            EnsureGlowBorder();
            if (_glowBorder == null)
            {
                return;
            }

            _glowBorder.gameObject.SetActive(visible);
            Color color = new Color(0.25f, 1f, 0.35f, emphasized ? 0.9f : 0.38f);
            _glowBorder.color = visible ? color : Color.clear;
            UpdateGlowMaterial();
        }

        private void EnsureGlowBorder()
        {
            if (_glowBorder != null)
            {
                EnsureGlowMaterial();
                return;
            }

            var go = new GameObject("TargetGlow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false);
            go.transform.SetAsFirstSibling();

            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-GlowPadding, -GlowPadding);
            rt.offsetMax = new Vector2(GlowPadding, GlowPadding);
            rt.pivot = new Vector2(0.5f, 0.5f);

            _glowBorder = go.GetComponent<Image>();
            _glowBorder.raycastTarget = false;
            _glowBorder.sprite = Resources.Load<Sprite>("Sprites/UI/white");
            _glowBorder.material = Resources.Load<Material>("Materials/UIOuterGlow");
            _glowBorder.color = Color.clear;
            _glowBorder.gameObject.SetActive(false);
            EnsureGlowMaterial();
        }

        private void EnsureGlowMaterial()
        {
            if (_glowBorder == null || _glowMaterial != null)
            {
                return;
            }

            Material baseMaterial = _glowBorder.material;
            if (baseMaterial == null || baseMaterial.shader == null || baseMaterial.shader.name != "GourmetProject/UIOuterGlow")
            {
                baseMaterial = Resources.Load<Material>("Materials/UIOuterGlow");
            }

            if (baseMaterial == null)
            {
                return;
            }

            _glowMaterial = new Material(baseMaterial);
            _glowBorder.material = _glowMaterial;
            UpdateGlowMaterial();
        }

        private void UpdateGlowMaterial()
        {
            if (_glowMaterial == null || _glowBorder == null)
            {
                return;
            }

            Rect r = _glowBorder.rectTransform.rect;
            _glowMaterial.SetVector(QuadSizeId, new Vector4(r.width, r.height, 0f, 0f));
            _glowMaterial.SetFloat(PaddingId, GlowPadding);
        }
    }
}
