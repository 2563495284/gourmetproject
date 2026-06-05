using System.Collections;
using GameFramework.Event;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace GourmetProject.Game.UI
{
    public sealed class CartoonSceneTransitionForm : UGuiForm
    {
        private const int SpeedLineCount = 10;
        private const int BurstRingCount = 3;
        private const int FoodCurtainCount = 6;
        private const string TableclothSpritePath = "Sprites/UI/cartoon_transition_tablecloth";
        private const string PlateSpritePath = "Sprites/UI/cartoon_transition_plate_wipe";
        private const string SpeedLineSpritePath = "Sprites/UI/cartoon_transition_speed_line";
        private const string SauceSplatSpritePath = "Sprites/UI/cartoon_transition_sauce_splat";
        private const string BurstRingSpritePath = "Sprites/UI/cartoon_transition_burst_ring";
        private const string FoodCurtainSpritePathPrefix = "Sprites/UI/cartoon_transition_food_curtain_";

        private RectTransform _wipeMask;
        private RectTransform _tableclothRect;
        private RectTransform _plateRect;
        private Image _plateImage;
        private RectTransform[] _speedLines;
        private Image[] _speedLineImages;
        private RectTransform _fadeRoot;
        private Image _fadeBackdrop;
        private RectTransform _irisRoot;
        private IrisWipeGraphic _irisGraphic;
        private RectTransform _irisRing;
        private Image _irisRingImage;
        private RectTransform _curtainRoot;
        private RectTransform _curtainLeft;
        private RectTransform _curtainRight;
        private Image _curtainLeftImage;
        private Image _curtainRightImage;
        private RectTransform _pageRoot;
        private RectTransform _pagePanel;
        private Image _pagePanelImage;
        private Image _pageShadowImage;
        private Image _pageEdgeImage;
        private RectTransform _sauceSplatRoot;
        private Image _sauceBackdrop;
        private RectTransform _sauceSplatRect;
        private Image _sauceSplatImage;
        private RectTransform _burstRoot;
        private Image _burstBackdrop;
        private RectTransform[] _burstRings;
        private Image[] _burstRingImages;
        private RectTransform _foodCurtainRoot;
        private Image _foodCurtainBackdrop;
        private RectTransform[] _foodCurtainItems;
        private Image[] _foodCurtainImages;
        private Text _messageText;
        private Outline _messageOutline;
        private CartoonSceneTransitionData _data;
        private Coroutine _animation;
        private bool _isWaitingForScene;
        private bool _subscribedSceneEvents;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            BuildView();
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            _data = userData as CartoonSceneTransitionData ?? new CartoonSceneTransitionData();
            EnsureTransitionBuilt(_data.TransitionType);
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

        private void BuildView()
        {
            var root = CachedTransform as RectTransform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var blocker = new GameObject("Blocker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            blocker.transform.SetParent(CachedTransform, false);
            Stretch((RectTransform)blocker.transform);
            var blockerImage = blocker.GetComponent<Image>();
            blockerImage.color = new Color(0f, 0f, 0f, 0.001f);
            blockerImage.raycastTarget = true;

            BuildFoodWipeLayer();
            BuildSpeedLines();
            BuildMessage();
        }

        private void BuildFoodWipeLayer()
        {
            var maskObject = new GameObject("TableclothMask", typeof(RectTransform), typeof(RectMask2D));
            maskObject.transform.SetParent(CachedTransform, false);
            _wipeMask = (RectTransform)maskObject.transform;
            _wipeMask.anchorMin = Vector2.zero;
            _wipeMask.anchorMax = new Vector2(0f, 1f);
            _wipeMask.pivot = new Vector2(0f, 0.5f);
            _wipeMask.anchoredPosition = Vector2.zero;
            _wipeMask.sizeDelta = Vector2.zero;

            var tableclothObject = new GameObject("Tablecloth", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            tableclothObject.transform.SetParent(_wipeMask, false);
            _tableclothRect = (RectTransform)tableclothObject.transform;
            _tableclothRect.anchorMin = Vector2.zero;
            _tableclothRect.anchorMax = new Vector2(0f, 1f);
            _tableclothRect.pivot = new Vector2(0f, 0.5f);
            _tableclothRect.anchoredPosition = Vector2.zero;
            _tableclothRect.sizeDelta = Vector2.zero;

            var tableclothImage = tableclothObject.GetComponent<Image>();
            tableclothImage.sprite = RequireSprite(TableclothSpritePath);
            tableclothImage.type = Image.Type.Simple;
            tableclothImage.raycastTarget = false;

            var plateObject = new GameObject("PlateWipe", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            plateObject.transform.SetParent(CachedTransform, false);
            _plateRect = (RectTransform)plateObject.transform;
            _plateRect.anchorMin = new Vector2(0.5f, 0.5f);
            _plateRect.anchorMax = new Vector2(0.5f, 0.5f);
            _plateRect.pivot = new Vector2(0.5f, 0.5f);
            _plateImage = plateObject.GetComponent<Image>();
            _plateImage.sprite = RequireSprite(PlateSpritePath);
            _plateImage.preserveAspect = true;
            _plateImage.raycastTarget = false;
        }

        private void BuildSpeedLines()
        {
            _speedLines = new RectTransform[SpeedLineCount];
            _speedLineImages = new Image[SpeedLineCount];
            var speedLineSprite = RequireSprite(SpeedLineSpritePath);

            for (int i = 0; i < SpeedLineCount; i++)
            {
                var lineObject = new GameObject($"FoodWhoosh_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                lineObject.transform.SetParent(CachedTransform, false);

                var rect = (RectTransform)lineObject.transform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(270f + i % 4 * 54f, 82f + i % 3 * 12f);
                rect.localRotation = Quaternion.Euler(0f, 0f, -10f + i % 4 * 5f);

                var image = lineObject.GetComponent<Image>();
                image.sprite = speedLineSprite;
                image.preserveAspect = true;
                image.color = new Color(1f, 1f, 1f, 0f);
                image.raycastTarget = false;

                _speedLines[i] = rect;
                _speedLineImages[i] = image;
            }
        }

        private void BuildMessage()
        {
            var label = new GameObject("Message", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline));
            label.transform.SetParent(CachedTransform, false);
            _messageText = label.GetComponent<Text>();
            _messageText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _messageText.fontSize = 72;
            _messageText.fontStyle = FontStyle.Bold;
            _messageText.alignment = TextAnchor.MiddleCenter;
            _messageText.color = new Color(1f, 0.9f, 0.18f, 0f);
            _messageText.raycastTarget = false;

            _messageOutline = label.GetComponent<Outline>();
            _messageOutline.effectColor = new Color(0.12f, 0.04f, 0.01f, 0f);
            _messageOutline.effectDistance = new Vector2(7f, -7f);

            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(860f, 170f);
            labelRect.anchoredPosition = new Vector2(0f, -12f);
            labelRect.localRotation = Quaternion.Euler(0f, 0f, -3.5f);
        }

        private void EnsureTransitionBuilt(CartoonTransitionType type)
        {
            switch (type)
            {
                case CartoonTransitionType.Fade:
                    EnsureFadeBuilt();
                    break;
                case CartoonTransitionType.IrisWipe:
                    EnsureIrisBuilt();
                    break;
                case CartoonTransitionType.Curtain:
                    EnsureCurtainBuilt();
                    break;
                case CartoonTransitionType.PageTurn:
                    EnsurePageTurnBuilt();
                    break;
                case CartoonTransitionType.SauceSplat:
                    EnsureSauceSplatBuilt();
                    break;
                case CartoonTransitionType.CartoonBurst:
                    EnsureCartoonBurstBuilt();
                    break;
                case CartoonTransitionType.FoodCurtain:
                    EnsureFoodCurtainBuilt();
                    break;
            }
        }

        private void EnsureFadeBuilt()
        {
            if (_fadeRoot != null)
            {
                return;
            }

            _fadeRoot = CreateFullScreenRoot("FadeTransition");
            _fadeBackdrop = CreateFullScreenImage("FadeBackdrop", _fadeRoot, new Color(0.12f, 0.07f, 0.045f, 0f));
        }

        private void EnsureIrisBuilt()
        {
            if (_irisRoot != null)
            {
                return;
            }

            _irisRoot = CreateFullScreenRoot("IrisWipeTransition");
            var irisObject = new GameObject("IrisOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(IrisWipeGraphic));
            irisObject.transform.SetParent(_irisRoot, false);
            Stretch((RectTransform)irisObject.transform);
            _irisGraphic = irisObject.GetComponent<IrisWipeGraphic>();
            _irisGraphic.color = new Color(0.13f, 0.075f, 0.035f, 1f);

            var ringObject = new GameObject("IrisRing", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            ringObject.transform.SetParent(_irisRoot, false);
            _irisRing = (RectTransform)ringObject.transform;
            _irisRing.anchorMin = new Vector2(0.5f, 0.5f);
            _irisRing.anchorMax = new Vector2(0.5f, 0.5f);
            _irisRing.pivot = new Vector2(0.5f, 0.5f);
            _irisRingImage = ringObject.GetComponent<Image>();
            _irisRingImage.sprite = RequireSprite(PlateSpritePath);
            _irisRingImage.preserveAspect = true;
            _irisRingImage.color = new Color(1f, 0.82f, 0.18f, 0f);
            _irisRingImage.raycastTarget = false;
        }

        private void EnsureCurtainBuilt()
        {
            if (_curtainRoot != null)
            {
                return;
            }

            _curtainRoot = CreateFullScreenRoot("CurtainTransition");
            _curtainLeftImage = CreatePanel("CurtainLeft", _curtainRoot, new Color(0.78f, 0.08f, 0.08f, 0f), out _curtainLeft);
            _curtainRightImage = CreatePanel("CurtainRight", _curtainRoot, new Color(0.62f, 0.035f, 0.04f, 0f), out _curtainRight);
            CreateCurtainFolds(_curtainLeft, false);
            CreateCurtainFolds(_curtainRight, true);
        }

        private void EnsurePageTurnBuilt()
        {
            if (_pageRoot != null)
            {
                return;
            }

            _pageRoot = CreateFullScreenRoot("PageTurnTransition");

            var shadowObject = new GameObject("PageShadow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            shadowObject.transform.SetParent(_pageRoot, false);
            Stretch((RectTransform)shadowObject.transform);
            _pageShadowImage = shadowObject.GetComponent<Image>();
            _pageShadowImage.color = new Color(0.05f, 0.025f, 0.015f, 0f);
            _pageShadowImage.raycastTarget = false;

            var pageObject = new GameObject("ComicPage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            pageObject.transform.SetParent(_pageRoot, false);
            _pagePanel = (RectTransform)pageObject.transform;
            _pagePanel.anchorMin = new Vector2(0.5f, 0.5f);
            _pagePanel.anchorMax = new Vector2(0.5f, 0.5f);
            _pagePanel.pivot = new Vector2(1f, 0.5f);
            _pagePanelImage = pageObject.GetComponent<Image>();
            _pagePanelImage.color = new Color(1f, 0.88f, 0.55f, 0f);
            _pagePanelImage.raycastTarget = false;

            var edgeObject = new GameObject("PageInkEdge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            edgeObject.transform.SetParent(_pagePanel, false);
            var edgeRect = (RectTransform)edgeObject.transform;
            edgeRect.anchorMin = new Vector2(0f, 0f);
            edgeRect.anchorMax = new Vector2(0f, 1f);
            edgeRect.pivot = new Vector2(0.5f, 0.5f);
            edgeRect.sizeDelta = new Vector2(18f, 0f);
            edgeRect.anchoredPosition = Vector2.zero;
            _pageEdgeImage = edgeObject.GetComponent<Image>();
            _pageEdgeImage.color = new Color(0.12f, 0.055f, 0.02f, 0f);
            _pageEdgeImage.raycastTarget = false;
        }

        private void EnsureSauceSplatBuilt()
        {
            if (_sauceSplatRoot != null)
            {
                return;
            }

            _sauceSplatRoot = CreateFullScreenRoot("SauceSplatTransition");
            _sauceBackdrop = CreateFullScreenImage("SauceBackdrop", _sauceSplatRoot, new Color(0.84f, 0.12f, 0.035f, 0f));

            var splatObject = new GameObject("SauceSplat", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            splatObject.transform.SetParent(_sauceSplatRoot, false);
            _sauceSplatRect = (RectTransform)splatObject.transform;
            _sauceSplatRect.anchorMin = new Vector2(0.5f, 0.5f);
            _sauceSplatRect.anchorMax = new Vector2(0.5f, 0.5f);
            _sauceSplatRect.pivot = new Vector2(0.5f, 0.5f);
            _sauceSplatImage = splatObject.GetComponent<Image>();
            _sauceSplatImage.sprite = RequireSprite(SauceSplatSpritePath);
            _sauceSplatImage.preserveAspect = true;
            _sauceSplatImage.color = new Color(1f, 1f, 1f, 0f);
            _sauceSplatImage.raycastTarget = false;
        }

        private void EnsureCartoonBurstBuilt()
        {
            if (_burstRoot != null)
            {
                return;
            }

            _burstRoot = CreateFullScreenRoot("CartoonBurstTransition");
            _burstBackdrop = CreateFullScreenImage("BurstBackdrop", _burstRoot, new Color(1f, 0.68f, 0.14f, 0f));

            _burstRings = new RectTransform[BurstRingCount];
            _burstRingImages = new Image[BurstRingCount];
            var ringSprite = RequireSprite(BurstRingSpritePath);

            for (int i = 0; i < BurstRingCount; i++)
            {
                var ringObject = new GameObject($"BurstRing_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                ringObject.transform.SetParent(_burstRoot, false);
                var rect = (RectTransform)ringObject.transform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.localRotation = Quaternion.Euler(0f, 0f, i * 17f);

                var image = ringObject.GetComponent<Image>();
                image.sprite = ringSprite;
                image.preserveAspect = true;
                image.color = new Color(1f, 1f, 1f, 0f);
                image.raycastTarget = false;

                _burstRings[i] = rect;
                _burstRingImages[i] = image;
            }
        }

        private void EnsureFoodCurtainBuilt()
        {
            if (_foodCurtainRoot != null)
            {
                return;
            }

            _foodCurtainRoot = CreateFullScreenRoot("FoodCurtainTransition");
            _foodCurtainBackdrop = CreateFullScreenImage("FoodCurtainBackdrop", _foodCurtainRoot, new Color(0.98f, 0.72f, 0.22f, 0f));
            _foodCurtainItems = new RectTransform[FoodCurtainCount];
            _foodCurtainImages = new Image[FoodCurtainCount];

            for (int i = 0; i < FoodCurtainCount; i++)
            {
                var itemObject = new GameObject($"FoodCurtainItem_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                itemObject.transform.SetParent(_foodCurtainRoot, false);
                var rect = (RectTransform)itemObject.transform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);

                var image = itemObject.GetComponent<Image>();
                image.sprite = RequireSprite($"{FoodCurtainSpritePathPrefix}{i + 1:00}");
                image.preserveAspect = true;
                image.color = new Color(1f, 1f, 1f, 0f);
                image.raycastTarget = false;

                _foodCurtainItems[i] = rect;
                _foodCurtainImages[i] = image;
            }
        }

        private RectTransform CreateFullScreenRoot(string name)
        {
            var rootObject = new GameObject(name, typeof(RectTransform));
            rootObject.transform.SetParent(CachedTransform, false);
            var root = (RectTransform)rootObject.transform;
            Stretch(root);
            return root;
        }

        private Image CreateFullScreenImage(string name, Transform parent, Color color)
        {
            var imageObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            Stretch((RectTransform)imageObject.transform);
            var image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Image CreatePanel(string name, Transform parent, Color color, out RectTransform rect)
        {
            var panelObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panelObject.transform.SetParent(parent, false);
            rect = (RectTransform)panelObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            var image = panelObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private void CreateCurtainFolds(RectTransform parent, bool rightSide)
        {
            for (int i = 0; i < 5; i++)
            {
                var foldObject = new GameObject($"Fold_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                foldObject.transform.SetParent(parent, false);
                var foldRect = (RectTransform)foldObject.transform;
                foldRect.anchorMin = new Vector2((i + 0.5f) / 5f, 0f);
                foldRect.anchorMax = new Vector2((i + 0.5f) / 5f, 1f);
                foldRect.pivot = new Vector2(0.5f, 0.5f);
                foldRect.sizeDelta = new Vector2(24f, 0f);
                foldRect.anchoredPosition = Vector2.zero;
                var foldImage = foldObject.GetComponent<Image>();
                foldImage.color = rightSide ? new Color(0.18f, 0.015f, 0.02f, 0.2f) : new Color(1f, 0.72f, 0.32f, 0.16f);
                foldImage.raycastTarget = false;
            }
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

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localPosition = Vector3.zero;
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

        private static Sprite RequireSprite(string path)
        {
            var sprite = Resources.Load<Sprite>(path);
            if (sprite != null)
            {
                return sprite;
            }

            var texture = Resources.Load<Texture2D>(path);
            if (texture != null)
            {
                return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            }

            Log.Error($"Missing UI sprite resource: {path}");
            return null;
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

    public sealed class IrisWipeGraphic : MaskableGraphic
    {
        public float Radius { get; set; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            Vector2 center = rect.center;
            float outerRadius = Mathf.Sqrt(rect.width * rect.width + rect.height * rect.height) * 0.56f + 32f;
            float innerRadius = Mathf.Clamp(Radius, 0f, outerRadius);
            const int segments = 96;

            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector2 dir0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                Vector2 dir1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                int index = vh.currentVertCount;
                AddVert(vh, center + dir0 * outerRadius);
                AddVert(vh, center + dir1 * outerRadius);
                AddVert(vh, center + dir1 * innerRadius);
                AddVert(vh, center + dir0 * innerRadius);
                vh.AddTriangle(index, index + 1, index + 2);
                vh.AddTriangle(index + 2, index + 3, index);
            }
        }

        private void AddVert(VertexHelper vh, Vector2 position)
        {
            UIVertex vert = UIVertex.simpleVert;
            vert.color = color;
            vert.position = position;
            vh.AddVert(vert);
        }
    }
}
