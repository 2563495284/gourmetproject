using System.Collections;
using GameFramework.Event;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 卡通风格转场界面。固定层级（食物擦除/速度线/消息文字 + 7 套转场根及其子物体、IrisWipeGraphic）
    /// 全部落在 CartoonSceneTransitionForm.prefab，sprite 在 prefab 里连好。
    /// 本脚本只负责按 <see cref="CartoonSceneTransitionData"/> 选择并逐帧驱动这些已存在节点的动画。
    /// </summary>
    public sealed class CartoonSceneTransitionForm : UGuiForm
    {
        [Header("食物擦除 / 速度线 / 消息")]
        [SerializeField] private RectTransform _wipeMask;
        [SerializeField] private RectTransform _tableclothRect;
        [SerializeField] private RectTransform _plateRect;
        [SerializeField] private Image _plateImage;
        [SerializeField] private RectTransform[] _speedLines;
        [SerializeField] private Image[] _speedLineImages;
        [SerializeField] private Text _messageText;
        [SerializeField] private Outline _messageOutline;

        [Header("Fade")]
        [SerializeField] private RectTransform _fadeRoot;
        [SerializeField] private Image _fadeBackdrop;

        [Header("IrisWipe")]
        [SerializeField] private RectTransform _irisRoot;
        [SerializeField] private IrisWipeGraphic _irisGraphic;
        [SerializeField] private RectTransform _irisRing;
        [SerializeField] private Image _irisRingImage;

        [Header("Curtain")]
        [SerializeField] private RectTransform _curtainRoot;
        [SerializeField] private RectTransform _curtainLeft;
        [SerializeField] private RectTransform _curtainRight;
        [SerializeField] private Image _curtainLeftImage;
        [SerializeField] private Image _curtainRightImage;

        [Header("PageTurn")]
        [SerializeField] private RectTransform _pageRoot;
        [SerializeField] private RectTransform _pagePanel;
        [SerializeField] private Image _pagePanelImage;
        [SerializeField] private Image _pageShadowImage;
        [SerializeField] private Image _pageEdgeImage;

        [Header("SauceSplat")]
        [SerializeField] private RectTransform _sauceSplatRoot;
        [SerializeField] private Image _sauceBackdrop;
        [SerializeField] private RectTransform _sauceSplatRect;
        [SerializeField] private Image _sauceSplatImage;

        [Header("CartoonBurst")]
        [SerializeField] private RectTransform _burstRoot;
        [SerializeField] private Image _burstBackdrop;
        [SerializeField] private RectTransform[] _burstRings;
        [SerializeField] private Image[] _burstRingImages;

        [Header("FoodCurtain")]
        [SerializeField] private RectTransform _foodCurtainRoot;
        [SerializeField] private Image _foodCurtainBackdrop;
        [SerializeField] private RectTransform[] _foodCurtainItems;
        [SerializeField] private Image[] _foodCurtainImages;

        private CartoonSceneTransitionData _data;
        private Coroutine _animation;
        private bool _isWaitingForScene;
        private bool _subscribedSceneEvents;

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            _data = userData as CartoonSceneTransitionData ?? new CartoonSceneTransitionData();
            SetTransitionObjectsVisible(_data.TransitionType);
            MoveForegroundToFront(_data.TransitionType);
            _messageText.text = _data.Message;
            _isWaitingForScene = false;
            SubscribeSceneEvents();
            SetProgress(0f);

            if (_animation != null)
            {
                StopCoroutine(_animation);
            }

            _animation = StartCoroutine(PlayTransition());
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            if (_animation != null)
            {
                StopCoroutine(_animation);
                _animation = null;
            }

            UnsubscribeSceneEvents();
            _isWaitingForScene = false;
            _data = null;
            base.OnClose(isShutdown, userData);
        }

        private void SetTransitionObjectsVisible(CartoonTransitionType type)
        {
            bool isFoodWipe = IsFoodWipe(type);
            _wipeMask.gameObject.SetActive(isFoodWipe);
            _plateRect.gameObject.SetActive(isFoodWipe);

            for (int i = 0; i < _speedLines.Length; i++)
            {
                _speedLines[i].gameObject.SetActive(isFoodWipe);
            }

            SetRootActive(_fadeRoot, type == CartoonTransitionType.Fade);
            SetRootActive(_irisRoot, type == CartoonTransitionType.IrisWipe);
            SetRootActive(_curtainRoot, type == CartoonTransitionType.Curtain);
            SetRootActive(_pageRoot, type == CartoonTransitionType.PageTurn);
            SetRootActive(_sauceSplatRoot, type == CartoonTransitionType.SauceSplat);
            SetRootActive(_burstRoot, type == CartoonTransitionType.CartoonBurst);
            SetRootActive(_foodCurtainRoot, type == CartoonTransitionType.FoodCurtain);
        }

        private void MoveForegroundToFront(CartoonTransitionType type)
        {
            switch (type)
            {
                case CartoonTransitionType.Fade:
                    _fadeRoot.SetAsLastSibling();
                    break;
                case CartoonTransitionType.IrisWipe:
                    _irisRoot.SetAsLastSibling();
                    break;
                case CartoonTransitionType.Curtain:
                    _curtainRoot.SetAsLastSibling();
                    break;
                case CartoonTransitionType.PageTurn:
                    _pageRoot.SetAsLastSibling();
                    break;
                case CartoonTransitionType.SauceSplat:
                    _sauceSplatRoot.SetAsLastSibling();
                    break;
                case CartoonTransitionType.CartoonBurst:
                    _burstRoot.SetAsLastSibling();
                    break;
                case CartoonTransitionType.FoodCurtain:
                    _foodCurtainRoot.SetAsLastSibling();
                    break;
                default:
                    _plateRect.SetAsLastSibling();
                    break;
            }

            _messageText.transform.SetAsLastSibling();
        }

        private static void SetRootActive(RectTransform root, bool active)
        {
            if (root != null)
            {
                root.gameObject.SetActive(active);
            }
        }

        private static bool IsFoodWipe(CartoonTransitionType type)
        {
            return type == CartoonTransitionType.PlateWipe || type == CartoonTransitionType.FoodWipe;
        }

        private IEnumerator PlayTransition()
        {
            yield return Animate(0f, 1f, Mathf.Max(0.01f, _data.CoverDuration), true);
            _data.OnCovered?.Invoke();

            if (!string.IsNullOrEmpty(_data.SceneAssetName))
            {
                _isWaitingForScene = true;
                GameApp.Scenes.Load(_data.SceneAssetName);
                yield return new WaitUntil(() => !_isWaitingForScene);
            }

            if (_data.HoldDuration > 0f)
            {
                yield return new WaitForSecondsRealtime(_data.HoldDuration);
            }

            yield return Animate(1f, 0f, Mathf.Max(0.01f, _data.RevealDuration), false);

            var onFinished = _data.OnFinished;
            GameApp.UI.CloseUIForm(UIForm);
            onFinished?.Invoke();
        }

        private IEnumerator Animate(float from, float to, float duration, bool covering)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = covering ? EaseOutBack(t) : EaseInCubic(t);
                SetProgress(Mathf.Lerp(from, to, eased));
                yield return null;
            }

            SetProgress(to);
        }

        private void SubscribeSceneEvents()
        {
            if (_subscribedSceneEvents)
            {
                return;
            }

            GameApp.Event.Subscribe(LoadSceneSuccessEventArgs.EventId, OnLoadSceneSuccess);
            GameApp.Event.Subscribe(LoadSceneFailureEventArgs.EventId, OnLoadSceneFailure);
            _subscribedSceneEvents = true;
        }

        private void UnsubscribeSceneEvents()
        {
            if (!_subscribedSceneEvents || GameApp.Event == null)
            {
                return;
            }

            GameApp.Event.Unsubscribe(LoadSceneSuccessEventArgs.EventId, OnLoadSceneSuccess);
            GameApp.Event.Unsubscribe(LoadSceneFailureEventArgs.EventId, OnLoadSceneFailure);
            _subscribedSceneEvents = false;
        }

        private void OnLoadSceneSuccess(object sender, GameEventArgs e)
        {
            if (e is LoadSceneSuccessEventArgs args && args.SceneAssetName == _data?.SceneAssetName)
            {
                _isWaitingForScene = false;
            }
        }

        private void OnLoadSceneFailure(object sender, GameEventArgs e)
        {
            if (e is LoadSceneFailureEventArgs args && args.SceneAssetName == _data?.SceneAssetName)
            {
                _isWaitingForScene = false;
            }
        }

        private void SetProgress(float progress)
        {
            float clamped = Mathf.Clamp01(progress);
            switch (_data.TransitionType)
            {
                case CartoonTransitionType.Fade:
                    UpdateFade(clamped);
                    break;
                case CartoonTransitionType.IrisWipe:
                    UpdateIrisWipe(clamped);
                    break;
                case CartoonTransitionType.Curtain:
                    UpdateCurtain(clamped);
                    break;
                case CartoonTransitionType.PageTurn:
                    UpdatePageTurn(clamped);
                    break;
                case CartoonTransitionType.SauceSplat:
                    UpdateSauceSplat(clamped);
                    break;
                case CartoonTransitionType.CartoonBurst:
                    UpdateCartoonBurst(clamped);
                    break;
                case CartoonTransitionType.FoodCurtain:
                    UpdateFoodCurtain(clamped);
                    break;
                default:
                    UpdateFoodWipe(clamped);
                    break;
            }

            UpdateMessage(clamped);
        }

        private void UpdateFade(float progress)
        {
            SetImageAlpha(_fadeBackdrop, Mathf.SmoothStep(0f, 1f, progress));
        }

        private void UpdateIrisWipe(float progress)
        {
            var root = (RectTransform)CachedTransform;
            Rect rect = root.rect;
            float diagonal = Mathf.Sqrt(rect.width * rect.width + rect.height * rect.height);
            float radius = Mathf.Lerp(diagonal * 0.62f, 0f, Mathf.SmoothStep(0f, 1f, progress));
            _irisGraphic.Radius = radius;
            _irisGraphic.SetVerticesDirty();

            float ringSize = Mathf.Max(radius * 2.1f, 0f);
            _irisRing.sizeDelta = Vector2.one * ringSize;
            _irisRing.localRotation = Quaternion.Euler(0f, 0f, Time.unscaledTime * 18f);
            SetImageAlpha(_irisRingImage, Mathf.SmoothStep(0f, 1f, progress) * (1f - Mathf.SmoothStep(0.94f, 1f, progress)));
        }

        private void UpdateCurtain(float progress)
        {
            var root = (RectTransform)CachedTransform;
            Rect rect = root.rect;
            float width = rect.width * 0.58f;
            float height = rect.height * 1.16f;
            float eased = EaseOutBounce(progress);
            float leftX = Mathf.Lerp(rect.xMin - width * 0.55f, rect.center.x - width * 0.48f, eased);
            float rightX = Mathf.Lerp(rect.xMax + width * 0.55f, rect.center.x + width * 0.48f, eased);

            _curtainLeft.sizeDelta = new Vector2(width, height);
            _curtainRight.sizeDelta = new Vector2(width, height);
            _curtainLeft.anchoredPosition = new Vector2(leftX, Mathf.Sin(Time.unscaledTime * 8f) * 5f * progress);
            _curtainRight.anchoredPosition = new Vector2(rightX, Mathf.Cos(Time.unscaledTime * 8f) * 5f * progress);
            _curtainLeft.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-8f, 1.5f, eased));
            _curtainRight.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(8f, -1.5f, eased));
            SetImageAlpha(_curtainLeftImage, Mathf.SmoothStep(0f, 1f, progress * 3f));
            SetImageAlpha(_curtainRightImage, Mathf.SmoothStep(0f, 1f, progress * 3f));
        }

        private void UpdatePageTurn(float progress)
        {
            var root = (RectTransform)CachedTransform;
            Rect rect = root.rect;
            float eased = EaseOutBack(progress);
            float width = rect.width * 1.18f;
            float height = rect.height * 1.16f;
            float x = Mathf.Lerp(rect.xMax + width * 0.55f, rect.center.x + width * 0.5f, eased);
            float y = Mathf.Sin(progress * Mathf.PI) * rect.height * 0.04f;

            _pagePanel.sizeDelta = new Vector2(width, height);
            _pagePanel.anchoredPosition = new Vector2(x, y);
            _pagePanel.localScale = new Vector3(Mathf.Lerp(0.14f, 1f, eased), 1f + Mathf.Sin(progress * Mathf.PI) * 0.06f, 1f);
            _pagePanel.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-18f, 0f, eased));
            SetImageAlpha(_pagePanelImage, Mathf.SmoothStep(0f, 1f, progress * 4f));
            SetImageAlpha(_pageEdgeImage, Mathf.SmoothStep(0f, 1f, progress * 5f));
            SetImageAlpha(_pageShadowImage, Mathf.SmoothStep(0f, 0.45f, progress) * (1f - Mathf.SmoothStep(0.86f, 1f, progress)));
        }

        private void UpdateFoodWipe(float progress)
        {
            UpdateWipe(progress);
            UpdateSpeedLines(progress);
        }

        private void UpdateWipe(float progress)
        {
            var root = (RectTransform)CachedTransform;
            Rect rect = root.rect;
            float unit = Mathf.Min(rect.width, rect.height);
            float leadX = Mathf.Lerp(rect.xMin - rect.width * 0.18f, rect.xMax + rect.width * 0.18f, progress);
            float coveredRight = Mathf.Clamp(leadX - unit * 0.04f, rect.xMin, rect.xMax);
            float coveredWidth = Mathf.Max(0f, coveredRight - rect.xMin);

            _wipeMask.sizeDelta = new Vector2(coveredWidth, 0f);
            _tableclothRect.sizeDelta = new Vector2(rect.width, 0f);
            _plateRect.sizeDelta = Vector2.one * (unit * 0.9f);
            _plateRect.anchoredPosition = new Vector2(leadX - unit * 0.02f, Mathf.Sin(Time.unscaledTime * 13f) * unit * 0.012f);
            _plateRect.localRotation = Quaternion.Euler(0f, 0f, -5f + Mathf.Sin(Time.unscaledTime * 15f) * 2.5f);

            Color plateColor = _plateImage.color;
            plateColor.a = progress > 0.01f && progress < 0.99f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress * 8f));
            _plateImage.color = plateColor;
        }

        private void UpdateSpeedLines(float progress)
        {
            var root = (RectTransform)CachedTransform;
            Rect rect = root.rect;
            float unit = Mathf.Min(rect.width, rect.height);
            float leadX = Mathf.Lerp(rect.xMin - rect.width * 0.24f, rect.xMax + rect.width * 0.22f, progress);
            float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress * 7f)) * (1f - Mathf.SmoothStep(0.74f, 1f, progress));

            for (int i = 0; i < _speedLines.Length; i++)
            {
                float y = Mathf.Lerp(rect.yMin + unit * 0.12f, rect.yMax - unit * 0.12f, (i + 0.5f) / _speedLines.Length);
                float stagger = (i % 5) * unit * 0.05f;
                _speedLines[i].anchoredPosition = new Vector2(leadX - unit * 0.22f - stagger, y + Mathf.Sin(Time.unscaledTime * 11f + i) * unit * 0.01f);
                _speedLines[i].localScale = Vector3.one * Mathf.Lerp(0.55f, 1.18f, alpha);

                Color color = _speedLineImages[i].color;
                color.a = alpha * (0.34f + i % 3 * 0.15f);
                _speedLineImages[i].color = color;
            }
        }

        private void UpdateSauceSplat(float progress)
        {
            var root = (RectTransform)CachedTransform;
            Rect rect = root.rect;
            float diagonal = Mathf.Sqrt(rect.width * rect.width + rect.height * rect.height);
            float size = diagonal * 1.12f;
            float eased = EaseOutBack(progress);
            float wobble = Mathf.Sin(Time.unscaledTime * 18f) * 0.035f * progress;

            SetImageAlpha(_sauceBackdrop, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - 0.22f) / 0.34f)) * 0.96f);
            _sauceSplatRect.sizeDelta = Vector2.one * size;
            _sauceSplatRect.anchoredPosition = new Vector2(Mathf.Sin(Time.unscaledTime * 9f) * 10f * progress, Mathf.Cos(Time.unscaledTime * 7f) * 8f * progress);
            _sauceSplatRect.localScale = Vector3.one * Mathf.Lerp(0.05f, 1.62f + wobble, eased);
            _sauceSplatRect.localRotation = Quaternion.Euler(0f, 0f, -16f + Mathf.Sin(Time.unscaledTime * 10f) * 4f * progress);
            SetImageAlpha(_sauceSplatImage, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress * 5f)));
        }

        private void UpdateCartoonBurst(float progress)
        {
            var root = (RectTransform)CachedTransform;
            Rect rect = root.rect;
            float size = Mathf.Max(rect.width, rect.height) * 1.45f;
            SetImageAlpha(_burstBackdrop, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - 0.08f) / 0.34f)) * 0.94f);

            for (int i = 0; i < _burstRings.Length; i++)
            {
                float ringProgress = Mathf.Clamp01((progress - i * 0.07f) / 0.66f);
                float scale = Mathf.Lerp(0.08f, 1.82f + i * 0.42f, EaseOutBack(ringProgress));
                _burstRings[i].sizeDelta = Vector2.one * size;
                _burstRings[i].localScale = new Vector3(scale * Mathf.Lerp(1.08f, 0.95f, ringProgress), scale * Mathf.Lerp(0.95f, 1.08f, ringProgress), 1f);
                _burstRings[i].localRotation = Quaternion.Euler(0f, 0f, i * 17f + Time.unscaledTime * (10f + i * 4f));
                SetImageAlpha(_burstRingImages[i], Mathf.SmoothStep(0f, 1f, ringProgress) * Mathf.Lerp(0.72f, 1f, 1f - i * 0.2f));
            }
        }

        private void UpdateFoodCurtain(float progress)
        {
            var root = (RectTransform)CachedTransform;
            Rect rect = root.rect;
            float itemSize = Mathf.Min(rect.width / 3.4f, rect.height * 0.42f);
            SetImageAlpha(_foodCurtainBackdrop, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - 0.12f) / 0.38f)));

            for (int i = 0; i < _foodCurtainItems.Length; i++)
            {
                float column = (i + 0.5f) / _foodCurtainItems.Length;
                float staggered = Mathf.Clamp01((progress - i * 0.045f) / 0.58f);
                float bounce = EaseOutBounce(staggered);
                float x = Mathf.Lerp(rect.xMin + itemSize * 0.45f, rect.xMax - itemSize * 0.45f, column);
                float targetY = rect.center.y + (i % 2 == 0 ? itemSize * 0.25f : -itemSize * 0.18f);
                float startY = rect.yMax + itemSize * (0.8f + i * 0.06f);
                float y = Mathf.Lerp(startY, targetY, bounce);

                _foodCurtainItems[i].sizeDelta = Vector2.one * itemSize;
                _foodCurtainItems[i].anchoredPosition = new Vector2(x, y);
                _foodCurtainItems[i].localScale = Vector3.one * Mathf.Lerp(0.68f, 1.05f, bounce);
                _foodCurtainItems[i].localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-28f + i * 9f, -8f + i * 4f, bounce) + Mathf.Sin(Time.unscaledTime * 8f + i) * 3f * progress);
                SetImageAlpha(_foodCurtainImages[i], Mathf.SmoothStep(0f, 1f, staggered));
            }
        }

        private void UpdateMessage(float progress)
        {
            float textProgress = Mathf.Clamp01((progress - 0.48f) / 0.34f);
            float alpha = Mathf.SmoothStep(0f, 1f, textProgress);
            float punch = Mathf.Sin(textProgress * Mathf.PI) * 0.22f;

            Color textColor = _messageText.color;
            textColor.a = alpha;
            _messageText.color = textColor;

            Color outlineColor = _messageOutline.effectColor;
            outlineColor.a = alpha;
            _messageOutline.effectColor = outlineColor;

            _messageText.transform.localScale = Vector3.one * (Mathf.Lerp(0.55f, 1.05f, alpha) + punch);
        }

        private static void SetImageAlpha(Image image, float alpha)
        {
            Color color = image.color;
            color.a = alpha;
            image.color = color;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        private static float EaseInCubic(float t)
        {
            return t * t * t;
        }

        private static float EaseOutBounce(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;

            if (t < 1f / d1)
            {
                return n1 * t * t;
            }

            if (t < 2f / d1)
            {
                t -= 1.5f / d1;
                return n1 * t * t + 0.75f;
            }

            if (t < 2.5f / d1)
            {
                t -= 2.25f / d1;
                return n1 * t * t + 0.9375f;
            }

            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }
    }
}
