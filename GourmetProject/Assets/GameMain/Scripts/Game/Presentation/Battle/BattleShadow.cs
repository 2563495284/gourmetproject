using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 战斗世界共享的「假阴影」资源：程序生成的径向羽化软暗斑，缩放后可当椭圆接触阴影/投影使用。
    /// 按 battle-fake-shadow 规则统一走 drop shadow，禁止 Light2D + ShadowCaster2D。
    /// 由 DishPieceView、ServeHandView 等表现单元共用，避免重复生成纹理。
    /// </summary>
    internal static class BattleShadow
    {
        private static Sprite _softShadowSprite;

        /// <summary>径向羽化软暗斑（白色 + alpha falloff），缩放后即可当椭圆接触阴影用。</summary>
        public static Sprite SoftShadowSprite
        {
            get
            {
                if (_softShadowSprite == null)
                {
                    _softShadowSprite = CreateSoftShadowSprite();
                }

                return _softShadowSprite;
            }
        }

        private static Sprite CreateSoftShadowSprite()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            float center = (size - 1) * 0.5f;
            float maxRadius = size * 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / maxRadius;
                    float dy = (y - center) / maxRadius;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    // 中心 0.4 半径内基本实心，向边缘 1.0 平滑渐隐。
                    float a = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.4f, 1f, dist));
                    byte alpha = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                    pixels[(y * size) + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
