using System.Collections.Generic;
using DG.Tweening;
using GourmetProject.Runtime.Pooling;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>单个欢乐蛋糕的分辨率无关视觉状态。</summary>
    public readonly struct CakeLayerVisualState
    {
        public CakeLayerVisualState(
            float viewportX,
            float viewportY,
            float rotationZ,
            float scaleX,
            float scaleY,
            float scaleZ)
        {
            ViewportX = viewportX;
            ViewportY = viewportY;
            RotationZ = rotationZ;
            ScaleX = scaleX;
            ScaleY = scaleY;
            ScaleZ = scaleZ;
        }

        public float ViewportX { get; }
        public float ViewportY { get; }
        public float RotationZ { get; }
        public float ScaleX { get; }
        public float ScaleY { get; }
        public float ScaleZ { get; }
    }

    /// <summary>欢乐蛋糕层数的世界表现：增加时从画面上方落下，消耗时随机溶解已有蛋糕。</summary>
    public sealed class CakeLayerWorldFx : MonoBehaviour
    {
        private const int CakePoolPrewarm = 8;
        private const int CakePoolMaxInactive = 32;
        private static readonly int SpriteUvRectId = Shader.PropertyToID("_SpriteUvRect");
        private static readonly int DigestProgressId = Shader.PropertyToID("_DigestProgress");
        private static readonly int DigestCenterId = Shader.PropertyToID("_DigestCenter");
        private static readonly int DigestGridSizeId = Shader.PropertyToID("_DigestGridSize");
        private static readonly int DigestSeedId = Shader.PropertyToID("_DigestSeed");

        private readonly List<SpriteRenderer> _cakes = new();
        private readonly HashSet<SpriteRenderer> _rentedCakes = new();
        private readonly List<SpriteRenderer> _releaseBuffer = new();
        private readonly Dictionary<SpriteRenderer, Tween> _cakeTweens = new();
        private readonly Dictionary<SpriteRenderer, Vector3> _cakeLandingPositions = new();
        private readonly Dictionary<SpriteRenderer, MaterialPropertyBlock> _cakePropertyBlocks = new();
        private readonly Dictionary<SpriteRenderer, BuffBurstVisualState> _buffBurstStates = new();
        private readonly List<Tween> _buffBurstTweens = new();
        [Header("Prefab Refs")]
        [SerializeField] private Transform _root;
        [SerializeField] private SpriteRenderer _cakePrefab;

        [Header("Designer Tuning")]
        [SerializeField] private Vector2 _randomScaleMultiplier = new(0.87f, 1.13f);
        [SerializeField] private Vector2 _randomRotationRange = new(-10f, 10f);
        [SerializeField] private float _fallDuration = 0.55f;
        [SerializeField] private float _fallStagger = 0.035f;
        [SerializeField] private float _maxFallStagger = 0.5f;
        [SerializeField] private float _dissolveDuration = 0.72f;
        [SerializeField] private float _dissolveStagger = 0.045f;

        private BattleWorldController _owner;
        private Camera _camera;
        private System.Random _random;
        private int _buffBurstVersion;
        private GameObjectPool _cakePool;

        private readonly struct BuffBurstVisualState
        {
            public BuffBurstVisualState(Vector3 scale, Color color)
            {
                Scale = scale;
                Color = color;
            }

            public Vector3 Scale { get; }
            public Color Color { get; }
        }

        public void Configure(BattleWorldController owner, Camera camera)
        {
            _owner = owner;
            _camera = camera != null ? camera : Camera.main;
            _random ??= new System.Random();
            EnsurePool();
        }

        public void PlayChange(int before, int after)
        {
            int delta = after - before;
            if (delta == 0)
            {
                return;
            }

            ResetBuffBurstVisuals();
            SetVisible(true);
            if (delta > 0)
            {
                for (int i = 0; i < delta; i++)
                {
                    CreateCake(RandomLandingPoint(), true, i);
                }
                return;
            }

            for (int i = 0; i < -delta && _cakes.Count > 0; i++)
            {
                int index = _random.Next(0, _cakes.Count);
                SpriteRenderer cake = _cakes[index];
                _cakes.RemoveAt(index);
                PlayDissolve(cake, i * _dissolveStagger);
            }
        }

        public void Clear()
        {
            ResetBuffBurstVisuals();
            _cakes.Clear();
            _releaseBuffer.Clear();
            _releaseBuffer.AddRange(_rentedCakes);
            for (int i = 0; i < _releaseBuffer.Count; i++)
            {
                ReleaseCake(_releaseBuffer[i]);
            }
            _releaseBuffer.Clear();
        }

        private void OnDisable()
        {
            ResetBuffBurstVisuals();
            StopCakeTweensForDisable();
        }

        private void OnDestroy()
        {
            ResetBuffBurstVisuals();
            _cakePool?.Clear();
            _cakePropertyBlocks.Clear();
            _cakeLandingPositions.Clear();
        }

        public List<CakeLayerVisualState> CaptureState()
        {
            var result = new List<CakeLayerVisualState>(_cakes.Count);
            if (_camera == null)
            {
                return result;
            }

            foreach (SpriteRenderer cake in _cakes)
            {
                if (cake == null)
                {
                    continue;
                }

                Transform cakeTransform = cake.transform;
                Vector3 viewport = _camera.WorldToViewportPoint(cakeTransform.position);
                Vector3 scale = cakeTransform.localScale;
                result.Add(new CakeLayerVisualState(
                    viewport.x,
                    viewport.y,
                    cakeTransform.eulerAngles.z,
                    scale.x,
                    scale.y,
                    scale.z));
            }

            return result;
        }

        public void RestoreState(IReadOnlyList<CakeLayerVisualState> states)
        {
            Clear();
            SetVisible(true);

            if (_camera == null || states == null)
            {
                return;
            }

            float cameraDepth = Mathf.Abs(_camera.transform.position.z);
            for (int i = 0; i < states.Count; i++)
            {
                CakeLayerVisualState state = states[i];
                Vector3 world = _camera.ViewportToWorldPoint(
                    new Vector3(state.ViewportX, state.ViewportY, cameraDepth));
                world.z = 0f;
                SpriteRenderer cake = CreateCake(world, animate: false, staggerIndex: 0);
                if (cake == null)
                {
                    continue;
                }

                Transform cakeTransform = cake.transform;
                cakeTransform.rotation = Quaternion.Euler(0f, 0f, state.RotationZ);
                cakeTransform.localScale = new Vector3(state.ScaleX, state.ScaleY, state.ScaleZ);
            }
        }

        public void SetVisible(bool visible)
        {
            if (_root != null)
            {
                _root.gameObject.SetActive(visible);
            }
        }

        /// <summary>让当前已经存在于场外的蛋糕进入短暂收缩、提亮的 Buff 聚能态。</summary>
        public void BeginBuffCharge(float duration)
        {
            ResetBuffBurstVisuals();
            CaptureBuffBurstStates();
            if (_buffBurstStates.Count == 0)
            {
                return;
            }

            int version = _buffBurstVersion;
            float safeDuration = Mathf.Max(0.02f, duration);
            float delayStep = Mathf.Min(0.008f, safeDuration * 0.04f);
            float maxStagger = Mathf.Min(0.12f, safeDuration * 0.42f);
            int cakeIndex = 0;
            for (int i = 0; i < _cakes.Count; i++)
            {
                SpriteRenderer cake = _cakes[i];
                if (cake == null
                    || !_buffBurstStates.TryGetValue(cake, out BuffBurstVisualState state))
                {
                    continue;
                }

                float delay = Mathf.Min(cakeIndex * delayStep, maxStagger);
                float tweenDuration = Mathf.Max(0.01f, safeDuration - delay);
                Color chargedColor = Color.Lerp(
                    state.Color,
                    new Color(1f, 0.78f, 0.42f, state.Color.a),
                    0.58f);
                Sequence sequence = DOTween.Sequence()
                    .AppendInterval(delay)
                    .Append(cake.transform.DOScale(state.Scale * 0.92f, tweenDuration)
                        .SetEase(Ease.InCubic))
                    .Join(cake.DOColor(chargedColor, tweenDuration)
                        .SetEase(Ease.InOutSine))
                    .SetAutoKill(false)
                    .SetLink(cake.gameObject)
                    .OnKill(() => RestoreBuffBurstStateIfCurrent(cake, state, version));
                _buffBurstTweens.Add(sequence);
                cakeIndex++;
            }
        }

        /// <summary>按三连爆发的当前强度脉冲所有场外蛋糕；不会创建新的世界物件。</summary>
        public void PlayBuffPulse(float strength, Color theme, float duration)
        {
            if (_buffBurstStates.Count == 0)
            {
                CaptureBuffBurstStates();
            }

            if (_buffBurstStates.Count == 0)
            {
                return;
            }

            KillBuffBurstTweens(restoreVisuals: true, clearStates: false);
            int version = _buffBurstVersion;
            float clampedStrength = Mathf.Clamp01(strength);
            float safeDuration = Mathf.Max(0.025f, duration);
            float delayStep = Mathf.Min(0.006f, safeDuration * 0.03f);
            float maxStagger = Mathf.Min(0.08f, safeDuration * 0.30f);
            float peakScale = Mathf.Lerp(1.08f, 1.20f, clampedStrength);
            int cakeIndex = 0;
            for (int i = 0; i < _cakes.Count; i++)
            {
                SpriteRenderer cake = _cakes[i];
                if (cake == null
                    || !_buffBurstStates.TryGetValue(cake, out BuffBurstVisualState state))
                {
                    continue;
                }

                float delay = Mathf.Min(cakeIndex * delayStep, maxStagger);
                float activeDuration = Mathf.Max(0.015f, safeDuration - delay);
                Color peakColor = Color.Lerp(
                    state.Color,
                    SettlementColorPalette.TextFor(theme),
                    Mathf.Lerp(0.34f, 0.68f, clampedStrength));
                Sequence sequence = DOTween.Sequence()
                    .AppendInterval(delay)
                    .Append(cake.transform.DOScale(
                            state.Scale * peakScale,
                            activeDuration * 0.42f)
                        .SetEase(Ease.OutBack))
                    .Join(cake.DOColor(peakColor, activeDuration * 0.42f)
                        .SetEase(Ease.OutCubic))
                    .Append(cake.transform.DOScale(state.Scale, activeDuration * 0.58f)
                        .SetEase(Ease.OutCubic))
                    .Join(cake.DOColor(state.Color, activeDuration * 0.58f)
                        .SetEase(Ease.OutCubic))
                    .SetLink(cake.gameObject)
                    .OnComplete(() => RestoreBuffBurstStateIfCurrent(cake, state, version))
                    .OnKill(() => RestoreBuffBurstStateIfCurrent(cake, state, version));
                _buffBurstTweens.Add(sequence);
                cakeIndex++;
            }
        }

        /// <summary>中断或结束结算时恢复每块蛋糕进入爆发前的随机缩放与颜色。</summary>
        public void ResetBuffBurstVisuals()
        {
            _buffBurstVersion++;
            KillBuffBurstTweens(restoreVisuals: true, clearStates: true);
        }

        private void CaptureBuffBurstStates()
        {
            _buffBurstVersion++;
            _buffBurstStates.Clear();
            for (int i = 0; i < _cakes.Count; i++)
            {
                SpriteRenderer cake = _cakes[i];
                if (cake == null)
                {
                    continue;
                }

                _buffBurstStates[cake] = new BuffBurstVisualState(
                    cake.transform.localScale,
                    cake.color);
            }
        }

        private void KillBuffBurstTweens(bool restoreVisuals, bool clearStates)
        {
            for (int i = 0; i < _buffBurstTweens.Count; i++)
            {
                _buffBurstTweens[i]?.Kill();
            }
            _buffBurstTweens.Clear();

            if (restoreVisuals)
            {
                foreach (KeyValuePair<SpriteRenderer, BuffBurstVisualState> pair in _buffBurstStates)
                {
                    if (pair.Key == null)
                    {
                        continue;
                    }

                    pair.Key.transform.localScale = pair.Value.Scale;
                    pair.Key.color = pair.Value.Color;
                }
            }

            if (clearStates)
            {
                _buffBurstStates.Clear();
            }
        }

        private void RestoreBuffBurstStateIfCurrent(
            SpriteRenderer cake,
            BuffBurstVisualState state,
            int version)
        {
            if (cake == null || version != _buffBurstVersion)
            {
                return;
            }

            cake.transform.localScale = state.Scale;
            cake.color = state.Color;
        }

        private SpriteRenderer CreateCake(Vector3 landing, bool animate, int staggerIndex)
        {
            if (_root == null || _cakePrefab == null)
            {
                Debug.LogError($"{nameof(CakeLayerWorldFx)} prefab references are incomplete.", this);
                return null;
            }

            EnsurePool();
            SpriteRenderer renderer = _cakePool?.Get<SpriteRenderer>(_root);
            if (renderer == null)
            {
                return null;
            }

            _rentedCakes.Add(renderer);
            GameObject cakeObject = renderer.gameObject;
            cakeObject.name = $"CakeLayer_{_cakes.Count + 1}";
            float scale = Mathf.Lerp(_randomScaleMultiplier.x, _randomScaleMultiplier.y, (float)_random.NextDouble());
            cakeObject.transform.localScale = Vector3.Scale(_cakePrefab.transform.localScale, Vector3.one * scale);
            float rotation = Mathf.Lerp(_randomRotationRange.x, _randomRotationRange.y, (float)_random.NextDouble());
            cakeObject.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
            BattleSorting.Apply(renderer, BattleSorting.Fx, BattleSorting.OrderFloatingText - 1);
            _cakes.Add(renderer);
            _cakeLandingPositions[renderer] = landing;

            if (!animate || _camera == null)
            {
                cakeObject.transform.position = landing;
                return renderer;
            }

            Vector3 start = landing;
            start.y = _camera.ViewportToWorldPoint(new Vector3(0.5f, 1.15f, Mathf.Abs(_camera.transform.position.z))).y;
            cakeObject.transform.position = start;
            float delay = Mathf.Min(staggerIndex * _fallStagger, _maxFallStagger);
            Tween fallTween = cakeObject.transform.DOMove(landing, _fallDuration)
                .SetDelay(delay)
                .SetEase(Ease.OutBounce)
                .SetLink(cakeObject)
                .OnComplete(() => ForgetCakeTween(renderer));
            _cakeTweens[renderer] = fallTween;
            return renderer;
        }

        private Vector3 RandomLandingPoint()
        {
            if (_camera == null)
            {
                return Vector3.zero;
            }

            Rect blocked = default;
            bool hasBlocked = _owner != null && _owner.TryGetExistingGridScreenRect(22f, out blocked);
            Vector2 screen = default;
            bool accepted = false;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                screen = new Vector2(
                    Mathf.Lerp(Screen.width * 0.18f, Screen.width * 0.82f, (float)_random.NextDouble()),
                    Mathf.Lerp(Screen.height * 0.12f, Screen.height * 0.84f, (float)_random.NextDouble()));
                if (!hasBlocked || !blocked.Contains(screen))
                {
                    accepted = true;
                    break;
                }
            }

            if (!accepted && hasBlocked)
            {
                // 极端大餐桌时仍保证落点在格子外：选屏幕内余量最大的一侧。
                float leftSpace = blocked.xMin;
                float rightSpace = Screen.width - blocked.xMax;
                float bottomSpace = blocked.yMin;
                float topSpace = Screen.height - blocked.yMax;
                float best = Mathf.Max(leftSpace, rightSpace, bottomSpace, topSpace);
                if (best == leftSpace)
                {
                    screen.x = Mathf.Max(8f, blocked.xMin - 28f);
                }
                else if (best == rightSpace)
                {
                    screen.x = Mathf.Min(Screen.width - 8f, blocked.xMax + 28f);
                }
                else if (best == bottomSpace)
                {
                    screen.y = Mathf.Max(8f, blocked.yMin - 28f);
                }
                else
                {
                    screen.y = Mathf.Min(Screen.height - 8f, blocked.yMax + 28f);
                }
            }

            Vector3 world = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, Mathf.Abs(_camera.transform.position.z)));
            world.z = 0f;
            return world;
        }

        private void PlayDissolve(SpriteRenderer cake, float delay)
        {
            if (cake == null)
            {
                return;
            }

            Material material = SpriteRenderStyle.DigestDissolveMaterial;
            if (material == null)
            {
                Color startColor = cake.color;
                Tween fadeTween = DOVirtual.Float(startColor.a, 0f, _dissolveDuration, alpha =>
                    {
                        if (cake == null) return;
                        Color color = startColor;
                        color.a = alpha;
                        cake.color = color;
                    })
                    .SetDelay(delay).SetLink(cake.gameObject)
                    .OnComplete(() => CompleteCakeTweenAndRelease(cake));
                _cakeTweens[cake] = fadeTween;
                return;
            }

            cake.sharedMaterial = material;
            if (!_cakePropertyBlocks.TryGetValue(cake, out MaterialPropertyBlock block))
            {
                block = new MaterialPropertyBlock();
                _cakePropertyBlocks[cake] = block;
            }
            else
            {
                block.Clear();
            }
            Sprite sprite = cake.sprite;
            Texture texture = sprite.texture;
            Rect rect = sprite.textureRect;
            block.SetVector(SpriteUvRectId, new Vector4(
                rect.xMin / texture.width, rect.yMin / texture.height,
                rect.width / texture.width, rect.height / texture.height));
            block.SetVector(DigestCenterId, new Vector4(0.5f, 0.5f, 0f, 0f));
            block.SetVector(DigestGridSizeId, Vector4.one);
            block.SetFloat(DigestSeedId, (float)_random.NextDouble() * 100f);
            block.SetFloat(DigestProgressId, 0f);
            cake.SetPropertyBlock(block);

            Tween dissolveTween = DOVirtual.Float(0f, 1f, _dissolveDuration, value =>
                {
                    if (cake == null) return;
                    block.SetFloat(DigestProgressId, Mathf.SmoothStep(0f, 1f, value));
                    cake.SetPropertyBlock(block);
                })
                .SetDelay(delay)
                .SetEase(Ease.Linear)
                .SetLink(cake.gameObject)
                .OnComplete(() => CompleteCakeTweenAndRelease(cake));
            _cakeTweens[cake] = dissolveTween;
        }

        private void EnsurePool()
        {
            if (_cakePool != null || _root == null || _cakePrefab == null)
            {
                return;
            }

            _cakePool = new GameObjectPool(
                _cakePrefab.gameObject,
                _root,
                CakePoolPrewarm,
                CakePoolMaxInactive,
                onGet: PrepareCakeForReuse,
                onRelease: ResetCakeForPool,
                onDestroy: ForgetCakeInstance);
        }

        private void PrepareCakeForReuse(GameObject cakeObject)
        {
            ResetCakeVisual(cakeObject);
        }

        private void ResetCakeForPool(GameObject cakeObject)
        {
            if (cakeObject == null)
            {
                return;
            }

            SpriteRenderer renderer = cakeObject.GetComponent<SpriteRenderer>();
            if (renderer != null && _cakeTweens.Remove(renderer, out Tween tween))
            {
                tween?.Kill(false);
            }

            _rentedCakes.Remove(renderer);
            _cakes.Remove(renderer);
            _cakeLandingPositions.Remove(renderer);
            ResetCakeVisual(cakeObject);
        }

        private void ForgetCakeInstance(GameObject cakeObject)
        {
            if (cakeObject == null)
            {
                return;
            }

            SpriteRenderer renderer = cakeObject.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                return;
            }

            if (_cakeTweens.Remove(renderer, out Tween tween))
            {
                tween?.Kill(false);
            }

            _rentedCakes.Remove(renderer);
            _cakes.Remove(renderer);
            _cakeLandingPositions.Remove(renderer);
            _cakePropertyBlocks.Remove(renderer);
            _buffBurstStates.Remove(renderer);
        }

        /// <summary>
        /// 页面隐藏时不能让 Tween 在后台继续修改池对象。仍属于当前蛋糕层的对象
        /// 保留在当前位置以便稍后捕获/恢复；已经进入溶解流程的对象立即归还。
        /// </summary>
        private void StopCakeTweensForDisable()
        {
            if (_cakeTweens.Count == 0)
            {
                return;
            }

            _releaseBuffer.Clear();
            foreach (SpriteRenderer cake in _cakeTweens.Keys)
            {
                if (cake != null)
                {
                    _releaseBuffer.Add(cake);
                }
            }

            for (int i = 0; i < _releaseBuffer.Count; i++)
            {
                SpriteRenderer cake = _releaseBuffer[i];
                if (_cakeTweens.TryGetValue(cake, out Tween tween))
                {
                    tween?.Kill(false);
                }

                if (_cakes.Contains(cake)
                    && _cakeLandingPositions.TryGetValue(cake, out Vector3 landing))
                {
                    cake.transform.position = landing;
                }
            }

            _cakeTweens.Clear();
            for (int i = 0; i < _releaseBuffer.Count; i++)
            {
                SpriteRenderer cake = _releaseBuffer[i];
                if (!_cakes.Contains(cake))
                {
                    ReleaseCake(cake);
                }
            }

            _releaseBuffer.Clear();
        }

        private void ResetCakeVisual(GameObject cakeObject)
        {
            if (cakeObject == null)
            {
                return;
            }

            cakeObject.transform.DOKill(false);
            SpriteRenderer renderer = cakeObject.GetComponent<SpriteRenderer>();
            if (renderer == null || _cakePrefab == null)
            {
                return;
            }

            renderer.DOKill(false);
            renderer.SetPropertyBlock(null);
            if (_cakePropertyBlocks.TryGetValue(renderer, out MaterialPropertyBlock block))
            {
                block.Clear();
            }
            renderer.sprite = _cakePrefab.sprite;
            renderer.sharedMaterial = _cakePrefab.sharedMaterial;
            renderer.color = _cakePrefab.color;
            renderer.flipX = _cakePrefab.flipX;
            renderer.flipY = _cakePrefab.flipY;
            renderer.enabled = _cakePrefab.enabled;
        }

        private void ReleaseCake(SpriteRenderer cake)
        {
            if (cake == null || !_rentedCakes.Contains(cake))
            {
                return;
            }

            if (_cakeTweens.Remove(cake, out Tween tween))
            {
                tween?.Kill(false);
            }

            _cakePool?.Release(cake);
        }

        private void CompleteCakeTweenAndRelease(SpriteRenderer cake)
        {
            if (cake == null)
            {
                return;
            }

            _cakeTweens.Remove(cake);
            ReleaseCake(cake);
        }

        private void ForgetCakeTween(SpriteRenderer cake)
        {
            if (cake != null)
            {
                _cakeTweens.Remove(cake);
            }
        }
    }
}
