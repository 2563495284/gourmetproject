using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GourmetProject.Game.Gameplay.Presentation
{
    /// <summary>
    /// 战斗世界运行时 2D 灯光：整体提亮场景（提升/复用全局光 + 棋盘主光），
    /// 并提供点光脉冲用于放置与结算触发的光影反馈，替代原本的格子高亮。
    /// </summary>
    public sealed class BattleLighting : MonoBehaviour
    {
        private static readonly Color WarmGlobal = new Color(1f, 0.96f, 0.9f, 1f);
        private static readonly Color WarmKey = new Color(1f, 0.93f, 0.82f, 1f);

        private const float GlobalIntensity = 1.0f;
        private const float KeyIntensity = 1.2f;

        private Light2D _global;
        private Light2D _key;

        public static BattleLighting Create(Transform parent, Vector3 boardCenter)
        {
            var go = new GameObject("BattleLighting");
            go.transform.SetParent(parent, false);
            BattleLighting lighting = go.AddComponent<BattleLighting>();
            lighting.Build(boardCenter);
            return lighting;
        }

        /// <summary>在世界坐标处闪一下点光，做正反馈。光体在结束后自动销毁。</summary>
        public void Pulse(Vector3 worldPos, Color color, float peak = 2.2f, float radius = 1.8f, float duration = 0.4f)
        {
            StartCoroutine(PulseRoutine(worldPos, color, peak, radius, duration));
        }

        private void Build(Vector3 boardCenter)
        {
            // 优先复用场景预置的全局光并提亮，避免出现多个 Global Light2D 互相叠加。
            Light2D existingGlobal = FindGlobalLight();
            if (existingGlobal != null)
            {
                existingGlobal.intensity = Mathf.Max(existingGlobal.intensity, GlobalIntensity);
                existingGlobal.color = WarmGlobal;
                _global = existingGlobal;
            }
            else
            {
                _global = CreateLight("GlobalLight2D", Vector3.zero, Light2D.LightType.Global);
                _global.intensity = GlobalIntensity;
                _global.color = WarmGlobal;
            }

            // 棋盘上方一盏暖色主光，让菜品有立体受光。
            _key = CreateLight("KeyLight2D", new Vector3(boardCenter.x, boardCenter.y, -1f), Light2D.LightType.Point);
            _key.color = WarmKey;
            _key.intensity = KeyIntensity;
            _key.pointLightInnerRadius = 2.5f;
            _key.pointLightOuterRadius = 8.0f;
        }

        private IEnumerator PulseRoutine(Vector3 worldPos, Color color, float peak, float radius, float duration)
        {
            Light2D flash = CreateLight("FlashLight2D", new Vector3(worldPos.x, worldPos.y, -1f), Light2D.LightType.Point);
            flash.color = color;
            flash.pointLightInnerRadius = radius * 0.2f;
            flash.pointLightOuterRadius = radius;

            float elapsed = 0f;
            while (elapsed < duration && flash != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // 快速亮起、缓慢回落。
                float curve = t < 0.25f ? t / 0.25f : 1f - (t - 0.25f) / 0.75f;
                flash.intensity = peak * Mathf.Clamp01(curve);
                yield return null;
            }

            if (flash != null)
            {
                Destroy(flash.gameObject);
            }
        }

        private Light2D CreateLight(string name, Vector3 position, Light2D.LightType type)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = position;
            Light2D light = go.AddComponent<Light2D>();
            light.lightType = type;
            return light;
        }

        private static Light2D FindGlobalLight()
        {
            Light2D[] lights = FindObjectsByType<Light2D>(FindObjectsSortMode.None);
            foreach (Light2D light in lights)
            {
                if (light != null && light.lightType == Light2D.LightType.Global)
                {
                    return light;
                }
            }

            return null;
        }
    }
}
