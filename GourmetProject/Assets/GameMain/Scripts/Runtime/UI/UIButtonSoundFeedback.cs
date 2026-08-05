using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GourmetProject.Runtime.UI
{
    /// <summary>
    /// 为 uGUI Button 提供统一点击音效。运行时自动安装，不依赖 Button.onClick，
    /// 因而不会被业务代码的 RemoveAllListeners 一并移除。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class UIButtonSoundFeedback : MonoBehaviour, IPointerClickHandler, ISubmitHandler
    {
        private enum SoundKind
        {
            Automatic,
            Button,
            Cancel,
        }

        [SerializeField] private SoundKind _soundKind = SoundKind.Automatic;

        private Button _button;

        private void Awake()
        {
            _button = GetComponent<Button>();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            PlayIfInteractable();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            PlayIfInteractable();
        }

        private void PlayIfInteractable()
        {
            if (_button == null)
            {
                _button = GetComponent<Button>();
            }

            if (_button == null || !_button.IsInteractable())
            {
                return;
            }

            bool cancel = _soundKind == SoundKind.Cancel
                || (_soundKind == SoundKind.Automatic && IsCancelButtonName(gameObject.name));
            if (cancel)
            {
                GameApp.Audio.PlayCancelClick();
            }
            else
            {
                GameApp.Audio.PlayButtonClick();
            }
        }

        internal static void Install(Transform root)
        {
            if (root == null)
            {
                return;
            }

            Button[] buttons = root.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button != null && button.GetComponent<UIButtonSoundFeedback>() == null)
                {
                    button.gameObject.AddComponent<UIButtonSoundFeedback>();
                }
            }

            if (root.GetComponent<UIButtonSoundFeedbackInstaller>() == null)
            {
                root.gameObject.AddComponent<UIButtonSoundFeedbackInstaller>();
            }
        }

        private static bool IsCancelButtonName(string buttonName)
        {
            return Contains(buttonName, "Cancel")
                || Contains(buttonName, "Back")
                || Contains(buttonName, "Close");
        }

        private static bool Contains(string value, string token)
        {
            return value?.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>定期补挂运行时动态生成的按钮。</summary>
    [DisallowMultipleComponent]
    internal sealed class UIButtonSoundFeedbackInstaller : MonoBehaviour
    {
        private const float ScanInterval = 0.35f;
        private float _nextScanTime;

        private void OnEnable()
        {
            UIButtonSoundFeedback.Install(transform);
            _nextScanTime = Time.unscaledTime + ScanInterval;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextScanTime)
            {
                return;
            }

            _nextScanTime = Time.unscaledTime + ScanInterval;
            UIButtonSoundFeedback.Install(transform);
        }
    }
}
