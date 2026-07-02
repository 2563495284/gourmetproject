using System;
using System.Collections;
using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 场景内按钮：固定结构（底色块 + 文案 + 碰撞盒）摆在 prefab 里，由 <see cref="Configure"/> 喂尺寸/颜色/文案/回调。
    /// 尺寸是数据驱动的（不同按钮大小不一），运行时按传入 size 缩放根节点，文案子物体反向缩放保持世界字号恒定。
    /// 显隐用可见度系数（乘到底色/文案原始 alpha 上），支持渐隐渐显；见 <see cref="SetVisible"/>。
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer), typeof(BoxCollider2D))]
    public sealed class WorldButtonView : MonoBehaviour
    {
        [Tooltip("按钮底色块（prefab 根节点上的 SpriteRenderer）。")]
        [SerializeField] private SpriteRenderer _background;

        [Tooltip("按钮文案（prefab 子物体 Label 上的 TextMesh）。")]
        [SerializeField] private TextMesh _label;

        [Tooltip("点击命中碰撞盒（prefab 根节点上的 BoxCollider2D）。")]
        [SerializeField] private BoxCollider2D _collider;

        [Tooltip("渐隐渐显时长（秒）。设 0 则瞬切，无过渡。")]
        [SerializeField] private float _fadeDuration = 0.18f;

        private Color _normalColor = Color.white;
        private Color _disabledColor;
        private Color _labelBaseColor = Color.white;
        private bool _labelBaseCaptured;
        private Action _clicked;
        private bool _interactable = true;

        // 可见度系数 [0,1]：1=完全显示，0=完全隐藏。与 interactable 的颜色相乘，供渐隐渐显。
        private float _visibility = 1f;
        private Coroutine _fadeRoutine;

        /// <summary>设置外观与点击回调。可重复调用以更新尺寸/颜色/文案/回调。</summary>
        public void Configure(Vector2 size, string label, Color color, Action clicked)
        {
            EnsureRefs();

            transform.localScale = new Vector3(size.x, size.y, 1f);

            // Label 反向缩放抵消根缩放，使世界字号不随按钮尺寸变化。
            if (_label != null)
            {
                _label.transform.localScale = new Vector3(
                    0.08f / Mathf.Max(size.x, 0.0001f),
                    0.08f / Mathf.Max(size.y, 0.0001f),
                    1f);
            }

            _clicked = clicked;
            _normalColor = color;
            _disabledColor = new Color(color.r * 0.45f, color.g * 0.45f, color.b * 0.45f, 0.75f);
            if (_background != null)
            {
                _background.drawMode = SpriteDrawMode.Sliced;
                _background.size = Vector2.one;
                BattleSorting.Apply(_background, BattleSorting.WorldUi, BattleSorting.OrderButtonBg);
                SpriteRenderStyle.ApplyUnlitMaterial(_background);
            }

            SetLabel(label);
            ApplyVisual();
        }

        public void SetLabel(string label)
        {
            if (_label != null)
            {
                _label.text = label;
            }
        }

        public void SetInteractable(bool interactable)
        {
            _interactable = interactable;
            ApplyVisual();
        }

        /// <summary>
        /// 渐隐渐显口子：显隐按钮。<paramref name="animated"/> 为真且物体在场景激活时按 <c>_fadeDuration</c> 淡入/淡出，
        /// 否则瞬切。淡出到 0 期间不响应点击（见 <see cref="Update"/>）。可重复调用打断上一段过渡。
        /// </summary>
        public void SetVisible(bool visible, bool animated = false)
        {
            EnsureRefs();
            float target = visible ? 1f : 0f;

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            if (!animated || _fadeDuration <= 0f || !gameObject.activeInHierarchy)
            {
                _visibility = target;
                ApplyVisual();
                return;
            }

            _fadeRoutine = StartCoroutine(FadeTo(target));
        }

        /// <summary>直接设置可见度系数 [0,1]（瞬时，无过渡），供外部逐帧驱动自定义过渡。</summary>
        public void SetVisibility(float visibility)
        {
            _visibility = Mathf.Clamp01(visibility);
            ApplyVisual();
        }

        private IEnumerator FadeTo(float target)
        {
            float start = _visibility;
            float t = 0f;
            while (t < _fadeDuration)
            {
                t += Time.deltaTime;
                _visibility = Mathf.Lerp(start, target, Mathf.Clamp01(t / _fadeDuration));
                ApplyVisual();
                yield return null;
            }

            _visibility = target;
            ApplyVisual();
            _fadeRoutine = null;
        }

        /// <summary>把 interactable 基色与可见度系数一并应用到底色块与文案上（alpha 相乘）。</summary>
        private void ApplyVisual()
        {
            if (_background != null)
            {
                Color baseColor = _interactable ? _normalColor : _disabledColor;
                baseColor.a *= _visibility;
                _background.color = baseColor;
            }

            if (_label != null)
            {
                Color labelColor = _labelBaseColor;
                labelColor.a *= _visibility;
                _label.color = labelColor;
            }
        }

        /// <summary>兜底解析 prefab 里的渲染体/文案/碰撞盒引用，容忍未在 prefab 里手动赋值的情况。</summary>
        private void EnsureRefs()
        {
            if (_background == null)
            {
                _background = GetComponent<SpriteRenderer>();
                if (_background == null)
                {
                    _background = gameObject.AddComponent<SpriteRenderer>();
                }
            }

            if (_background.sprite == null)
            {
                _background.sprite = Resources.Load<Sprite>("Sprites/UI/white");
            }

            if (_collider == null)
            {
                _collider = GetComponent<BoxCollider2D>();
                if (_collider == null)
                {
                    _collider = gameObject.AddComponent<BoxCollider2D>();
                }
            }

            _collider.size = Vector2.one;

            if (_label == null)
            {
                Transform t = transform.Find("Label");
                if (t != null)
                {
                    _label = t.GetComponent<TextMesh>();
                }
            }

            // 记录 prefab 里作者调好的文案原色，作为可见度相乘的基准（只捕获一次，避免捕到已被渐隐缩放过的 alpha）。
            if (!_labelBaseCaptured && _label != null)
            {
                _labelBaseColor = _label.color;
                _labelBaseCaptured = true;
            }
        }

        private void Update()
        {
            // 完全/接近隐藏时不接受点击，避免渐隐过程中的幽灵命中。
            if (!_interactable || _visibility < 0.5f || !WorldInput.PrimaryPressedThisFrame || _collider == null)
            {
                return;
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            Vector2 world = WorldInput.MouseWorld(cam);
            if (_collider.OverlapPoint(world))
            {
                _clicked?.Invoke();
            }
        }
    }
}
