using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 经营挑战世界共享的径向羽化暗斑资源，仅供粒子、舞台与其他不具备 Sprite 轮廓的特效使用。
    /// 按 battle-fake-shadow 规则统一走 drop shadow，禁止 Light2D + ShadowCaster2D。
    /// 食物接触影由 DishPieceView 直接复用食物 Alpha 轮廓，不得回退到这里的矩形占格软斑。
    /// </summary>
    internal static class BattleShadow
    {
        private static Sprite _softShadowSprite;
        private static Sprite _diffuseShadowSprite;

        /// <summary>径向羽化软暗斑（白色 + alpha falloff），中心带实心核，缩放后即可当椭圆接触阴影用（锐利核心层）。</summary>
        public static Sprite SoftShadowSprite
        {
            get
            {
                if (_softShadowSprite == null)
                {
                    // 中心 0.4 半径内基本实心，向边缘 1.0 平滑渐隐。
                    _softShadowSprite = CreateRadialShadowSprite(0.4f);
                }

                return _softShadowSprite;
            }
        }

        /// <summary>全程从中心羽化的弥散软斑（无实心核），用于粒子或舞台特效的低频扩散层。</summary>
        public static Sprite DiffuseShadowSprite
        {
            get
            {
                if (_diffuseShadowSprite == null)
                {
                    // 无实心核：从中心一路渐隐到边缘，形成低频弥散斑。
                    _diffuseShadowSprite = CreateRadialShadowSprite(0f);
                }

                return _diffuseShadowSprite;
            }
        }

        /// <summary>生成径向羽化暗斑：<paramref name="solidInnerRadius"/>（0..1）以内基本实心，向边缘 1.0 平滑渐隐。</summary>
        private static Sprite CreateRadialShadowSprite(float solidInnerRadius)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            float center = (size - 1) * 0.5f;
            float maxRadius = size * 0.5f;
            float inner = Mathf.Clamp01(solidInnerRadius);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / maxRadius;
                    float dy = (y - center) / maxRadius;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(inner, 1f, dist));
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
