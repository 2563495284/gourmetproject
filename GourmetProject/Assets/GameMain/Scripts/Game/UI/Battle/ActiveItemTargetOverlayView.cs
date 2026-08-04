using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Battle
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class ActiveItemTargetOverlayView : MonoBehaviour
    {
        [SerializeField] private RectTransform _panel;
        [SerializeField] private TMP_Text _promptText;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private Button _targetButtonPrefab;

        public RectTransform Panel => _panel;
        public TMP_Text PromptText => _promptText;
        public Button ConfirmButton => _confirmButton;
        public Button TargetButtonPrefab => _targetButtonPrefab;

        private void Awake()
        {
        }

        public void ConfigureButtons(Action onConfirm, Action onCancel)
        {
            BindButton(_confirmButton, onConfirm);
            BindButton(_cancelButton, onCancel);
        }

        public void StretchToParent()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        public TMP_Text LabelOf(Button button)
        {
            return button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        }

        private static void BindButton(Button button, Action action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            if (action != null)
            {
                button.onClick.AddListener(() => action());
            }
        }
    }
}
