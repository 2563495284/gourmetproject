using System;
using System.Collections.Generic;
using System.Threading;
using DG.Tweening;
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

        private RectTransform _overlay;
        private CanvasGroup _blocker;
        private RectTransform _bubble;
        private CanvasGroup _bubbleGroup;
        private TMP_Text _dialogueText;
        private RectTransform _hand;
        private CanvasGroup _handGroup;
        private RectTransform _cue;
        private CanvasGroup _cueGroup;
        private TMP_Text _cueText;
        private CancellationTokenSource _animationCts;
        private readonly System.Random _cosmeticRandom = new System.Random();

        public bool IsPlaying { get; private set; }

        public void EnsureBuilt()
        {
            if (_overlay != null)
            {
                _overlay.SetAsLastSibling();
                return;
            }

            var overlayObject = new GameObject(
                "BossDebuffPresentationOverlay",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(Image));
            overlayObject.transform.SetParent(transform, false);
            _overlay = (RectTransform)overlayObject.transform;
            Stretch(_overlay);
            _blocker = overlayObject.GetComponent<CanvasGroup>();
            _blocker.alpha = 1f;
            _blocker.interactable = false;
            _blocker.blocksRaycasts = false;
            Image blockerImage = overlayObject.GetComponent<Image>();
            blockerImage.color = new Color(0f, 0f, 0f, 0.001f);
            blockerImage.raycastTarget = true;

            _bubble = CreatePanel("DialogueBubble", new Vector2(560f, 380f), out _bubbleGroup);
            Image bubblePanel = _bubble.GetComponent<Image>();
            bubblePanel.enabled = false;
            Sprite bubbleSprite = Resources.Load<Sprite>("Sprites/UI/STS2/speech_bubble3");
            RectTransform bubbleShadow = CreateImage("DialogueBubbleShadow", bubbleSprite, _bubble);
            bubbleShadow.sizeDelta = _bubble.sizeDelta;
            bubbleShadow.anchoredPosition = new Vector2(0f, 10f);
            bubbleShadow.localRotation = Quaternion.Euler(0f, 0f, 180f);
            bubbleShadow.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
            RectTransform bubbleArt = CreateImage("DialogueBubbleArt", bubbleSprite, _bubble);
            bubbleArt.sizeDelta = _bubble.sizeDelta;
            bubbleArt.localRotation = Quaternion.Euler(0f, 0f, 180f);
            // STS2 merchant_rug_dialogue.tscn uses its HSV shader with v=0.4.
            bubbleArt.GetComponent<Image>().color = new Color(0.4f, 0.4f, 0.4f, 1f);
            _dialogueText = CreateText(_bubble, "Dialogue", 34f, new Color(1f, 0.965f, 0.886f, 0.9f));
            _dialogueText.rectTransform.offsetMin = new Vector2(58f, 82f);
            _dialogueText.rectTransform.offsetMax = new Vector2(-58f, -82f);
            _dialogueText.alignment = TextAlignmentOptions.Center;

            _hand = CreateImage("BossHand", Resources.Load<Sprite>("Sprites/UI/serve_hand"));
            _hand.sizeDelta = new Vector2(190f, 190f);
            _hand.pivot = new Vector2(0.5f, 0.08f);
            _handGroup = _hand.gameObject.AddComponent<CanvasGroup>();
            _handGroup.alpha = 0f;

            _cue = CreatePanel("BossCue", new Vector2(300f, 78f), out _cueGroup);
            _cue.GetComponent<Image>().color = new Color(0.18f, 0.10f, 0.04f, 0.92f);
            _cueText = CreateText(_cue, "Cue", 28f, new Color(1f, 0.80f, 0.24f, 1f));
            _cueText.alignment = TextAlignmentOptions.Center;

            HideVisuals();
            _overlay.SetAsLastSibling();
        }

        public async Awaitable PlayLockedAsync(
            Func<CancellationToken, Awaitable> sequence,
            CancellationToken externalToken)
        {
            EnsureBuilt();
            CancelCurrent();
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
                IsPlaying = false;
                if (_blocker != null)
                {
                    _blocker.interactable = false;
                    _blocker.blocksRaycasts = false;
                }

                HideVisuals();
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
            Vector2 target = ScreenToLocal(screenPoint) + new Vector2(0f, 48f);
            _hand.anchoredPosition = target + new Vector2(0f, 150f);
            _hand.localRotation = Quaternion.Euler(0f, 0f, -8f);
            _handGroup.alpha = 0f;
            Sequence sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(_hand.gameObject)
                .Append(_handGroup.DOFade(1f, 0.12f))
                .Join(_hand.DOAnchorPos(target, 0.28f).SetEase(Ease.OutCubic))
                .Append(_hand.DOPunchAnchorPos(new Vector2(0f, -18f), 0.24f, 4, 0.3f))
                .AppendInterval(Mathf.Max(0f, hold))
                .Append(_handGroup.DOFade(0f, 0.16f));
            await AwaitTweenAsync(sequence, token);
        }

        public async Awaitable GrabAsync(Vector2 screenPoint, CancellationToken token)
        {
            EnsureBuilt();
            Vector2 target = ScreenToLocal(screenPoint) + new Vector2(0f, 30f);
            Vector2 above = new Vector2(target.x, _overlay.rect.yMax + 220f);
            _hand.anchoredPosition = above;
            _hand.localRotation = Quaternion.identity;
            _handGroup.alpha = 1f;
            Sequence sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(_hand.gameObject)
                .Append(_hand.DOAnchorPos(target, 0.36f).SetEase(Ease.OutCubic))
                .Append(_hand.DOPunchAnchorPos(new Vector2(0f, -24f), 0.2f, 4, 0.2f))
                .AppendInterval(0.12f)
                .Append(_hand.DOAnchorPos(above, 0.42f).SetEase(Ease.InCubic))
                .Join(_handGroup.DOFade(0f, 0.32f));
            await AwaitTweenAsync(sequence, token);
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
            _handGroup.alpha = 1f;
            _hand.localRotation = Quaternion.Euler(0f, 0f, -12f);
            _hand.anchoredPosition = ScreenToLocal(screenPoints[0]) + new Vector2(-120f, 90f);
            for (int i = 0; i < screenPoints.Count; i++)
            {
                Vector2 target = ScreenToLocal(screenPoints[i]) + new Vector2(0f, 50f);
                Tween move = _hand.DOAnchorPos(target, 0.22f)
                    .SetEase(Ease.InOutSine)
                    .SetUpdate(true)
                    .SetLink(_hand.gameObject);
                await AwaitTweenAsync(move, token);
                onReached?.Invoke(i);
                await WaitUnscaledAsync(0.08f, token);
            }

            Tween fade = _handGroup.DOFade(0f, 0.18f).SetUpdate(true).SetLink(_hand.gameObject);
            await AwaitTweenAsync(fade, token);
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

        private RectTransform CreatePanel(string name, Vector2 size, out CanvasGroup group)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            go.transform.SetParent(_overlay, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            group = go.GetComponent<CanvasGroup>();
            go.GetComponent<Image>().raycastTarget = false;
            return rect;
        }

        private RectTransform CreateImage(string name, Sprite sprite, RectTransform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent != null ? parent : _overlay, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return rect;
        }

        private static TMP_Text CreateText(RectTransform parent, string name, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(28f, 16f);
            rect.offsetMax = new Vector2(-28f, -16f);
            TMP_Text text = go.GetComponent<TMP_Text>();
            text.fontSize = size;
            text.color = color;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            return text;
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

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void HideVisuals()
        {
            if (_bubbleGroup != null) _bubbleGroup.alpha = 0f;
            if (_handGroup != null) _handGroup.alpha = 0f;
            if (_cueGroup != null) _cueGroup.alpha = 0f;
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
