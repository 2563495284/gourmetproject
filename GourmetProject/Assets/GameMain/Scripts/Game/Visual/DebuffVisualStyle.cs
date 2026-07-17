using GourmetProject.Game.Presentation.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.Visual
{
    /// <summary>Boss debuff 禁用态的统一视觉材质入口，复刻 Balatro 风格的低饱和红叉遮罩。</summary>
    public static class DebuffVisualStyle
    {
        private static Material _spriteMaterial;
        private static Material _uiMaterial;

        private static Shader Shader => Resources.Load<Shader>("Shaders/DebuffMask") ?? Shader.Find("GourmetProject/DebuffMask");

        private static Material SpriteMaterial
        {
            get
            {
                if (_spriteMaterial == null && Shader != null)
                {
                    _spriteMaterial = new Material(Shader)
                    {
                        name = "RuntimeSpriteDebuffMask",
                    };
                }

                return _spriteMaterial;
            }
        }

        private static Material UiMaterial
        {
            get
            {
                if (_uiMaterial == null && Shader != null)
                {
                    _uiMaterial = new Material(Shader)
                    {
                        name = "RuntimeUIDebuffMask",
                    };
                }

                return _uiMaterial;
            }
        }

        public static void ApplyToSprite(SpriteRenderer renderer)
        {
            if (renderer != null && SpriteMaterial != null)
            {
                renderer.sharedMaterial = SpriteMaterial;
            }
        }

        public static bool IsAppliedToSprite(SpriteRenderer renderer)
        {
            return renderer != null && SpriteMaterial != null && renderer.sharedMaterial == SpriteMaterial;
        }

        public static void ClearSprite(SpriteRenderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.SetPropertyBlock(null);
            SpriteRenderStyle.ApplyUnlitMaterial(renderer);
        }

        public static void ApplyToGraphic(Graphic graphic)
        {
            if (graphic != null && UiMaterial != null)
            {
                graphic.material = UiMaterial;
            }
        }

        public static bool IsAppliedToGraphic(Graphic graphic)
        {
            return graphic != null && UiMaterial != null && graphic.material == UiMaterial;
        }

        public static void ClearGraphic(Graphic graphic)
        {
            if (graphic != null)
            {
                graphic.material = null;
            }
        }
    }
}
