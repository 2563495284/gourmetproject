using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GourmetProject.Game.Platformer
{
    /// <summary>
    /// 光照视野系统（URP 2D Light2D，设计文档第四节 / 9）：
    /// 全局极暗冷光作黑暗基底；玩家默认视野（冷蓝灰小光圈）；打火机（暖橙，能量驱动半径 + 火焰闪烁）；
    /// 逗号键全图照亮（拉高全局光强度）。检查点光由此处工厂方法创建。
    /// </summary>
    public sealed class LightingSystem : MonoBehaviour
    {
        private static readonly Color CoolVision = new Color(80f / 255f, 110f / 255f, 160f / 255f);
        private static readonly Color WarmLighter = new Color(255f / 255f, 180f / 255f, 90f / 255f);

        private const float GlobalDarkIntensity = 0.05f;
        private const float GlobalRevealIntensity = 1.0f;

        private Light2D _global;
        private Light2D _vision;
        private Light2D _lighter;
        private Transform _player;
        private EnergySystem _energy;
        private float _flickerSeed;
        private bool _revealAll;

        public bool RevealAll
        {
            get => _revealAll;
            set
            {
                _revealAll = value;
                if (_global != null) _global.intensity = _revealAll ? GlobalRevealIntensity : GlobalDarkIntensity;
            }
        }

        public void Init(Transform player, EnergySystem energy)
        {
            _player = player;
            _energy = energy;
            _flickerSeed = Random.value * 1000f;

            _global = CreateLight("GlobalLight", Light2D.LightType.Global, transform);
            _global.color = new Color(0.5f, 0.6f, 0.8f);
            _global.intensity = GlobalDarkIntensity;

            _vision = CreateLight("VisionLight", Light2D.LightType.Point, player);
            _vision.color = CoolVision;
            _vision.intensity = 0.9f;
            _vision.pointLightOuterRadius = GameConst.DefaultVisionRadius;
            _vision.pointLightInnerRadius = GameConst.DefaultVisionRadius * 0.25f;
            _vision.falloffIntensity = 0.6f;
            // 真实遮挡：默认视野投射较弱阴影（地形背后变暗）。
            _vision.shadowsEnabled = true;
            _vision.shadowIntensity = 0.5f;

            _lighter = CreateLight("LighterLight", Light2D.LightType.Point, player);
            _lighter.color = WarmLighter;
            _lighter.intensity = 1.2f;
            _lighter.pointLightOuterRadius = GameConst.LighterBaseRadius;
            _lighter.pointLightInnerRadius = GameConst.LighterBaseRadius * 0.2f;
            _lighter.falloffIntensity = 0.55f;
            // 真实遮挡：打火机投射明显阴影（地形/墙体背后形成暗区）。
            _lighter.shadowsEnabled = true;
            _lighter.shadowIntensity = 0.75f;
            _lighter.enabled = false;
        }

        private void LateUpdate()
        {
            if (_energy == null) return;

            // 视野遮挡（飞蛾贴附 / 萤火群光团）：收缩视野与打火机光圈，但不归零。
            float occ = GameWorld.Current != null ? GameWorld.Current.VisionOcclusion : 0f;
            float occMul = 1f - occ * 0.8f;

            if (_vision != null)
            {
                float vr = GameConst.DefaultVisionRadius * occMul;
                _vision.pointLightOuterRadius = vr;
                _vision.pointLightInnerRadius = vr * 0.25f;
            }

            if (_energy.LighterOn)
            {
                if (!_lighter.enabled) _lighter.enabled = true;
                float baseR = _energy.LighterRadius * occMul;
                // 火焰闪烁：正弦低频 + 随机高频抖动，半径波动 ±5%。
                float t = Time.time;
                float flicker = 1f
                    + Mathf.Sin((t + _flickerSeed) * 11f) * 0.03f
                    + (Mathf.PerlinNoise((t + _flickerSeed) * 25f, 0f) - 0.5f) * 0.04f;
                float r = Mathf.Max(0.01f, baseR * flicker);
                _lighter.pointLightOuterRadius = r;
                _lighter.pointLightInnerRadius = r * 0.2f;
                _lighter.intensity = 1.2f * flicker;
            }
            else if (_lighter.enabled)
            {
                _lighter.enabled = false;
            }
        }

        /// <summary>工厂：创建并修正目标排序层（运行时 AddComponent 不会触发 Reset，需补齐）。</summary>
        public Light2D CreateLight(string name, Light2D.LightType type, Transform parent)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var light = go.AddComponent<Light2D>();
            light.lightType = type;
            ApplyAllSortingLayers(light);
            return light;
        }

        /// <summary>创建跟随世界坐标的点光（检查点等用）。</summary>
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
                // 反射失败时退化为默认行为，不致命。
            }
        }
    }
}
