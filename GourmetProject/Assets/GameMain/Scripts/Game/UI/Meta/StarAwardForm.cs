using System;
using DG.Tweening;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Common;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>星级评鉴结算后的单星获得演出；演出结束后由玩家点击继续。</summary>
    public sealed class StarAwardForm : UGuiForm
    {
        [SerializeField] private CanvasGroup _transitionGroup;
        [SerializeField] private RectTransform _transitionPanel;
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _messageText;
        [SerializeField] private Image _largeStar;
        [SerializeField] private RectTransform _glow;
        [SerializeField] private StarProgressView _progress;
        [SerializeField] private Button _continueButton;

        private Sequence _sequence;
        private Tween _glowTween;
        private Action _onContinue;
        private bool _completed;
        private bool _closing;
        private bool _largeStarHomeCaptured;
        private Vector2 _largeStarHome;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            _continueButton?.onClick.AddListener(OnContinue);
            CaptureLargeStarHome();
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            if (userData is not StarAwardFormOpenArgs args)
            {
                CloseWithoutCallback();
                return;
            }

            _completed = false;
            _closing = false;
            _onContinue = args.OnContinue;
            Bind(args);
            Play(args);
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            _sequence?.Kill();
            _sequence = null;
            _glowTween?.Kill();
            _glowTween = null;
            _onContinue = null;
            base.OnClose(isShutdown, userData);
        }

        internal void Bind(StarAwardFormOpenArgs args)
        {
            if (_titleText != null)
            {
                _titleText.text = "星级评鉴完成";
            }

            if (_messageText != null)
            {
                _messageText.text = "获得 1 颗星";
            }

            _progress?.Bind(args.BeforeStars);
            if (_transitionGroup != null)
            {
                _transitionGroup.alpha = 0f;
                _transitionGroup.blocksRaycasts = true;
            }

            if (_transitionPanel != null)
            {
                _transitionPanel.localScale = Vector3.one * 0.92f;
            }

            if (_largeStar != null)
            {
                CaptureLargeStarHome();
                _largeStar.gameObject.SetActive(true);
                _largeStar.rectTransform.anchoredPosition = _largeStarHome;
                _largeStar.rectTransform.localScale = Vector3.one * 0.28f;
                _largeStar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -18f);
                _largeStar.color = Color.white;
            }

            if (_continueButton != null)
            {
                _continueButton.interactable = false;
            }

            if (_glow != null)
            {
                _glow.localRotation = Quaternion.identity;
            }
        }

        internal void Play(StarAwardFormOpenArgs args)
        {
            _sequence?.Kill();
            _glowTween?.Kill();
            _sequence = DOTween.Sequence().SetUpdate(true).SetTarget(this);

            if (_transitionGroup != null)
            {
                _sequence.Append(_transitionGroup.DOFade(1f, 0.18f).SetEase(Ease.OutQuad));
            }

            if (_transitionPanel != null)
            {
                _sequence.Join(_transitionPanel.DOScale(1f, 0.25f).SetEase(Ease.OutBack));
            }

            if (_largeStar != null)
            {
                RectTransform starRect = _largeStar.rectTransform;
                _sequence.Append(starRect.DOScale(1f, 0.42f).SetEase(Ease.OutBack));
                _sequence.Join(starRect.DOLocalRotate(Vector3.zero, 0.42f).SetEase(Ease.OutCubic));
                _sequence.AppendCallback(() => GameApp.Audio.PlayPickup());
                _sequence.AppendInterval(0.24f);

                RectTransform target = _progress?.GetStarRect(args.AfterStars - 1);
                if (target != null)
                {
                    _sequence.Append(starRect.DOMove(target.position, 0.42f).SetEase(Ease.InOutCubic));
                    _sequence.Join(starRect.DOScale(0.42f, 0.42f).SetEase(Ease.InCubic));
                    _sequence.AppendCallback(() =>
                    {
                        _progress.Bind(args.AfterStars);
                        _largeStar.gameObject.SetActive(false);
                    });
                    _sequence.Append(target.DOPunchScale(Vector3.one * 0.24f, 0.32f, 8, 0.58f));
                }
                else
                {
                    _sequence.AppendCallback(() => _progress?.Bind(args.AfterStars));
                }
            }
            else
            {
                _sequence.AppendCallback(() => _progress?.Bind(args.AfterStars));
            }

            if (_glow != null)
            {
                _glowTween = _glow.DOLocalRotate(new Vector3(0f, 0f, 90f), 2.4f)
                    .SetEase(Ease.Linear)
                    .SetLoops(-1, LoopType.Incremental)
                    .SetUpdate(true)
                    .SetTarget(this);
            }

            _sequence.AppendInterval(0.12f);
            _sequence.OnComplete(() =>
            {
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
            _sequence?.Kill();
            _sequence = DOTween.Sequence().SetUpdate(true).SetTarget(this);
            if (_transitionGroup != null)
            {
                _sequence.Append(_transitionGroup.DOFade(0f, 0.18f).SetEase(Ease.InQuad));
            }
            else
            {
                _sequence.AppendInterval(0.01f);
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
            Action callback = _onContinue;
            _onContinue = null;
            GameApp.UI.CloseUIForm(UIForm);
            callback?.Invoke();
        }

        private void CloseWithoutCallback()
        {
            _completed = true;
            _onContinue = null;
            GameApp.UI.CloseUIForm(UIForm);
        }

        private void CaptureLargeStarHome()
        {
            if (_largeStarHomeCaptured || _largeStar == null)
            {
                return;
            }

            _largeStarHome = _largeStar.rectTransform.anchoredPosition;
            _largeStarHomeCaptured = true;
        }
    }

    public sealed class StarAwardFormOpenArgs
    {
        public StarAwardFormOpenArgs(
            string battleKey,
            int beforeStars,
            int afterStars,
            Action onContinue = null)
        {
            BattleKey = battleKey ?? string.Empty;
            BeforeStars = Math.Clamp(beforeStars, 0, GameRun.MaxRatingStars);
            AfterStars = Math.Clamp(afterStars, BeforeStars, GameRun.MaxRatingStars);
            OnContinue = onContinue;
        }

        public string BattleKey { get; }

        public int BeforeStars { get; }

        public int AfterStars { get; }

        public Action OnContinue { get; }
    }
}
