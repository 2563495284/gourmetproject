using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 战斗世界的两类材质：
    /// - 菜品本体与上菜手走 <see cref="ApplyLitMaterial"/>（Sprite-Lit-Default），接受真实 URP 2D Light2D 受光，
    ///   出明暗/高光立体感。注意：Light2D 默认只照 Default 排序层，灯的 Target Sorting Layers 必须包含
    ///   Pieces / PiecesFlying，否则 Lit sprite 会全黑（见 battle-fake-shadow 规则）。
    /// - 棋盘、背景、世界 UI、假阴影等仍走 <see cref="ApplyUnlitMaterial"/>（Sprite-Unlit-Default）平涂，不受光。
    /// SpriteRenderer 在 URP 2D 下默认材质即 Lit，需要平涂处必须显式改 Unlit。
    /// </summary>
    internal static class SpriteRenderStyle
    {
        private static Material _spriteUnlitMaterial;
        private static Material _spriteLitMaterial;

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

        public static void ApplyUnlitMaterial(SpriteRenderer renderer)
        {
            if (renderer != null && SpriteUnlitMaterial != null)
            {
                renderer.sharedMaterial = SpriteUnlitMaterial;
            }
        }

        /// <summary>受光平涂：让菜品/手接受 Light2D 明暗。需要灯的 Target Sorting Layers 覆盖其排序层。</summary>
        public static void ApplyLitMaterial(SpriteRenderer renderer)
        {
            if (renderer != null && SpriteLitMaterial != null)
            {
                renderer.sharedMaterial = SpriteLitMaterial;
            }
        }
    }
}
