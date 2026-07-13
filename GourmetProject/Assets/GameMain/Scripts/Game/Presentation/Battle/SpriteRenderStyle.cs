using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 战斗世界统一使用 Unlit 平涂材质：不依赖 Light2D 受光，画面靠 sprite 原色 + URP 2D 卡通后处理出味，
    /// 避免「分层 ↔ 灯目标排序层」的耦合坑。SpriteRenderer 在 URP 2D 下默认材质是 Lit，必须显式改 Unlit。
    /// </summary>
    internal static class SpriteRenderStyle
    {
        private static Material _spriteUnlitMaterial;
        private static Material _spriteOutlineMaterial;
        private static Material _spriteStainMaterial;

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

        public static Material SpriteOutlineMaterial
        {
            get
            {
                if (_spriteOutlineMaterial == null)
                {
                    Shader shader = Resources.Load<Shader>("Shaders/SpriteOutline") ?? Shader.Find("GourmetProject/SpriteOutline");
                    if (shader != null)
                    {
                        _spriteOutlineMaterial = new Material(shader)
                        {
                            name = "RuntimeSpriteOutline",
                        };
                    }
                }

                return _spriteOutlineMaterial;
            }
        }

        public static Material SpriteStainMaterial
        {
            get
            {
                if (_spriteStainMaterial == null)
                {
                    Shader shader = Resources.Load<Shader>("Shaders/FlavorStain") ?? Shader.Find("GourmetProject/FlavorStain");
                    if (shader != null)
                    {
                        _spriteStainMaterial = new Material(shader)
                        {
                            name = "RuntimeFlavorStain",
                        };
                    }
                }

                return _spriteStainMaterial;
            }
        }

        public static void ApplyUnlitMaterial(SpriteRenderer renderer)
        {
            if (renderer != null && SpriteUnlitMaterial != null)
            {
                renderer.sharedMaterial = SpriteUnlitMaterial;
            }
        }

        public static void ApplyOutlineMaterial(SpriteRenderer renderer)
        {
            if (renderer != null && SpriteOutlineMaterial != null)
            {
                renderer.sharedMaterial = SpriteOutlineMaterial;
            }
        }

        public static void ApplyStainMaterial(SpriteRenderer renderer)
        {
            if (renderer != null && SpriteStainMaterial != null)
            {
                renderer.sharedMaterial = SpriteStainMaterial;
            }
        }
    }
}
