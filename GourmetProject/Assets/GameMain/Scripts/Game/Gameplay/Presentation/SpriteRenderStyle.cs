using UnityEngine;

namespace GourmetProject.Game.Gameplay.Presentation
{
    /// <summary>
    /// 战斗世界统一使用 Unlit 平涂材质：不依赖 Light2D 受光，画面靠 sprite 原色 + URP 2D 卡通后处理出味，
    /// 避免「分层 ↔ 灯目标排序层」的耦合坑。SpriteRenderer 在 URP 2D 下默认材质是 Lit，必须显式改 Unlit。
    /// </summary>
    internal static class SpriteRenderStyle
    {
        private static Material _spriteUnlitMaterial;

        public static Material SpriteUnlitMaterial
        {
            get
            {
                if (_spriteUnlitMaterial == null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                    if (shader != null)
                    {
                        _spriteUnlitMaterial = new Material(shader)
                        {
                            name = "RuntimeSpriteUnlitDefault",
                        };
                    }
                }

                return _spriteUnlitMaterial;
            }
        }

        public static void ApplyUnlitMaterial(SpriteRenderer renderer)
        {
            if (renderer != null && SpriteUnlitMaterial != null)
            {
                renderer.sharedMaterial = SpriteUnlitMaterial;
            }
        }
    }
}
