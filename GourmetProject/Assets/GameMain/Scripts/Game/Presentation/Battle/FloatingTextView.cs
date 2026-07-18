using DG.Tweening;
using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 结算演出用的飘字：从某处缓缓上浮并淡出后自毁。
    /// 渲染体（TextMesh）预拼在 prefab 上、默认参数走 SerializeField（见 presentation-prefab 规则）。
    /// <see cref="Spawn"/> 传 null 的可选参数表示沿用 prefab 里的默认值。
    /// </summary>
    internal sealed class FloatingTextView : MonoBehaviour
    {
        private const int FloatingOrderBase = BattleSorting.OrderFloatingText;
        private const int FloatingOrderRange = 10000;
        private static int _nextOrderOffset;

        [Header("默认参数（prefab 可调，Spawn 不传时沿用）")]
        [SerializeField] private float _characterSize = 0.14f;
        [SerializeField] private float _rise = 0.9f;
        [SerializeField] private float _duration = 0.9f;
        [SerializeField] private int _fontSize = 64;

        private TextMesh _text;
        private Tween _tween;
        private int _sortingOrder = FloatingOrderBase;

        public static void Spawn(
            FloatingTextView prefab,
            Transform parent,
            Vector3 worldPos,
            string text,
            Color color,
            float? characterSize = null,
            float? rise = null,
            float? duration = null)
        {
            if (prefab == null)
            {
                Debug.LogError($"{nameof(FloatingTextView)} 缺少 prefab。");
                return;
            }

            FloatingTextView view = Instantiate(prefab, parent);

            view.transform.position = worldPos;
            view.SetSortingOrder(NextSortingOrder());
            view.Play(text, color, characterSize, rise, duration);
        }

        public static FloatingTextView SpawnStatic(
            FloatingTextView prefab,
            Transform parent,
            Vector3 worldPos,
            string text,
            Color color,
            float? characterSize = null)
        {
            if (prefab == null)
            {
                Debug.LogError($"{nameof(FloatingTextView)} 缺少 prefab。");
                return null;
            }

            FloatingTextView view = Instantiate(prefab, parent);
            view.transform.position = worldPos;
            view.SetSortingOrder(NextSortingOrder());
            view.SetStaticText(text, color, characterSize);
            return view;
        }

        private static int NextSortingOrder()
        {
            int order = FloatingOrderBase + _nextOrderOffset;
            _nextOrderOffset = (_nextOrderOffset + 1) % FloatingOrderRange;
            return order;
        }

        private void SetSortingOrder(int sortingOrder)
        {
            _sortingOrder = sortingOrder;
            ApplySortingOrder();
        }

        public void SetStaticText(string text, Color color, float? characterSize = null)
        {
            KillAnimation();

            TextMesh tm = EnsureText();
            if (tm == null)
            {
                return;
            }

            tm.text = text;
            tm.color = color;
            tm.characterSize = characterSize ?? _characterSize;
        }

        private void Play(string text, Color color, float? characterSize, float? rise, float? duration)
        {
            KillAnimation();

            float cs = characterSize ?? _characterSize;
            float r = rise ?? _rise;
            float d = duration ?? _duration;

            TextMesh tm = EnsureText();
            if (tm == null)
            {
                return;
            }

            tm.text = text;
            tm.color = color;
            tm.characterSize = cs;

            Animate(tm, transform.position, r, d);
        }

        private void OnDestroy()
        {
            KillAnimation();
        }

        /// <summary>解析 prefab 预拼的 TextMesh 并归一化锚点/排序。</summary>
        private TextMesh EnsureText()
        {
            if (_text != null)
            {
                return _text;
            }

            TextMesh tm = GetComponent<TextMesh>();
            if (tm == null)
            {
                Debug.LogError($"{nameof(FloatingTextView)} prefab 缺少 TextMesh。", this);
                return null;
            }

            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = _fontSize;

            ApplySortingOrder();
            _text = tm;
            return tm;
        }

        private void ApplySortingOrder()
        {
            BattleSorting.Apply(GetComponent<MeshRenderer>(), BattleSorting.Fx, _sortingOrder);
        }

        private void Animate(TextMesh tm, Vector3 start, float rise, float duration)
        {
            Color baseColor = tm.color;
            _tween = DOVirtual.Float(0f, 1f, Mathf.Max(0.0001f, duration), t =>
                {
                    if (tm == null)
                    {
                        return;
                    }

                    transform.position = start + new Vector3(0f, rise * t, 0f);
                    Color c = baseColor;
                    c.a = 1f - t;
                    tm.color = c;
                })
                .SetEase(Ease.Linear)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    if (this != null)
                    {
                        Destroy(gameObject);
                    }
                });
        }

        private void KillAnimation()
        {
            if (_tween != null && _tween.IsActive())
            {
                _tween.Kill(false);
            }

            _tween = null;
        }
    }
}
