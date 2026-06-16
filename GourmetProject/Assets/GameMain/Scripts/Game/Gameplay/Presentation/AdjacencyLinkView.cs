using System.Collections;
using UnityEngine;

namespace GourmetProject.Game.Gameplay.Presentation
{
    /// <summary>结算演出用的邻接协同连线：在两道相邻菜品之间闪一条线后自毁。</summary>
    internal sealed class AdjacencyLinkView : MonoBehaviour
    {
        private LineRenderer _line;
        private Color _color = Color.white;

        public static void Spawn(AdjacencyLinkView prefab, Transform parent, Vector3 a, Vector3 b, Color color, float duration = 0.4f)
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
            view.StartCoroutine(view.Flash(a, b, duration));
        }

        private void Build(Color color)
        {
            _color = color;
            _line = gameObject.GetComponent<LineRenderer>();
            if (_line == null)
            {
                _line = gameObject.AddComponent<LineRenderer>();
            }

            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.numCapVertices = 4;
            _line.widthMultiplier = 0.07f;
            _line.textureMode = LineTextureMode.Stretch;
            BattleSorting.Apply(_line, BattleSorting.Fx, BattleSorting.OrderLink);

            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                _line.material = new Material(shader);
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
