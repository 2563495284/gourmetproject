using System;
using DG.Tweening;
using GourmetProject.Game.Tutorial;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>经营挑战未达标后的碎心演出；演出结束后由玩家点击“继续”推进。</summary>
    public sealed class HeartBreakForm : UGuiForm
    {
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private HeartBreakHeartRow _heartRow;
        [SerializeField] private CanvasGroup _contentGroup;
        [SerializeField] private Button _continueButton;
        [SerializeField] private CanvasGroup _transitionGroup;
        [SerializeField] private RectTransform _transitionPanel;

        private Sequence _sequence;
        private Action _onComplete;
        private bool _completed;
        private bool _closing;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            _continueButton?.onClick.AddListener(OnContinue);
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
            TutorialAnchorRegistry.Register(TutorialAnchorId.HeartBreak, _transitionPanel);
            Bind(args);
            GameApp.Audio.PlayLossFanfare();
            Play();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            _sequence?.Kill();
            _sequence = null;
            _heartRow?.StopAndReset();
            _onComplete = null;
            _closing = false;
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.HeartBreak, _transitionPanel);
            base.OnClose(isShutdown, userData);
        }

        internal void Bind(HeartBreakFormOpenArgs args)
        {
            if (_titleText != null)
            {
                _titleText.text = "营业失败";
            }

            _heartRow?.Bind(
                args.BeforeHeartCount,
                args.AfterHeartCount,
                args.HeartCapacity,
                args.IsTerminal);

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

        internal void Play()
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

            _sequence.AppendInterval(0.18f);
            _heartRow?.AppendLossAnimation(_sequence);
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
