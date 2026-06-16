using UnityEngine;

namespace GourmetProject.Game.Gameplay.Presentation
{
    internal static class SpriteRenderStyle
    {
        private static Material _spriteLitMaterial;

        public static Material SpriteLitMaterial
        {
            get
            {
                if (_spriteLitMaterial == null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
                    if (shader != null)
                    {
                        _spriteLitMaterial = new Material(shader)
                        {
                            name = "RuntimeSpriteLitDefault",
                        };
                    }
                }

                return _spriteLitMaterial;
            }
        }

        public static void ApplyLitMaterial(SpriteRenderer renderer)
        {
            if (renderer != null && SpriteLitMaterial != null)
            {
                renderer.sharedMaterial = SpriteLitMaterial;
            }
        }
    }
}
