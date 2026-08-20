using UnityEngine;

namespace GourmetProject.Game.UI.Common
{
    [CreateAssetMenu(fileName = "ToastTheme", menuName = "Gourmet Project/UI/Toast Theme")]
    public sealed class ToastTheme : ScriptableObject
    {
        [Header("Sprites")]
        [SerializeField] private Sprite _bubble;
        [SerializeField] private Sprite _tail;
        [SerializeField] private Sprite _infoIcon;
        [SerializeField] private Sprite _successIcon;
        [SerializeField] private Sprite _warningIcon;

        [Header("Style")]
        [SerializeField] private Color _textColor = new Color32(91, 57, 38, 255);

        [Header("Layout")]
        [SerializeField, Min(1f)] private float _minimumWidth = 320f;
        [SerializeField, Min(1f)] private float _maximumWidth = 680f;
        [SerializeField, Min(1f)] private float _singleLineHeight = 104f;
        [SerializeField, Min(1f)] private float _doubleLineHeight = 148f;
        [SerializeField, Min(1f)] private float _iconSize = 44f;

        [Header("Motion")]
        [SerializeField, Min(0.01f)] private float _enterDuration = 0.16f;
        [SerializeField, Min(0f)] private float _holdDuration = 0.65f;
        [SerializeField, Min(0.01f)] private float _exitDuration = 0.48f;
        [SerializeField] private float _enterOffsetY = -16f;

        public Sprite Bubble => _bubble;
        public Sprite Tail => _tail;
        public Color TextColor => _textColor;
        public float MinimumWidth => _minimumWidth;
        public float MaximumWidth => _maximumWidth;
        public float SingleLineHeight => _singleLineHeight;
        public float DoubleLineHeight => _doubleLineHeight;
        public float IconSize => _iconSize;
        public float EnterDuration => _enterDuration;
        public float HoldDuration => _holdDuration;
        public float ExitDuration => _exitDuration;
        public float EnterOffsetY => _enterOffsetY;

        public Sprite ResolveIcon(ToastKind kind)
        {
            return kind switch
            {
                ToastKind.Success => _successIcon,
                ToastKind.Warning => _warningIcon,
                _ => _infoIcon,
            };
        }
    }
}
