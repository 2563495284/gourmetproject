using System;
using UnityEngine;

namespace GourmetProject.Game.Gameplay.Presentation
{
    /// <summary>
    /// 简单场景内按钮，避免战斗操作继续依赖 uGUI。
    /// 现以脚本根 prefab 形式存在：场景里摆好的固定按钮直接 <see cref="Configure"/>，
    /// 运行时动态按钮（主动道具）由控制器 Instantiate 后再 Configure。
    /// </summary>
    public sealed class WorldButtonView : MonoBehaviour
    {
        private SpriteRenderer _background;
        private TextMesh _label;
        private BoxCollider2D _collider;
        private Color _normalColor;
        private Color _disabledColor;
        private Action _clicked;
        private bool _interactable = true;
        private bool _built;

        /// <summary>构建（若未构建）并设置外观与点击回调。可重复调用以更新颜色/文案/回调。</summary>
        public void Configure(Vector2 size, string label, Color color, Action clicked)
        {
            EnsureBuilt(size);
            _clicked = clicked;
            _normalColor = color;
            _disabledColor = new Color(color.r * 0.45f, color.g * 0.45f, color.b * 0.45f, 0.75f);
            if (_background != null)
            {
                _background.color = _interactable ? _normalColor : _disabledColor;
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

        private void EnsureBuilt(Vector2 size)
        {
            transform.localScale = new Vector3(size.x, size.y, 1f);
            if (_built)
            {
                return;
            }

            _built = true;

            _background = gameObject.GetComponent<SpriteRenderer>();
            if (_background == null)
            {
                _background = gameObject.AddComponent<SpriteRenderer>();
            }

            _background.sprite = CreatePixelSprite();
            BattleSorting.Apply(_background, BattleSorting.WorldUi, BattleSorting.OrderButtonBg);
            SpriteRenderStyle.ApplyUnlitMaterial(_background);

            _collider = gameObject.GetComponent<BoxCollider2D>();
            if (_collider == null)
            {
                _collider = gameObject.AddComponent<BoxCollider2D>();
            }

            _collider.size = Vector2.one;

            GameObject textGo = new GameObject("Label");
            textGo.transform.SetParent(transform, false);
            textGo.transform.localPosition = new Vector3(0f, -0.08f, -0.01f);
            textGo.transform.localScale = new Vector3(0.08f / size.x, 0.08f / size.y, 1f);
            _label = textGo.AddComponent<TextMesh>();
            _label.text = string.Empty;
            _label.anchor = TextAnchor.MiddleCenter;
            _label.alignment = TextAlignment.Center;
            _label.color = Color.white;
            _label.fontSize = 48;
            _label.characterSize = 1f;
            var meshRenderer = textGo.GetComponent<MeshRenderer>();
            BattleSorting.Apply(meshRenderer, BattleSorting.WorldUi, BattleSorting.OrderButtonLabel);
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

        private static Sprite CreatePixelSprite()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}
