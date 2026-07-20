using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 风味脏印调色板：flavorId -> 脏印颜色（rgb=色，a=强度）的表现层硬编码映射。
    /// 后续可迁移到配置表(tbflavor 增 color 字段)；这里先落地，未知 id 不画。
    /// </summary>
    internal static class FlavorStainPalette
    {
        public const string NumbFlavorId = "t_numb";

        private const int MaxStains = 4;
        private static readonly int StainCountId = Shader.PropertyToID("_StainCount");
        private static readonly int StainScaleId = Shader.PropertyToID("_StainScale");
        private static readonly int StainThresholdId = Shader.PropertyToID("_StainThreshold");
        private static readonly int StainSoftnessId = Shader.PropertyToID("_StainSoftness");
        private static readonly int StainDarkenId = Shader.PropertyToID("_StainDarken");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int[] StainColorIds =
        {
            Shader.PropertyToID("_StainColor0"),
            Shader.PropertyToID("_StainColor1"),
            Shader.PropertyToID("_StainColor2"),
            Shader.PropertyToID("_StainColor3"),
        };

        private static readonly Dictionary<string, Color> Colors = new()
        {
            { "t_sour", new Color(0.36f, 0.82f, 0.30f, 0.85f) },   // 酸：绿
            { "t_rust", new Color(0.72f, 0.38f, 0.14f, 0.88f) },   // 锈：锈橙
            { "t_sweet", new Color(1.00f, 0.55f, 0.72f, 0.80f) },  // 甜：粉桃
            { "t_bitter", new Color(0.28f, 0.24f, 0.16f, 0.88f) }, // 苦：墨褐
            { "t_numb", new Color(0.60f, 0.40f, 0.78f, 0.82f) },   // 麻：花椒紫褐
            { "t_salty", new Color(0.90f, 0.90f, 0.86f, 0.75f) },  // 咸：浅灰白
            { "t_feast", new Color(1.00f, 0.82f, 0.28f, 0.82f) },  // 盛宴：金
            { "t_golden", new Color(1.00f, 0.82f, 0.28f, 0.85f) }, // 黄金：金
            { "t_spicy", new Color(1.00f, 0.23f, 0.19f, 0.88f) },  // 辣：红（配置未来补）
        };

        public readonly struct Settings
        {
            public Settings(float scale, float threshold, float softness, float darken, float seed)
            {
                Scale = scale;
                Threshold = threshold;
                Softness = softness;
                Darken = darken;
                Seed = seed;
            }

            public float Scale { get; }
            public float Threshold { get; }
            public float Softness { get; }
            public float Darken { get; }
            public float Seed { get; }
        }

        /// <summary>解析风味脏印颜色；未知 id 返回 false（不画脏印）。</summary>
        public static bool TryResolve(string flavorId, out Color color)
        {
            if (!string.IsNullOrEmpty(flavorId) && Colors.TryGetValue(flavorId, out color))
            {
                return true;
            }

            color = default;
            return false;
        }

        /// <summary>
        /// 把风味脏印 shader 应用到世界 SpriteRenderer。无可映射风味时恢复普通 Unlit 材质。
        /// </summary>
        public static void ApplyToSpriteRenderer(
            SpriteRenderer renderer,
            IReadOnlyList<string> flavorIds,
            ref MaterialPropertyBlock propertyBlock,
            Settings settings)
        {
            if (renderer == null)
            {
                return;
            }

            List<Color> colors = ResolveColors(flavorIds);
            if (colors.Count == 0 || SpriteRenderStyle.SpriteStainMaterial == null)
            {
                renderer.SetPropertyBlock(null);
                SpriteRenderStyle.ApplyUnlitMaterial(renderer);
                return;
            }

            SpriteRenderStyle.ApplyStainMaterial(renderer);
            propertyBlock ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(propertyBlock);
            ApplyProperties(propertyBlock, colors, settings);
            renderer.SetPropertyBlock(propertyBlock);
        }

        /// <summary>
        /// 把风味脏印 shader 应用到 UI Image/Graphic。无可映射风味时清空自定义材质。
        /// </summary>
        public static void ApplyToGraphic(
            Graphic graphic,
            IReadOnlyList<string> flavorIds,
            ref Material material,
            Settings settings)
        {
            if (graphic == null)
            {
                return;
            }

            List<Color> colors = ResolveColors(flavorIds);
            if (colors.Count == 0 || SpriteRenderStyle.SpriteStainMaterial == null)
            {
                graphic.material = null;
                return;
            }

            if (material == null)
            {
                material = new Material(SpriteRenderStyle.SpriteStainMaterial)
                {
                    name = "RuntimeUIFlavorStain",
                };
            }

            ApplyProperties(material, colors, settings);
            graphic.material = material;
        }

        public static void ReleaseMaterial(ref Material material)
        {
            if (material == null)
            {
                return;
            }

            Object.Destroy(material);
            material = null;
        }

        /// <summary>按「麻」风味计算显示朝向：麻为逆时针 90°，折算到 DishShape 的顺时针 rotationIndex。</summary>
        public static int DisplayRotationIndex(int baseRotationIndex, IReadOnlyList<string> flavorIds)
        {
            int rotationIndex = ((baseRotationIndex % 4) + 4) % 4;
            int numbSteps = NumbSteps(flavorIds);
            if (numbSteps <= 0)
            {
                return rotationIndex;
            }

            int clockwiseSteps = (4 - (numbSteps % 4)) % 4;
            return ((rotationIndex + clockwiseSteps) % 4 + 4) % 4;
        }

        private static List<Color> ResolveColors(IReadOnlyList<string> flavorIds)
        {
            var colors = new List<Color>(MaxStains);
            if (flavorIds == null)
            {
                return colors;
            }

            for (int i = 0; i < flavorIds.Count && colors.Count < MaxStains; i++)
            {
                if (TryResolve(flavorIds[i], out Color color) && !colors.Contains(color))
                {
                    colors.Add(color);
                }
            }

            return colors;
        }

        private static int NumbSteps(IReadOnlyList<string> flavorIds)
        {
            if (flavorIds == null)
            {
                return 0;
            }

            int steps = 0;
            for (int i = 0; i < flavorIds.Count; i++)
            {
                if (flavorIds[i] == NumbFlavorId)
                {
                    steps++;
                }
            }

            return steps;
        }

        private static void ApplyProperties(MaterialPropertyBlock block, IReadOnlyList<Color> colors, Settings settings)
        {
            block.SetFloat(StainCountId, colors.Count);
            block.SetFloat(StainScaleId, settings.Scale);
            block.SetFloat(StainThresholdId, settings.Threshold);
            block.SetFloat(StainSoftnessId, settings.Softness);
            block.SetFloat(StainDarkenId, settings.Darken);
            block.SetFloat(SeedId, settings.Seed);
            for (int i = 0; i < MaxStains; i++)
            {
                Color color = i < colors.Count ? colors[i] : Color.clear;
                block.SetColor(StainColorIds[i], color);
            }
        }

        private static void ApplyProperties(Material material, IReadOnlyList<Color> colors, Settings settings)
        {
            material.SetFloat(StainCountId, colors.Count);
            material.SetFloat(StainScaleId, settings.Scale);
            material.SetFloat(StainThresholdId, settings.Threshold);
            material.SetFloat(StainSoftnessId, settings.Softness);
            material.SetFloat(StainDarkenId, settings.Darken);
            material.SetFloat(SeedId, settings.Seed);
            for (int i = 0; i < MaxStains; i++)
            {
                Color color = i < colors.Count ? colors[i] : Color.clear;
                material.SetColor(StainColorIds[i], color);
            }
        }
    }
}
