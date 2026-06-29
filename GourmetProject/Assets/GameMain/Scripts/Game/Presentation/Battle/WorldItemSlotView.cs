using System;
using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 战斗世界层道具槽：用于原型图右侧被动道具栏与右下角主动道具栏。
    /// 固定结构优先放 prefab；引用缺失时运行时补齐，便于先接入显示再逐步美术化。
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class WorldItemSlotView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _background;
        [SerializeField] private SpriteRenderer _icon;
        [SerializeField] private TextMesh _badge;
        [SerializeField] private TextMesh _fallbackLabel;
        [SerializeField] private BoxCollider2D _collider;

        private Action _clicked;
        private bool _interactable;
        private Sprite _whiteSprite;

        public void Bind(
            Vector2 size,
            Sprite icon,
            string fallbackLabel,
            string badgeText,
            Color frameColor,
            bool interactable,
            Action clicked)
        {
            EnsureRefs();

            transform.localScale = new Vector3(size.x, size.y, 1f);
            _clicked = clicked;
            _interactable = interactable && clicked != null;

            ConfigureBackground(frameColor, icon != null || !string.IsNullOrEmpty(fallbackLabel));
            ConfigureIcon(icon);
            ConfigureText(_fallbackLabel, icon == null ? fallbackLabel : string.Empty, 0.18f, new Vector3(0f, -0.03f, -0.02f));
            ConfigureText(_badge, badgeText, 0.11f, new Vector3(0.29f, -0.27f, -0.03f));
            _collider.size = Vector2.one;
        }

        public void SetInteractable(bool interactable)
        {
            _interactable = interactable && _clicked != null;
            if (_icon != null)
            {
                _icon.color = _interactable ? Color.white : new Color(0.62f, 0.62f, 0.62f, 0.9f);
            }
        }

        private void ConfigureBackground(Color frameColor, bool occupied)
        {
            _background.sprite = WhiteSprite;
            _background.drawMode = SpriteDrawMode.Sliced;
            _background.size = Vector2.one;
            _background.color = occupied ? frameColor : new Color(0.18f, 0.16f, 0.14f, 0.35f);
            BattleSorting.Apply(_background, BattleSorting.WorldUi, BattleSorting.OrderButtonBg);
            SpriteRenderStyle.ApplyUnlitMaterial(_background);
        }

        private void ConfigureIcon(Sprite sprite)
        {
            _icon.sprite = sprite;
            _icon.color = _interactable ? Color.white : new Color(0.62f, 0.62f, 0.62f, 0.9f);
            _icon.gameObject.SetActive(sprite != null);
            BattleSorting.Apply(_icon, BattleSorting.WorldUi, BattleSorting.OrderButtonLabel);
            SpriteRenderStyle.ApplyUnlitMaterial(_icon);

            if (sprite == null)
            {
                return;
            }

            Vector2 bounds = sprite.bounds.size;
            float max = Mathf.Max(bounds.x, bounds.y);
            float scale = max > 0f ? 0.76f / max : 0.76f;
            _icon.transform.localScale = new Vector3(scale, scale, 1f);
            _icon.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        }

        private static void ConfigureText(TextMesh text, string value, float size, Vector3 localPosition)
        {
            if (text == null)
            {
                return;
            }

            text.text = value ?? string.Empty;
            text.characterSize = size;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.black;
            text.transform.localPosition = localPosition;
            Renderer renderer = text.GetComponent<Renderer>();
            BattleSorting.Apply(renderer, BattleSorting.WorldUi, BattleSorting.OrderButtonLabel + 1);
        }

        private void EnsureRefs()
        {
            if (_collider == null)
            {
                _collider = GetComponent<BoxCollider2D>();
                if (_collider == null)
                {
                    _collider = gameObject.AddComponent<BoxCollider2D>();
                }
            }

            if (_background == null)
            {
                _background = EnsureSpriteChild("Background");
            }

            if (_icon == null)
            {
                _icon = EnsureSpriteChild("Icon");
            }

            if (_badge == null)
            {
                _badge = EnsureTextChild("Badge");
            }

            if (_fallbackLabel == null)
            {
                _fallbackLabel = EnsureTextChild("FallbackLabel");
            }
        }

        private SpriteRenderer EnsureSpriteChild(string childName)
        {
            Transform child = transform.Find(childName);
            if (child == null)
            {
                var go = new GameObject(childName);
                child = go.transform;
                child.SetParent(transform, false);
            }

            SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
            return renderer != null ? renderer : child.gameObject.AddComponent<SpriteRenderer>();
        }

        private TextMesh EnsureTextChild(string childName)
        {
            Transform child = transform.Find(childName);
            if (child == null)
            {
                var go = new GameObject(childName);
                child = go.transform;
                child.SetParent(transform, false);
            }

            TextMesh text = child.GetComponent<TextMesh>();
            return text != null ? text : child.gameObject.AddComponent<TextMesh>();
        }

        private Sprite WhiteSprite
        {
            get
            {
                if (_whiteSprite == null)
                {
                    _whiteSprite = Resources.Load<Sprite>("Sprites/UI/white");
                }

                return _whiteSprite;
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
