using System.Collections;
using UnityEngine;

namespace GourmetProject.Game.Gameplay.Presentation
{
    /// <summary>
    /// 结算演出用的邻接协同连线：在两道相邻菜品之间闪一条线后自毁。
    /// 渲染体（LineRenderer）预拼在 prefab 上、线宽/端点/时长走 SerializeField（见 presentation-prefab 规则），
    /// 缺 prefab 时运行时补齐。<see cref="Spawn"/> 传 null 的 duration 表示沿用 prefab 里的默认值。
    /// </summary>
    internal sealed class AdjacencyLinkView : MonoBehaviour
    {
        [Header("线条参数（prefab 可调）")]
        [SerializeField] private float _widthMultiplier = 0.07f;
        [SerializeField] private int _numCapVertices = 4;
        [SerializeField] private float _duration = 0.4f;

        [Header("固定结构（prefab 预拼，运行时引用）")]
        [SerializeField] private LineRenderer _line;

        private Color _color = Color.white;

        public static void Spawn(AdjacencyLinkView prefab, Transform parent, Vector3 a, Vector3 b, Color color, float? duration = null)
        {
            AdjacencyLinkView view;
            if (prefab != null)
            {
                view = Instantiate(prefab, parent);
            }
            else
            {
                var go = new GameObject("AdjacencyLink");
                go.transform.SetParent(parent, false);
                view = go.AddComponent<AdjacencyLinkView>();
            }

            view.Build(color);
            view.StartCoroutine(view.Flash(a, b, duration ?? view._duration));
        }

        private void Build(Color color)
        {
            _color = color;

            if (_line == null)
            {
                _line = gameObject.GetComponent<LineRenderer>();
                if (_line == null)
                {
                    _line = gameObject.AddComponent<LineRenderer>();
                }
            }

            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.numCapVertices = _numCapVertices;
            _line.widthMultiplier = _widthMultiplier;
            _line.textureMode = LineTextureMode.Stretch;
            BattleSorting.Apply(_line, BattleSorting.Fx, BattleSorting.OrderLink);

            // prefab 未配材质时用 Sprites/Default 兜底（透明混合，配合 alpha 淡入淡出）。
            if (_line.sharedMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    _line.material = new Material(shader);
                }
            }

            SetAlpha(0f);
        }

        private IEnumerator Flash(Vector3 a, Vector3 b, float duration)
        {
            if (_line != null)
            {
                _line.SetPosition(0, a);
                _line.SetPosition(1, b);
            }

            float half = Mathf.Max(0.0001f, duration * 0.5f);
            float elapsed = 0f;
            while (elapsed < half && _line != null)
            {
                elapsed += Time.deltaTime;
                SetAlpha(Mathf.Clamp01(elapsed / half));
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < half && _line != null)
            {
                elapsed += Time.deltaTime;
                SetAlpha(1f - Mathf.Clamp01(elapsed / half));
                yield return null;
            }

            if (this != null)
            {
                Destroy(gameObject);
            }
        }

        private void SetAlpha(float alpha)
        {
            if (_line == null)
            {
                return;
            }

            Color c = _color;
            c.a = alpha;
            _line.startColor = c;
            _line.endColor = c;
        }
    }
}
