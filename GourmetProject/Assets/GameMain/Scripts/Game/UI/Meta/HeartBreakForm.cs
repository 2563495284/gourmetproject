using System;
using DG.Tweening;
using GourmetProject.Game.UI.Common;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GourmetProject.Game.Tutorial;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>经营挑战未达标后的碎心演出；演出结束后由玩家点击“继续”推进。</summary>
    public sealed class HeartBreakForm : UGuiForm
    {
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _heartCountText;
        [SerializeField] private TMP_Text _centerHeartText;
        [SerializeField] private RectTransform _leftHalf;
        [SerializeField] private RectTransform _rightHalf;
        [SerializeField] private CanvasGroup _crackGroup;
        [SerializeField] private CanvasGroup _contentGroup;
        [SerializeField] private Button _continueButton;
        [SerializeField] private CanvasGroup _transitionGroup;
        [SerializeField] private RectTransform _transitionPanel;

        private Sequence _sequence;
        private Action _onComplete;
        private bool _completed;
        private bool _closing;
        private Vector2 _leftStart;
        private Vector2 _rightStart;
        private int _lostCount;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            _continueButton?.onClick.AddListener(OnContinue);
            _leftStart = _leftHalf != null ? _leftHalf.anchoredPosition : Vector2.zero;
            _rightStart = _rightHalf != null ? _rightHalf.anchoredPosition : Vector2.zero;
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            if (userData is not HeartBreakFormOpenArgs args)
            {
                CloseWithoutCallback();
                return;
            }

            _completed = false;
            _closing = false;
            _onComplete = args.OnComplete;
            _lostCount = Mathf.Max(1, args.BeforeHeartCount - args.AfterHeartCount);
            TutorialAnchorRegistry.Register(TutorialAnchorId.HeartBreak, _transitionPanel);
            Bind(args);
            GameApp.Audio.PlayLossFanfare();
            Play();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            _sequence?.Kill();
            _sequence = null;
            _onComplete = null;
            _closing = false;
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.HeartBreak, _transitionPanel);
            base.OnClose(isShutdown, userData);
        }

        private void Bind(HeartBreakFormOpenArgs args)
        {
            if (_titleText != null)
            {
                _titleText.text = "营业失败";
            }

            if (_heartCountText != null)
            {
                _heartCountText.text = HeartDisplayText.Build(args.AfterHeartCount, args.HeartCapacity);
            }

            if (_centerHeartText != null)
            {
                _centerHeartText.text = "♥";
                _centerHeartText.color = new Color(0.91f, 0.12f, 0.18f, 1f);
                _centerHeartText.rectTransform.localScale = Vector3.one;
            }

            ResetHalf(_leftHalf, _leftStart);
            ResetHalf(_rightHalf, _rightStart);
            if (_crackGroup != null)
            {
                _crackGroup.alpha = 0f;
            }

            if (_contentGroup != null)
            {
                _contentGroup.alpha = 1f;
            }

            if (_continueButton != null)
            {
                _continueButton.interactable = false;
            }

            if (_transitionGroup != null)
            {
                _transitionGroup.alpha = 0f;
                _transitionGroup.blocksRaycasts = false;
            }

            if (_transitionPanel != null)
            {
                _transitionPanel.localScale = Vector3.one * 0.92f;
            }
        }

        private void Play()
        {
            _sequence?.Kill();
            _sequence = DOTween.Sequence().SetUpdate(true).SetTarget(this);

            if (_transitionGroup != null)
            {
                _sequence.Append(DOTween.To(
                    () => _transitionGroup.alpha,
                    value => _transitionGroup.alpha = value,
                    1f,
                    0.18f).SetEase(Ease.OutQuad));
            }

            if (_transitionPanel != null)
            {
                _sequence.Join(_transitionPanel.DOScale(1f, 0.24f).SetEase(Ease.OutBack));
            }

            if (_centerHeartText != null)
            {
                _sequence.Join(_centerHeartText.rectTransform.DOPunchScale(Vector3.one * 0.2f, 0.28f, 5, 0.5f));
                _sequence.Append(DOTween.To(
                    () => _centerHeartText.color.a,
                    value =>
                    {
                        Color color = _centerHeartText.color;
                        color.a = value;
                        _centerHeartText.color = color;
                    },
                    0f,
                    0.08f));
                if (_lostCount >= 2)
                {
                    _sequence.AppendInterval(0.08f);
                    _sequence.AppendCallback(() =>
                    {
                        Color color = _centerHeartText.color;
                        color.a = 1f;
                        _centerHeartText.color = color;
                        _centerHeartText.rectTransform.localScale = Vector3.one;
                    });
                    _sequence.Append(_centerHeartText.rectTransform.DOPunchScale(Vector3.one * 0.2f, 0.28f, 5, 0.5f));
                    _sequence.Append(DOTween.To(
                        () => _centerHeartText.color.a,
                        value =>
                        {
                            Color color = _centerHeartText.color;
                            color.a = value;
                            _centerHeartText.color = color;
                        },
                        0f,
                        0.08f));
                }
            }

            if (_crackGroup != null)
            {
                _sequence.Join(DOTween.To(
                    () => _crackGroup.alpha,
                    value => _crackGroup.alpha = value,
                    1f,
                    0.08f));
            }

            if (_leftHalf != null)
            {
                Vector2 leftTarget = _leftHalf.anchoredPosition + new Vector2(-68f, -95f);
                _sequence.Append(DOTween.To(
                    () => _leftHalf.anchoredPosition,
                    value => _leftHalf.anchoredPosition = value,
                    leftTarget,
                    0.45f).SetEase(Ease.InQuad));
                _sequence.Join(_leftHalf.DORotate(new Vector3(0f, 0f, 24f), 0.45f).SetEase(Ease.InQuad));
            }

            if (_rightHalf != null)
            {
                Vector2 rightTarget = _rightHalf.anchoredPosition + new Vector2(68f, -95f);
                _sequence.Join(DOTween.To(
                    () => _rightHalf.anchoredPosition,
                    value => _rightHalf.anchoredPosition = value,
                    rightTarget,
                    0.45f).SetEase(Ease.InQuad));
                _sequence.Join(_rightHalf.DORotate(new Vector3(0f, 0f, -24f), 0.45f).SetEase(Ease.InQuad));
            }

            _sequence.AppendInterval(0.16f);
            _sequence.OnComplete(() =>
            {
                if (_transitionGroup != null)
                {
                    _transitionGroup.blocksRaycasts = true;
                }

                if (_continueButton != null)
                {
                    _continueButton.interactable = true;
                }

            });
        }

        private void OnContinue()
        {
            if (_completed || _closing || (_continueButton != null && !_continueButton.interactable))
            {
                return;
            }

            _closing = true;
            if (_continueButton != null)
            {
                _continueButton.interactable = false;
            }

            if (_transitionGroup != null)
            {
                _transitionGroup.blocksRaycasts = false;
            }

            _sequence?.Kill();
            _sequence = DOTween.Sequence().SetUpdate(true).SetTarget(this);
            if (_transitionGroup != null)
            {
                _sequence.Append(DOTween.To(
                    () => _transitionGroup.alpha,
                    value => _transitionGroup.alpha = value,
                    0f,
                    0.18f).SetEase(Ease.InQuad));
            }

            if (_transitionPanel != null)
            {
                _sequence.Join(_transitionPanel.DOScale(0.96f, 0.18f).SetEase(Ease.InQuad));
            }

            _sequence.OnComplete(CompleteOnce);
        }

        private static void ResetHalf(RectTransform half, Vector2 anchoredPosition)
        {
            if (half == null)
            {
                return;
            }

            half.localRotation = Quaternion.identity;
            half.localScale = Vector3.one;
            half.anchoredPosition = anchoredPosition;
        }

        private void CompleteOnce()
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            Action callback = _onComplete;
            _onComplete = null;
            GameApp.UI.CloseUIForm(UIForm);
            callback?.Invoke();
        }

        private void CloseWithoutCallback()
        {
            _completed = true;
            _onComplete = null;
            GameApp.UI.CloseUIForm(UIForm);
        }
    }

    public sealed class HeartBreakFormOpenArgs
    {
        public HeartBreakFormOpenArgs(
            int beforeHeartCount,
            int afterHeartCount,
            int heartCapacity,
            bool isTerminal,
            Action onComplete = null)
        {
            BeforeHeartCount = beforeHeartCount;
            AfterHeartCount = afterHeartCount;
            HeartCapacity = System.Math.Max(1, heartCapacity);
            IsTerminal = isTerminal;
            OnComplete = onComplete;
        }

        public int BeforeHeartCount { get; }

        public int AfterHeartCount { get; }

        public int HeartCapacity { get; }

        public bool IsTerminal { get; }

        public Action OnComplete { get; }
    }
}
