using System;
using UnityEngine;

namespace GourmetProject.Game.Gameplay.Presentation
{
    /// <summary>
    /// 场景内按钮：固定结构（底色块 + 文案 + 碰撞盒）摆在 prefab 里，由 <see cref="Configure"/> 喂尺寸/颜色/文案/回调。
    /// 尺寸是数据驱动的（不同按钮大小不一），运行时按传入 size 缩放根节点，文案子物体反向缩放保持世界字号恒定。
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

        private Color _normalColor = Color.white;
        private Color _disabledColor;
        private Action _clicked;
        private bool _interactable = true;

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
                _background.color = _interactable ? _normalColor : _disabledColor;
                BattleSorting.Apply(_background, BattleSorting.WorldUi, BattleSorting.OrderButtonBg);
                SpriteRenderStyle.ApplyUnlitMaterial(_background);
            }

            SetLabel(label);
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
            if (_background != null)
            {
                _background.color = interactable ? _normalColor : _disabledColor;
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
        }

        private void Update()
        {
            if (!_interactable || !WorldInput.PrimaryPressedThisFrame || _collider == null)
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
