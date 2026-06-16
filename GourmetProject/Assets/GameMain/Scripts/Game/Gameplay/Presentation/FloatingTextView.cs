using System.Collections;
using UnityEngine;

namespace GourmetProject.Game.Gameplay.Presentation
{
    /// <summary>结算演出用的飘字：从某处缓缓上浮并淡出后自毁。</summary>
    internal sealed class FloatingTextView : MonoBehaviour
    {
        public static void Spawn(
            Transform parent,
            Vector3 worldPos,
            string text,
            Color color,
            float characterSize = 0.14f,
            float rise = 0.9f,
            float duration = 0.9f)
        {
            var go = new GameObject("FloatingText");
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;

            TextMesh tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            tm.fontSize = 64;
            tm.characterSize = characterSize;

            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            mr.sortingOrder = 90;

            FloatingTextView view = go.AddComponent<FloatingTextView>();
            view.StartCoroutine(view.Animate(tm, worldPos, rise, duration));
        }

        private IEnumerator Animate(TextMesh tm, Vector3 start, float rise, float duration)
        {
            Color baseColor = tm.color;
            float elapsed = 0f;
            while (elapsed < duration && tm != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                transform.position = start + new Vector3(0f, rise * t, 0f);
                Color c = baseColor;
                c.a = 1f - t;
                tm.color = c;
                yield return null;
            }

            if (this != null)
            {
                Destroy(gameObject);
            }
        }
    }
}
