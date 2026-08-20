using System;
using System.Collections.Generic;
using System.Threading;
using DG.Tweening;
using GourmetProject.Game.Presentation.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Battle
{
    /// <summary>BattleForm 顶层 Boss 演出：全屏输入拦截、对白、手掌和短 Cue。</summary>
    public sealed class BossDebuffPresentationView : MonoBehaviour
    {
        private const float TypeInterval = 0.045f;
        private const float DialogueHold = 1.05f;
        private const float DialogueFade = 0.22f;

        [Header("Prefab hierarchy")]
        [SerializeField] private RectTransform _overlay;
        [SerializeField] private CanvasGroup _blocker;
        [SerializeField] private RectTransform _bubble;
        [SerializeField] private CanvasGroup _bubbleGroup;
        [SerializeField] private TMP_Text _dialogueText;
        [SerializeField] private RectTransform _hand;
        [SerializeField] private CanvasGroup _handGroup;
        [SerializeField] private Image _handImage;
        [SerializeField] private RectTransform _grabbedDish;
        [SerializeField] private CanvasGroup _grabbedDishGroup;
        [SerializeField] private Image _grabbedDishImage;
        [SerializeField] private RectTransform _cue;
        [SerializeField] private CanvasGroup _cueGroup;
        [SerializeField] private TMP_Text _cueText;

        [Header("Designer assets")]
        [SerializeField] private Sprite _pointHandSprite;
        [SerializeField] private Sprite _grabHandSprite;

        private CancellationTokenSource _animationCts;
        private readonly System.Random _cosmeticRandom = new System.Random();
        private bool _isBound;
        private int _playbackVersion;

        public bool IsPlaying { get; private set; }

        public void EnsureBuilt()
        {
            if (_isBound)
            {
                _overlay.SetAsLastSibling();
                return;
            }

            if (!HasCompletePrefabBindings(out string missingBinding))
            {
                throw new MissingReferenceException(
                    $"BossDebuffPresentationView requires prefab binding '{missingBinding}'. " +
                    "Edit BossDebuffPresentationOverlay.prefab instead of constructing UI at runtime.");
            }

            _isBound = true;
            _blocker.interactable = false;
            _blocker.blocksRaycasts = false;
            HideVisuals();
            _overlay.SetAsLastSibling();
        }

        public async Awaitable PlayLockedAsync(
            Func<CancellationToken, Awaitable> sequence,
            CancellationToken externalToken)
        {
            EnsureBuilt();
            CancelCurrent();
            int playbackVersion = ++_playbackVersion;
            _animationCts = CancellationTokenSource.CreateLinkedTokenSource(
                externalToken,
                destroyCancellationToken);
            CancellationToken token = _animationCts.Token;
            IsPlaying = true;
            _overlay.SetAsLastSibling();
            _blocker.interactable = true;
            _blocker.blocksRaycasts = true;
            try
            {
                if (sequence != null)
                {
                    await sequence(token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (_playbackVersion == playbackVersion)
                {
                    IsPlaying = false;
                    if (_blocker != null)
                    {
                        _blocker.interactable = false;
                        _blocker.blocksRaycasts = false;
                    }

                    HideVisuals();
                    _animationCts?.Dispose();
                    _animationCts = null;
                }
            }
        }

        public async Awaitable ShowDialogueAsync(
            string dialogue,
            CancellationToken token,
            float extraHoldSeconds = 0f)
        {
            if (string.IsNullOrWhiteSpace(dialogue))
            {
                return;
            }

            EnsureBuilt();
            _bubble.anchoredPosition = new Vector2(
                RandomRange(-80f, 80f),
                RandomRange(205f, 285f));
            _dialogueText.text = dialogue;
            _dialogueText.maxVisibleCharacters = 0;
            _dialogueText.ForceMeshUpdate();
            int characters = Mathf.Max(1, _dialogueText.textInfo.characterCount);
            _bubbleGroup.alpha = 1f;
            _bubble.localScale = Vector3.one * 0.72f;
            Tween enter = _bubble.DOScale(1f, 0.22f)
                .SetEase(Ease.OutBack)
                .SetUpdate(true)
                .SetLink(_bubble.gameObject);
            await AwaitTweenAsync(enter, token);

            for (int i = 1; i <= characters; i++)
            {
                token.ThrowIfCancellationRequested();
                _dialogueText.maxVisibleCharacters = i;
                await WaitUnscaledAsync(TypeInterval, token);
            }

            await WaitUnscaledAsync(DialogueHold + Mathf.Max(0f, extraHoldSeconds), token);
            Tween exit = _bubbleGroup.DOFade(0f, DialogueFade)
                .SetUpdate(true)
                .SetLink(_bubble.gameObject);
            await AwaitTweenAsync(exit, token);
        }

        public async Awaitable PointAsync(Vector2 screenPoint, CancellationToken token, float hold = 0.38f)
        {
            EnsureBuilt();
            Vector2 target = ScreenToLocal(screenPoint);
            ConfigureHand(_pointHandSprite ?? _grabHandSprite, target, pivotY: 0.02f, minimumHeight: 620f);
            Vector2 above = HandOffscreenPosition(target.x);
            _hand.anchoredPosition = above;
            _hand.localRotation = Quaternion.identity;
            _handGroup.alpha = 1f;
            _hand.SetAsLastSibling();
            Sequence sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(_hand.gameObject)
                .Append(_hand.DOAnchorPos(target, 0.36f).SetEase(Ease.OutCubic))
                .Append(_hand.DOPunchAnchorPos(new Vector2(0f, -12f), 0.20f, 3, 0.2f))
                .AppendInterval(Mathf.Max(0f, hold))
                .Append(_hand.DOAnchorPos(above, 0.34f).SetEase(Ease.InCubic));
            await AwaitTweenAsync(sequence, token);
            _handGroup.alpha = 0f;
        }

        public async Awaitable GrabDishAsync(
            DishGrabVisualSnapshot dishVisual,
            Action onVisualTakenOver,
            CancellationToken token)
        {
            EnsureBuilt();
            if (dishVisual.Sprite == null)
            {
                return;
            }

            Vector2 target = ScreenToLocal(dishVisual.ScreenCenter);
            Vector2 halfScreenSize = dishVisual.ScreenSize * 0.5f;
            Vector2 localMin = ScreenToLocal(dishVisual.ScreenCenter - halfScreenSize);
            Vector2 localMax = ScreenToLocal(dishVisual.ScreenCenter + halfScreenSize);
            _grabbedDish.sizeDelta = new Vector2(
                Mathf.Max(80f, Mathf.Abs(localMax.x - localMin.x)),
                Mathf.Max(80f, Mathf.Abs(localMax.y - localMin.y)));
            float handWidth = Mathf.Clamp(_grabbedDish.sizeDelta.x * 1.15f, 280f, 320f);
            ConfigureHand(
                _grabHandSprite,
                target,
                pivotY: 0.18f,
                minimumHeight: 680f,
                preferredWidth: handWidth);
            Vector2 above = HandOffscreenPosition(target.x);

            _grabbedDish.anchoredPosition = target;
            _grabbedDish.localRotation = Quaternion.Euler(0f, 0f, dishVisual.ScreenRotationDegrees);
            _grabbedDish.localScale = new Vector3(
                dishVisual.FlipX ? -1f : 1f,
                dishVisual.FlipY ? -1f : 1f,
                1f);
            _grabbedDishImage.sprite = dishVisual.Sprite;
            _grabbedDishImage.color = dishVisual.Color;
            _grabbedDishGroup.alpha = 1f;

            _hand.anchoredPosition = above;
            _hand.localRotation = Quaternion.identity;
            _handGroup.alpha = 1f;
            _hand.SetAsLastSibling();
            _grabbedDish.SetAsLastSibling();
            onVisualTakenOver?.Invoke();

            Sequence sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(_hand.gameObject)
                .Append(_hand.DOAnchorPos(target, 0.42f).SetEase(Ease.OutCubic))
                .AppendInterval(0.14f)
                .Append(_hand.DOAnchorPos(above, 0.50f).SetEase(Ease.InCubic))
                .Join(_grabbedDish.DOAnchorPos(above, 0.50f).SetEase(Ease.InCubic));
            await AwaitTweenAsync(sequence, token);
            _handGroup.alpha = 0f;
            _grabbedDishGroup.alpha = 0f;
        }

        public async Awaitable SweepAsync(
            IReadOnlyList<Vector2> screenPoints,
            Action<int> onReached,
            CancellationToken token)
        {
            if (screenPoints == null || screenPoints.Count == 0)
            {
                return;
            }

            EnsureBuilt();
            var targets = new List<Vector2>(screenPoints.Count);
            float lowestY = float.MaxValue;
            for (int i = 0; i < screenPoints.Count; i++)
            {
                Vector2 local = ScreenToLocal(screenPoints[i]);
                targets.Add(local);
                lowestY = Mathf.Min(lowestY, local.y);
            }

            ConfigureHand(
                _pointHandSprite ?? _grabHandSprite,
                new Vector2(0f, lowestY),
                pivotY: 0.02f,
                minimumHeight: 620f);
            _handGroup.alpha = 1f;
            _hand.localRotation = Quaternion.identity;
            _hand.anchoredPosition = HandOffscreenPosition(targets[0].x);
            _hand.SetAsLastSibling();

            Tween enter = _hand.DOAnchorPos(targets[0], 0.36f)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .SetLink(_hand.gameObject);
            await AwaitTweenAsync(enter, token);
            onReached?.Invoke(0);
            await WaitUnscaledAsync(0.08f, token);

            for (int i = 1; i < targets.Count; i++)
            {
                Tween move = _hand.DOAnchorPos(targets[i], 0.22f)
                    .SetEase(Ease.InOutSine)
                    .SetUpdate(true)
                    .SetLink(_hand.gameObject);
                await AwaitTweenAsync(move, token);
                onReached?.Invoke(i);
                await WaitUnscaledAsync(0.08f, token);
            }

            Vector2 exit = HandOffscreenPosition(targets[targets.Count - 1].x);
            Tween leave = _hand.DOAnchorPos(exit, 0.34f)
                .SetEase(Ease.InCubic)
                .SetUpdate(true)
                .SetLink(_hand.gameObject);
            await AwaitTweenAsync(leave, token);
            _handGroup.alpha = 0f;
        }

        public async Awaitable ShowCueAsync(Vector2 screenPoint, string text, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            _cue.anchoredPosition = ScreenToLocal(screenPoint) + new Vector2(0f, 95f);
            _cueText.text = text;
            _cueGroup.alpha = 0f;
            _cue.localScale = Vector3.one * 0.72f;
            Sequence sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(_cue.gameObject)
                .Append(_cueGroup.DOFade(1f, 0.12f))
                .Join(_cue.DOScale(1f, 0.18f).SetEase(Ease.OutBack))
                .AppendInterval(0.58f)
                .Append(_cueGroup.DOFade(0f, 0.18f));
            await AwaitTweenAsync(sequence, token);
        }

        public void CancelCurrent()
        {
            _playbackVersion++;
            if (_animationCts != null)
            {
                _animationCts.Cancel();
                _animationCts.Dispose();
                _animationCts = null;
            }

            IsPlaying = false;
            if (_blocker != null)
            {
                _blocker.interactable = false;
                _blocker.blocksRaycasts = false;
            }

            HideVisuals();
        }

        private void ConfigureHand(
            Sprite sprite,
            Vector2 target,
            float pivotY,
            float minimumHeight,
            float preferredWidth = 0f)
        {
            Canvas.ForceUpdateCanvases();
            _handImage.sprite = sprite;
            _handImage.color = Color.white;
            bool stretchArmOnly = preferredWidth > 0f && sprite != null && sprite.border.sqrMagnitude > 0f;
            _handImage.type = stretchArmOnly ? Image.Type.Sliced : Image.Type.Simple;
            _handImage.pixelsPerUnitMultiplier = stretchArmOnly
                ? sprite.rect.width / Mathf.Max(1f, preferredWidth)
                : 1f;
            _hand.pivot = new Vector2(0.5f, Mathf.Clamp01(pivotY));
            float heightAbovePivot = Mathf.Max(0.05f, 1f - _hand.pivot.y);
            float requiredHeight = (_overlay.rect.yMax - target.y + 80f) / heightAbovePivot;
            float height = Mathf.Max(minimumHeight, requiredHeight);
            float aspect = sprite != null && sprite.rect.height > 0f
                ? sprite.rect.width / sprite.rect.height
                : 0.5f;
            float width = preferredWidth > 0f ? preferredWidth : height * aspect;
            _hand.sizeDelta = new Vector2(width, height);
            _hand.localScale = Vector3.one;
        }

        private Vector2 HandOffscreenPosition(float x)
        {
            float belowPivot = _hand.pivot.y * _hand.rect.height;
            return new Vector2(x, _overlay.rect.yMax + belowPivot + 80f);
        }

        private Vector2 ScreenToLocal(Vector2 screenPoint)
        {
            Canvas canvas = _overlay.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_overlay, screenPoint, camera, out Vector2 local);
            return local;
        }

        private void HideVisuals()
        {
            if (_bubbleGroup != null) _bubbleGroup.alpha = 0f;
            if (_handGroup != null) _handGroup.alpha = 0f;
            if (_grabbedDishGroup != null) _grabbedDishGroup.alpha = 0f;
            if (_cueGroup != null) _cueGroup.alpha = 0f;
        }

        private bool HasCompletePrefabBindings(out string missingBinding)
        {
            (UnityEngine.Object Target, string Name)[] bindings =
            {
                (_overlay, nameof(_overlay)),
                (_blocker, nameof(_blocker)),
                (_bubble, nameof(_bubble)),
                (_bubbleGroup, nameof(_bubbleGroup)),
                (_dialogueText, nameof(_dialogueText)),
                (_hand, nameof(_hand)),
                (_handGroup, nameof(_handGroup)),
                (_handImage, nameof(_handImage)),
                (_grabbedDish, nameof(_grabbedDish)),
                (_grabbedDishGroup, nameof(_grabbedDishGroup)),
                (_grabbedDishImage, nameof(_grabbedDishImage)),
                (_cue, nameof(_cue)),
                (_cueGroup, nameof(_cueGroup)),
                (_cueText, nameof(_cueText)),
                (_pointHandSprite, nameof(_pointHandSprite)),
                (_grabHandSprite, nameof(_grabHandSprite)),
            };

            foreach ((UnityEngine.Object target, string name) in bindings)
            {
                if (target == null)
                {
                    missingBinding = name;
                    return false;
                }
            }

            missingBinding = string.Empty;
            return true;
        }

        private float RandomRange(float min, float max)
            => min + (float)_cosmeticRandom.NextDouble() * (max - min);

        private static async Awaitable WaitUnscaledAsync(float seconds, CancellationToken token)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                token.ThrowIfCancellationRequested();
                await Awaitable.NextFrameAsync(token);
                elapsed += Time.unscaledDeltaTime;
            }
        }

        private static async Awaitable AwaitTweenAsync(Tween tween, CancellationToken token)
        {
            try
            {
                while (tween != null && tween.active && !tween.IsComplete())
                {
                    token.ThrowIfCancellationRequested();
                    await Awaitable.NextFrameAsync(token);
                }
            }
            catch
            {
                tween?.Kill();
                throw;
            }
        }

        private void OnDisable() => CancelCurrent();

        private void OnDestroy() => CancelCurrent();
    }
}
