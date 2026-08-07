using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 商店购买视觉副本。食物走带双层轨迹的卡牌弧线，装饰品和消耗品走 STS2 对应的药水/遗物入栏表现。
    /// 本组件独占传入的 RenderTexture，并保证在完成或中断时释放。
    /// </summary>
    public sealed class ShopPurchaseFlyView : MonoBehaviour
    {
        public const float ActiveFlyDuration = 0.35f;
        public const float PassiveFlyDuration = 0.35f;
        public const float ActiveFlashDuration = 1f;
        public const float PassiveFlashDuration = 0.75f;
        public const float PassiveFlashStagger = 0.12f;
        public const float FoodControlOffsetMin = 100f;
        public const float FoodControlOffsetMax = 400f;
        public const float FoodSpeedMin = 3.3f;
        public const float FoodSpeedMax = 3.75f;
        public const float FoodAccelerationMin = 4f;
        public const float FoodAccelerationMax = 5f;
        public const float FoodDurationMin = 0.5f;
        public const float FoodDurationMax = 0.8f;
        public const float FoodSpriteFadeInDuration = 0.3f;
        public const float FoodSpriteHoldDuration = 1f;

        private static readonly Color FoodDarkColor = new(0.16f, 0.16f, 0.16f, 1f);
        private static readonly Color OuterTrailColor = new(1f, 0.18f, 0.035f, 0.9f);
        private static readonly Color InnerTrailColor = new(1f, 0.72f, 0.08f, 0.95f);

        private RectTransform _rect;
        private RectTransform _layer;
        private CanvasGroup _group;
        private Image _image;
        private RawImage _rawImage;
        private Material _flashMaterial;
        private Sequence _sequence;
        private RenderTexture _ownedTexture;
        private readonly List<GameObject> _spawnedObjects = new();
        private Action _onArrived;
        private Action _onFinished;
        private bool _arrived;
        private bool _finished;
        private bool _beingDestroyed;

        public bool Initialize(RectTransform layer)
        {
            _rect = transform as RectTransform;
            _layer = layer;
            _group = GetComponent<CanvasGroup>();
            _image = GetComponent<Image>();
            if (_rect == null || _layer == null || _group == null || _image == null)
            {
                return false;
            }

            _group.alpha = 1f;
            _group.interactable = false;
            _group.blocksRaycasts = false;
            _image.raycastTarget = false;
            _image.preserveAspect = true;
            gameObject.layer = _layer.gameObject.layer;
            ConfigureRect(_rect, Vector2.zero, Vector2.one * 100f);
            return true;
        }

        public void PlayFood(
            Vector2 startCenter,
            Vector2 startSize,
            Vector2 endCenter,
            RenderTexture texture,
            Action onArrived,
            Action onFinished)
        {
            _ownedTexture = texture;
            _onArrived = onArrived;
            _onFinished = onFinished;
            ConfigureFoodTexture(texture);
            ConfigureRect(_rect, startCenter, startSize);

            var random = new System.Random(unchecked(Environment.TickCount * 397 ^ GetInstanceID()));
            float offset = RandomRange(random, FoodControlOffsetMin, FoodControlOffsetMax);
            float speed = RandomRange(random, FoodSpeedMin, FoodSpeedMax);
            float acceleration = RandomRange(random, FoodAccelerationMin, FoodAccelerationMax);
            float duration = RandomRange(random, FoodDurationMin, FoodDurationMax);
            float viewportCenterX = _layer.rect.center.x;
            Vector2 control = CalculateFoodControlPoint(
                startCenter,
                endCenter,
                offset,
                viewportCenterX);

            ShopPurchaseTrailGraphic outerTrail = CreateTrail("FoodTrailOuter", 18f, OuterTrailColor);
            ShopPurchaseTrailGraphic innerTrail = CreateTrail("FoodTrailInner", 10f, InnerTrailColor);
            ShopPurchaseSparkGraphic sparks = CreateSparks("FoodTrailSparks");
            _rect.SetAsLastSibling();

            _sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            _sequence.Append(DOVirtual.Float(0f, 1f, duration, normalized =>
            {
                float progress = EvaluateAcceleratedProgress(normalized, speed, acceleration);
                Vector2 position = CalculateQuadraticBezier(startCenter, control, endCenter, progress);
                Vector2 tangent = CalculateQuadraticTangent(startCenter, control, endCenter, progress);
                _rect.anchoredPosition = position;
                if (tangent.sqrMagnitude > 0.001f)
                {
                    _rect.localEulerAngles = new Vector3(
                        0f,
                        0f,
                        Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg - 90f);
                }

                float intro = Mathf.Clamp01(progress * 3f);
                _rect.localScale = Vector3.one * Mathf.Lerp(1f, 0.1f, intro);
                SetVisualColor(Color.Lerp(Color.white, FoodDarkColor, intro));
                outerTrail?.AddPoint(position);
                innerTrail?.AddPoint(position);
                sparks?.Tick(position, Time.unscaledDeltaTime, true);
            }).SetEase(Ease.Linear));
            _sequence.AppendCallback(CompleteArrival);
            _sequence.Append(DOVirtual.Float(0f, 1f, 0.28f, progress =>
            {
                _rect.localScale = Vector3.one * Mathf.Lerp(0.1f, 0f, progress);
                float trailFade = progress <= 0.25f
                    ? 1f
                    : 1f - (progress - 0.25f) / 0.75f;
                if (outerTrail != null)
                {
                    outerTrail.Fade = trailFade;
                }

                if (innerTrail != null)
                {
                    innerTrail.Fade = trailFade;
                }

                sparks?.Tick(endCenter, Time.unscaledDeltaTime, false);
                if (sparks != null)
                {
                    sparks.Fade = trailFade;
                }
            }).SetEase(Ease.InQuad));
            BindCompletionCallbacks();
        }

        public void PlayActive(
            Vector2 startCenter,
            Vector2 endCenter,
            Vector2 targetSize,
            Sprite sprite,
            Color fallbackColor,
            Action onArrived,
            Action onFinished)
        {
            _onArrived = onArrived;
            _onFinished = onFinished;
            ConfigureSprite(sprite, fallbackColor);
            ConfigureRect(_rect, startCenter, targetSize);
            Image flash = CreateFlashImage("ActiveItemArrivalFlash", endCenter, targetSize, sprite, fallbackColor);

            _sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            _sequence.Append(DOVirtual.Float(0f, 1f, ActiveFlyDuration, progress =>
            {
                float eased = 1f - (1f - progress) * (1f - progress);
                _rect.anchoredPosition = Vector2.LerpUnclamped(startCenter, endCenter, eased);
            }).SetEase(Ease.Linear));
            _sequence.AppendCallback(() =>
            {
                CompleteArrival();
                SetMainVisualVisible(false);
            });
            _sequence.Append(DOVirtual.Float(0f, 1f, ActiveFlashDuration, progress =>
            {
                UpdateFlash(
                    flash,
                    progress,
                    1f,
                    1.78f,
                    FlashAlpha(progress, 0.08f, 0.55f));
            }).SetEase(Ease.Linear));
            BindCompletionCallbacks();
        }

        /// <summary>复制食物表现共用的 Sprite 飞行轨迹；不创建或接管 RenderTexture。</summary>
        public void PlayFoodSprite(
            Vector2 startCenter,
            Vector2 startSize,
            Vector2 endCenter,
            Sprite sprite,
            Action onArrived,
            Action onFinished)
        {
            _onArrived = onArrived;
            _onFinished = onFinished;
            ConfigureSprite(sprite, Color.white);
            ConfigureRect(_rect, startCenter, startSize);
            _group.alpha = 0f;

            var random = new System.Random(unchecked(Environment.TickCount * 397 ^ GetInstanceID()));
            float offset = RandomRange(random, FoodControlOffsetMin, FoodControlOffsetMax);
            float speed = RandomRange(random, FoodSpeedMin, FoodSpeedMax);
            float acceleration = RandomRange(random, FoodAccelerationMin, FoodAccelerationMax);
            float duration = RandomRange(random, FoodDurationMin, FoodDurationMax);
            Vector2 control = CalculateFoodControlPoint(
                startCenter,
                endCenter,
                offset,
                _layer.rect.center.x);

            ShopPurchaseTrailGraphic outerTrail = CreateTrail("FoodTrailOuter", 18f, OuterTrailColor);
            ShopPurchaseTrailGraphic innerTrail = CreateTrail("FoodTrailInner", 10f, InnerTrailColor);
            ShopPurchaseSparkGraphic sparks = CreateSparks("FoodTrailSparks");
            _rect.SetAsLastSibling();

            _sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            _sequence.Append(_group
                .DOFade(1f, FoodSpriteFadeInDuration)
                .SetEase(Ease.OutSine));
            _sequence.AppendInterval(FoodSpriteHoldDuration);
            _sequence.Append(DOVirtual.Float(0f, 1f, duration, normalized =>
            {
                float progress = EvaluateAcceleratedProgress(normalized, speed, acceleration);
                Vector2 position = CalculateQuadraticBezier(startCenter, control, endCenter, progress);
                Vector2 tangent = CalculateQuadraticTangent(startCenter, control, endCenter, progress);
                _rect.anchoredPosition = position;
                if (tangent.sqrMagnitude > 0.001f)
                {
                    _rect.localEulerAngles = new Vector3(
                        0f,
                        0f,
                        Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg - 90f);
                }

                float intro = Mathf.Clamp01(progress * 3f);
                _rect.localScale = Vector3.one * Mathf.Lerp(1f, 0.1f, intro);
                SetVisualColor(Color.Lerp(Color.white, FoodDarkColor, intro));
                outerTrail?.AddPoint(position);
                innerTrail?.AddPoint(position);
                sparks?.Tick(position, Time.unscaledDeltaTime, true);
            }).SetEase(Ease.Linear));
            _sequence.AppendCallback(CompleteArrival);
            _sequence.Append(DOVirtual.Float(0f, 1f, 0.28f, progress =>
            {
                _rect.localScale = Vector3.one * Mathf.Lerp(0.1f, 0f, progress);
                float trailFade = progress <= 0.25f
                    ? 1f
                    : 1f - (progress - 0.25f) / 0.75f;
                if (outerTrail != null) outerTrail.Fade = trailFade;
                if (innerTrail != null) innerTrail.Fade = trailFade;
                sparks?.Tick(endCenter, Time.unscaledDeltaTime, false);
                if (sparks != null) sparks.Fade = trailFade;
            }).SetEase(Ease.InQuad));
            BindCompletionCallbacks();
        }

        public void PlayPassive(
            Vector2 startCenter,
            Vector2 startSize,
            Vector2 endCenter,
            Vector2 targetSize,
            Sprite sprite,
            Color fallbackColor,
            Action onArrived,
            Action onFinished)
        {
            _onArrived = onArrived;
            _onFinished = onFinished;
            ConfigureSprite(sprite, fallbackColor);
            ConfigureRect(_rect, startCenter, startSize);
            Image[] flashes = new Image[3];
            for (int i = 0; i < flashes.Length; i++)
            {
                flashes[i] = CreateFlashImage(
                    $"PassiveItemArrivalFlash_{i}",
                    endCenter,
                    targetSize,
                    sprite,
                    fallbackColor);
            }

            _sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            _sequence.Append(DOVirtual.Float(0f, 1f, PassiveFlyDuration, progress =>
            {
                float eased = Mathf.Sin(progress * Mathf.PI * 0.5f);
                _rect.anchoredPosition = Vector2.LerpUnclamped(startCenter, endCenter, eased);
                _rect.sizeDelta = Vector2.LerpUnclamped(startSize, targetSize, eased);
            }).SetEase(Ease.Linear));
            _sequence.AppendCallback(() =>
            {
                CompleteArrival();
                SetMainVisualVisible(false);
            });
            float flashTotalDuration =
                PassiveFlashDuration + PassiveFlashStagger * (flashes.Length - 1);
            _sequence.Append(DOVirtual.Float(0f, flashTotalDuration, flashTotalDuration, elapsed =>
            {
                for (int i = 0; i < flashes.Length; i++)
                {
                    float progress = Mathf.Clamp01(
                        (elapsed - PassiveFlashStagger * i) / PassiveFlashDuration);
                    if (elapsed < PassiveFlashStagger * i)
                    {
                        UpdateFlash(flashes[i], 0f, 0.61f, 1.35f, 0f);
                        continue;
                    }

                    UpdateFlash(
                        flashes[i],
                        progress,
                        0.61f,
                        1.35f,
                        FlashAlpha(progress, 0.16f, 0.62f));
                }
            }).SetEase(Ease.Linear));
            BindCompletionCallbacks();
        }

        public void Cancel()
        {
            if (_finished)
            {
                return;
            }

            if (_sequence != null && _sequence.IsActive())
            {
                _sequence.Kill();
            }

            CompleteFinish();
        }

        public static float EvaluateAcceleratedProgress(
            float normalizedTime,
            float initialSpeed,
            float acceleration)
        {
            float time = Mathf.Clamp01(normalizedTime);
            float denominator = Mathf.Max(0.0001f, initialSpeed + acceleration * 0.5f);
            return Mathf.Clamp01(
                (initialSpeed * time + acceleration * time * time * 0.5f) / denominator);
        }

        public static Vector2 CalculateQuadraticBezier(
            Vector2 start,
            Vector2 control,
            Vector2 end,
            float progress)
        {
            float t = Mathf.Clamp01(progress);
            float inverse = 1f - t;
            return inverse * inverse * start
                + 2f * inverse * t * control
                + t * t * end;
        }

        public static Vector2 CalculateQuadraticTangent(
            Vector2 start,
            Vector2 control,
            Vector2 end,
            float progress)
        {
            float t = Mathf.Clamp01(progress);
            return 2f * (1f - t) * (control - start)
                + 2f * t * (end - control);
        }

        public static Vector2 CalculateFoodControlPoint(
            Vector2 start,
            Vector2 end,
            float offset,
            float viewportCenterX)
        {
            Vector2 direction = end - start;
            Vector2 perpendicular = direction.sqrMagnitude > 0.001f
                ? new Vector2(-direction.y, direction.x).normalized
                : Vector2.up;
            float side = end.x < viewportCenterX ? -1f : 1f;
            return (start + end) * 0.5f
                + perpendicular * Mathf.Clamp(
                    offset,
                    FoodControlOffsetMin,
                    FoodControlOffsetMax) * side;
        }

        private void OnDestroy()
        {
            _beingDestroyed = true;
            if (!_finished)
            {
                if (_sequence != null && _sequence.IsActive())
                {
                    _sequence.Kill();
                }

                CompleteFinish(completeArrival: false);
            }
        }

        private void ConfigureFoodTexture(RenderTexture texture)
        {
            _image.enabled = false;
            if (_rawImage == null)
            {
                var go = new GameObject(
                    "FoodRenderTexture",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(RawImage));
                go.transform.SetParent(_rect, false);
                _rawImage = go.GetComponent<RawImage>();
                RectTransform rawRect = _rawImage.rectTransform;
                rawRect.anchorMin = Vector2.zero;
                rawRect.anchorMax = Vector2.one;
                rawRect.offsetMin = Vector2.zero;
                rawRect.offsetMax = Vector2.zero;
            }

            _rawImage.enabled = texture != null;
            _rawImage.texture = texture;
            _rawImage.color = Color.white;
            _rawImage.raycastTarget = false;
        }

        private void ConfigureSprite(Sprite sprite, Color fallbackColor)
        {
            if (_rawImage != null)
            {
                _rawImage.enabled = false;
            }

            _image.enabled = true;
            _image.sprite = sprite;
            _image.color = sprite != null ? Color.white : fallbackColor;
            _image.preserveAspect = true;
        }

        private void SetVisualColor(Color color)
        {
            if (_rawImage != null && _rawImage.enabled)
            {
                _rawImage.color = color;
            }
            else if (_image != null)
            {
                _image.color = color;
            }
        }

        private void SetMainVisualVisible(bool visible)
        {
            if (_rawImage != null)
            {
                _rawImage.enabled = visible && _rawImage.texture != null;
            }

            if (_image != null)
            {
                _image.enabled = visible;
            }
        }

        private ShopPurchaseTrailGraphic CreateTrail(string objectName, float width, Color color)
        {
            var go = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(ShopPurchaseTrailGraphic));
            go.transform.SetParent(_layer, false);
            go.layer = _layer.gameObject.layer;
            ConfigureFullLayerRect(go.transform as RectTransform);
            var trail = go.GetComponent<ShopPurchaseTrailGraphic>();
            trail.raycastTarget = false;
            trail.Width = width;
            trail.TrailColor = color;
            _spawnedObjects.Add(go);
            return trail;
        }

        private ShopPurchaseSparkGraphic CreateSparks(string objectName)
        {
            var go = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(ShopPurchaseSparkGraphic));
            go.transform.SetParent(_layer, false);
            go.layer = _layer.gameObject.layer;
            ConfigureFullLayerRect(go.transform as RectTransform);
            var sparks = go.GetComponent<ShopPurchaseSparkGraphic>();
            sparks.raycastTarget = false;
            _spawnedObjects.Add(go);
            return sparks;
        }

        private Image CreateFlashImage(
            string objectName,
            Vector2 center,
            Vector2 size,
            Sprite sprite,
            Color fallbackColor)
        {
            var go = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            go.transform.SetParent(_layer, false);
            go.layer = _layer.gameObject.layer;
            var image = go.GetComponent<Image>();
            ConfigureRect(image.rectTransform, center, size);
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.material = GetFlashMaterial();
            Color color = sprite != null ? Color.white : fallbackColor;
            color.a = 0f;
            image.color = color;
            _spawnedObjects.Add(go);
            return image;
        }

        private Material GetFlashMaterial()
        {
            if (_flashMaterial != null)
            {
                return _flashMaterial;
            }

            Shader shader = Shader.Find("GourmetProject/UIShopPurchaseTrail");
            if (shader == null)
            {
                return null;
            }

            _flashMaterial = new Material(shader)
            {
                name = "UIShopPurchaseFlash_Runtime",
                hideFlags = HideFlags.HideAndDontSave,
            };
            return _flashMaterial;
        }

        private static void UpdateFlash(
            Image flash,
            float progress,
            float startScale,
            float endScale,
            float alpha)
        {
            if (flash == null)
            {
                return;
            }

            float easedScale = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI * 0.5f);
            flash.rectTransform.localScale =
                Vector3.one * Mathf.Lerp(startScale, endScale, easedScale);
            Color color = flash.color;
            color.r = Mathf.Max(color.r, 1.35f);
            color.g = Mathf.Max(color.g, 1.1f);
            color.b = Mathf.Max(color.b, 0.45f);
            color.a = Mathf.Clamp01(alpha);
            flash.color = color;
        }

        private static float FlashAlpha(float progress, float fadeInEnd, float fadeOutStart)
        {
            float t = Mathf.Clamp01(progress);
            if (t < fadeInEnd)
            {
                return t / Mathf.Max(0.001f, fadeInEnd);
            }

            if (t <= fadeOutStart)
            {
                return 1f;
            }

            return 1f - (t - fadeOutStart) / Mathf.Max(0.001f, 1f - fadeOutStart);
        }

        private void BindCompletionCallbacks()
        {
            _sequence.OnComplete(() => CompleteFinish());
            _sequence.OnKill(() => CompleteFinish(completeArrival: !_beingDestroyed));
        }

        private void CompleteArrival()
        {
            if (_arrived)
            {
                return;
            }

            _arrived = true;
            Action callback = _onArrived;
            _onArrived = null;
            callback?.Invoke();
        }

        private void CompleteFinish(bool completeArrival = true)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            if (completeArrival)
            {
                CompleteArrival();
            }
            else
            {
                // 场景/父 UI 正在销毁时，刷新抵达目标会访问同样正在销毁的 HUD。
                // 完成回调仍会注销本动画；宿主销毁时会自行清零飞行计数。
                _onArrived = null;
            }

            ReleaseOwnedTexture();
            for (int i = 0; i < _spawnedObjects.Count; i++)
            {
                GameObject spawned = _spawnedObjects[i];
                if (spawned != null)
                {
                    DestroyRuntimeObject(spawned);
                }
            }

            _spawnedObjects.Clear();
            if (_flashMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_flashMaterial);
                }
                else
                {
                    DestroyImmediate(_flashMaterial);
                }

                _flashMaterial = null;
            }

            Action callback = _onFinished;
            _onFinished = null;
            callback?.Invoke();
            if (!_beingDestroyed && gameObject != null)
            {
                DestroyRuntimeObject(gameObject);
            }
        }

        private void ReleaseOwnedTexture()
        {
            if (_ownedTexture == null)
            {
                return;
            }

            if (_rawImage != null && _rawImage.texture == _ownedTexture)
            {
                _rawImage.texture = null;
            }

            _ownedTexture.Release();
            if (Application.isPlaying)
            {
                Destroy(_ownedTexture);
            }
            else
            {
                DestroyImmediate(_ownedTexture);
            }

            _ownedTexture = null;
        }

        private static void ConfigureRect(
            RectTransform rect,
            Vector2 center,
            Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = center;
            rect.sizeDelta = new Vector2(
                Mathf.Max(1f, size.x),
                Mathf.Max(1f, size.y));
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private static void ConfigureFullLayerRect(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private static float RandomRange(System.Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }

        private static void DestroyRuntimeObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }

    /// <summary>程序化双层 UI 丝带；使用加色 UI Shader，不依赖外部贴图。</summary>
    public sealed class ShopPurchaseTrailGraphic : MaskableGraphic
    {
        private const int MaxPoints = 56;
        private readonly List<Vector2> _points = new();
        private Material _runtimeMaterial;
        private float _fade = 1f;

        public float Width { get; set; } = 12f;
        public Color TrailColor { get; set; } = Color.white;

        public float Fade
        {
            get => _fade;
            set
            {
                _fade = Mathf.Clamp01(value);
                SetVerticesDirty();
            }
        }

        protected override void Awake()
        {
            base.Awake();
            Shader shader = Shader.Find("GourmetProject/UIShopPurchaseTrail");
            if (shader != null)
            {
                _runtimeMaterial = new Material(shader)
                {
                    name = "UIShopPurchaseTrail_Runtime",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                material = _runtimeMaterial;
            }
        }

        public void AddPoint(Vector2 point)
        {
            if (_points.Count == 0 || Vector2.SqrMagnitude(_points[_points.Count - 1] - point) >= 4f)
            {
                _points.Add(point);
                if (_points.Count > MaxPoints)
                {
                    _points.RemoveAt(0);
                }

                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (_points.Count < 2 || _fade <= 0f)
            {
                return;
            }

            for (int i = 1; i < _points.Count; i++)
            {
                Vector2 start = _points[i - 1];
                Vector2 end = _points[i];
                Vector2 direction = end - start;
                if (direction.sqrMagnitude <= 0.001f)
                {
                    continue;
                }

                Vector2 normal = new Vector2(-direction.y, direction.x).normalized;
                float age = i / (float)(_points.Count - 1);
                float halfWidth = Width * 0.5f * Mathf.Lerp(0.18f, 1f, age);
                Color segmentColor = TrailColor;
                segmentColor.a *= age * _fade;
                AddQuad(
                    vertexHelper,
                    start - normal * halfWidth,
                    start + normal * halfWidth,
                    end + normal * halfWidth,
                    end - normal * halfWidth,
                    segmentColor);
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_runtimeMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_runtimeMaterial);
                }
                else
                {
                    DestroyImmediate(_runtimeMaterial);
                }
            }
        }

        internal static void AddQuad(
            VertexHelper helper,
            Vector2 bottomLeft,
            Vector2 topLeft,
            Vector2 topRight,
            Vector2 bottomRight,
            Color color)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;
            int startIndex = helper.currentVertCount;
            vertex.position = bottomLeft;
            vertex.uv0 = new Vector2(0f, 0f);
            helper.AddVert(vertex);
            vertex.position = topLeft;
            vertex.uv0 = new Vector2(0f, 1f);
            helper.AddVert(vertex);
            vertex.position = topRight;
            vertex.uv0 = new Vector2(1f, 1f);
            helper.AddVert(vertex);
            vertex.position = bottomRight;
            vertex.uv0 = new Vector2(1f, 0f);
            helper.AddVert(vertex);
            helper.AddTriangle(startIndex, startIndex + 1, startIndex + 2);
            helper.AddTriangle(startIndex + 2, startIndex + 3, startIndex);
        }
    }

    /// <summary>卡牌轨迹上的轻量程序化火花。</summary>
    public sealed class ShopPurchaseSparkGraphic : MaskableGraphic
    {
        private const int MaxParticles = 28;
        private readonly List<SparkParticle> _particles = new();
        private Material _runtimeMaterial;
        private System.Random _random;
        private float _spawnAccumulator;
        private float _fade = 1f;

        public float Fade
        {
            get => _fade;
            set
            {
                _fade = Mathf.Clamp01(value);
                SetVerticesDirty();
            }
        }

        protected override void Awake()
        {
            base.Awake();
            _random = new System.Random(unchecked(Environment.TickCount * 397 ^ GetInstanceID()));
            Shader shader = Shader.Find("GourmetProject/UIShopPurchaseTrail");
            if (shader != null)
            {
                _runtimeMaterial = new Material(shader)
                {
                    name = "UIShopPurchaseSpark_Runtime",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                material = _runtimeMaterial;
            }
        }

        public void Tick(Vector2 head, float deltaTime, bool spawn)
        {
            float dt = Mathf.Clamp(deltaTime, 0f, 0.05f);
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                SparkParticle particle = _particles[i];
                particle.Age += dt;
                if (particle.Age >= particle.Lifetime)
                {
                    _particles.RemoveAt(i);
                    continue;
                }

                particle.Position += particle.Velocity * dt;
                particle.Velocity *= Mathf.Pow(0.18f, dt);
                _particles[i] = particle;
            }

            if (spawn)
            {
                _spawnAccumulator += dt;
                while (_spawnAccumulator >= 0.035f && _particles.Count < MaxParticles)
                {
                    _spawnAccumulator -= 0.035f;
                    float angle = RandomRange(0f, Mathf.PI * 2f);
                    float speed = RandomRange(22f, 58f);
                    _particles.Add(new SparkParticle
                    {
                        Position = head,
                        Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed,
                        Age = 0f,
                        Lifetime = RandomRange(0.28f, 0.58f),
                        Size = RandomRange(2.5f, 6f),
                    });
                }
            }

            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            for (int i = 0; i < _particles.Count; i++)
            {
                SparkParticle particle = _particles[i];
                float life = 1f - particle.Age / particle.Lifetime;
                float halfSize = particle.Size * 0.5f * Mathf.Lerp(0.4f, 1f, life);
                Color color = Color.Lerp(
                    new Color(1f, 0.18f, 0.03f, 1f),
                    new Color(1f, 0.92f, 0.28f, 1f),
                    life);
                color.a = life * _fade;
                Vector2 min = particle.Position - Vector2.one * halfSize;
                Vector2 max = particle.Position + Vector2.one * halfSize;
                ShopPurchaseTrailGraphic.AddQuad(
                    vertexHelper,
                    new Vector2(min.x, min.y),
                    new Vector2(min.x, max.y),
                    new Vector2(max.x, max.y),
                    new Vector2(max.x, min.y),
                    color);
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_runtimeMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_runtimeMaterial);
                }
                else
                {
                    DestroyImmediate(_runtimeMaterial);
                }
            }
        }

        private struct SparkParticle
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public float Age;
            public float Lifetime;
            public float Size;
        }

        private float RandomRange(float min, float max)
        {
            return min + (float)_random.NextDouble() * (max - min);
        }
    }
}
