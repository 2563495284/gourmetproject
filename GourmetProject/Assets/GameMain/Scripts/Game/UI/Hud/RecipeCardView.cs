using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 扇形菜谱条里的一本菜谱本卡：显示容量文本（如 10/12）+ 点击回调（战斗态用于上菜，非战斗态置灰）。
    /// 固定结构在 Recipe.prefab，由 <see cref="RecipeView"/> 数据驱动实例化并做扇形排布/补间动画。
    /// </summary>
    public sealed class RecipeCardView : MonoBehaviour, IPointerClickHandler
    {
        private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");
        private static readonly int PaddingId = Shader.PropertyToID("_Padding");

        private const float GlowPadding = 28f;

        [FormerlySerializedAs("_capacityText")]
        [SerializeField] private Text _infoText;
        [SerializeField] private Button _button;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Image _glowBorder;

        private RectTransform _rect;
        private Material _glowMaterial;
        private Action _onRightClick;
        private bool _clickSuppressed;
        private int _clickSuppressedUntilFrame = -1;

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

        public void Bind(string capacity, bool interactable, Action onClick, Action onRightClick = null)
        {
            _onRightClick = onRightClick;

            if (_infoText != null)
            {
                _infoText.text = capacity ?? string.Empty;
            }

            if (_button != null)
            {
                _button.onClick.RemoveAllListeners();
                _button.interactable = interactable;
                if (onClick != null)
                {
                    _button.onClick.AddListener(() =>
                    {
                        if (!ShouldSuppressClick())
                        {
                            onClick();
                        }
                    });
                }
            }
        }

        public void SetClickSuppressed(bool suppressed)
        {
            _clickSuppressed = suppressed;
            if (!suppressed)
            {
                _clickSuppressedUntilFrame = Time.frameCount;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null)
            {
                return;
            }

            if (ShouldSuppressClick())
            {
                eventData.Use();
                return;
            }

            if (eventData.button != PointerEventData.InputButton.Right)
            {
                return;
            }

            _onRightClick?.Invoke();
            eventData.Use();
        }

        private bool ShouldSuppressClick()
        {
            return _clickSuppressed || Time.frameCount <= _clickSuppressedUntilFrame;
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

            Debug.LogError($"{nameof(RecipeCardView)} prefab 缺少 TargetGlow。", this);
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
