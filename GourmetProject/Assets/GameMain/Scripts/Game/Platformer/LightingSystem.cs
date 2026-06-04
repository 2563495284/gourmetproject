using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GourmetProject.Game.Platformer
{
    /// <summary>
    /// 光照视野系统（URP 2D Light2D + 屏幕战争迷雾）：
    /// 全局月光；圈内点光（冷视野 / 暖打火机）；圆外由 <see cref="VisionFogOverlay"/> 完全遮挡。
    /// 逗号键全图照亮时关闭迷雾并抬全局光。
    /// </summary>
    public sealed class LightingSystem : MonoBehaviour
    {
        private static readonly Color CoolVision = new Color(130f / 255f, 155f / 255f, 200f / 255f);
        private static readonly Color WarmLighter = new Color(255f / 255f, 180f / 255f, 90f / 255f);

        private Light2D _global;
        private Light2D _vision;
        private Light2D _lighter;
        private Transform _player;
        private EnergySystem _energy;
        private float _flickerSeed;
        private bool _revealAll;

        private float _lighterIntensityBase = 1.25f;

        public bool RevealAll
        {
            get => _revealAll;
            set
            {
                if (_revealAll == value) return;
                _revealAll = value;
                ApplyRevealState();
            }
        }

        public void Init(Transform player, EnergySystem energy)
        {
            _player = player;
            _energy = energy;
            _flickerSeed = Random.value * 1000f;

            _global = CreateLight("GlobalLight", Light2D.LightType.Global, transform);
            _global.color = GameConst.GlobalMoonColor;
            _global.intensity = GameConst.GlobalMoonIntensity;

            _vision = CreateLight("VisionLight", Light2D.LightType.Point, player);
            _vision.color = CoolVision;
            _vision.intensity = GameConst.VisionLightIntensity;
            _vision.pointLightOuterRadius = GameConst.DefaultVisionRadius;
            ApplyPointLightFogProfile(_vision, GameConst.DefaultVisionRadius, GameConst.VisionLightInnerRatio, GameConst.VisionLightFalloff);
            _vision.shadowsEnabled = true;
            _vision.shadowIntensity = 0.12f;

            _lighter = CreateLight("LighterLight", Light2D.LightType.Point, player);
            _lighter.color = WarmLighter;
            _lighter.intensity = _lighterIntensityBase;
            _lighter.pointLightOuterRadius = GameConst.LighterBaseRadius;
            ApplyPointLightFogProfile(_lighter, GameConst.LighterBaseRadius, GameConst.LighterLightInnerRatio, GameConst.LighterLightFalloff);
            _lighter.shadowsEnabled = true;
            _lighter.shadowIntensity = 0.75f;
            _lighter.enabled = false;

            ApplyRevealState();
        }

        private void ApplyRevealState()
        {
            if (_global != null)
            {
                _global.color = _revealAll ? GameConst.GlobalRevealColor : GameConst.GlobalMoonColor;
                _global.intensity = _revealAll ? GameConst.GlobalRevealIntensity : GameConst.GlobalMoonIntensity;
            }

            if (_vision != null)
            {
                _vision.shadowsEnabled = !_revealAll;
                _vision.intensity = _revealAll ? GameConst.VisionLightIntensity * 0.55f : GameConst.VisionLightIntensity;
                _vision.falloffIntensity = _revealAll ? 0.6f : 1f;
            }

            if (_lighter != null)
            {
                _lighter.shadowsEnabled = !_revealAll;
                _lighter.falloffIntensity = _revealAll ? 0.55f : 1f;
                if (!_lighter.enabled)
                    _lighter.intensity = _revealAll ? _lighterIntensityBase * 0.5f : _lighterIntensityBase;
            }
        }

        private void LateUpdate()
        {
            if (_energy == null) return;

            float occ = GameWorld.Current != null ? GameWorld.Current.VisionOcclusion : 0f;
            float occMul = 1f - occ * 0.8f;

            bool lighterOn = _energy.LighterOn;

            if (_vision != null)
            {
                _vision.enabled = !lighterOn || _revealAll;
                if (_vision.enabled)
                {
                    float vr = GameConst.DefaultVisionRadius * occMul;
                    _vision.pointLightOuterRadius = vr;
                    if (_revealAll)
                        _vision.pointLightInnerRadius = vr * 0.25f;
                    else
                        ApplyPointLightFogProfile(_vision, vr, GameConst.VisionLightInnerRatio, GameConst.VisionLightFalloff);
                }
            }

            if (lighterOn)
            {
                if (!_lighter.enabled) _lighter.enabled = true;

                float baseR = _energy.LighterRadius * occMul;
                float t = Time.time;
                float flicker = 1f
                    + Mathf.Sin((t + _flickerSeed) * 11f) * 0.03f
                    + (Mathf.PerlinNoise((t + _flickerSeed) * 25f, 0f) - 0.5f) * 0.04f;
                float r = Mathf.Max(0.01f, baseR * flicker);
                _lighter.pointLightOuterRadius = r;
                if (_revealAll)
                {
                    _lighter.pointLightInnerRadius = r * 0.2f;
                }
                else
                {
                    ApplyPointLightFogProfile(_lighter, r, GameConst.LighterLightInnerRatio, GameConst.LighterLightFalloff);
                }

                float litBase = _revealAll ? _lighterIntensityBase * 0.5f : _lighterIntensityBase;
                _lighter.intensity = litBase * flicker;
            }
            else if (_lighter.enabled)
            {
                _lighter.enabled = false;
            }
        }

        private static void ApplyPointLightFogProfile(Light2D light, float outerRadius, float innerRatio, float falloff)
        {
            light.pointLightOuterRadius = outerRadius;
            light.pointLightInnerRadius = outerRadius * innerRatio;
            light.falloffIntensity = falloff;
        }

        public Light2D CreateLight(string name, Light2D.LightType type, Transform parent)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var light = go.AddComponent<Light2D>();
            light.lightType = type;
            ApplyAllSortingLayers(light);
            return light;
        }

        public Light2D CreatePointLight(string name, Vector2 worldPos, Color color, float radius, float intensity)
        {
            var light = CreateLight(name, Light2D.LightType.Point, transform);
            light.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
            light.color = color;
            light.intensity = intensity;
            light.pointLightOuterRadius = radius;
            light.pointLightInnerRadius = radius * 0.2f;
            light.falloffIntensity = 0.5f;
            return light;
        }

        private static FieldInfo _sortingLayersField;

        private static void ApplyAllSortingLayers(Light2D light)
        {
            try
            {
                _sortingLayersField ??= typeof(Light2D).GetField("m_ApplyToSortingLayers", BindingFlags.NonPublic | BindingFlags.Instance);
                if (_sortingLayersField == null) return;
                var layers = SortingLayer.layers;
                int[] ids = new int[layers.Length];
                for (int i = 0; i < layers.Length; i++) ids[i] = layers[i].id;
                _sortingLayersField.SetValue(light, ids);
            }
            catch
            {
            }
        }
    }
}
