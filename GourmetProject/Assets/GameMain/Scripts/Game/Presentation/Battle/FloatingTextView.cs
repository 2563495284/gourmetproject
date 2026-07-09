using DG.Tweening;
using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 结算演出用的飘字：从某处缓缓上浮并淡出后自毁。
    /// 渲染体（TextMesh）预拼在 prefab 上、默认参数走 SerializeField（见 presentation-prefab 规则），
    /// 缺 prefab 时运行时补齐。<see cref="Spawn"/> 传 null 的可选参数表示沿用 prefab 里的默认值。
    /// </summary>
    internal sealed class FloatingTextView : MonoBehaviour
    {
        [Header("默认参数（prefab 可调，Spawn 不传时沿用）")]
        [SerializeField] private float _characterSize = 0.14f;
        [SerializeField] private float _rise = 0.9f;
        [SerializeField] private float _duration = 0.9f;
        [SerializeField] private int _fontSize = 64;

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
            FloatingTextView view;
            if (prefab != null)
            {
                view = Instantiate(prefab, parent);
            }
            else
            {
                var go = new GameObject("FloatingText");
                go.transform.SetParent(parent, false);
                view = go.AddComponent<FloatingTextView>();
            }

            view.transform.position = worldPos;
            view.Play(text, color, characterSize, rise, duration);
        }

        private void Play(string text, Color color, float? characterSize, float? rise, float? duration)
        {
            float cs = characterSize ?? _characterSize;
            float r = rise ?? _rise;
            float d = duration ?? _duration;

            TextMesh tm = EnsureText();
            tm.text = text;
            tm.color = color;
            tm.characterSize = cs;

            Animate(tm, transform.position, r, d);
        }

        /// <summary>兜底解析/补齐 prefab 预拼的 TextMesh 并归一化锚点/排序。</summary>
        private TextMesh EnsureText()
        {
            TextMesh tm = GetComponent<TextMesh>();
            if (tm == null)
            {
                tm = gameObject.AddComponent<TextMesh>();
            }

            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = _fontSize;

            BattleSorting.Apply(GetComponent<MeshRenderer>(), BattleSorting.Fx, BattleSorting.OrderFloatingText);
            return tm;
        }

        private void Animate(TextMesh tm, Vector3 start, float rise, float duration)
        {
            Color baseColor = tm.color;
            DOVirtual.Float(0f, 1f, Mathf.Max(0.0001f, duration), t =>
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
    }
}
