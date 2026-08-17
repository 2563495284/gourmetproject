using System.Collections.Generic;
using DG.Tweening;
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
        private static readonly int SpriteUvRectId = Shader.PropertyToID("_SpriteUvRect");
        private static readonly int DigestProgressId = Shader.PropertyToID("_DigestProgress");
        private static readonly int DigestCenterId = Shader.PropertyToID("_DigestCenter");
        private static readonly int DigestGridSizeId = Shader.PropertyToID("_DigestGridSize");
        private static readonly int DigestSeedId = Shader.PropertyToID("_DigestSeed");

        private readonly List<SpriteRenderer> _cakes = new();
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

        public void Configure(BattleWorldController owner, Camera camera)
        {
            _owner = owner;
            _camera = camera != null ? camera : Camera.main;
            _random ??= new System.Random();
        }

        public void PlayChange(int before, int after)
        {
            int delta = after - before;
            if (delta == 0)
            {
                return;
            }

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
            if (_root == null)
            {
                return;
            }

            _cakes.Clear();
            for (int i = _root.childCount - 1; i >= 0; i--)
            {
                Destroy(_root.GetChild(i).gameObject);
            }
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

        private SpriteRenderer CreateCake(Vector3 landing, bool animate, int staggerIndex)
        {
            if (_root == null || _cakePrefab == null)
            {
                Debug.LogError($"{nameof(CakeLayerWorldFx)} prefab references are incomplete.", this);
                return null;
            }

            SpriteRenderer renderer = Instantiate(_cakePrefab, _root, false);
            GameObject cakeObject = renderer.gameObject;
            cakeObject.name = $"CakeLayer_{_cakes.Count + 1}";
            float scale = Mathf.Lerp(_randomScaleMultiplier.x, _randomScaleMultiplier.y, (float)_random.NextDouble());
            cakeObject.transform.localScale = Vector3.Scale(_cakePrefab.transform.localScale, Vector3.one * scale);
            float rotation = Mathf.Lerp(_randomRotationRange.x, _randomRotationRange.y, (float)_random.NextDouble());
            cakeObject.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
            BattleSorting.Apply(renderer, BattleSorting.Fx, BattleSorting.OrderFloatingText - 1);
            _cakes.Add(renderer);

            if (!animate || _camera == null)
            {
                cakeObject.transform.position = landing;
                return renderer;
            }

            Vector3 start = landing;
            start.y = _camera.ViewportToWorldPoint(new Vector3(0.5f, 1.15f, Mathf.Abs(_camera.transform.position.z))).y;
            cakeObject.transform.position = start;
            float delay = Mathf.Min(staggerIndex * _fallStagger, _maxFallStagger);
            cakeObject.transform.DOMove(landing, _fallDuration)
                .SetDelay(delay)
                .SetEase(Ease.OutBounce)
                .SetLink(cakeObject);
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
                DOVirtual.Float(startColor.a, 0f, _dissolveDuration, alpha =>
                    {
                        if (cake == null) return;
                        Color color = startColor;
                        color.a = alpha;
                        cake.color = color;
                    })
                    .SetDelay(delay).SetLink(cake.gameObject)
                    .OnComplete(() => Destroy(cake.gameObject));
                return;
            }

            cake.sharedMaterial = material;
            var block = new MaterialPropertyBlock();
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

            DOVirtual.Float(0f, 1f, _dissolveDuration, value =>
                {
                    if (cake == null) return;
                    block.SetFloat(DigestProgressId, Mathf.SmoothStep(0f, 1f, value));
                    cake.SetPropertyBlock(block);
                })
                .SetDelay(delay)
                .SetEase(Ease.Linear)
                .SetLink(cake.gameObject)
                .OnComplete(() => { if (cake != null) Destroy(cake.gameObject); });
        }
    }
}
