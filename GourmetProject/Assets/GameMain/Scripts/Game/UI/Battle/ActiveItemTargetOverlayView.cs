using System;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Battle
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class ActiveItemTargetOverlayView : MonoBehaviour
    {
        [SerializeField] private RectTransform _panel;
        [SerializeField] private Text _promptText;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private Button _targetButtonPrefab;

        public RectTransform Panel => _panel;
        public Text PromptText => _promptText;
        public Button ConfirmButton => _confirmButton;
        public Button TargetButtonPrefab => _targetButtonPrefab;

        private void Awake()
        {
            ValidateStructure();
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

        public Text LabelOf(Button button)
        {
            return button != null ? button.GetComponentInChildren<Text>(true) : null;
        }

        private void ValidateStructure()
        {
            if (_panel == null)
            {
                _panel = transform.Find("Panel") as RectTransform;
            }

            if (_promptText == null && _panel != null)
            {
                Transform prompt = _panel.Find("Prompt");
                _promptText = prompt != null ? prompt.GetComponent<Text>() : null;
            }

            if (_confirmButton == null && _panel != null)
            {
                Transform confirm = _panel.Find("ConfirmButton");
                _confirmButton = confirm != null ? confirm.GetComponent<Button>() : null;
            }

            if (_cancelButton == null && _panel != null)
            {
                Transform cancel = _panel.Find("CancelButton");
                _cancelButton = cancel != null ? cancel.GetComponent<Button>() : null;
            }

            if (_targetButtonPrefab == null && _panel != null)
            {
                Transform targetButton = _panel.Find("TargetButtonTemplate");
                _targetButtonPrefab = targetButton != null ? targetButton.GetComponent<Button>() : null;
            }

            if (_panel == null || _promptText == null || _confirmButton == null || _cancelButton == null || _targetButtonPrefab == null)
            {
                Debug.LogError($"{nameof(ActiveItemTargetOverlayView)} prefab 缺少 Panel/Prompt/ConfirmButton/CancelButton/TargetButtonTemplate。", this);
            }
            else
            {
                _targetButtonPrefab.gameObject.SetActive(false);
            }
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
