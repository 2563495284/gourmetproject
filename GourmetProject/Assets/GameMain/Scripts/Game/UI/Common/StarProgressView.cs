using System;
using Coffee.UIEffects;
using DG.Tweening;
using GourmetProject.Game.Run;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Common
{
    /// <summary>固定六槽、按行优先顺序显示星级评鉴进度。</summary>
    public sealed class StarProgressView : MonoBehaviour
    {
        [SerializeField] private Image[] _stars = new Image[GameRun.MaxRatingStars];
        [SerializeField] private Sprite _starSprite;
        [SerializeField] private Color _earnedColor = Color.white;
        [SerializeField] private Color _lockedColor = new Color(0.42f, 0.39f, 0.35f, 0.28f);
        [SerializeField] private bool _enableHudEffects;
        [SerializeField] private UIEffect[] _starEffects = Array.Empty<UIEffect>();
        [SerializeField] private UIEffectTweener[] _starShinyTweeners = Array.Empty<UIEffectTweener>();
        [SerializeField] private StarCardSparkleGraphic _sparkles;

        private Tween[] _breathTweens = Array.Empty<Tween>();
        private Tween[] _awardTweens = Array.Empty<Tween>();
        private RectTransform[] _starRects = Array.Empty<RectTransform>();
        private bool _hasBound;

        public int EarnedStars { get; private set; }

        public int SlotCount => _stars?.Length ?? 0;

        public bool HudEffectsEnabled => _enableHudEffects;

        public StarCardSparkleGraphic Sparkles => _sparkles;

        public void Bind(int earnedStars)
        {
            int previousStars = EarnedStars;
            EarnedStars = Mathf.Clamp(earnedStars, 0, GameRun.MaxRatingStars);
            if (_stars == null)
            {
                _hasBound = true;
                return;
            }

            EnsureTweenArrays();
            for (int i = 0; i < _stars.Length; i++)
            {
                Image star = _stars[i];
                if (star == null)
                {
                    continue;
                }

                if (_starSprite != null)
                {
                    star.sprite = _starSprite;
                }

                star.preserveAspect = true;
                bool earned = i < EarnedStars;
                star.color = earned ? _earnedColor : _lockedColor;
                SetStarEffectState(i, earned);
            }

            _sparkles?.SetProgress(EarnedStars, StarRects());
            RefreshBreathing();

            bool playIncrease = _hasBound
                && _enableHudEffects
                && Application.isPlaying
                && isActiveAndEnabled
                && EarnedStars > previousStars;
            _hasBound = true;
            if (!playIncrease)
            {
                return;
            }

            for (int i = previousStars; i < EarnedStars && i < _stars.Length; i++)
            {
                PlayEarnedStar(i, (i - previousStars) * 0.08f);
            }
        }

        public RectTransform GetStarRect(int index)
        {
            return _stars != null && index >= 0 && index < _stars.Length && _stars[index] != null
                ? _stars[index].rectTransform
                : null;
        }

        private void OnEnable()
        {
            if (_hasBound)
            {
                for (int i = 0; i < SlotCount; i++)
                {
                    SetStarEffectState(i, i < EarnedStars);
                }

                _sparkles?.SetProgress(EarnedStars, StarRects());
                RefreshBreathing();
            }
        }

        private void OnDisable()
        {
            KillAllTweens();
            ResetStarScales();
        }

        private void OnDestroy()
        {
            KillAllTweens();
        }

        private void PlayEarnedStar(int index, float delay)
        {
            RectTransform starRect = GetStarRect(index);
            if (starRect == null)
            {
                return;
            }

            StopBreathing(index);
            StopAwardTween(index);
            starRect.localScale = Vector3.one;

            Sequence sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetTarget(this);
            if (delay > 0f)
            {
                sequence.AppendInterval(delay);
            }

            sequence.AppendCallback(() =>
            {
                if (starRect != null)
                {
                    _sparkles?.Burst(starRect, 12);
                }
            });
            sequence.Append(starRect.DOPunchScale(Vector3.one * 0.18f, 0.38f, 8, 0.58f));
            sequence.OnComplete(() =>
            {
                if (index >= 0 && index < _awardTweens.Length)
                {
                    _awardTweens[index] = null;
                }

                if (starRect != null)
                {
                    starRect.localScale = Vector3.one;
                    StartBreathing(index);
                }
            });
            _awardTweens[index] = sequence;
        }

        private void RefreshBreathing()
        {
            EnsureTweenArrays();
            for (int i = 0; i < SlotCount; i++)
            {
                if (_enableHudEffects
                    && Application.isPlaying
                    && isActiveAndEnabled
                    && i < EarnedStars
                    && _awardTweens[i] == null)
                {
                    StartBreathing(i);
                }
                else if (!_enableHudEffects || i >= EarnedStars)
                {
                    StopBreathing(i);
                }
            }
        }

        private void StartBreathing(int index)
        {
            if (!_enableHudEffects
                || !Application.isPlaying
                || !isActiveAndEnabled
                || index < 0
                || index >= EarnedStars)
            {
                return;
            }

            EnsureTweenArrays();
            if (_breathTweens[index] != null && _breathTweens[index].active)
            {
                return;
            }

            RectTransform starRect = GetStarRect(index);
            if (starRect == null)
            {
                return;
            }

            starRect.localScale = Vector3.one;
            _breathTweens[index] = starRect
                .DOScale(1.035f, 1.5f)
                .SetDelay(index * 0.12f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true)
                .SetTarget(this);
        }

        private void StopBreathing(int index)
        {
            if (index < 0 || index >= _breathTweens.Length)
            {
                return;
            }

            _breathTweens[index]?.Kill();
            _breathTweens[index] = null;
            RectTransform starRect = GetStarRect(index);
            if (starRect != null)
            {
                starRect.localScale = Vector3.one;
            }
        }

        private void StopAwardTween(int index)
        {
            if (index < 0 || index >= _awardTweens.Length)
            {
                return;
            }

            _awardTweens[index]?.Kill();
            _awardTweens[index] = null;
        }

        private void SetStarEffectState(int index, bool earned)
        {
            if (!_enableHudEffects)
            {
                earned = false;
            }

            if (_starEffects != null && index >= 0 && index < _starEffects.Length && _starEffects[index] != null)
            {
                _starEffects[index].enabled = earned;
            }

            if (_starShinyTweeners != null
                && index >= 0
                && index < _starShinyTweeners.Length
                && _starShinyTweeners[index] != null)
            {
                _starShinyTweeners[index].enabled = earned;
            }
        }

        private RectTransform[] StarRects()
        {
            int count = SlotCount;
            if (_starRects.Length != count)
            {
                _starRects = new RectTransform[count];
            }

            for (int i = 0; i < _starRects.Length; i++)
            {
                _starRects[i] = GetStarRect(i);
            }

            return _starRects;
        }

        private void EnsureTweenArrays()
        {
            int count = SlotCount;
            if (_breathTweens.Length != count)
            {
                _breathTweens = new Tween[count];
            }

            if (_awardTweens.Length != count)
            {
                _awardTweens = new Tween[count];
            }
        }

        private void KillAllTweens()
        {
            for (int i = 0; i < _breathTweens.Length; i++)
            {
                _breathTweens[i]?.Kill();
                _breathTweens[i] = null;
            }

            for (int i = 0; i < _awardTweens.Length; i++)
            {
                _awardTweens[i]?.Kill();
                _awardTweens[i] = null;
            }
        }

        private void ResetStarScales()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                RectTransform starRect = GetStarRect(i);
                if (starRect != null)
                {
                    starRect.localScale = Vector3.one;
                }
            }
        }

#if UNITY_EDITOR
        internal void EditorConfigure(Image[] stars, Sprite sprite)
        {
            _stars = stars ?? Array.Empty<Image>();
            _starSprite = sprite;
            Bind(0);
        }
#endif
    }
}
