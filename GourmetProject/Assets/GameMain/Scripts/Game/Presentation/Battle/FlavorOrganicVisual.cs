using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 正式食物的有机味区表现绑定器。共享材质，逐实例只写 MaterialPropertyBlock；
    /// 未知风味忽略，重复风味由位掩码天然去重。
    /// </summary>
    internal static class FlavorOrganicVisual
    {
        public const float DefaultIntensity = 0.72f;

        private static readonly int FlavorMaskId = Shader.PropertyToID("_FlavorMask");
        private static readonly int FlavorCountId = Shader.PropertyToID("_FlavorCount");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");
        private static readonly int AnimationEnabledId = Shader.PropertyToID("_AnimationEnabled");
        private static readonly int MotionTimeId = Shader.PropertyToID("_MotionTime");
        private static readonly int UseGlobalTimeId = Shader.PropertyToID("_UseGlobalTime");
        private static readonly int SpriteUvRectId = Shader.PropertyToID("_SpriteUvRect");
        private static readonly Dictionary<Sprite, Vector4> SpriteUvRectCache = new();

        public static bool HasVisibleFlavor(IReadOnlyList<string> flavorIds)
        {
            return FlavorVisualCatalog.BuildMask(flavorIds) != 0;
        }

        public static void ApplyToSpriteRenderer(
            SpriteRenderer renderer,
            IReadOnlyList<string> flavorIds,
            ref MaterialPropertyBlock propertyBlock,
            float seed,
            float intensity = DefaultIntensity,
            bool useGlobalTime = true)
        {
            if (renderer == null)
            {
                return;
            }

            int mask = FlavorVisualCatalog.BuildMask(flavorIds);
            if (mask == 0 || SpriteRenderStyle.SpriteFlavorOrganicMaterial == null)
            {
                renderer.SetPropertyBlock(null);
                SpriteRenderStyle.ApplyUnlitMaterial(renderer);
                return;
            }

            SpriteRenderStyle.ApplyFlavorOrganicMaterial(renderer);
            propertyBlock ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(FlavorMaskId, mask);
            propertyBlock.SetFloat(FlavorCountId, FlavorVisualCatalog.CountBits(mask));
            propertyBlock.SetFloat(SeedId, seed);
            propertyBlock.SetFloat(IntensityId, Mathf.Clamp01(intensity));
            propertyBlock.SetFloat(AspectId, SpriteAspect(renderer.sprite));
            propertyBlock.SetFloat(AnimationEnabledId, 1f);
            propertyBlock.SetFloat(MotionTimeId, 0f);
            propertyBlock.SetFloat(UseGlobalTimeId, useGlobalTime ? 1f : 0f);
            propertyBlock.SetVector(SpriteUvRectId, SpriteUvRect(renderer.sprite));
            renderer.SetPropertyBlock(propertyBlock);
        }

        private static float SpriteAspect(Sprite sprite)
        {
            if (sprite == null || sprite.bounds.size.y <= 0.0001f)
            {
                return 1f;
            }

            return sprite.bounds.size.x / sprite.bounds.size.y;
        }

        private static Vector4 SpriteUvRect(Sprite sprite)
        {
            if (sprite == null)
            {
                return new Vector4(0f, 0f, 1f, 1f);
            }

            if (SpriteUvRectCache.TryGetValue(sprite, out Vector4 cached))
            {
                return cached;
            }

            Vector2[] uv = sprite.uv;
            if (uv == null || uv.Length == 0)
            {
                return new Vector4(0f, 0f, 1f, 1f);
            }

            Vector2 min = uv[0];
            Vector2 max = min;
            for (int i = 1; i < uv.Length; i++)
            {
                min = Vector2.Min(min, uv[i]);
                max = Vector2.Max(max, uv[i]);
            }

            var rect = new Vector4(min.x, min.y, max.x, max.y);
            SpriteUvRectCache[sprite] = rect;
            return rect;
        }
    }
}
